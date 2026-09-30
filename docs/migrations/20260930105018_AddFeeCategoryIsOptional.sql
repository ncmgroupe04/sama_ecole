START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260930105018_AddFeeCategoryIsOptional') THEN
    ALTER TABLE fee_categories ADD "IsOptional" boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260930105018_AddFeeCategoryIsOptional') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260930105018_AddFeeCategoryIsOptional', '9.0.1');
    END IF;
END $EF$;
COMMIT;

