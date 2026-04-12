# Portal Test Layer Separation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Split the mixed portal test project into explicit unit and integration test projects, keep the publish workflow limited to unit tests, and introduce a separate validation workflow that runs both layers.

**Architecture:** The change is a mechanical repository refactor. Existing isolated tests move into a new unit test project, existing `WebApplicationFactory<Program>` tests move into a new integration test project, and CI is updated so publish stays fast while validation preserves integration coverage. The old mixed project is removed only after both new projects are green.

**Tech Stack:** .NET 10, xUnit v3, ASP.NET Core `WebApplicationFactory`, GitHub Actions, solution file (`.slnx`) maintenance

---

## File Map

- Create: `tests/WiSave.Portal.UnitTests/WiSave.Portal.UnitTests.csproj` — package and project references for isolated tests only.
- Create: `tests/WiSave.Portal.UnitTests/Authorization/PermissionHandlerTests.cs` — moved unit test file with updated namespace.
- Create: `tests/WiSave.Portal.UnitTests/Contracts/ForwardedUserContextTests.cs` — moved unit test file with updated namespace.
- Create: `tests/WiSave.Portal.UnitTests/Session/SessionConfigurationTests.cs` — moved unit test file with updated namespace.
- Create: `tests/WiSave.Portal.IntegrationTests/WiSave.Portal.IntegrationTests.csproj` — package and project references for host-boot integration tests.
- Create: `tests/WiSave.Portal.IntegrationTests/Auth/AuthEndpointsTests.cs` — moved integration test file with updated namespace.
- Create: `tests/WiSave.Portal.IntegrationTests/Gateway/UserHeaderTransformTests.cs` — moved integration test file with updated namespace.
- Create: `tests/WiSave.Portal.IntegrationTests/Hubs/NotificationsHubTests.cs` — moved integration test file with updated namespace.
- Create: `tests/WiSave.Portal.IntegrationTests/Messaging/ConsumerSignalRTests.cs` — moved integration test file with updated namespace.
- Create: `.github/workflows/portal-validation.yml` — dedicated validation workflow for unit and integration suites.
- Modify: `WiSave.Portal.slnx` — include the new test projects and remove the old mixed project.
- Modify: `.github/workflows/publish-portal-contracts.yml` — point the publish workflow at the new unit test project.
- Delete: `tests/WiSave.Portal.Tests/WiSave.Portal.Tests.csproj`
- Delete: `tests/WiSave.Portal.Tests/Auth/AuthEndpointsTests.cs`
- Delete: `tests/WiSave.Portal.Tests/Authorization/PermissionHandlerTests.cs`
- Delete: `tests/WiSave.Portal.Tests/Contracts/ForwardedUserContextTests.cs`
- Delete: `tests/WiSave.Portal.Tests/Gateway/UserHeaderTransformTests.cs`
- Delete: `tests/WiSave.Portal.Tests/Hubs/NotificationsHubTests.cs`
- Delete: `tests/WiSave.Portal.Tests/Messaging/ConsumerSignalRTests.cs`
- Delete: `tests/WiSave.Portal.Tests/Session/SessionConfigurationTests.cs`

### Task 1: Create the Unit Test Project

**Files:**
- Create: `tests/WiSave.Portal.UnitTests/WiSave.Portal.UnitTests.csproj`
- Create: `tests/WiSave.Portal.UnitTests/Authorization/PermissionHandlerTests.cs`
- Create: `tests/WiSave.Portal.UnitTests/Contracts/ForwardedUserContextTests.cs`
- Create: `tests/WiSave.Portal.UnitTests/Session/SessionConfigurationTests.cs`
- Delete: `tests/WiSave.Portal.Tests/Authorization/PermissionHandlerTests.cs`
- Delete: `tests/WiSave.Portal.Tests/Contracts/ForwardedUserContextTests.cs`
- Delete: `tests/WiSave.Portal.Tests/Session/SessionConfigurationTests.cs`

- [ ] **Step 1: Prove the unit test project does not exist yet**

Run:

```bash
dotnet test tests/WiSave.Portal.UnitTests/WiSave.Portal.UnitTests.csproj
```

Expected: FAIL with output indicating the project file does not exist.

- [ ] **Step 2: Create the new unit test project file**

