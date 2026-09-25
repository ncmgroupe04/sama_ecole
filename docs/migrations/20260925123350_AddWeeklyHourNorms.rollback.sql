START TRANSACTION;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925123350_AddWeeklyHourNorms') THEN
    DO $patch$
    DECLARE
        v_def text;
    BEGIN
        v_def := pg_get_functiondef('reset_school_data(uuid)'::regprocedure);
        EXECUTE replace(v_def, '[''weekly_hour_norms'', ''Volumes horaires de l''''établissement''],' || chr(10) || '                        ', '');
    END
    $patch$;
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925123350_AddWeeklyHourNorms') THEN
    DROP POLICY IF EXISTS weekly_hour_norms_tenant_isolation ON "weekly_hour_norms";
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925123350_AddWeeklyHourNorms') THEN
    DROP TABLE weekly_hour_norms;
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925123350_AddWeeklyHourNorms') THEN
    DELETE FROM "__EFMigrationsHistory"
    WHERE "MigrationId" = '20260925123350_AddWeeklyHourNorms';
    END IF;
END $EF$;
COMMIT;

