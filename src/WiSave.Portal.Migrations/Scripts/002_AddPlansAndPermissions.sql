-- Plans table
CREATE TABLE IF NOT EXISTS "Plans" (
    "Id" character varying(50) NOT NULL,
    "Name" character varying(100) NOT NULL,
    "IsActive" boolean NOT NULL DEFAULT true,
    CONSTRAINT "PK_Plans" PRIMARY KEY ("Id")
);

-- Permissions table
CREATE TABLE IF NOT EXISTS "Permissions" (
    "Id" uuid NOT NULL,
    "Name" character varying(100) NOT NULL,
    "Description" text,
    CONSTRAINT "PK_Permissions" PRIMARY KEY ("Id")
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_Permissions_Name" ON "Permissions" ("Name");

-- PlanPermissions join table
CREATE TABLE IF NOT EXISTS "PlanPermissions" (
    "PlanId" character varying(50) NOT NULL,
    "PermissionId" uuid NOT NULL,
    CONSTRAINT "PK_PlanPermissions" PRIMARY KEY ("PlanId", "PermissionId"),
    CONSTRAINT "FK_PlanPermissions_Plans_PlanId" FOREIGN KEY ("PlanId") REFERENCES "Plans" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_PlanPermissions_Permissions_PermissionId" FOREIGN KEY ("PermissionId") REFERENCES "Permissions" ("Id") ON DELETE CASCADE
);

-- Add PlanId column to AspNetUsers
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'AspNetUsers' AND column_name = 'PlanId') THEN
        ALTER TABLE "AspNetUsers" ADD COLUMN "PlanId" character varying(50) NOT NULL DEFAULT 'free';
    END IF;
END $$;
