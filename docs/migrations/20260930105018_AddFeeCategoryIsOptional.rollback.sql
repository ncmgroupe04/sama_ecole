START TRANSACTION;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260930105018_AddFeeCategoryIsOptional') THEN
    ALTER TABLE fee_categories DROP COLUMN "IsOptional";
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260930105018_AddFeeCategoryIsOptional') THEN
    DELETE FROM "__EFMigrationsHistory"
    WHERE "MigrationId" = '20260930105018_AddFeeCategoryIsOptional';
    END IF;
END $EF$;
COMMIT;
