# WiSave.Portal.Contracts Design

Date: 2026-04-12

## Summary

Create a small NuGet package named `WiSave.Portal.Contracts` to define the authentication and authorization transport contract between `WiSave.Portal` and downstream services such as `WiSave.Expenses`.

The package will be owned by the portal, published independently, and consumed by child services. It will centralize:

- forwarded auth header names
- forwarded user context types
- header parsing and serialization helpers
- shared permission constants used across portal and child services

The package will remain a pure .NET class library with no ASP.NET Core dependency.

## Current State

Today the portal authenticates the user, resolves plan permissions, and forwards identity and permission data to downstream services through proxy headers.

Current portal behavior:

- service access is gated through reverse-proxy authorization policies such as `require-expenses`
- plan permissions are resolved in portal middleware
- downstream requests receive `X-User-Id`, `X-User-Email`, `X-User-Roles`, and `X-User-Permissions`

Current expenses behavior:

- the API does not authenticate independently
- it trusts the forwarded headers from the portal
- it parses the forwarded headers locally
- it defines its own local permission constants for the same values the portal seeds and checks

This creates drift risk because both repos currently duplicate:

- header names
- forwarded auth parsing rules
- permission string values

## Goals

- define one source of truth for portal-to-service auth transport
- let child services consume a stable NuGet package instead of re-creating local header contracts
- centralize permission constants used by both portal and downstream services
- keep the package narrow and safe to version independently

## Non-Goals

- moving portal EF, Identity, session, or database models into the package
- introducing ASP.NET Core framework dependencies into the package
- redesigning the auth protocol to use JWTs or mTLS in this change
- replacing service business contracts unrelated to auth transport

## Package Scope

`WiSave.Portal.Contracts` is a boundary-contract package. It represents what the portal sends to downstream services, not how the portal stores or authenticates users internally.

Allowed contents:

- constants for forwarded auth header names
- immutable records/classes for forwarded user context
- pure helper methods for reading and writing header dictionaries
- shared permission constants
- optional validation/parsing result helpers

Disallowed contents:

- `ApplicationUser`, plans, plan-permission persistence types
- cookie, session, or antiforgery types
- auth endpoint request/response DTOs
- gateway configuration or YARP-specific code
- service-specific domain contracts that are unrelated to forwarded auth

## Proposed Package Structure

Suggested namespace layout:

- `WiSave.Portal.Contracts.Identity`
- `WiSave.Portal.Contracts.Authorization`

Suggested files:

- `Identity/PortalHeaderNames.cs`
- `Identity/ForwardedUserContext.cs`
- `Identity/ForwardedUserContextReader.cs`
- `Identity/ForwardedUserContextWriter.cs`
- `Authorization/PortalPermissions.cs`

Optional later additions:

- `Identity/ForwardedUserContextParseResult.cs`
- `Identity/ForwardedUserContextValidation.cs`

## API Shape

The concrete names can change slightly, but the package should look approximately like this:

```csharp
namespace WiSave.Portal.Contracts.Identity;

public static class PortalHeaderNames
{
    public const string UserId = "X-User-Id";
    public const string UserEmail = "X-User-Email";
    public const string UserPermissions = "X-User-Permissions";
    public const string UserRoles = "X-User-Roles";
}

public sealed record ForwardedUserContext(
    string UserId,
    string? Email,
    IReadOnlySet<string> Permissions,
    IReadOnlySet<string> Roles);

public static class ForwardedUserContextReader
{
    public static ForwardedUserContext? Read(IReadOnlyDictionary<string, string[]> headers);
}

public static class ForwardedUserContextWriter
{
    public static IReadOnlyDictionary<string, string[]> Write(ForwardedUserContext context);
}
```

```csharp
namespace WiSave.Portal.Contracts.Authorization;

public static class PortalPermissions
{
    public static class Expenses
    {
        public const string Read = "expenses:read";
        public const string Write = "expenses:write";
        public const string Delete = "expenses:delete";
    }

    public static class Incomes
    {
        public const string Read = "incomes:read";
        public const string Write = "incomes:write";
        public const string Delete = "incomes:delete";
        public const string Import = "incomes:import";
    }

    public static class Stocks
    {
        public const string Read = "stocks:read";
        public const string Write = "stocks:write";
        public const string PortfolioManage = "stocks:portfolio:manage";
        public const string WatchlistManage = "stocks:watchlist:manage";
    }
}
```

