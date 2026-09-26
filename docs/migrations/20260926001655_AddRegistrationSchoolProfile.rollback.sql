START TRANSACTION;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260926001655_AddRegistrationSchoolProfile') THEN
    ALTER TABLE school_registration_requests DROP COLUMN "CycleProfile";
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260926001655_AddRegistrationSchoolProfile') THEN
    ALTER TABLE school_registration_requests DROP COLUMN "Ownership";
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260926001655_AddRegistrationSchoolProfile') THEN
    ALTER TABLE school_registration_requests DROP COLUMN "SizeTier";
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260926001655_AddRegistrationSchoolProfile') THEN
    DELETE FROM "__EFMigrationsHistory"
    WHERE "MigrationId" = '20260926001655_AddRegistrationSchoolProfile';
    END IF;
END $EF$;
COMMIT;

