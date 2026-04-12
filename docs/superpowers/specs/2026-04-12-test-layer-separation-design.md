# Portal Test Layer Separation Design

## Goal

Split the current mixed `tests/WiSave.Portal.Tests` project into clear testing layers so the repository has an explicit boundary between unit tests, integration tests, and future end-to-end tests.

## Current State

The repository currently has a single test project, `tests/WiSave.Portal.Tests/WiSave.Portal.Tests.csproj`, and the solution and CI workflow reference that project directly.

That project mixes two different kinds of tests:

- Unit-style tests that execute isolated logic or configuration without booting the application host.
- Integration-style tests that use `WebApplicationFactory<Program>` to boot the portal in-process and exercise multiple application components together.

The integration tests are lightweight because they use in-memory substitutes for infrastructure, but they are still integration tests by definition because they execute the real ASP.NET Core host, DI graph, routing, middleware, auth, hubs, and endpoint wiring.

## Definitions

### Unit Tests

Unit tests verify a class, method, or configuration rule in isolation.

Rules:

- Must not boot `Program` or use `WebApplicationFactory`.
- Must not require live infrastructure or real network connections.
- May use simple DI setup when the subject under test is still isolated.

Initial examples:

- `tests/WiSave.Portal.Tests/Authorization/PermissionHandlerTests.cs`
- `tests/WiSave.Portal.Tests/Contracts/ForwardedUserContextTests.cs`
- `tests/WiSave.Portal.Tests/Session/SessionConfigurationTests.cs`

### Integration Tests

Integration tests verify that application components work together inside the real portal host.

Rules:

- May use `WebApplicationFactory<Program>`.
- May boot the real ASP.NET Core app and exercise routing, middleware, auth, SignalR, and endpoint behavior.
- May use in-memory or fake infrastructure for determinism.
- Must not be labeled as unit tests even if they are fast.

Initial examples:

- `tests/WiSave.Portal.Tests/Auth/AuthEndpointsTests.cs`
- `tests/WiSave.Portal.Tests/Gateway/UserHeaderTransformTests.cs`
- `tests/WiSave.Portal.Tests/Hubs/NotificationsHubTests.cs`
- `tests/WiSave.Portal.Tests/Messaging/ConsumerSignalRTests.cs`

### End-to-End Tests

End-to-end tests execute the system from outside the application boundary.

Rules:

- Must not use in-process host shortcuts such as `WebApplicationFactory`.
- Should target a running stack through HTTP, browser automation, or deployed environment entry points.
- Are intentionally slower and operationally separate from routine validation.

There are no current E2E tests in this repository. The design reserves a clear place for them so they can be added later without mixing them into the unit or integration layers.

## Target Repository Structure

The repository should move to this layout:

- `tests/WiSave.Portal.UnitTests`
- `tests/WiSave.Portal.IntegrationTests`
- `tests/WiSave.Portal.E2E` reserved for future use, but not created until there is at least one real E2E scenario

Only two projects should be created now:

- `tests/WiSave.Portal.UnitTests/WiSave.Portal.UnitTests.csproj`
- `tests/WiSave.Portal.IntegrationTests/WiSave.Portal.IntegrationTests.csproj`

The existing mixed project should be retired after its files are redistributed.

## Naming and Namespace Conventions

Namespaces should follow the project boundary so the layer is visible from the code itself:

- `WiSave.Portal.UnitTests.*`
- `WiSave.Portal.IntegrationTests.*`
- Future: `WiSave.Portal.E2E.*` or `WiSave.Portal.E2ETests.*`

Files should be grouped by behavior, not by historical location. A test file belongs in the project that matches how it exercises the system.

## CI Strategy

### Publish Workflow

The package publishing workflow should run unit tests only.

Reasoning:

- Publishing contracts should stay fast and deterministic.
- That workflow should not depend on application-host boot unless contract packaging itself requires it.
- Unit coverage is enough there to protect isolated logic used by packaging-adjacent changes.

Target command:

```bash
dotnet test tests/WiSave.Portal.UnitTests/WiSave.Portal.UnitTests.csproj --configuration Release --no-restore
```

### Main Validation Workflow

A separate validation workflow should run both unit and integration tests on pushes and pull requests.

Target commands:

```bash
dotnet test tests/WiSave.Portal.UnitTests/WiSave.Portal.UnitTests.csproj --configuration Release
dotnet test tests/WiSave.Portal.IntegrationTests/WiSave.Portal.IntegrationTests.csproj --configuration Release
```

This workflow must be added immediately if integration tests are removed from the publish workflow so coverage is not silently lost.

### Future E2E Workflow

E2E should have its own workflow with slower triggers such as:

- manual dispatch
- nightly schedule
- release or pre-release validation

The E2E workflow is intentionally separate so operational flakiness or browser/runtime costs do not contaminate normal application validation.

## Migration Plan Shape

The implementation should proceed in this order:

1. Create the `UnitTests` and `IntegrationTests` projects.
2. Move existing test files into the correct project based on behavior.
3. Update namespaces and project references.
4. Update `WiSave.Portal.slnx` to include the new projects and remove the old mixed project.
5. Update workflows to run the new projects in the correct lanes.
6. Update any active documentation that points contributors to the old mixed test project.

This keeps the refactor mechanical and lowers the risk of mixing classification changes with unrelated test rewrites.

## Non-Goals

- Adding Testcontainers in this change.
- Introducing browser tooling in this change.
- Rewriting existing tests unless a move requires minor namespace or helper adjustments.
- Broad cleanup of all historical docs that mention `WiSave.Portal.Tests`.

## Risks and Controls

### Risk: Misclassifying fast host-based tests as unit tests

Control:

Treat any `WebApplicationFactory<Program>` test as integration by definition.

### Risk: Losing CI coverage by narrowing the publish workflow

Control:

Add or update a dedicated validation workflow in the same implementation batch.

### Risk: Shared helpers blur layer boundaries again

Control:

Keep host-boot and app-fixture helpers in the integration project only. Keep unit helpers local to the unit project.

## Success Criteria

The design is complete when:

- unit and integration tests live in separate test projects
- no `WebApplicationFactory` test remains in the unit test project
- the publish workflow runs unit tests only
- a validation workflow runs both unit and integration suites
- the repository has an explicit reserved path for future E2E tests
