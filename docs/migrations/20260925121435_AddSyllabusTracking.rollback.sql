START TRANSACTION;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925121435_AddSyllabusTracking') THEN
    DO $patch$
    DECLARE
        v_def text;
    BEGIN
        v_def := pg_get_functiondef('reset_school_data(uuid)'::regprocedure);
        EXECUTE replace(v_def, '[''class_journal_entry_units'', ''Chapitres cochés au cahier de texte''],' || chr(10) || '                        ', '');
    END
    $patch$;
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925121435_AddSyllabusTracking') THEN
    DO $patch$
    DECLARE
        v_def text;
    BEGIN
        v_def := pg_get_functiondef('reset_school_data(uuid)'::regprocedure);
        EXECUTE replace(v_def, '[''syllabus_units'', ''Référentiel des programmes''],' || chr(10) || '                        ', '');
    END
    $patch$;
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925121435_AddSyllabusTracking') THEN
    DROP POLICY IF EXISTS syllabus_units_tenant_isolation ON "syllabus_units";
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925121435_AddSyllabusTracking') THEN
    DROP POLICY IF EXISTS class_journal_entry_units_tenant_isolation ON "class_journal_entry_units";
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925121435_AddSyllabusTracking') THEN
    DROP TABLE class_journal_entry_units;
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925121435_AddSyllabusTracking') THEN
    DROP TABLE syllabus_units;
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925121435_AddSyllabusTracking') THEN
    ALTER TABLE class_journal_entries DROP CONSTRAINT "AK_class_journal_entries_SchoolId_Id";
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925121435_AddSyllabusTracking') THEN
    DELETE FROM "__EFMigrationsHistory"
    WHERE "MigrationId" = '20260925121435_AddSyllabusTracking';
    END IF;
END $EF$;
COMMIT;

