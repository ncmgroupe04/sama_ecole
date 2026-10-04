START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261004094307_AddUniformsModule') THEN
    ALTER TABLE stock_movements ADD CONSTRAINT "AK_stock_movements_SchoolId_Id" UNIQUE ("SchoolId", "Id");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261004094307_AddUniformsModule') THEN
    ALTER TABLE inventory_items ADD CONSTRAINT "AK_inventory_items_SchoolId_Id" UNIQUE ("SchoolId", "Id");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261004094307_AddUniformsModule') THEN
    ALTER TABLE enrollment_fee_lines ADD CONSTRAINT "AK_enrollment_fee_lines_SchoolId_Id" UNIQUE ("SchoolId", "Id");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261004094307_AddUniformsModule') THEN
    CREATE TABLE uniform_items (
        "Id" uuid NOT NULL,
        "SchoolId" uuid NOT NULL,
        "Name" character varying(100) NOT NULL,
        "Kind" character varying(20) NOT NULL,
        "Gender" character varying(20) NOT NULL,
        "Cycle" character varying(20),
        "FeeCategoryId" uuid,
        "QuantityIncluded" integer NOT NULL,
        "Notes" character varying(300),
        "CreatedAt" timestamp with time zone NOT NULL,
        "CreatedBy" text,
        "UpdatedAt" timestamp with time zone,
        "UpdatedBy" text,
        "IsDeleted" boolean NOT NULL,
        "DeletedAt" timestamp with time zone,
        "DeletedBy" text,
        CONSTRAINT "PK_uniform_items" PRIMARY KEY ("Id"),
        CONSTRAINT "AK_uniform_items_SchoolId_Id" UNIQUE ("SchoolId", "Id"),
        CONSTRAINT "CK_uniform_items_quantity_included" CHECK ("QuantityIncluded" >= 1),
        CONSTRAINT "FK_uniform_items_fee_categories_SchoolId_FeeCategoryId" FOREIGN KEY ("SchoolId", "FeeCategoryId") REFERENCES fee_categories ("SchoolId", "Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_uniform_items_schools_SchoolId" FOREIGN KEY ("SchoolId") REFERENCES schools ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261004094307_AddUniformsModule') THEN
    CREATE TABLE uniform_variants (
        "Id" uuid NOT NULL,
        "SchoolId" uuid NOT NULL,
        "UniformItemId" uuid NOT NULL,
        "SizeLabel" character varying(30) NOT NULL,
        "SortOrder" integer NOT NULL,
        "InventoryItemId" uuid NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "CreatedBy" text,
        "UpdatedAt" timestamp with time zone,
        "UpdatedBy" text,
        "IsDeleted" boolean NOT NULL,
        "DeletedAt" timestamp with time zone,
        "DeletedBy" text,
        CONSTRAINT "PK_uniform_variants" PRIMARY KEY ("Id"),
        CONSTRAINT "AK_uniform_variants_SchoolId_Id" UNIQUE ("SchoolId", "Id"),
        CONSTRAINT "FK_uniform_variants_inventory_items_SchoolId_InventoryItemId" FOREIGN KEY ("SchoolId", "InventoryItemId") REFERENCES inventory_items ("SchoolId", "Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_uniform_variants_schools_SchoolId" FOREIGN KEY ("SchoolId") REFERENCES schools ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_uniform_variants_uniform_items_SchoolId_UniformItemId" FOREIGN KEY ("SchoolId", "UniformItemId") REFERENCES uniform_items ("SchoolId", "Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261004094307_AddUniformsModule') THEN
    CREATE TABLE student_uniforms (
        "Id" uuid NOT NULL,
        "SchoolId" uuid NOT NULL,
        "StudentId" uuid NOT NULL,
        "EnrollmentId" uuid NOT NULL,
        "SchoolYearId" uuid NOT NULL,
        "UniformItemId" uuid NOT NULL,
        "UniformVariantId" uuid,
        "Quantity" integer NOT NULL,
        "Status" character varying(20) NOT NULL,
        "DeliveredOn" date,
        "DeliveredByUserId" uuid,
        "DeliveryStockMovementId" uuid,
        "ReturnStockMovementId" uuid,
        "BillingStatus" character varying(20) NOT NULL,
        "FeeLineId" uuid,
        "NotBilledReason" character varying(200),
        "ReturnReason" character varying(200),
        "ItemLabel" character varying(100),
        "SizeLabel" character varying(30),
        "StudentLabel" character varying(200),
        "CreatedAt" timestamp with time zone NOT NULL,
        "CreatedBy" text,
        "UpdatedAt" timestamp with time zone,
        "UpdatedBy" text,
        "IsDeleted" boolean NOT NULL,
        "DeletedAt" timestamp with time zone,
        "DeletedBy" text,
        CONSTRAINT "PK_student_uniforms" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_student_uniforms_billed_has_fee_line" CHECK ("BillingStatus" <> 'Billed' OR "FeeLineId" IS NOT NULL),
        CONSTRAINT "CK_student_uniforms_delivered_has_movement" CHECK ("Status" NOT IN ('Delivered', 'Returned') OR ("DeliveredOn" IS NOT NULL AND "DeliveredByUserId" IS NOT NULL AND "DeliveryStockMovementId" IS NOT NULL AND "UniformVariantId" IS NOT NULL)),
        CONSTRAINT "CK_student_uniforms_not_billed_has_reason" CHECK (NOT ("Status" IN ('Delivered', 'Returned') AND "BillingStatus" = 'NotBilled') OR "NotBilledReason" IS NOT NULL),
        CONSTRAINT "CK_student_uniforms_quantity" CHECK ("Quantity" > 0),
        CONSTRAINT "CK_student_uniforms_returned_has_movement" CHECK ("Status" <> 'Returned' OR ("ReturnStockMovementId" IS NOT NULL AND "ReturnReason" IS NOT NULL)),
        CONSTRAINT "FK_student_uniforms_enrollment_fee_lines_SchoolId_FeeLineId" FOREIGN KEY ("SchoolId", "FeeLineId") REFERENCES enrollment_fee_lines ("SchoolId", "Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_student_uniforms_enrollments_SchoolId_EnrollmentId" FOREIGN KEY ("SchoolId", "EnrollmentId") REFERENCES enrollments ("SchoolId", "Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_student_uniforms_school_years_SchoolId_SchoolYearId" FOREIGN KEY ("SchoolId", "SchoolYearId") REFERENCES school_years ("SchoolId", "Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_student_uniforms_schools_SchoolId" FOREIGN KEY ("SchoolId") REFERENCES schools ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_student_uniforms_stock_movements_SchoolId_DeliveryStockMove~" FOREIGN KEY ("SchoolId", "DeliveryStockMovementId") REFERENCES stock_movements ("SchoolId", "Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_student_uniforms_stock_movements_SchoolId_ReturnStockMoveme~" FOREIGN KEY ("SchoolId", "ReturnStockMovementId") REFERENCES stock_movements ("SchoolId", "Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_student_uniforms_students_SchoolId_StudentId" FOREIGN KEY ("SchoolId", "StudentId") REFERENCES students ("SchoolId", "Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_student_uniforms_uniform_items_SchoolId_UniformItemId" FOREIGN KEY ("SchoolId", "UniformItemId") REFERENCES uniform_items ("SchoolId", "Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_student_uniforms_uniform_variants_SchoolId_UniformVariantId" FOREIGN KEY ("SchoolId", "UniformVariantId") REFERENCES uniform_variants ("SchoolId", "Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261004094307_AddUniformsModule') THEN
    CREATE INDEX "IX_student_uniforms_SchoolId_DeliveryStockMovementId" ON student_uniforms ("SchoolId", "DeliveryStockMovementId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261004094307_AddUniformsModule') THEN
    CREATE INDEX "IX_student_uniforms_SchoolId_FeeLineId" ON student_uniforms ("SchoolId", "FeeLineId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261004094307_AddUniformsModule') THEN
    CREATE INDEX "IX_student_uniforms_SchoolId_ReturnStockMovementId" ON student_uniforms ("SchoolId", "ReturnStockMovementId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261004094307_AddUniformsModule') THEN
    CREATE INDEX "IX_student_uniforms_SchoolId_SchoolYearId_Status" ON student_uniforms ("SchoolId", "SchoolYearId", "Status");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261004094307_AddUniformsModule') THEN
    CREATE INDEX "IX_student_uniforms_SchoolId_StudentId" ON student_uniforms ("SchoolId", "StudentId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261004094307_AddUniformsModule') THEN
    CREATE INDEX "IX_student_uniforms_SchoolId_UniformItemId" ON student_uniforms ("SchoolId", "UniformItemId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261004094307_AddUniformsModule') THEN
    CREATE INDEX "IX_student_uniforms_SchoolId_UniformVariantId_Status" ON student_uniforms ("SchoolId", "UniformVariantId", "Status");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261004094307_AddUniformsModule') THEN
    CREATE UNIQUE INDEX "UX_student_uniforms_active" ON student_uniforms ("SchoolId", "EnrollmentId", "UniformItemId") WHERE "Status" IN ('Requested', 'Delivered') AND "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261004094307_AddUniformsModule') THEN
    CREATE INDEX "IX_uniform_items_SchoolId_FeeCategoryId" ON uniform_items ("SchoolId", "FeeCategoryId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261004094307_AddUniformsModule') THEN
    CREATE UNIQUE INDEX "UX_uniform_items_name_gender" ON uniform_items ("SchoolId", "Name", "Gender") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261004094307_AddUniformsModule') THEN
    CREATE UNIQUE INDEX "UX_uniform_variants_inventory_item" ON uniform_variants ("SchoolId", "InventoryItemId") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261004094307_AddUniformsModule') THEN
    CREATE UNIQUE INDEX "UX_uniform_variants_item_size" ON uniform_variants ("SchoolId", "UniformItemId", "SizeLabel") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261004094307_AddUniformsModule') THEN
    ALTER TABLE "uniform_items" ENABLE ROW LEVEL SECURITY;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261004094307_AddUniformsModule') THEN
    CREATE POLICY uniform_items_tenant_isolation ON "uniform_items"
        USING ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid)
        WITH CHECK ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261004094307_AddUniformsModule') THEN
    DO $inner$
    BEGIN
        IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'sama_ecole_app') THEN
            EXECUTE 'GRANT SELECT, INSERT, UPDATE ON "uniform_items" TO sama_ecole_app';
        ELSE
            RAISE WARNING 'Rôle % absent : l''application ne pourra pas lire la table uniform_items.', 'sama_ecole_app';
        END IF;
    END
    $inner$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261004094307_AddUniformsModule') THEN
    ALTER TABLE "uniform_variants" ENABLE ROW LEVEL SECURITY;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261004094307_AddUniformsModule') THEN
    CREATE POLICY uniform_variants_tenant_isolation ON "uniform_variants"
        USING ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid)
        WITH CHECK ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261004094307_AddUniformsModule') THEN
    DO $inner$
    BEGIN
        IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'sama_ecole_app') THEN
            EXECUTE 'GRANT SELECT, INSERT, UPDATE ON "uniform_variants" TO sama_ecole_app';
        ELSE
            RAISE WARNING 'Rôle % absent : l''application ne pourra pas lire la table uniform_variants.', 'sama_ecole_app';
        END IF;
    END
    $inner$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261004094307_AddUniformsModule') THEN
    ALTER TABLE "student_uniforms" ENABLE ROW LEVEL SECURITY;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261004094307_AddUniformsModule') THEN
    CREATE POLICY student_uniforms_tenant_isolation ON "student_uniforms"
        USING ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid)
        WITH CHECK ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261004094307_AddUniformsModule') THEN
    DO $inner$
    BEGIN
        IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'sama_ecole_app') THEN
            EXECUTE 'GRANT SELECT, INSERT, UPDATE ON "student_uniforms" TO sama_ecole_app';
        ELSE
            RAISE WARNING 'Rôle % absent : l''application ne pourra pas lire la table student_uniforms.', 'sama_ecole_app';
        END IF;
    END
    $inner$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261004094307_AddUniformsModule') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261004094307_AddUniformsModule', '9.0.1');
    END IF;
END $EF$;
COMMIT;

