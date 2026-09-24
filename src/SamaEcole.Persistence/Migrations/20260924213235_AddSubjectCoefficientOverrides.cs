using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// Surcharges de coefficient par série ou par classe (Évolution N°4) — une table tenant :
    /// subject_coefficient_overrides.
    ///
    /// EF ne génère jamais les policies RLS : ajoutées à la main ci-dessous (AGENTS.md règle #2), comme
    /// pour AddClassJournal. RlsCoverageTests échoue si elle manque. UPDATE accordé (une surcharge se
    /// corrige), aucun DELETE : « Rétablir » est une suppression logique (règle #6).
    ///
    /// Les fonctions de purge (reset_school_data, delete_school_year) sont corrigées par la migration
    /// SUIVANTE, AddSubjectCoefficientOverridesToPurges — sans elle, « Réinitialiser les données » et la
    /// suppression d'une année en mode test échoueraient en 23503 (FK RESTRICT vers subjects, classrooms
    /// et school_years).
    /// </summary>
    public partial class AddSubjectCoefficientOverrides : Migration
    {
        private static readonly string[] TenantTables = ["subject_coefficient_overrides"];

        private const string AppRole = "sama_ecole_app";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "subject_coefficient_overrides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolYearId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClassroomId = table.Column<Guid>(type: "uuid", nullable: true),
                    Series = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    Coefficient = table.Column<decimal>(type: "numeric(4,2)", precision: 4, scale: 2, nullable: false),
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
                    table.PrimaryKey("PK_subject_coefficient_overrides", x => x.Id);
                    table.CheckConstraint("CK_subject_coefficient_overrides_one_scope", "num_nonnulls(\"ClassroomId\", \"Series\") = 1");
                    table.ForeignKey(
                        name: "FK_subject_coefficient_overrides_classrooms_SchoolId_Classroom~",
                        columns: x => new { x.SchoolId, x.ClassroomId },
                        principalTable: "classrooms",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_subject_coefficient_overrides_school_years_SchoolId_SchoolY~",
                        columns: x => new { x.SchoolId, x.SchoolYearId },
                        principalTable: "school_years",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_subject_coefficient_overrides_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_subject_coefficient_overrides_subjects_SchoolId_SubjectId",
                        columns: x => new { x.SchoolId, x.SubjectId },
                        principalTable: "subjects",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_subject_coefficient_overrides_SchoolId",
                table: "subject_coefficient_overrides",
                column: "SchoolId");

            migrationBuilder.CreateIndex(
                name: "IX_subject_coefficient_overrides_SchoolId_ClassroomId",
                table: "subject_coefficient_overrides",
                columns: new[] { "SchoolId", "ClassroomId" });

            migrationBuilder.CreateIndex(
                name: "IX_subject_coefficient_overrides_SchoolId_SchoolYearId",
                table: "subject_coefficient_overrides",
                columns: new[] { "SchoolId", "SchoolYearId" });

            migrationBuilder.CreateIndex(
                name: "IX_subject_coefficient_overrides_SchoolId_SubjectId",
                table: "subject_coefficient_overrides",
                columns: new[] { "SchoolId", "SubjectId" });

            migrationBuilder.CreateIndex(
                name: "UX_subject_coefficient_overrides_classroom",
                table: "subject_coefficient_overrides",
                columns: new[] { "SchoolYearId", "SubjectId", "ClassroomId" },
                unique: true,
                filter: "\"ClassroomId\" IS NOT NULL AND NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "UX_subject_coefficient_overrides_series",
                table: "subject_coefficient_overrides",
                columns: new[] { "SchoolYearId", "SubjectId", "Series" },
                unique: true,
                filter: "\"Series\" IS NOT NULL AND NOT \"IsDeleted\"");

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
                name: "subject_coefficient_overrides");
        }
    }
}
