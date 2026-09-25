START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925191029_AddStudentSubjectExemptions') THEN
    CREATE TABLE student_subject_exemptions (
        "Id" uuid NOT NULL,
        "SchoolId" uuid NOT NULL,
        "StudentId" uuid NOT NULL,
        "SubjectId" uuid NOT NULL,
        "SchoolYearId" uuid NOT NULL,
        "Reason" character varying(200) NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "CreatedBy" text,
        "UpdatedAt" timestamp with time zone,
        "UpdatedBy" text,
        "IsDeleted" boolean NOT NULL,
        "DeletedAt" timestamp with time zone,
        "DeletedBy" text,
        CONSTRAINT "PK_student_subject_exemptions" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_student_subject_exemptions_school_years_SchoolId_SchoolYear~" FOREIGN KEY ("SchoolId", "SchoolYearId") REFERENCES school_years ("SchoolId", "Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_student_subject_exemptions_schools_SchoolId" FOREIGN KEY ("SchoolId") REFERENCES schools ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_student_subject_exemptions_students_SchoolId_StudentId" FOREIGN KEY ("SchoolId", "StudentId") REFERENCES students ("SchoolId", "Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_student_subject_exemptions_subjects_SchoolId_SubjectId" FOREIGN KEY ("SchoolId", "SubjectId") REFERENCES subjects ("SchoolId", "Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925191029_AddStudentSubjectExemptions') THEN
    CREATE INDEX "IX_student_subject_exemptions_SchoolId_SchoolYearId" ON student_subject_exemptions ("SchoolId", "SchoolYearId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925191029_AddStudentSubjectExemptions') THEN
    CREATE INDEX "IX_student_subject_exemptions_SchoolId_SubjectId_SchoolYearId" ON student_subject_exemptions ("SchoolId", "SubjectId", "SchoolYearId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925191029_AddStudentSubjectExemptions') THEN
    CREATE UNIQUE INDEX "UX_student_subject_exemptions_key" ON student_subject_exemptions ("SchoolId", "StudentId", "SubjectId", "SchoolYearId") WHERE NOT "IsDeleted";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925191029_AddStudentSubjectExemptions') THEN
    ALTER TABLE "student_subject_exemptions" ENABLE ROW LEVEL SECURITY;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925191029_AddStudentSubjectExemptions') THEN
    CREATE POLICY student_subject_exemptions_tenant_isolation ON "student_subject_exemptions"
        USING ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid)
        WITH CHECK ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925191029_AddStudentSubjectExemptions') THEN
    DO $inner$
    BEGIN
        IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'sama_ecole_app') THEN
            EXECUTE 'GRANT SELECT, INSERT, UPDATE ON "student_subject_exemptions" TO sama_ecole_app';
        ELSE
            RAISE WARNING 'Rôle % absent : l''application ne pourra pas lire la table student_subject_exemptions.', 'sama_ecole_app';
        END IF;
    END
    $inner$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925191029_AddStudentSubjectExemptions') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260925191029_AddStudentSubjectExemptions', '9.0.1');
    END IF;
END $EF$;
COMMIT;