Write `tests/WiSave.Portal.UnitTests/WiSave.Portal.UnitTests.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <OutputType>Exe</OutputType>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.0" />
    <PackageReference Include="xunit.v3" Version="2.0.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.0.2" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="../../src/WiSave.Portal.Contracts/WiSave.Portal.Contracts.csproj" />
    <ProjectReference Include="../../src/WiSave.Portal/WiSave.Portal.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Move the isolated tests into the new unit project**

Run:

```bash
mkdir -p tests/WiSave.Portal.UnitTests/Authorization tests/WiSave.Portal.UnitTests/Contracts tests/WiSave.Portal.UnitTests/Session
mv tests/WiSave.Portal.Tests/Authorization/PermissionHandlerTests.cs tests/WiSave.Portal.UnitTests/Authorization/PermissionHandlerTests.cs
mv tests/WiSave.Portal.Tests/Contracts/ForwardedUserContextTests.cs tests/WiSave.Portal.UnitTests/Contracts/ForwardedUserContextTests.cs
mv tests/WiSave.Portal.Tests/Session/SessionConfigurationTests.cs tests/WiSave.Portal.UnitTests/Session/SessionConfigurationTests.cs
```

- [ ] **Step 4: Update unit test namespaces to match the new project**

Change the namespace declarations to:

```csharp
namespace WiSave.Portal.UnitTests.Authorization;
```

```csharp
namespace WiSave.Portal.UnitTests.Contracts;
```

```csharp
namespace WiSave.Portal.UnitTests.Session;
```

- [ ] **Step 5: Run the unit-only suite to verify it passes**

Run:

```bash
dotnet test tests/WiSave.Portal.UnitTests/WiSave.Portal.UnitTests.csproj
```

Expected: PASS with only the moved isolated tests executing.

- [ ] **Step 6: Commit the unit project split**

Run:

```bash
git add tests/WiSave.Portal.UnitTests tests/WiSave.Portal.Tests/Authorization/PermissionHandlerTests.cs tests/WiSave.Portal.Tests/Contracts/ForwardedUserContextTests.cs tests/WiSave.Portal.Tests/Session/SessionConfigurationTests.cs
git commit -m "test: split portal unit tests into dedicated project"
```

### Task 2: Create the Integration Test Project

**Files:**
- Create: `tests/WiSave.Portal.IntegrationTests/WiSave.Portal.IntegrationTests.csproj`
- Create: `tests/WiSave.Portal.IntegrationTests/Auth/AuthEndpointsTests.cs`
- Create: `tests/WiSave.Portal.IntegrationTests/Gateway/UserHeaderTransformTests.cs`
- Create: `tests/WiSave.Portal.IntegrationTests/Hubs/NotificationsHubTests.cs`
- Create: `tests/WiSave.Portal.IntegrationTests/Messaging/ConsumerSignalRTests.cs`
- Delete: `tests/WiSave.Portal.Tests/Auth/AuthEndpointsTests.cs`
- Delete: `tests/WiSave.Portal.Tests/Gateway/UserHeaderTransformTests.cs`
- Delete: `tests/WiSave.Portal.Tests/Hubs/NotificationsHubTests.cs`
- Delete: `tests/WiSave.Portal.Tests/Messaging/ConsumerSignalRTests.cs`

- [ ] **Step 1: Prove the integration test project does not exist yet**

Run:

```bash
dotnet test tests/WiSave.Portal.IntegrationTests/WiSave.Portal.IntegrationTests.csproj
```

Expected: FAIL with output indicating the project file does not exist.

- [ ] **Step 2: Create the integration test project file with the current host-testing dependencies**

Write `tests/WiSave.Portal.IntegrationTests/WiSave.Portal.IntegrationTests.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <OutputType>Exe</OutputType>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="MassTransit" Version="8.5.8" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.0" />
    <PackageReference Include="Microsoft.AspNetCore.SignalR.Client" Version="10.0.0" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" Version="10.0.5" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Relational" Version="10.0.5" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.0" />
    <PackageReference Include="xunit.v3" Version="2.0.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.0.2" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="../../src/WiSave.Portal.Contracts/WiSave.Portal.Contracts.csproj" />
    <ProjectReference Include="../../src/WiSave.Portal/WiSave.Portal.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Move the `WebApplicationFactory` tests into the integration project**

Run:

