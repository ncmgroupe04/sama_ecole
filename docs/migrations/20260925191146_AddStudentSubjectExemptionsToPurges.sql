START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925191146_AddStudentSubjectExemptionsToPurges') THEN
    DO $patch$
    DECLARE
        v_def    text;
        v_anchor text := $a$['enrollments',$a$;
        v_ins    text := $i$['student_subject_exemptions', 'Dispenses de matières'],
                            $i$;
    BEGIN
        v_def := pg_get_functiondef('reset_school_data(uuid)'::regprocedure);

        IF position('student_subject_exemptions' IN v_def) > 0 THEN
            RETURN;
        END IF;

        IF array_length(string_to_array(v_def, v_anchor), 1) <> 2 THEN
            RAISE EXCEPTION 'reset_school_data(uuid) : ancre % introuvable ou ambiguë — la fonction a changé de forme, corriger cette migration.', v_anchor;
        END IF;

        EXECUTE replace(v_def, v_anchor, v_ins || v_anchor);
    END
    $patch$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925191146_AddStudentSubjectExemptionsToPurges') THEN
    DO $patch$
    DECLARE
        v_def    text;
        v_anchor text := $a$DELETE FROM enrollments$a$;
        v_ins    text := $i$DELETE FROM student_subject_exemptions
        WHERE "SchoolYearId" = p_school_year_id AND "SchoolId" = p_school_id;
        GET DIAGNOSTICS v_deleted = ROW_COUNT;
        label := 'Dispenses de matières'; rows_deleted := v_deleted; RETURN NEXT;

        $i$;
    BEGIN
        v_def := pg_get_functiondef('delete_school_year(uuid, uuid)'::regprocedure);

        IF position('student_subject_exemptions' IN v_def) > 0 THEN
            RETURN;
        END IF;

        IF array_length(string_to_array(v_def, v_anchor), 1) <> 2 THEN
            RAISE EXCEPTION 'delete_school_year(uuid, uuid) : ancre % introuvable ou ambiguë — la fonction a changé de forme, corriger cette migration.', v_anchor;
        END IF;

        EXECUTE replace(v_def, v_anchor, v_ins || v_anchor);
    END
    $patch$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925191146_AddStudentSubjectExemptionsToPurges') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260925191146_AddStudentSubjectExemptionsToPurges', '9.0.1');
    END IF;
END $EF$;
COMMIT;

