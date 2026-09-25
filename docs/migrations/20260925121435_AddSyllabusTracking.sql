START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925121435_AddSyllabusTracking') THEN
    ALTER TABLE class_journal_entries ADD CONSTRAINT "AK_class_journal_entries_SchoolId_Id" UNIQUE ("SchoolId", "Id");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925121435_AddSyllabusTracking') THEN
    CREATE TABLE syllabus_units (
        "Id" uuid NOT NULL,
        "SchoolId" uuid NOT NULL,
        "SubjectId" uuid NOT NULL,
        "GradeLevel" character varying(20) NOT NULL,
        "Section" character varying(120),
        "Title" character varying(200) NOT NULL,
        "Order" integer NOT NULL,
        "PlannedHours" numeric(5,2),
        "CreatedAt" timestamp with time zone NOT NULL,
        "CreatedBy" text,
        "UpdatedAt" timestamp with time zone,
        "UpdatedBy" text,
        "IsDeleted" boolean NOT NULL,
        "DeletedAt" timestamp with time zone,
        "DeletedBy" text,
        CONSTRAINT "PK_syllabus_units" PRIMARY KEY ("Id"),
        CONSTRAINT "AK_syllabus_units_SchoolId_Id" UNIQUE ("SchoolId", "Id"),
        CONSTRAINT "FK_syllabus_units_schools_SchoolId" FOREIGN KEY ("SchoolId") REFERENCES schools ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_syllabus_units_subjects_SchoolId_SubjectId" FOREIGN KEY ("SchoolId", "SubjectId") REFERENCES subjects ("SchoolId", "Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925121435_AddSyllabusTracking') THEN
    CREATE TABLE class_journal_entry_units (
        "Id" uuid NOT NULL,
        "SchoolId" uuid NOT NULL,
        "ClassJournalEntryId" uuid NOT NULL,
        "SyllabusUnitId" uuid NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "CreatedBy" text,
        "UpdatedAt" timestamp with time zone,
        "UpdatedBy" text,
        "IsDeleted" boolean NOT NULL,
        "DeletedAt" timestamp with time zone,
        "DeletedBy" text,
        CONSTRAINT "PK_class_journal_entry_units" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_class_journal_entry_units_class_journal_entries_SchoolId_Cl~" FOREIGN KEY ("SchoolId", "ClassJournalEntryId") REFERENCES class_journal_entries ("SchoolId", "Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_class_journal_entry_units_schools_SchoolId" FOREIGN KEY ("SchoolId") REFERENCES schools ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_class_journal_entry_units_syllabus_units_SchoolId_SyllabusU~" FOREIGN KEY ("SchoolId", "SyllabusUnitId") REFERENCES syllabus_units ("SchoolId", "Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925121435_AddSyllabusTracking') THEN
    CREATE INDEX "IX_class_journal_entry_units_SchoolId" ON class_journal_entry_units ("SchoolId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925121435_AddSyllabusTracking') THEN
    CREATE INDEX "IX_class_journal_entry_units_SchoolId_ClassJournalEntryId" ON class_journal_entry_units ("SchoolId", "ClassJournalEntryId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925121435_AddSyllabusTracking') THEN
    CREATE INDEX "IX_class_journal_entry_units_SchoolId_SyllabusUnitId" ON class_journal_entry_units ("SchoolId", "SyllabusUnitId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925121435_AddSyllabusTracking') THEN
    CREATE UNIQUE INDEX "UX_class_journal_entry_units_link" ON class_journal_entry_units ("ClassJournalEntryId", "SyllabusUnitId") WHERE NOT "IsDeleted";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925121435_AddSyllabusTracking') THEN
    CREATE UNIQUE INDEX "UX_syllabus_units_title" ON syllabus_units ("SchoolId", "SubjectId", "GradeLevel", "Title") WHERE NOT "IsDeleted";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925121435_AddSyllabusTracking') THEN
    ALTER TABLE "syllabus_units" ENABLE ROW LEVEL SECURITY;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925121435_AddSyllabusTracking') THEN
    CREATE POLICY syllabus_units_tenant_isolation ON "syllabus_units"
        USING ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid)
        WITH CHECK ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925121435_AddSyllabusTracking') THEN
    DO $inner$
    BEGIN
        IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'sama_ecole_app') THEN
            EXECUTE 'GRANT SELECT, INSERT, UPDATE ON "syllabus_units" TO sama_ecole_app';
        ELSE
            RAISE WARNING 'Rôle % absent : l''application ne pourra pas lire la table syllabus_units.', 'sama_ecole_app';
        END IF;
    END
    $inner$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925121435_AddSyllabusTracking') THEN
    ALTER TABLE "class_journal_entry_units" ENABLE ROW LEVEL SECURITY;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925121435_AddSyllabusTracking') THEN
    CREATE POLICY class_journal_entry_units_tenant_isolation ON "class_journal_entry_units"
        USING ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid)
        WITH CHECK ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925121435_AddSyllabusTracking') THEN
    DO $inner$
    BEGIN
        IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'sama_ecole_app') THEN
            EXECUTE 'GRANT SELECT, INSERT, UPDATE ON "class_journal_entry_units" TO sama_ecole_app';
        ELSE
            RAISE WARNING 'Rôle % absent : l''application ne pourra pas lire la table class_journal_entry_units.', 'sama_ecole_app';
        END IF;
    END
    $inner$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925121435_AddSyllabusTracking') THEN
    DO $patch$
    DECLARE
        v_def    text;
        v_anchor text := '[''class_journal_entries'',';
    BEGIN
        v_def := pg_get_functiondef('reset_school_data(uuid)'::regprocedure);

        IF position('''syllabus_units''' IN v_def) > 0 THEN
            RETURN;
        END IF;

        IF array_length(string_to_array(v_def, v_anchor), 1) <> 2 THEN
            RAISE EXCEPTION 'reset_school_data : ancre % introuvable ou ambiguë — la fonction a changé de forme, corriger cette migration.', v_anchor;
        END IF;

        EXECUTE replace(v_def, v_anchor,
            '[''syllabus_units'', ''Référentiel des programmes''],' || chr(10) || '                        ' || v_anchor);
    END
    $patch$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925121435_AddSyllabusTracking') THEN
    DO $patch$
    DECLARE
        v_def    text;
        v_anchor text := '[''syllabus_units'', ''Référentiel des programmes''],';
    BEGIN
        v_def := pg_get_functiondef('reset_school_data(uuid)'::regprocedure);

        IF position('''class_journal_entry_units''' IN v_def) > 0 THEN
            RETURN;
        END IF;

        IF array_length(string_to_array(v_def, v_anchor), 1) <> 2 THEN
            RAISE EXCEPTION 'reset_school_data : ancre % introuvable ou ambiguë — la fonction a changé de forme, corriger cette migration.', v_anchor;
        END IF;

        EXECUTE replace(v_def, v_anchor,
            '[''class_journal_entry_units'', ''Chapitres cochés au cahier de texte''],' || chr(10) || '                        ' || v_anchor);
    END
    $patch$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925121435_AddSyllabusTracking') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260925121435_AddSyllabusTracking', '9.0.1');
    END IF;
END $EF$;
COMMIT;

