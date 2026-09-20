using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Socle de données Franco-Arabe/Daara (docs/superpowers/specs/2026-09-20-franco-arabic-core-design.md) :
    /// deux colonnes descriptives (subjects.section_type, school_settings.school_type) et deux tables
    /// tenant préparatoires (quran_progress, quran_evaluations). Comme pour AddClassJournal/
    /// AddInternatBoarding, EF ne génère jamais les policies RLS : ajoutées à la main ci-dessous
    /// (AGENTS.md règle #2). RlsCoverageTests échoue si elle manque. Aucun DELETE nulle part : soft
    /// delete uniquement (règle #6).
    /// </summary>
    public partial class AddQuranCoreModule : Migration
    {
        private static readonly string[] TenantTables = ["quran_progress", "quran_evaluations"];

        private const string AppRole = "sama_ecole_app";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SectionType",
                table: "subjects",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "French");

            migrationBuilder.AddColumn<string>(
                name: "SchoolType",
                table: "school_settings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Standard");

            migrationBuilder.CreateTable(
                name: "quran_evaluations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvaluationDate = table.Column<DateOnly>(type: "date", nullable: false),
                    MemoryMistakes = table.Column<int>(type: "integer", nullable: false),
                    TajwidMistakes = table.Column<int>(type: "integer", nullable: false),
                    Hesitations = table.Column<int>(type: "integer", nullable: false),
                    FinalScore = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
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
                    table.PrimaryKey("PK_quran_evaluations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_quran_evaluations_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_quran_evaluations_students_SchoolId_StudentId",
                        columns: x => new { x.SchoolId, x.StudentId },
                        principalTable: "students",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "quran_progress",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    JuzNumber = table.Column<int>(type: "integer", nullable: false),
                    HizbNumber = table.Column<int>(type: "integer", nullable: false),
                    SurahNumber = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "InProcess"),
                    EvaluationDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
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
                    table.PrimaryKey("PK_quran_progress", x => x.Id);
                    table.ForeignKey(
                        name: "FK_quran_progress_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_quran_progress_students_SchoolId_StudentId",
                        columns: x => new { x.SchoolId, x.StudentId },
                        principalTable: "students",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_quran_evaluations_SchoolId_StudentId",
                table: "quran_evaluations",
                columns: new[] { "SchoolId", "StudentId" });

            migrationBuilder.CreateIndex(
                name: "IX_quran_progress_SchoolId_StudentId",
                table: "quran_progress",
                columns: new[] { "SchoolId", "StudentId" });

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
                name: "quran_evaluations");

            migrationBuilder.DropTable(
                name: "quran_progress");

            migrationBuilder.DropColumn(
                name: "SectionType",
                table: "subjects");

            migrationBuilder.DropColumn(
                name: "SchoolType",
                table: "school_settings");
        }
    }
}