## Design Notes

### Pure class library

The package must not depend on ASP.NET Core types such as `HttpContext`, `IHeaderDictionary`, or endpoint metadata. This keeps the package reusable in:

- ASP.NET services
- worker processes
- tests
- future non-web consumers

The integration layer in each service can adapt between framework-specific types and the pure contract helpers.

### Roles

`X-User-Roles` is currently forwarded by the portal but not used by expenses. The contract may still include it because it is already part of the boundary. If the team decides to trim unused data later, roles can be deprecated in the package and removed in a coordinated version bump.

### Missing identity behavior

The contracts package should not decide HTTP status codes. It should only parse and expose context. Each downstream service remains responsible for deciding whether missing or invalid forwarded identity maps to `401`, `403`, or another result.

### Serialization rules

The package should define the wire format explicitly:

- `UserId` and `Email` are forwarded as single header values
- `Permissions` are forwarded as a comma-separated list in `X-User-Permissions`
- `Roles` are forwarded as a comma-separated list in `X-User-Roles`
- permission and role parsing should trim whitespace and use case-insensitive set semantics

This preserves compatibility with the current portal behavior while making the parsing rules owned by one package.

## Integration Plan

### Portal changes

Portal will consume `WiSave.Portal.Contracts` and replace hardcoded header strings with package constants. The user-header transform should construct a `ForwardedUserContext` and serialize it through the shared writer.

Expected portal updates:

- replace raw header name literals with `PortalHeaderNames`
- replace permission string literals in application code and tests with `PortalPermissions`
- keep SQL seed scripts as literal values unless the team introduces SQL generation later

### Expenses changes

Expenses will consume `WiSave.Portal.Contracts` and replace local header parsing with the shared reader.

Expected expenses updates:

- replace local header name literals with `PortalHeaderNames`
- replace local permission constants with `PortalPermissions.Expenses`
- adapt `HeaderCurrentUser` and `PermissionContext` to use a parsed `ForwardedUserContext`

### Migration order

1. Create and publish `WiSave.Portal.Contracts`
2. Update portal to consume the package first
3. Update expenses to consume the package
4. remove now-redundant local constants and parsing helpers from expenses

Portal-first rollout is preferred because the portal owns the boundary definition.

## Testing Strategy

Package tests:

- header constant coverage
- parse valid forwarded context
- parse missing optional values
- parse empty permission/role lists
- serialize and parse round-trip
- case-insensitive permission handling

Portal tests:

- continue verifying spoofed inbound headers are stripped
- verify forwarded header names come from package constants
- verify forwarded permissions match shared constants

Expenses tests:

- verify permission checks still succeed with package-defined permission constants
- verify missing forwarded identity is handled consistently
- verify the shared reader drives both current-user resolution and permission evaluation

Cross-repo validation:

- at minimum, run portal and expenses test suites that cover proxy forwarding and downstream auth
- ideally add a shared contract test fixture later to prevent behavioral drift

## Risks

- versioning discipline becomes important because downstream services depend on a published package
- if the contract package grows beyond auth transport, it will become a dumping ground and lose clarity
- raw trusted headers are still a security boundary assumption; this package reduces drift but does not harden the protocol by itself

## Future Work

The next likely hardening step is replacing raw forwarded headers with a signed short-lived internal token. That is intentionally out of scope for this package introduction. The current proposal focuses on making the existing protocol explicit, shared, and easier to evolve safely.

## Recommendation

Proceed with `WiSave.Portal.Contracts` as a small, pure, portal-owned NuGet package that contains:

- header name constants
- forwarded user context transport types
- pure parsing/serialization helpers
- shared permission constants

Do not place portal internals in the package. Keep it strictly focused on the portal-to-child auth boundary.
