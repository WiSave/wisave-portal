# AGENTS.md

Guidance for coding agents working in this repository.

## Project Overview

- `WiSave.Portal` is an ASP.NET Core portal/gateway service.
- It handles authentication, permission resolution, session management, SignalR hubs, messaging, and YARP-based proxying to downstream services.
- Keep changes focused and consistent with the existing architecture. Avoid broad refactors unless they directly support the requested work.

## Repository Layout

- `src/WiSave.Portal` — main application code
- `src/WiSave.Portal/Auth` — identity setup and auth models
- `src/WiSave.Portal/Authorization` — permission resolution and authorization helpers
- `src/WiSave.Portal/Endpoints` — HTTP endpoint mappings
- `src/WiSave.Portal/Gateway` — YARP proxy configuration and transforms
- `src/WiSave.Portal/Infrastructure` — infrastructure wiring and database access
- `src/WiSave.Portal/Session` — session storage implementation
- `src/WiSave.Portal/Hubs` — SignalR hubs
- `src/WiSave.Portal/Messaging` — messaging-related integration code
- `src/WiSave.Portal.Migrations` — DbUp migration project
- `src/WiSave.Portal.EfTools` — EF Core tooling support
- `tests/WiSave.Portal.Tests` — integration and behavior tests
- `scripts` — helper scripts for local development and migration workflows
- `docs` — project and workflow documentation

## Working Style

- Prefer minimal, surgical changes that solve the root problem.
- Follow existing naming, folder structure, and registration patterns.
- Do not add new abstractions unless repetition or coupling clearly justifies them.
- Avoid unrelated cleanup while implementing a task.
- When modifying behavior, update or add the nearest relevant tests.

## Build, Run, and Test

Use the existing documented commands:

```bash
dotnet build
dotnet run --project src/WiSave.Portal
dotnet test
dotnet test --filter "FullyQualifiedName~WiSave.Portal.Tests.Auth.AuthEndpointsTests"
docker compose up -d
docker compose --profile portal up -d --build
./scripts/generate-dbup-script.sh <from-migration|0> <to-migration> <output-sql-path>
```

## Testing Expectations

- Prefer targeted test execution first, then broader validation if needed.
- Tests use the in-memory database setup unless the task explicitly requires infrastructure-backed validation.
- Do not claim a fix is complete without running the most relevant verification you can run.
- If you cannot run verification, say so clearly and explain what should be run.

## Portal-Specific Guidance

- Auth and authorization changes should preserve the current plan/permission model.
- Gateway changes must keep header handling and proxy safety in mind, especially identity and permission headers.
- Unsafe proxied HTTP methods should continue respecting antiforgery requirements unless the task explicitly changes that behavior.
- Session-related changes should consider Redis-backed behavior and in-memory fallback paths.
- Database-related changes should stay aligned between runtime code, migrations, and tests.

## Migrations and Data Changes

- Put schema evolution in the appropriate migrations project instead of ad hoc runtime logic.
- Keep EF tooling and DbUp workflows compatible when changing persistence-related code.
- Backend agents must not create, edit, delete, regenerate, rename, or otherwise modify EF migration files or DbUp SQL scripts unless the user explicitly asks for that exact migration/script change.
- When working on backend changes, agents may read EF migrations and DbUp scripts for context only. Treat migration and DbUp script files as read-only by default.
- Mention any required migration or seed follow-up in the handoff.

## Agent Handoff

When finishing work:

- summarize what changed and why
- list the files touched
- note verification performed
- call out any follow-up steps, risks, or commands the user should run