```bash
mkdir -p tests/WiSave.Portal.IntegrationTests/Auth tests/WiSave.Portal.IntegrationTests/Gateway tests/WiSave.Portal.IntegrationTests/Hubs tests/WiSave.Portal.IntegrationTests/Messaging
mv tests/WiSave.Portal.Tests/Auth/AuthEndpointsTests.cs tests/WiSave.Portal.IntegrationTests/Auth/AuthEndpointsTests.cs
mv tests/WiSave.Portal.Tests/Gateway/UserHeaderTransformTests.cs tests/WiSave.Portal.IntegrationTests/Gateway/UserHeaderTransformTests.cs
mv tests/WiSave.Portal.Tests/Hubs/NotificationsHubTests.cs tests/WiSave.Portal.IntegrationTests/Hubs/NotificationsHubTests.cs
mv tests/WiSave.Portal.Tests/Messaging/ConsumerSignalRTests.cs tests/WiSave.Portal.IntegrationTests/Messaging/ConsumerSignalRTests.cs
```

- [ ] **Step 4: Update integration test namespaces to match the new project**

Change the namespace declarations to:

```csharp
namespace WiSave.Portal.IntegrationTests.Auth;
```

```csharp
namespace WiSave.Portal.IntegrationTests.Gateway;
```

```csharp
namespace WiSave.Portal.IntegrationTests.Hubs;
```

```csharp
namespace WiSave.Portal.IntegrationTests.Messaging;
```

- [ ] **Step 5: Run the integration-only suite to verify the moved host-based tests pass**

Run:

```bash
dotnet test tests/WiSave.Portal.IntegrationTests/WiSave.Portal.IntegrationTests.csproj
```

Expected: PASS with the `WebApplicationFactory`-based tests executing from the new project.

- [ ] **Step 6: Commit the integration project split**

Run:

```bash
git add tests/WiSave.Portal.IntegrationTests tests/WiSave.Portal.Tests/Auth/AuthEndpointsTests.cs tests/WiSave.Portal.Tests/Gateway/UserHeaderTransformTests.cs tests/WiSave.Portal.Tests/Hubs/NotificationsHubTests.cs tests/WiSave.Portal.Tests/Messaging/ConsumerSignalRTests.cs
git commit -m "test: split portal integration tests into dedicated project"
```

### Task 3: Rewire the Solution and Remove the Mixed Test Project

**Files:**
- Modify: `WiSave.Portal.slnx`
- Delete: `tests/WiSave.Portal.Tests/WiSave.Portal.Tests.csproj`

- [ ] **Step 1: Update the solution to reference the two new test projects**

Replace the current single test project entry in `WiSave.Portal.slnx`:

```xml
  <Folder Name="/tests/" />
  <Folder Name="/Solution Items/">
    <File Path="CLAUDE.md" />
    <File Path="docker-compose.yml" />
  </Folder>
  <Project Path="tests/WiSave.Portal.Tests/WiSave.Portal.Tests.csproj" />
```

with:

```xml
  <Folder Name="/tests/">
    <Project Path="tests/WiSave.Portal.UnitTests/WiSave.Portal.UnitTests.csproj" />
    <Project Path="tests/WiSave.Portal.IntegrationTests/WiSave.Portal.IntegrationTests.csproj" />
  </Folder>
  <Folder Name="/Solution Items/">
    <File Path="CLAUDE.md" />
    <File Path="docker-compose.yml" />
  </Folder>
```

- [ ] **Step 2: Remove the old mixed test project file**

Run:

```bash
rm tests/WiSave.Portal.Tests/WiSave.Portal.Tests.csproj
```

- [ ] **Step 3: Verify the solution sees the new test projects**

Run:

```bash
dotnet sln WiSave.Portal.slnx list
```

Expected: output includes:

```text
tests/WiSave.Portal.UnitTests/WiSave.Portal.UnitTests.csproj
tests/WiSave.Portal.IntegrationTests/WiSave.Portal.IntegrationTests.csproj
```

- [ ] **Step 4: Run both suites through their new project boundaries**

Run:

```bash
dotnet test tests/WiSave.Portal.UnitTests/WiSave.Portal.UnitTests.csproj
dotnet test tests/WiSave.Portal.IntegrationTests/WiSave.Portal.IntegrationTests.csproj
```

Expected: both commands PASS and no test depends on the deleted mixed project.

- [ ] **Step 5: Commit the solution rewire**

Run:

```bash
git add WiSave.Portal.slnx tests/WiSave.Portal.Tests/WiSave.Portal.Tests.csproj
git commit -m "build: replace mixed portal test project with explicit test layers"
```

### Task 4: Update CI for the New Test Layers

**Files:**
- Modify: `.github/workflows/publish-portal-contracts.yml`
- Create: `.github/workflows/portal-validation.yml`

- [ ] **Step 1: Prove the current publish workflow still points at the mixed test project**

Run:

