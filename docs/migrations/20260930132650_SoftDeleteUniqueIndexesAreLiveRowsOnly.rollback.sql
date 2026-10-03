START TRANSACTION;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260930132650_SoftDeleteUniqueIndexesAreLiveRowsOnly') THEN
    DROP INDEX "IX_user_schools_UserId_SchoolId";
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260930132650_SoftDeleteUniqueIndexesAreLiveRowsOnly') THEN
    DROP INDEX "IX_terms_SchoolId_SchoolYearId_Order";
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260930132650_SoftDeleteUniqueIndexesAreLiveRowsOnly') THEN
    DROP INDEX "IX_school_years_SchoolId_Label";
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260930132650_SoftDeleteUniqueIndexesAreLiveRowsOnly') THEN
    DROP INDEX "IX_rooms_SchoolId_BuildingId_Name";
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260930132650_SoftDeleteUniqueIndexesAreLiveRowsOnly') THEN
    DROP INDEX "IX_mentions_SchoolId_Label";
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260930132650_SoftDeleteUniqueIndexesAreLiveRowsOnly') THEN
    DROP INDEX "IX_inventory_categories_SchoolId_Name";
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260930132650_SoftDeleteUniqueIndexesAreLiveRowsOnly') THEN
    DROP INDEX "IX_fee_categories_SchoolId_Name";
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260930132650_SoftDeleteUniqueIndexesAreLiveRowsOnly') THEN
    DROP INDEX "IX_classrooms_SchoolId_Name";
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260930132650_SoftDeleteUniqueIndexesAreLiveRowsOnly') THEN
    DROP INDEX "IX_class_fees_SchoolId_FeeCategoryId_ClassroomId";
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260930132650_SoftDeleteUniqueIndexesAreLiveRowsOnly') THEN
    DROP INDEX "IX_buildings_SchoolId_Name";
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260930132650_SoftDeleteUniqueIndexesAreLiveRowsOnly') THEN
    CREATE UNIQUE INDEX "IX_user_schools_UserId_SchoolId_IsDeleted" ON user_schools ("UserId", "SchoolId", "IsDeleted");
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260930132650_SoftDeleteUniqueIndexesAreLiveRowsOnly') THEN
    CREATE UNIQUE INDEX "IX_terms_SchoolId_SchoolYearId_Order_IsDeleted" ON terms ("SchoolId", "SchoolYearId", "Order", "IsDeleted");
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260930132650_SoftDeleteUniqueIndexesAreLiveRowsOnly') THEN
    CREATE UNIQUE INDEX "IX_school_years_SchoolId_Label_IsDeleted" ON school_years ("SchoolId", "Label", "IsDeleted");
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260930132650_SoftDeleteUniqueIndexesAreLiveRowsOnly') THEN
    CREATE UNIQUE INDEX "IX_rooms_SchoolId_BuildingId_Name_IsDeleted" ON rooms ("SchoolId", "BuildingId", "Name", "IsDeleted");
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260930132650_SoftDeleteUniqueIndexesAreLiveRowsOnly') THEN
    CREATE UNIQUE INDEX "IX_mentions_SchoolId_Label_IsDeleted" ON mentions ("SchoolId", "Label", "IsDeleted");
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260930132650_SoftDeleteUniqueIndexesAreLiveRowsOnly') THEN
    CREATE UNIQUE INDEX "IX_inventory_categories_SchoolId_Name_IsDeleted" ON inventory_categories ("SchoolId", "Name", "IsDeleted");
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260930132650_SoftDeleteUniqueIndexesAreLiveRowsOnly') THEN
    CREATE UNIQUE INDEX "IX_fee_categories_SchoolId_Name_IsDeleted" ON fee_categories ("SchoolId", "Name", "IsDeleted");
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260930132650_SoftDeleteUniqueIndexesAreLiveRowsOnly') THEN
    CREATE UNIQUE INDEX "IX_classrooms_SchoolId_Name_IsDeleted" ON classrooms ("SchoolId", "Name", "IsDeleted");
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260930132650_SoftDeleteUniqueIndexesAreLiveRowsOnly') THEN
    CREATE UNIQUE INDEX "IX_class_fees_SchoolId_FeeCategoryId_ClassroomId_IsDeleted" ON class_fees ("SchoolId", "FeeCategoryId", "ClassroomId", "IsDeleted");
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260930132650_SoftDeleteUniqueIndexesAreLiveRowsOnly') THEN
    CREATE UNIQUE INDEX "IX_buildings_SchoolId_Name_IsDeleted" ON buildings ("SchoolId", "Name", "IsDeleted");
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260930132650_SoftDeleteUniqueIndexesAreLiveRowsOnly') THEN
    DELETE FROM "__EFMigrationsHistory"
    WHERE "MigrationId" = '20260930132650_SoftDeleteUniqueIndexesAreLiveRowsOnly';
    END IF;
END $EF$;
COMMIT;

