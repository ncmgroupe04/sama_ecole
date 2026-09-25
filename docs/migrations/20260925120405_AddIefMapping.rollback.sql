START TRANSACTION;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925120405_AddIefMapping') THEN
    DROP POLICY IF EXISTS grade_age_norms_tenant_isolation ON "grade_age_norms";
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925120405_AddIefMapping') THEN
    DROP TABLE grade_age_norms;
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925120405_AddIefMapping') THEN
    ALTER TABLE enrollments DROP COLUMN "IsTransferredIn";
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925120405_AddIefMapping') THEN
    ALTER TABLE enrollments DROP COLUMN "PreviousSchoolName";
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925120405_AddIefMapping') THEN
    DELETE FROM "__EFMigrationsHistory"
    WHERE "MigrationId" = '20260925120405_AddIefMapping';
    END IF;
END $EF$;
COMMIT;

