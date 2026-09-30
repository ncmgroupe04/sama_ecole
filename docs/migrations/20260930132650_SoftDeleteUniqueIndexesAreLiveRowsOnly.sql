START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260930132650_SoftDeleteUniqueIndexesAreLiveRowsOnly') THEN
    DROP INDEX "IX_user_schools_UserId_SchoolId_IsDeleted";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260930132650_SoftDeleteUniqueIndexesAreLiveRowsOnly') THEN
    DROP INDEX "IX_terms_SchoolId_SchoolYearId_Order_IsDeleted";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260930132650_SoftDeleteUniqueIndexesAreLiveRowsOnly') THEN
    DROP INDEX "IX_school_years_SchoolId_Label_IsDeleted";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260930132650_SoftDeleteUniqueIndexesAreLiveRowsOnly') THEN
    DROP INDEX "IX_rooms_SchoolId_BuildingId_Name_IsDeleted";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260930132650_SoftDeleteUniqueIndexesAreLiveRowsOnly') THEN
    DROP INDEX "IX_mentions_SchoolId_Label_IsDeleted";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260930132650_SoftDeleteUniqueIndexesAreLiveRowsOnly') THEN
    DROP INDEX "IX_inventory_categories_SchoolId_Name_IsDeleted";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260930132650_SoftDeleteUniqueIndexesAreLiveRowsOnly') THEN
    DROP INDEX "IX_fee_categories_SchoolId_Name_IsDeleted";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260930132650_SoftDeleteUniqueIndexesAreLiveRowsOnly') THEN
    DROP INDEX "IX_classrooms_SchoolId_Name_IsDeleted";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260930132650_SoftDeleteUniqueIndexesAreLiveRowsOnly') THEN
    DROP INDEX "IX_class_fees_SchoolId_FeeCategoryId_ClassroomId_IsDeleted";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260930132650_SoftDeleteUniqueIndexesAreLiveRowsOnly') THEN
    DROP INDEX "IX_buildings_SchoolId_Name_IsDeleted";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260930132650_SoftDeleteUniqueIndexesAreLiveRowsOnly') THEN
    CREATE UNIQUE INDEX "IX_user_schools_UserId_SchoolId" ON user_schools ("UserId", "SchoolId") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260930132650_SoftDeleteUniqueIndexesAreLiveRowsOnly') THEN
    CREATE UNIQUE INDEX "IX_terms_SchoolId_SchoolYearId_Order" ON terms ("SchoolId", "SchoolYearId", "Order") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260930132650_SoftDeleteUniqueIndexesAreLiveRowsOnly') THEN
    CREATE UNIQUE INDEX "IX_school_years_SchoolId_Label" ON school_years ("SchoolId", "Label") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260930132650_SoftDeleteUniqueIndexesAreLiveRowsOnly') THEN
    CREATE UNIQUE INDEX "IX_rooms_SchoolId_BuildingId_Name" ON rooms ("SchoolId", "BuildingId", "Name") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260930132650_SoftDeleteUniqueIndexesAreLiveRowsOnly') THEN
    CREATE UNIQUE INDEX "IX_mentions_SchoolId_Label" ON mentions ("SchoolId", "Label") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260930132650_SoftDeleteUniqueIndexesAreLiveRowsOnly') THEN
    CREATE UNIQUE INDEX "IX_inventory_categories_SchoolId_Name" ON inventory_categories ("SchoolId", "Name") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260930132650_SoftDeleteUniqueIndexesAreLiveRowsOnly') THEN
    CREATE UNIQUE INDEX "IX_fee_categories_SchoolId_Name" ON fee_categories ("SchoolId", "Name") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260930132650_SoftDeleteUniqueIndexesAreLiveRowsOnly') THEN
    CREATE UNIQUE INDEX "IX_classrooms_SchoolId_Name" ON classrooms ("SchoolId", "Name") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260930132650_SoftDeleteUniqueIndexesAreLiveRowsOnly') THEN
    CREATE UNIQUE INDEX "IX_class_fees_SchoolId_FeeCategoryId_ClassroomId" ON class_fees ("SchoolId", "FeeCategoryId", "ClassroomId") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260930132650_SoftDeleteUniqueIndexesAreLiveRowsOnly') THEN
    CREATE UNIQUE INDEX "IX_buildings_SchoolId_Name" ON buildings ("SchoolId", "Name") WHERE "IsDeleted" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260930132650_SoftDeleteUniqueIndexesAreLiveRowsOnly') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260930132650_SoftDeleteUniqueIndexesAreLiveRowsOnly', '9.0.1');
    END IF;
END $EF$;
COMMIT;

