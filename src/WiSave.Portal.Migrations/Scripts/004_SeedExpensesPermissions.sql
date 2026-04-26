-- Seed expenses permissions
INSERT INTO "Permissions" ("Id", "Name", "Description") VALUES
    ('a3000000-0000-0000-0000-000000000001', 'expenses:read', 'View expenses, funding accounts, credit card accounts, budgets, and categories'),
    ('a3000000-0000-0000-0000-000000000002', 'expenses:write', 'Create and edit expenses, funding accounts, credit card accounts, budgets, and categories'),
    ('a3000000-0000-0000-0000-000000000003', 'expenses:delete', 'Delete expenses, funding accounts, credit card accounts, budgets, and categories')
ON CONFLICT ("Id") DO NOTHING;

-- Free plan: expenses:read only
INSERT INTO "PlanPermissions" ("PlanId", "PermissionId") VALUES
    ('free', 'a3000000-0000-0000-0000-000000000001')
ON CONFLICT DO NOTHING;

-- Standard plan: expenses:read + expenses:write
INSERT INTO "PlanPermissions" ("PlanId", "PermissionId") VALUES
    ('standard', 'a3000000-0000-0000-0000-000000000001'),
    ('standard', 'a3000000-0000-0000-0000-000000000002')
ON CONFLICT DO NOTHING;

-- Premium plan: all expenses permissions
INSERT INTO "PlanPermissions" ("PlanId", "PermissionId") VALUES
    ('premium', 'a3000000-0000-0000-0000-000000000001'),
    ('premium', 'a3000000-0000-0000-0000-000000000002'),
    ('premium', 'a3000000-0000-0000-0000-000000000003')
ON CONFLICT DO NOTHING;
