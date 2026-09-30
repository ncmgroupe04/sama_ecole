START TRANSACTION;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260929093804_AddStudentArabicNames') THEN
    ALTER TABLE students DROP COLUMN "FullNameAr";
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260929093804_AddStudentArabicNames') THEN
    ALTER TABLE students DROP COLUMN "GuardianNameAr";
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260929093804_AddStudentArabicNames') THEN
    DELETE FROM "__EFMigrationsHistory"
    WHERE "MigrationId" = '20260929093804_AddStudentArabicNames';
    END IF;
END $EF$;
COMMIT;

