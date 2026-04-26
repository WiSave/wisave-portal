# Identity Plan Permissions Design

## Context

WiSave.Portal currently uses ASP.NET Core Identity for users, passwords, cookies, and broad roles, but uses custom `Plans`, `Permissions`, and `PlanPermissions` tables to resolve plan-based permissions. `PermissionResolutionMiddleware` reads the current user's `PlanId`, loads permissions through custom caches, stores them in `HttpContext.Items["UserPermissions"]`, and `UserHeaderTransform` forwards them to downstream services as `X-User-Permissions`.

For the current requirement, plans are fixed account tiers: free, standard, and premium. A normal user should have exactly one plan. Admin roles remain separate from plans.

## Goal

Simplify plan and permission handling by relying on built-in ASP.NET Core Identity structures as much as possible:

- Identity users remain the account source of truth.
- Identity roles represent plan tiers and admin roles.
- Identity role claims represent permissions.
- The existing gateway contract continues forwarding `X-User-Permissions`.

## Role Model

Use Identity roles for both account plans and administrative roles.

Plan roles:

- `plan:free`
- `plan:standard`
- `plan:premium`

Administrative roles:

- `admin`
- `superadmin`

Plan roles are mutually exclusive for normal users. A user may also have administrative roles. `admin` and `superadmin` continue to grant all permissions.

The existing generic `user` role becomes unnecessary for permission resolution. It can either be retained temporarily for compatibility or removed after confirming no downstream service depends on `X-User-Roles` containing `user`.

## Permission Model

Store permissions as Identity role claims:

- claim type: `permission`
- claim value: permission name, for example `incomes:read`

The stable permission names are the authorization contract. The existing GUID permission IDs are not needed if permissions move into role claims.

Initial permission set:

- `incomes:read`
- `incomes:write`
- `incomes:delete`
- `incomes:import`
- `stocks:read`
- `stocks:write`
- `stocks:portfolio:manage`
- `stocks:watchlist:manage`
- `expenses:read`
- `expenses:write`
- `expenses:delete`

Initial plan mapping:

- `plan:free`: `incomes:read`
- `plan:standard`: `incomes:read`, `incomes:write`, `stocks:read`, `expenses:read`, `expenses:write`
- `plan:premium`: all listed permissions

This mapping is the implementation default. Changes to plan capabilities should be made in the seed migration and reflected in tests.

## Authentication Flow

Registration should accept a selected plan. If no plan is provided, default to `plan:free`.

On registration:

1. Validate the selected plan role is one of the supported plan roles.
2. Create the Identity user.
3. Assign exactly one plan role to the user.
4. Sign the user in.

Login should not change the user's plan. Login authenticates the existing account. Registration or a dedicated plan-change endpoint controls plan assignment. If the product later requires choosing a plan during login, that should be designed as a separate change because it affects billing, auditability, and downgrade behavior.

## Permission Resolution

Replace custom plan permission resolution with Identity role claim resolution.

Request-time behavior:

1. If the user is unauthenticated, continue without permissions.
2. If the user has `admin` or `superadmin`, set permissions to `*`.
3. Otherwise, read the user's roles.
4. Load role claims for those roles.
5. Collect all claim values where claim type is `permission`.
6. Store the resulting set in `HttpContext.Items["UserPermissions"]`.

`UserHeaderTransform` can continue forwarding `X-User-Permissions` without changing the downstream contract.

Caching can be simpler than the current user-plan and plan-permission caches. Role claims are small and mostly static, so the first implementation can resolve them directly through Identity. If needed later, add one cache keyed by role name or role ID for role permission claims.

## Database Changes

New seed migration should ensure:

- plan roles exist
- admin roles exist
- permission claims exist on plan roles
- obsolete or duplicate role claims are not inserted twice

The custom tables can be deprecated:

- `Plans`
- `Permissions`
- `PlanPermissions`
- `AspNetUsers.PlanId`

For a minimal first migration, keep old columns/tables unused to avoid destructive schema changes. A later cleanup migration can drop them after the new flow is verified and no production code reads them.

## Code Changes

Expected implementation scope:

- Update registration DTO and endpoint logic to use plan roles instead of `ApplicationUser.PlanId`.
- Remove runtime dependency on `PortalDbContext.Plans` during registration.
- Replace `UserPlanCache` and `PlanPermissionCache` usage in `PermissionResolutionMiddleware`.
- Add a small role permission resolver service if the middleware should stay thin.
- Keep `UserHeaderTransform` behavior intact.
- Update tests to seed Identity roles and role claims instead of custom plan rows.

## Testing

Targeted tests should cover:

- registering with `free`, `standard`, and `premium` assigns the correct plan role
- invalid plan selection returns `400`
- users receive `X-User-Permissions` based on plan role claims
- spoofed permission headers are stripped and replaced
- admin users receive `*`
- login preserves the assigned plan role

Run targeted auth and gateway tests first, then `dotnet test` if the targeted suite passes.

## Deferred Decisions

No implementation blocker remains for the initial simplification. A future billing/subscription design should decide whether plan roles are changed by users directly, an admin workflow, or an external billing webhook.
