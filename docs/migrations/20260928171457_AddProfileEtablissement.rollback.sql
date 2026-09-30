START TRANSACTION;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260928171457_AddProfileEtablissement') THEN
    ALTER TABLE school_settings DROP COLUMN "ProfileEtablissement";
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260928171457_AddProfileEtablissement') THEN
    DELETE FROM "__EFMigrationsHistory"
    WHERE "MigrationId" = '20260928171457_AddProfileEtablissement';
    END IF;
END $EF$;
COMMIT;

