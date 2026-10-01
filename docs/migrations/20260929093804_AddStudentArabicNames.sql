START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260929093804_AddStudentArabicNames') THEN
    ALTER TABLE students ADD "FullNameAr" character varying(200);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260929093804_AddStudentArabicNames') THEN
    ALTER TABLE students ADD "GuardianNameAr" character varying(200);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260929093804_AddStudentArabicNames') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260929093804_AddStudentArabicNames', '9.0.1');
    END IF;
END $EF$;
COMMIT;

