-- Seed plans
INSERT INTO "Plans" ("Id", "Name", "IsActive") VALUES
    ('free', 'Free', true),
    ('standard', 'Standard', true),
    ('premium', 'Premium', true)
ON CONFLICT ("Id") DO NOTHING;

-- Seed permissions
INSERT INTO "Permissions" ("Id", "Name", "Description") VALUES
    ('a1000000-0000-0000-0000-000000000001', 'incomes:read', 'View incomes'),
    ('a1000000-0000-0000-0000-000000000002', 'incomes:write', 'Create and edit incomes'),
    ('a1000000-0000-0000-0000-000000000003', 'incomes:delete', 'Delete incomes'),
    ('a1000000-0000-0000-0000-000000000004', 'incomes:import', 'Bulk import incomes'),
    ('a2000000-0000-0000-0000-000000000001', 'stocks:read', 'View stock data'),
    ('a2000000-0000-0000-0000-000000000002', 'stocks:write', 'Create and edit stock entries'),
    ('a2000000-0000-0000-0000-000000000003', 'stocks:portfolio:manage', 'Manage stock portfolios'),
    ('a2000000-0000-0000-0000-000000000004', 'stocks:watchlist:manage', 'Manage stock watchlists')
ON CONFLICT ("Id") DO NOTHING;

-- Free: incomes:read
INSERT INTO "PlanPermissions" ("PlanId", "PermissionId") VALUES
    ('free', 'a1000000-0000-0000-0000-000000000001')
ON CONFLICT DO NOTHING;

-- Standard: incomes:read, incomes:write, stocks:read
INSERT INTO "PlanPermissions" ("PlanId", "PermissionId") VALUES
    ('standard', 'a1000000-0000-0000-0000-000000000001'),
    ('standard', 'a1000000-0000-0000-0000-000000000002'),
    ('standard', 'a2000000-0000-0000-0000-000000000001')
ON CONFLICT DO NOTHING;

-- Premium: all permissions
INSERT INTO "PlanPermissions" ("PlanId", "PermissionId") VALUES
    ('premium', 'a1000000-0000-0000-0000-000000000001'),
    ('premium', 'a1000000-0000-0000-0000-000000000002'),
    ('premium', 'a1000000-0000-0000-0000-000000000003'),
    ('premium', 'a1000000-0000-0000-0000-000000000004'),
    ('premium', 'a2000000-0000-0000-0000-000000000001'),
    ('premium', 'a2000000-0000-0000-0000-000000000002'),
    ('premium', 'a2000000-0000-0000-0000-000000000003'),
    ('premium', 'a2000000-0000-0000-0000-000000000004')
ON CONFLICT DO NOTHING;

-- Seed roles
INSERT INTO "AspNetRoles" ("Id", "Name", "NormalizedName", "ConcurrencyStamp") VALUES
    ('role-superadmin', 'superadmin', 'SUPERADMIN', gen_random_uuid()::text),
    ('role-admin', 'admin', 'ADMIN', gen_random_uuid()::text),
    ('role-user', 'user', 'USER', gen_random_uuid()::text)
ON CONFLICT ("Id") DO NOTHING;
