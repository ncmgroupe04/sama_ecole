START TRANSACTION;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925191029_AddStudentSubjectExemptions') THEN
    DROP POLICY IF EXISTS student_subject_exemptions_tenant_isolation ON "student_subject_exemptions";
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925191029_AddStudentSubjectExemptions') THEN
    DROP TABLE student_subject_exemptions;
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925191029_AddStudentSubjectExemptions') THEN
    DELETE FROM "__EFMigrationsHistory"
    WHERE "MigrationId" = '20260925191029_AddStudentSubjectExemptions';
    END IF;
END $EF$;
COMMIT;

