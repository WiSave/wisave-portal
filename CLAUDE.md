# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run Commands

```bash
# Build
dotnet build

# Run the portal (requires Postgres + Redis; see docker-compose for local infra)
dotnet run --project src/WiSave.Portal

# Publish the portal container image consumed by docker-compose
dotnet publish src/WiSave.Portal/WiSave.Portal.csproj -c Release --os linux /t:PublishContainer

# Run all tests (uses in-memory database, no external deps needed)
dotnet test

# Run a single test by fully-qualified name
dotnet test --filter "FullyQualifiedName~WiSave.Portal.Tests.Auth.AuthEndpointsTests.Register_ReturnsOk"

# Run tests in a specific class
dotnet test --filter "FullyQualifiedName~WiSave.Portal.Tests.Auth.AuthEndpointsTests"

# Start local infrastructure (Postgres + Redis)
docker compose up -d postgres redis

# Generate a DbUp migration SQL from EF Core migrations
./scripts/generate-dbup-script.sh <from-migration|0> <to-migration> <output-sql-path>
```

## Architecture

**WiSave Portal** is an API gateway/portal service built on ASP.NET Core (.NET 10). It authenticates users, resolves plan-based permissions, and proxies requests to downstream microservices via YARP reverse proxy.

### Solution Projects

- **WiSave.Portal** — Main application: auth endpoints, YARP gateway, session management, authorization middleware
- **WiSave.Portal.Migrations** — DbUp-based PostgreSQL migrations (SQL scripts in `Scripts/`, run via `DbMigrator.Run()`)
- **WiSave.Portal.Tests** — xUnit v3 integration tests using `WebApplicationFactory` with in-memory database
- **WiSave.Portal.EfTools** — Design-time helper for EF Core CLI tooling (used by `generate-dbup-script.sh`)

### Key Layers (within WiSave.Portal)

- **Auth** (`/Auth`) — ASP.NET Core Identity configuration, `ApplicationUser` model (extends `IdentityUser` with `Name` and `PlanId`)
- **Authorization** (`/Authorization`) — `PermissionResolutionMiddleware` resolves user permissions from their plan, caches via `UserPlanCache` and `PlanPermissionCache` (1hr TTL, distributed cache)
- **Gateway** (`/Gateway`) — YARP config (`YarpConfiguration.cs`), `UserHeaderTransform` strips client identity headers and injects authenticated user info (X-User-Id, X-User-Email, X-User-Roles, X-User-Permissions)
- **Session** (`/Session`) — Redis-backed session storage (`RedisTicketStore`), falls back to in-memory cache
- **Endpoints** (`/Endpoints`) — Auth endpoint mappings at `/api/auth` (register, login, logout, me, antiforgery-token)
- **Infrastructure/Database** (`/Infrastructure/Database`) — `PortalDbContext` with Identity tables + Plans, Permissions, PlanPermissions

### Permission Model

Users belong to a **Plan** (free/standard/premium). Each plan maps to a set of **Permissions** (e.g., `incomes:read`, `stocks:write`). Admin roles (`superadmin`, `admin`) get wildcard `*` permissions. Resolved permissions are injected into proxy headers for downstream services.

### Proxy Routes

YARP routes `/api/incomes/{**remainder}` and `/api/stocks/{**remainder}` to downstream service clusters. Antiforgery validation is enforced on unsafe HTTP methods (POST/PUT/DELETE/PATCH) in the proxy pipeline.

### Local Development

Default downstream targets: incomes at `localhost:5114`, stocks at `localhost:5086`. Override via `ReverseProxy:Clusters` config or environment variables. CORS allows `http://localhost:4200` (Angular frontend). API docs available at `/scalar/v1` in Development mode.

## Testing

Tests use `WebApplicationFactory` with `UseInMemoryDatabase=true` — no external services required. Each test seeds its own roles and plans. `UserHeaderTransformTests` spins up a local echo server to validate proxy header injection.
