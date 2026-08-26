using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Module Examens officiels — trois tables tenant : campagnes (exam_sessions), dossiers de
    /// candidature (exam_dossiers) et résultats de délibération (exam_results).
    ///
    /// Deux choses que le scaffolding EF n'écrit PAS et qui sont ajoutées à la main ci-dessous :
    ///
    ///   1. Les policies RLS. EF ne les génère jamais (comme dans AddInventoryModule). Une table
    ///      tenant protégée par le seul filtre EF fuit dès la première requête SQL brute —
    ///      RlsCoverageTests échoue si l'une des trois manque.
    ///   2. L'index unique (SchoolId, SchoolYearId, ExamType, Series) d'exam_sessions, avec un
    ///      COALESCE sur Series : NULL n'est jamais égal à NULL dans un index UNIQUE standard, et sans
    ///      ce contournement deux sessions CFEE de la même année (Series toujours nul) ne seraient
    ///      jamais détectées comme doublon. HasIndex ne sait pas exprimer une expression de colonne,
    ///      d'où le SQL brut (voir Volume_3_DDS.md §5.10).
    /// </summary>
    public partial class AddExamsModule : Migration
    {
        /// <summary>Les trois tables tenant du module, dans l'ordre de création.</summary>
        private static readonly string[] TenantTables = ["exam_sessions", "exam_dossiers", "exam_results"];

        private const string AppRole = "sama_ecole_app";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "exam_sessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolYearId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExamType = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Series = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    CenterName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    Status = table.Column<string>(type: "character varying(25)", maxLength: 25, nullable: false, defaultValue: "EnPreparation"),
                    NextCandidateSeq = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
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
                    table.PrimaryKey("PK_exam_sessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_exam_sessions_school_years_SchoolYearId",
                        column: x => x.SchoolYearId,
                        principalTable: "school_years",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_exam_sessions_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "exam_dossiers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExamSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClassroomId = table.Column<Guid>(type: "uuid", nullable: false),
                    CandidateNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ExamCenterName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    BirthCertificateNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    BirthCertificatePresent = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CivilStatusConforming = table.Column<bool>(type: "boolean", nullable: true),
                    CivilStatusNotes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Incomplet"),
                    TransmittedOn = table.Column<DateOnly>(type: "date", nullable: true),
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
                    table.PrimaryKey("PK_exam_dossiers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_exam_dossiers_classrooms_ClassroomId",
                        column: x => x.ClassroomId,
                        principalTable: "classrooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_exam_dossiers_exam_sessions_ExamSessionId",
                        column: x => x.ExamSessionId,
                        principalTable: "exam_sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_exam_dossiers_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_exam_dossiers_students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "exam_results",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExamDossierId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsAdmitted = table.Column<bool>(type: "boolean", nullable: false),
                    Mention = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    AverageScore = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    DeliberatedOn = table.Column<DateOnly>(type: "date", nullable: false),
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
                    table.PrimaryKey("PK_exam_results", x => x.Id);
                    table.ForeignKey(
                        name: "FK_exam_results_exam_dossiers_ExamDossierId",
                        column: x => x.ExamDossierId,
                        principalTable: "exam_dossiers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_exam_results_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_exam_dossiers_ClassroomId",
                table: "exam_dossiers",
                column: "ClassroomId");

            migrationBuilder.CreateIndex(
                name: "IX_exam_dossiers_ExamSessionId",
                table: "exam_dossiers",
                column: "ExamSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_exam_dossiers_SchoolId_ClassroomId",
                table: "exam_dossiers",
                columns: new[] { "SchoolId", "ClassroomId" });

            migrationBuilder.CreateIndex(
                name: "IX_exam_dossiers_SchoolId_ExamSessionId_CandidateNumber",
                table: "exam_dossiers",
                columns: new[] { "SchoolId", "ExamSessionId", "CandidateNumber" },
                unique: true,
                filter: "\"CandidateNumber\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_exam_dossiers_SchoolId_ExamSessionId_StudentId",
                table: "exam_dossiers",
                columns: new[] { "SchoolId", "ExamSessionId", "StudentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_exam_dossiers_SchoolId_Status",
                table: "exam_dossiers",
                columns: new[] { "SchoolId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_exam_dossiers_StudentId",
                table: "exam_dossiers",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_exam_results_ExamDossierId",
                table: "exam_results",
                column: "ExamDossierId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_exam_results_SchoolId",
                table: "exam_results",
                column: "SchoolId");

            migrationBuilder.CreateIndex(
                name: "IX_exam_sessions_SchoolId_SchoolYearId",
                table: "exam_sessions",
                columns: new[] { "SchoolId", "SchoolYearId" });

            migrationBuilder.CreateIndex(
                name: "IX_exam_sessions_SchoolYearId",
                table: "exam_sessions",
                column: "SchoolYearId");

            // Unicité (SchoolId, SchoolYearId, ExamType, Series) avec Series neutralisé par COALESCE :
            // voir le commentaire de classe. Pas d'équivalent HasIndex côté configuration.
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX "UX_exam_sessions_school_year_type_series"
                ON "exam_sessions" ("SchoolId", "SchoolYearId", "ExamType", COALESCE("Series", ''));
                """);

            // --- Isolation multi-tenant (AGENTS.md règle #2) ---
            foreach (var table in TenantTables)
            {
                migrationBuilder.Sql($"ALTER TABLE \"{table}\" ENABLE ROW LEVEL SECURITY;");

                migrationBuilder.Sql($$"""
                    CREATE POLICY {{table}}_tenant_isolation ON "{{table}}"
                        USING ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid)
                        WITH CHECK ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid);
                    """);

                // Aucun DELETE nulle part : le soft delete n'émet jamais de SQL DELETE (règle #6).
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in TenantTables)
            {
                migrationBuilder.Sql($"DROP POLICY IF EXISTS {table}_tenant_isolation ON \"{table}\";");
            }

            migrationBuilder.DropTable(
                name: "exam_results");

            migrationBuilder.DropTable(
                name: "exam_dossiers");

            migrationBuilder.DropTable(
                name: "exam_sessions");
        }
    }
}
