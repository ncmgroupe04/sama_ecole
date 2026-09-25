START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925120405_AddIefMapping') THEN
    ALTER TABLE enrollments ADD "IsTransferredIn" boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925120405_AddIefMapping') THEN
    ALTER TABLE enrollments ADD "PreviousSchoolName" character varying(150);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925120405_AddIefMapping') THEN
    CREATE TABLE grade_age_norms (
        "Id" uuid NOT NULL,
        "SchoolId" uuid NOT NULL,
        "GradeLevel" character varying(20) NOT NULL,
        "MinAge" integer NOT NULL,
        "MaxAge" integer NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "CreatedBy" text,
        "UpdatedAt" timestamp with time zone,
        "UpdatedBy" text,
        "IsDeleted" boolean NOT NULL,
        "DeletedAt" timestamp with time zone,
        "DeletedBy" text,
        CONSTRAINT "PK_grade_age_norms" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_grade_age_norms_range" CHECK ("MinAge" >= 0 AND "MaxAge" >= "MinAge" AND "MaxAge" <= 30),
        CONSTRAINT "FK_grade_age_norms_schools_SchoolId" FOREIGN KEY ("SchoolId") REFERENCES schools ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925120405_AddIefMapping') THEN
    CREATE UNIQUE INDEX "UX_grade_age_norms_level" ON grade_age_norms ("SchoolId", "GradeLevel") WHERE NOT "IsDeleted";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925120405_AddIefMapping') THEN
    ALTER TABLE "grade_age_norms" ENABLE ROW LEVEL SECURITY;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925120405_AddIefMapping') THEN
    CREATE POLICY grade_age_norms_tenant_isolation ON "grade_age_norms"
        USING ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid)
        WITH CHECK ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925120405_AddIefMapping') THEN
    DO $inner$
    BEGIN
        IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'sama_ecole_app') THEN
            EXECUTE 'GRANT SELECT, INSERT, UPDATE ON "grade_age_norms" TO sama_ecole_app';
        ELSE
            RAISE WARNING 'Rôle % absent : l''application ne pourra pas lire la table grade_age_norms.', 'sama_ecole_app';
        END IF;
    END
    $inner$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260925120405_AddIefMapping') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260925120405_AddIefMapping', '9.0.1');
    END IF;
END $EF$;
COMMIT;

