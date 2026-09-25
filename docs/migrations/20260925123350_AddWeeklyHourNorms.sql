START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925123350_AddWeeklyHourNorms') THEN
    CREATE TABLE weekly_hour_norms (
        "Id" uuid NOT NULL,
        "SchoolId" uuid NOT NULL,
        "GradeLevel" character varying(20) NOT NULL,
        "Series" character varying(10),
        "SubjectId" uuid NOT NULL,
        "WeeklyHours" numeric(4,2) NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "CreatedBy" text,
        "UpdatedAt" timestamp with time zone,
        "UpdatedBy" text,
        "IsDeleted" boolean NOT NULL,
        "DeletedAt" timestamp with time zone,
        "DeletedBy" text,
        CONSTRAINT "PK_weekly_hour_norms" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_weekly_hour_norms_hours" CHECK ("WeeklyHours" >= 0 AND "WeeklyHours" <= 40),
        CONSTRAINT "FK_weekly_hour_norms_schools_SchoolId" FOREIGN KEY ("SchoolId") REFERENCES schools ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_weekly_hour_norms_subjects_SchoolId_SubjectId" FOREIGN KEY ("SchoolId", "SubjectId") REFERENCES subjects ("SchoolId", "Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925123350_AddWeeklyHourNorms') THEN
    CREATE INDEX "IX_weekly_hour_norms_SchoolId_SubjectId" ON weekly_hour_norms ("SchoolId", "SubjectId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925123350_AddWeeklyHourNorms') THEN
    CREATE UNIQUE INDEX "UX_weekly_hour_norms_scope" ON weekly_hour_norms ("SchoolId", "GradeLevel", "Series", "SubjectId") NULLS NOT DISTINCT WHERE NOT "IsDeleted";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925123350_AddWeeklyHourNorms') THEN
    ALTER TABLE "weekly_hour_norms" ENABLE ROW LEVEL SECURITY;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925123350_AddWeeklyHourNorms') THEN
    CREATE POLICY weekly_hour_norms_tenant_isolation ON "weekly_hour_norms"
        USING ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid)
        WITH CHECK ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925123350_AddWeeklyHourNorms') THEN
    DO $inner$
    BEGIN
        IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'sama_ecole_app') THEN
            EXECUTE 'GRANT SELECT, INSERT, UPDATE ON "weekly_hour_norms" TO sama_ecole_app';
        ELSE
            RAISE WARNING 'Rôle % absent : l''application ne pourra pas lire la table weekly_hour_norms.', 'sama_ecole_app';
        END IF;
    END
    $inner$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925123350_AddWeeklyHourNorms') THEN
    DO $patch$
    DECLARE
        v_def    text;
        v_anchor text := '[''subjects'',';
    BEGIN
        v_def := pg_get_functiondef('reset_school_data(uuid)'::regprocedure);

        IF position('''weekly_hour_norms''' IN v_def) > 0 THEN
            RETURN;
        END IF;

        IF array_length(string_to_array(v_def, v_anchor), 1) <> 2 THEN
            RAISE EXCEPTION 'reset_school_data : ancre % introuvable ou ambiguë — la fonction a changé de forme, corriger cette migration.', v_anchor;
        END IF;

        EXECUTE replace(v_def, v_anchor, '[''weekly_hour_norms'', ''Volumes horaires de l''''établissement''],' || chr(10) || '                        ' || v_anchor);
    END
    $patch$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925123350_AddWeeklyHourNorms') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260925123350_AddWeeklyHourNorms', '9.0.1');
    END IF;
END $EF$;
COMMIT;

