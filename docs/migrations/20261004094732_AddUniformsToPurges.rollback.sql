START TRANSACTION;
DO $patch$
DECLARE
    v_def text;
BEGIN
    v_def := pg_get_functiondef('reset_school_data(uuid)'::regprocedure);

    IF position('student_uniforms' IN v_def) = 0 THEN
        RETURN;
    END IF;

    EXECUTE replace(v_def, $w$['student_uniforms', 'Dotations de tenues'],
                        ['uniform_variants', 'Tailles de tenues'],
                        ['uniform_items', 'Modèles de tenues'],
                        ['enrollment_fee_lines',$w$, $o$['enrollment_fee_lines',$o$);
END
$patch$;

DO $patch$
DECLARE
    v_def text;
BEGIN
    v_def := pg_get_functiondef('delete_school_year(uuid, uuid)'::regprocedure);

    IF position('student_uniforms' IN v_def) = 0 THEN
        RETURN;
    END IF;

    EXECUTE replace(v_def, $w$DELETE FROM student_uniforms
    WHERE "SchoolYearId" = p_school_year_id AND "SchoolId" = p_school_id;
    GET DIAGNOSTICS v_deleted = ROW_COUNT;
    label := 'Dotations de tenues'; rows_deleted := v_deleted; RETURN NEXT;

    DELETE FROM enrollment_fee_lines$w$, $o$DELETE FROM enrollment_fee_lines$o$);
END
$patch$;

DELETE FROM "__EFMigrationsHistory"
WHERE "MigrationId" = '20261004094732_AddUniformsToPurges';

COMMIT;