```bash
rg -n "WiSave\\.Portal\\.Tests/WiSave\\.Portal\\.Tests\\.csproj" .github/workflows/publish-portal-contracts.yml
```

Expected: one match in the `Run portal tests` step.

- [ ] **Step 2: Update the publish workflow to run only unit tests**

Change the test step in `.github/workflows/publish-portal-contracts.yml` to:

```yaml
      - name: Run portal unit tests
        run: dotnet test tests/WiSave.Portal.UnitTests/WiSave.Portal.UnitTests.csproj --configuration Release --no-restore
```

- [ ] **Step 3: Add the dedicated validation workflow for unit and integration suites**

Write `.github/workflows/portal-validation.yml` with:

```yaml
name: Validate WiSave.Portal

on:
  pull_request:
  push:

permissions:
  contents: read
  packages: read

concurrency:
  group: portal-validation-${{ github.ref }}
  cancel-in-progress: true

jobs:
  validate:
    runs-on: ubuntu-latest
    env:
      PACKAGES_USERNAME: ${{ github.repository_owner }}
      PACKAGES_TOKEN: ${{ secrets.PACKAGES_READ_TOKEN }}

    steps:
      - name: Checkout repository
        uses: actions/checkout@v6

      - name: Setup .NET SDK
        uses: actions/setup-dotnet@v5
        with:
          dotnet-version: 10.0.x

      - name: Validate GitHub Packages credentials
        shell: bash
        run: |
          if [ -z "${PACKAGES_TOKEN}" ]; then
            echo "::error title=Missing GitHub Packages token::Repository secret PACKAGES_READ_TOKEN is not set."
            exit 1
          fi

      - name: Restore solution
        env:
          NuGetPackageSourceCredentials_github: Username=${{ env.PACKAGES_USERNAME }};Password=${{ env.PACKAGES_TOKEN }}
        run: dotnet restore WiSave.Portal.slnx --configfile NuGet.Config

      - name: Run portal unit tests
        run: dotnet test tests/WiSave.Portal.UnitTests/WiSave.Portal.UnitTests.csproj --configuration Release --no-restore

      - name: Run portal integration tests
        run: dotnet test tests/WiSave.Portal.IntegrationTests/WiSave.Portal.IntegrationTests.csproj --configuration Release --no-restore
```

- [ ] **Step 4: Verify the workflow references are clean**

Run:

```bash
rg -n "WiSave\\.Portal\\.Tests/WiSave\\.Portal\\.Tests\\.csproj|WiSave\\.Portal\\.UnitTests|WiSave\\.Portal\\.IntegrationTests" .github/workflows
```

Expected:

- `publish-portal-contracts.yml` references only `WiSave.Portal.UnitTests`
- `portal-validation.yml` references both `WiSave.Portal.UnitTests` and `WiSave.Portal.IntegrationTests`
- no workflow references the deleted mixed project

- [ ] **Step 5: Commit the CI split**

Run:

```bash
git add .github/workflows/publish-portal-contracts.yml .github/workflows/portal-validation.yml
git commit -m "ci: separate portal unit and integration test workflows"
```

### Task 5: Final Verification and Cleanup

**Files:**
- Modify: all files from Tasks 1-4 as needed for final corrections

- [ ] **Step 1: Run the full validation sequence locally**

Run:

```bash
dotnet test tests/WiSave.Portal.UnitTests/WiSave.Portal.UnitTests.csproj
dotnet test tests/WiSave.Portal.IntegrationTests/WiSave.Portal.IntegrationTests.csproj
git diff --check
```

Expected:

- both test commands PASS
- `git diff --check` prints no whitespace or conflict issues

- [ ] **Step 2: Run a final search for stale mixed-project references**

Run:

```bash
rg -n "tests/WiSave\\.Portal\\.Tests|WiSave\\.Portal\\.Tests\\.csproj" WiSave.Portal.slnx .github tests docs
```

Expected:

- no matches in `WiSave.Portal.slnx` or `.github/workflows`
- historical docs may still match; only update them if they are active guidance and still misleading

- [ ] **Step 3: Stage and commit any final fixes**

Run:

```bash
git add WiSave.Portal.slnx .github/workflows tests
git commit -m "chore: finalize portal test layer separation"
```

- [ ] **Step 4: Prepare the branch handoff**

Report:

```text
Unit test project: tests/WiSave.Portal.UnitTests
Integration test project: tests/WiSave.Portal.IntegrationTests
Publish workflow: unit tests only
Validation workflow: unit + integration
Reserved future layer: tests/WiSave.Portal.E2E
```
