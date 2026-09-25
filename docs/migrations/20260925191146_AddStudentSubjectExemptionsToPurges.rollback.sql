START TRANSACTION;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925191146_AddStudentSubjectExemptionsToPurges') THEN
    DO $patch$
    DECLARE
        v_def text;
    BEGIN
        v_def := pg_get_functiondef('reset_school_data(uuid)'::regprocedure);

        IF position('student_subject_exemptions' IN v_def) = 0 THEN
            RETURN;
        END IF;

        EXECUTE replace(v_def, $w$['student_subject_exemptions', 'Dispenses de matières'],
                            ['enrollments',$w$, $o$['enrollments',$o$);
    END
    $patch$;
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925191146_AddStudentSubjectExemptionsToPurges') THEN
    DO $patch$
    DECLARE
        v_def text;
    BEGIN
        v_def := pg_get_functiondef('delete_school_year(uuid, uuid)'::regprocedure);

        IF position('student_subject_exemptions' IN v_def) = 0 THEN
            RETURN;
        END IF;

        EXECUTE replace(v_def, $w$DELETE FROM student_subject_exemptions
        WHERE "SchoolYearId" = p_school_year_id AND "SchoolId" = p_school_id;
        GET DIAGNOSTICS v_deleted = ROW_COUNT;
        label := 'Dispenses de matières'; rows_deleted := v_deleted; RETURN NEXT;

        DELETE FROM enrollments$w$, $o$DELETE FROM enrollments$o$);
    END
    $patch$;
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925191146_AddStudentSubjectExemptionsToPurges') THEN
    DELETE FROM "__EFMigrationsHistory"
    WHERE "MigrationId" = '20260925191146_AddStudentSubjectExemptionsToPurges';
    END IF;
END $EF$;
COMMIT;

