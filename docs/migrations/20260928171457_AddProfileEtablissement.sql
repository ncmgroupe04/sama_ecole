START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260928171457_AddProfileEtablissement') THEN
    ALTER TABLE school_settings ADD "ProfileEtablissement" character varying(30);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260928171457_AddProfileEtablissement') THEN
    UPDATE school_settings SET "ProfileEtablissement" = 'General'
    WHERE "ProfileEtablissement" IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260928171457_AddProfileEtablissement') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260928171457_AddProfileEtablissement', '9.0.1');
    END IF;
END $EF$;
COMMIT;

