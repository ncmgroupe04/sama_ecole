START TRANSACTION;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925100215_AddClassSubjectsAndOptions') THEN
    DO $patch$
    DECLARE
        v_def text;
    BEGIN
        v_def := pg_get_functiondef('reset_school_data(uuid)'::regprocedure);
        EXECUTE replace(v_def, '[''class_subjects'', ''Matières des classes''],' || chr(10) || '                        ', '');
    END
    $patch$;
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925100215_AddClassSubjectsAndOptions') THEN
    DO $patch$
    DECLARE
        v_def text;
    BEGIN
        v_def := pg_get_functiondef('reset_school_data(uuid)'::regprocedure);
        EXECUTE replace(v_def, '[''student_subject_enrollments'', ''Options choisies par les élèves''],' || chr(10) || '                        ', '');
    END
    $patch$;
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925100215_AddClassSubjectsAndOptions') THEN
    DO $patch$
    DECLARE
        v_def  text;
        v_step text := $step$DELETE FROM student_subject_enrollments
            WHERE "SchoolYearId" = p_school_year_id AND "SchoolId" = p_school_id;
            GET DIAGNOSTICS v_deleted = ROW_COUNT;
            label := 'Options choisies par les élèves'; rows_deleted := v_deleted; RETURN NEXT;

            $step$;
    BEGIN
        v_def := pg_get_functiondef('delete_school_year(uuid, uuid)'::regprocedure);
        EXECUTE replace(v_def, v_step, '');
    END
    $patch$;
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925100215_AddClassSubjectsAndOptions') THEN
    DROP POLICY IF EXISTS class_subjects_tenant_isolation ON "class_subjects";
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925100215_AddClassSubjectsAndOptions') THEN
    DROP POLICY IF EXISTS student_subject_enrollments_tenant_isolation ON "student_subject_enrollments";
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925100215_AddClassSubjectsAndOptions') THEN
    DROP TABLE student_subject_enrollments;
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925100215_AddClassSubjectsAndOptions') THEN
    DROP TABLE class_subjects;
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925100215_AddClassSubjectsAndOptions') THEN
    DELETE FROM "__EFMigrationsHistory"
    WHERE "MigrationId" = '20260925100215_AddClassSubjectsAndOptions';
    END IF;
END $EF$;
COMMIT;

