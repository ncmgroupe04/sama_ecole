START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260926001655_AddRegistrationSchoolProfile') THEN
    ALTER TABLE school_registration_requests ADD "CycleProfile" character varying(20) NOT NULL DEFAULT 'Primaire';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260926001655_AddRegistrationSchoolProfile') THEN
    ALTER TABLE school_registration_requests ADD "Ownership" character varying(20) NOT NULL DEFAULT 'Private';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260926001655_AddRegistrationSchoolProfile') THEN
    ALTER TABLE school_registration_requests ADD "SizeTier" character varying(20);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260926001655_AddRegistrationSchoolProfile') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260926001655_AddRegistrationSchoolProfile', '9.0.1');
    END IF;
END $EF$;
COMMIT;

