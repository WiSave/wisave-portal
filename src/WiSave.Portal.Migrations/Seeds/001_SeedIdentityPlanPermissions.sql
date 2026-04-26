WITH roles("Id", "Name", "NormalizedName") AS (
    VALUES
        ('role-superadmin', 'superadmin', 'SUPERADMIN'),
        ('role-admin', 'admin', 'ADMIN'),
        ('role-plan-free', 'plan:free', 'PLAN:FREE'),
        ('role-plan-standard', 'plan:standard', 'PLAN:STANDARD'),
        ('role-plan-premium', 'plan:premium', 'PLAN:PREMIUM')
)
INSERT INTO public."AspNetRoles" ("Id", "Name", "NormalizedName", "ConcurrencyStamp")
SELECT roles."Id", roles."Name", roles."NormalizedName", gen_random_uuid()::text
FROM roles
WHERE NOT EXISTS (
    SELECT 1
    FROM public."AspNetRoles" existing
    WHERE existing."NormalizedName" = roles."NormalizedName"
);

WITH role_permissions("NormalizedName", "ClaimValue") AS (
    VALUES
        ('PLAN:FREE', 'incomes:read'),

        ('PLAN:STANDARD', 'incomes:read'),
        ('PLAN:STANDARD', 'incomes:write'),
        ('PLAN:STANDARD', 'stocks:read'),
        ('PLAN:STANDARD', 'expenses:read'),
        ('PLAN:STANDARD', 'expenses:write'),

        ('PLAN:PREMIUM', 'incomes:read'),
        ('PLAN:PREMIUM', 'incomes:write'),
        ('PLAN:PREMIUM', 'incomes:delete'),
        ('PLAN:PREMIUM', 'incomes:import'),
        ('PLAN:PREMIUM', 'stocks:read'),
        ('PLAN:PREMIUM', 'stocks:write'),
        ('PLAN:PREMIUM', 'stocks:portfolio:manage'),
        ('PLAN:PREMIUM', 'stocks:watchlist:manage'),
        ('PLAN:PREMIUM', 'expenses:read'),
        ('PLAN:PREMIUM', 'expenses:write'),
        ('PLAN:PREMIUM', 'expenses:delete')
)
INSERT INTO public."AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue")
SELECT roles."Id", 'permission', role_permissions."ClaimValue"
FROM role_permissions
JOIN public."AspNetRoles" roles
    ON roles."NormalizedName" = role_permissions."NormalizedName"
WHERE NOT EXISTS (
    SELECT 1
    FROM public."AspNetRoleClaims" claims
    WHERE claims."RoleId" = roles."Id"
      AND claims."ClaimType" = 'permission'
      AND claims."ClaimValue" = role_permissions."ClaimValue"
);
