START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925100215_AddClassSubjectsAndOptions') THEN
    CREATE TABLE class_subjects (
        "Id" uuid NOT NULL,
        "SchoolId" uuid NOT NULL,
        "ClassroomId" uuid NOT NULL,
        "SubjectId" uuid NOT NULL,
        "OptionGroup" character varying(40),
        "IsCustom" boolean NOT NULL,
        "IsActive" boolean NOT NULL DEFAULT TRUE,
        "DisplayOrder" integer NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "CreatedBy" text,
        "UpdatedAt" timestamp with time zone,
        "UpdatedBy" text,
        "IsDeleted" boolean NOT NULL,
        "DeletedAt" timestamp with time zone,
        "DeletedBy" text,
        CONSTRAINT "PK_class_subjects" PRIMARY KEY ("Id"),
        CONSTRAINT "AK_class_subjects_SchoolId_Id" UNIQUE ("SchoolId", "Id"),
        CONSTRAINT "FK_class_subjects_classrooms_SchoolId_ClassroomId" FOREIGN KEY ("SchoolId", "ClassroomId") REFERENCES classrooms ("SchoolId", "Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_class_subjects_schools_SchoolId" FOREIGN KEY ("SchoolId") REFERENCES schools ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_class_subjects_subjects_SchoolId_SubjectId" FOREIGN KEY ("SchoolId", "SubjectId") REFERENCES subjects ("SchoolId", "Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925100215_AddClassSubjectsAndOptions') THEN
    CREATE TABLE student_subject_enrollments (
        "Id" uuid NOT NULL,
        "SchoolId" uuid NOT NULL,
        "StudentId" uuid NOT NULL,
        "ClassSubjectId" uuid NOT NULL,
        "SchoolYearId" uuid NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "CreatedBy" text,
        "UpdatedAt" timestamp with time zone,
        "UpdatedBy" text,
        "IsDeleted" boolean NOT NULL,
        "DeletedAt" timestamp with time zone,
        "DeletedBy" text,
        CONSTRAINT "PK_student_subject_enrollments" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_student_subject_enrollments_class_subjects_SchoolId_ClassSu~" FOREIGN KEY ("SchoolId", "ClassSubjectId") REFERENCES class_subjects ("SchoolId", "Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_student_subject_enrollments_school_years_SchoolId_SchoolYea~" FOREIGN KEY ("SchoolId", "SchoolYearId") REFERENCES school_years ("SchoolId", "Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_student_subject_enrollments_schools_SchoolId" FOREIGN KEY ("SchoolId") REFERENCES schools ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_student_subject_enrollments_students_SchoolId_StudentId" FOREIGN KEY ("SchoolId", "StudentId") REFERENCES students ("SchoolId", "Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925100215_AddClassSubjectsAndOptions') THEN
    CREATE INDEX "IX_class_subjects_SchoolId" ON class_subjects ("SchoolId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925100215_AddClassSubjectsAndOptions') THEN
    CREATE INDEX "IX_class_subjects_SchoolId_ClassroomId" ON class_subjects ("SchoolId", "ClassroomId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925100215_AddClassSubjectsAndOptions') THEN
    CREATE INDEX "IX_class_subjects_SchoolId_SubjectId" ON class_subjects ("SchoolId", "SubjectId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925100215_AddClassSubjectsAndOptions') THEN
    CREATE UNIQUE INDEX "UX_class_subjects_classroom_subject" ON class_subjects ("ClassroomId", "SubjectId") WHERE NOT "IsDeleted";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925100215_AddClassSubjectsAndOptions') THEN
    CREATE INDEX "IX_student_subject_enrollments_SchoolId" ON student_subject_enrollments ("SchoolId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925100215_AddClassSubjectsAndOptions') THEN
    CREATE INDEX "IX_student_subject_enrollments_SchoolId_ClassSubjectId" ON student_subject_enrollments ("SchoolId", "ClassSubjectId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925100215_AddClassSubjectsAndOptions') THEN
    CREATE INDEX "IX_student_subject_enrollments_SchoolId_SchoolYearId" ON student_subject_enrollments ("SchoolId", "SchoolYearId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925100215_AddClassSubjectsAndOptions') THEN
    CREATE INDEX "IX_student_subject_enrollments_SchoolId_StudentId" ON student_subject_enrollments ("SchoolId", "StudentId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925100215_AddClassSubjectsAndOptions') THEN
    CREATE UNIQUE INDEX "UX_student_subject_enrollments_choice" ON student_subject_enrollments ("StudentId", "ClassSubjectId", "SchoolYearId") WHERE NOT "IsDeleted";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925100215_AddClassSubjectsAndOptions') THEN
    ALTER TABLE "class_subjects" ENABLE ROW LEVEL SECURITY;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925100215_AddClassSubjectsAndOptions') THEN
    CREATE POLICY class_subjects_tenant_isolation ON "class_subjects"
        USING ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid)
        WITH CHECK ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925100215_AddClassSubjectsAndOptions') THEN
    DO $inner$
    BEGIN
        IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'sama_ecole_app') THEN
            EXECUTE 'GRANT SELECT, INSERT, UPDATE ON "class_subjects" TO sama_ecole_app';
        ELSE
            RAISE WARNING 'Rôle % absent : l''application ne pourra pas lire la table class_subjects.', 'sama_ecole_app';
        END IF;
    END
    $inner$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925100215_AddClassSubjectsAndOptions') THEN
    ALTER TABLE "student_subject_enrollments" ENABLE ROW LEVEL SECURITY;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925100215_AddClassSubjectsAndOptions') THEN
    CREATE POLICY student_subject_enrollments_tenant_isolation ON "student_subject_enrollments"
        USING ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid)
        WITH CHECK ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925100215_AddClassSubjectsAndOptions') THEN
    DO $inner$
    BEGIN
        IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'sama_ecole_app') THEN
            EXECUTE 'GRANT SELECT, INSERT, UPDATE ON "student_subject_enrollments" TO sama_ecole_app';
        ELSE
            RAISE WARNING 'Rôle % absent : l''application ne pourra pas lire la table student_subject_enrollments.', 'sama_ecole_app';
        END IF;
    END
    $inner$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925100215_AddClassSubjectsAndOptions') THEN
    DO $patch$
    DECLARE
        v_def    text;
        v_anchor text := '[''students'',';
    BEGIN
        v_def := pg_get_functiondef('reset_school_data(uuid)'::regprocedure);

        IF position('''student_subject_enrollments''' IN v_def) > 0 THEN
            RETURN;
        END IF;

        IF array_length(string_to_array(v_def, v_anchor), 1) <> 2 THEN
            RAISE EXCEPTION 'reset_school_data : ancre % introuvable ou ambiguë — la fonction a changé de forme, corriger cette migration.', v_anchor;
        END IF;

        EXECUTE replace(v_def, v_anchor,
            '[''student_subject_enrollments'', ''Options choisies par les élèves''],' || chr(10) || '                        ' || v_anchor);
    END
    $patch$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925100215_AddClassSubjectsAndOptions') THEN
    DO $patch$
    DECLARE
        v_def    text;
        v_anchor text := '[''subject_coefficient_overrides'',';
    BEGIN
        v_def := pg_get_functiondef('reset_school_data(uuid)'::regprocedure);

        IF position('''class_subjects''' IN v_def) > 0 THEN
            RETURN;
        END IF;

        IF array_length(string_to_array(v_def, v_anchor), 1) <> 2 THEN
            RAISE EXCEPTION 'reset_school_data : ancre % introuvable ou ambiguë — la fonction a changé de forme, corriger cette migration.', v_anchor;
        END IF;

        EXECUTE replace(v_def, v_anchor,
            '[''class_subjects'', ''Matières des classes''],' || chr(10) || '                        ' || v_anchor);
    END
    $patch$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925100215_AddClassSubjectsAndOptions') THEN
    DO $patch$
    DECLARE
        v_def    text;
        v_anchor text := 'DELETE FROM school_years';
        v_step   text := $step$DELETE FROM student_subject_enrollments
            WHERE "SchoolYearId" = p_school_year_id AND "SchoolId" = p_school_id;
            GET DIAGNOSTICS v_deleted = ROW_COUNT;
            label := 'Options choisies par les élèves'; rows_deleted := v_deleted; RETURN NEXT;

            $step$;
    BEGIN
        v_def := pg_get_functiondef('delete_school_year(uuid, uuid)'::regprocedure);

        IF position('student_subject_enrollments' IN v_def) > 0 THEN
            RETURN;
        END IF;

        IF array_length(string_to_array(v_def, v_anchor), 1) <> 2 THEN
            RAISE EXCEPTION 'delete_school_year : ancre % introuvable ou ambiguë — la fonction a changé de forme, corriger cette migration.', v_anchor;
        END IF;

        EXECUTE replace(v_def, v_anchor, v_step || v_anchor);
    END
    $patch$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925100215_AddClassSubjectsAndOptions') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260925100215_AddClassSubjectsAndOptions', '9.0.1');
    END IF;
END $EF$;
COMMIT;

