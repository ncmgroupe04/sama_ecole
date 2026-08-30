using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Module Intégration étatique (SIMEN / Planète / STATEDUC) — Volume 1 §23, Volume 3 §5.11.
    ///
    /// L'essentiel du module tient en COLONNES ajoutées à des tables existantes (students, teachers,
    /// schools, exam_dossiers). Une seule table nouvelle : <c>student_mutation_certificates</c>.
    ///
    /// La policy RLS de cette table est ajoutée à la main ci-dessous — EF ne la génère jamais, comme
    /// dans AddExamsModule et AddInventoryModule. Une table tenant protégée par le seul filtre EF
    /// fuit dès la première requête SQL brute, et <c>RlsCoverageTests</c> échoue tant qu'elle manque
    /// (AGENTS.md règle #2). Les index uniques PARTIELS (students.IenNumber,
    /// schools.NationalSchoolCode) sont, eux, exprimés via HasIndex().HasFilter() et déjà présents
    /// dans le scaffold — rien à reprendre pour ceux-là.
    /// </summary>
    public partial class AddStateIntegrationModule : Migration
    {
        /// <summary>
        /// La seule table tenant NOUVELLE du module. Le certificat de mutation est une pièce remise à
        /// une famille et opposable à l'école d'accueil : son isolation par école n'est pas
        /// négociable. Les colonnes ajoutées à students/teachers/schools/exam_dossiers héritent des
        /// policies déjà en place sur ces tables.
        /// </summary>
        private static readonly string[] TenantTables = ["student_mutation_certificates"];

        private const string AppRole = "sama_ecole_app";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AcademicQualification",
                table: "teachers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "NonRenseigne");

            migrationBuilder.AddColumn<string>(
                name: "CivilServiceMatricule",
                table: "teachers",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CivilServiceStatus",
                table: "teachers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "NonRenseigne");

            migrationBuilder.AddColumn<DateOnly>(
                name: "FirstAppointmentDate",
                table: "teachers",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Gender",
                table: "teachers",
                type: "character varying(1)",
                maxLength: 1,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProfessionalQualification",
                table: "teachers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "NonRenseigne");

            migrationBuilder.AddColumn<string>(
                name: "IenNumber",
                table: "students",
                type: "character varying(24)",
                maxLength: 24,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsIenProvisional",
                table: "students",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "GpsLatitude",
                table: "schools",
                type: "numeric(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "GpsLongitude",
                table: "schools",
                type: "numeric(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MinistryAuthorizationNumber",
                table: "schools",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NationalSchoolCode",
                table: "schools",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SchoolDistrictCode",
                table: "schools",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CivilRegistryDocumentStatus",
                table: "exam_dossiers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "NonFourni");

            migrationBuilder.AddColumn<string>(
                name: "ExamCenterCode",
                table: "exam_dossiers",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TableNumber",
                table: "exam_dossiers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "student_mutation_certificates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolYearId = table.Column<Guid>(type: "uuid", nullable: false),
                    CertificateNumber = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    VerificationCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DestinationSchoolName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    DestinationCity = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Reason = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "Autre"),
                    ReasonDetails = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    ClassroomNameSnapshot = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IssuedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    WasFinanciallyClear = table.Column<bool>(type: "boolean", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevocationReason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_student_mutation_certificates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_student_mutation_certificates_school_years_SchoolYearId",
                        column: x => x.SchoolYearId,
                        principalTable: "school_years",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_student_mutation_certificates_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_student_mutation_certificates_students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_students_SchoolId_IenNumber",
                table: "students",
                columns: new[] { "SchoolId", "IenNumber" },
                unique: true,
                filter: "\"IenNumber\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_schools_NationalSchoolCode",
                table: "schools",
                column: "NationalSchoolCode",
                unique: true,
                filter: "\"NationalSchoolCode\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_student_mutation_certificates_SchoolId_CertificateNumber",
                table: "student_mutation_certificates",
                columns: new[] { "SchoolId", "CertificateNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_student_mutation_certificates_SchoolId_StudentId",
                table: "student_mutation_certificates",
                columns: new[] { "SchoolId", "StudentId" });

            migrationBuilder.CreateIndex(
                name: "IX_student_mutation_certificates_SchoolYearId",
                table: "student_mutation_certificates",
                column: "SchoolYearId");

            migrationBuilder.CreateIndex(
                name: "IX_student_mutation_certificates_StudentId",
                table: "student_mutation_certificates",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_student_mutation_certificates_VerificationCode",
                table: "student_mutation_certificates",
                column: "VerificationCode",
                unique: true);

            // --- Isolation multi-tenant (AGENTS.md règle #2) ---
            // Même bloc, mot pour mot, qu'AddExamsModule / AddInventoryModule : RLS activée + policy
            // (SchoolId) en USING et WITH CHECK, puis GRANT SELECT/INSERT/UPDATE au rôle applicatif —
            // jamais DELETE, le soft delete n'émet aucun SQL DELETE (règle #6).
            foreach (var table in TenantTables)
            {
                migrationBuilder.Sql($"ALTER TABLE \"{table}\" ENABLE ROW LEVEL SECURITY;");

                migrationBuilder.Sql($$"""
                    CREATE POLICY {{table}}_tenant_isolation ON "{{table}}"
                        USING ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid)
                        WITH CHECK ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid);
                    """);

                migrationBuilder.Sql($$"""
                    DO $inner$
                    BEGIN
                        IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{{AppRole}}') THEN
                            EXECUTE 'GRANT SELECT, INSERT, UPDATE ON "{{table}}" TO {{AppRole}}';
                        ELSE
                            RAISE WARNING 'Rôle % absent : l''application ne pourra pas lire la table {{table}}.', '{{AppRole}}';
                        END IF;
                    END
                    $inner$;
                    """);
            }

            // --- Vérification publique d'un certificat de mutation (Volume 1 §23.5) ---
            //
            // Un tiers HORS PLATEFORME (l'école d'accueil) scanne le QR : sa requête n'a pas de tenant,
            // et la policy RLS ci-dessus lui renverrait zéro ligne. Même situation que le chemin de
            // login et l'annuaire public — même réponse : une fonction SECURITY DEFINER, exécutée avec
            // les droits du rôle propriétaire (BYPASSRLS), au périmètre volontairement minuscule.
            //
            // CE QUE LA FONCTION RÉVÈLE, et rien d'autre : l'existence du certificat, s'il est révoqué,
            // son numéro, sa date, et le nom de l'établissement émetteur — de quoi rapprocher le papier
            // présenté. AUCUNE donnée de l'élève (ni nom, ni date de naissance, ni IEN, ni classe).
            // Le paramètre est le code de vérification (32 hex d'aléa) : il n'y a rien à énumérer.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION public.verify_mutation_certificate(p_code text)
                RETURNS TABLE (
                    "Status"           text,
                    "CertificateNumber" text,
                    "IssuedOn"         date,
                    "IssuingSchoolName" text,
                    "RevokedAt"        timestamptz
                )
                LANGUAGE sql
                SECURITY DEFINER
                SET search_path = public
                AS $fn$
                    SELECT
                        CASE
                            WHEN c."RevokedAt" IS NOT NULL THEN 'revoked'
                            ELSE 'valid'
                        END,
                        c."CertificateNumber",
                        c."IssuedOn",
                        s."Name",
                        c."RevokedAt"
                    FROM student_mutation_certificates c
                    JOIN schools s ON s."Id" = c."SchoolId"
                    WHERE c."VerificationCode" = upper(trim(p_code))
                      AND c."IsDeleted" = FALSE;
                $fn$;
                """);

            migrationBuilder.Sql($$"""
                DO $grant$
                BEGIN
                    -- La fonction est exécutable par le rôle applicatif ; on RÉVOQUE l'accès par
                    -- défaut à PUBLIC pour qu'aucun rôle non prévu ne l'appelle directement.
                    REVOKE ALL ON FUNCTION public.verify_mutation_certificate(text) FROM PUBLIC;

                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{{AppRole}}') THEN
                        EXECUTE 'GRANT EXECUTE ON FUNCTION public.verify_mutation_certificate(text) TO {{AppRole}}';
                    ELSE
                        RAISE WARNING 'Rôle % absent : la vérification publique de certificat sera indisponible.', '{{AppRole}}';
                    END IF;
                END
                $grant$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS public.verify_mutation_certificate(text);");

            foreach (var table in TenantTables)
            {
                migrationBuilder.Sql($"DROP POLICY IF EXISTS {table}_tenant_isolation ON \"{table}\";");
            }

            migrationBuilder.DropTable(
                name: "student_mutation_certificates");

            migrationBuilder.DropIndex(
                name: "IX_students_SchoolId_IenNumber",
                table: "students");

            migrationBuilder.DropIndex(
                name: "IX_schools_NationalSchoolCode",
                table: "schools");

            migrationBuilder.DropColumn(
                name: "AcademicQualification",
                table: "teachers");

            migrationBuilder.DropColumn(
                name: "CivilServiceMatricule",
                table: "teachers");

            migrationBuilder.DropColumn(
                name: "CivilServiceStatus",
                table: "teachers");

            migrationBuilder.DropColumn(
                name: "FirstAppointmentDate",
                table: "teachers");

            migrationBuilder.DropColumn(
                name: "Gender",
                table: "teachers");

            migrationBuilder.DropColumn(
                name: "ProfessionalQualification",
                table: "teachers");

            migrationBuilder.DropColumn(
                name: "IenNumber",
                table: "students");

            migrationBuilder.DropColumn(
                name: "IsIenProvisional",
                table: "students");

            migrationBuilder.DropColumn(
                name: "GpsLatitude",
                table: "schools");

            migrationBuilder.DropColumn(
                name: "GpsLongitude",
                table: "schools");

            migrationBuilder.DropColumn(
                name: "MinistryAuthorizationNumber",
                table: "schools");

            migrationBuilder.DropColumn(
                name: "NationalSchoolCode",
                table: "schools");

            migrationBuilder.DropColumn(
                name: "SchoolDistrictCode",
                table: "schools");

            migrationBuilder.DropColumn(
                name: "CivilRegistryDocumentStatus",
                table: "exam_dossiers");

            migrationBuilder.DropColumn(
                name: "ExamCenterCode",
                table: "exam_dossiers");

            migrationBuilder.DropColumn(
                name: "TableNumber",
                table: "exam_dossiers");
        }
    }
}
