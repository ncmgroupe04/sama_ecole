using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Ticket JGK-G01 — `terms` (trimestres, générés automatiquement à la création d'une année
    /// scolaire) et `grades` (notes, verrou optimiste xmin).
    ///
    /// Ce qu'EF Core ne génère PAS et qu'il faut poser à la main (voir AddSubjects/AddFees) :
    ///
    ///   1. La policy RLS des DEUX tables tenant (AGENTS.md règle #2).
    ///   2. Le GRANT du rôle applicatif — SELECT, INSERT, UPDATE, jamais DELETE (soft delete, règle #6).
    /// </summary>
    public partial class AddGrades : Migration
    {
        private const string AppRole = "sama_ecole_app";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_subjects_SchoolId_Id",
                table: "subjects",
                columns: new[] { "SchoolId", "Id" });

            migrationBuilder.CreateTable(
                name: "terms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolYearId = table.Column<Guid>(type: "uuid", nullable: false),
                    Label = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
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
                    table.PrimaryKey("PK_terms", x => x.Id);
                    table.UniqueConstraint("AK_terms_SchoolId_Id", x => new { x.SchoolId, x.Id });
                    table.ForeignKey(
                        name: "FK_terms_school_years_SchoolId_SchoolYearId",
                        columns: x => new { x.SchoolId, x.SchoolYearId },
                        principalTable: "school_years",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_terms_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "grades",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    TermId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvaluationType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Value = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
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
                    table.PrimaryKey("PK_grades", x => x.Id);
                    table.ForeignKey(
                        name: "FK_grades_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_grades_students_SchoolId_StudentId",
                        columns: x => new { x.SchoolId, x.StudentId },
                        principalTable: "students",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_grades_subjects_SchoolId_SubjectId",
                        columns: x => new { x.SchoolId, x.SubjectId },
                        principalTable: "subjects",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_grades_terms_SchoolId_TermId",
                        columns: x => new { x.SchoolId, x.TermId },
                        principalTable: "terms",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_grades_SchoolId_StudentId_TermId",
                table: "grades",
                columns: new[] { "SchoolId", "StudentId", "TermId" });

            migrationBuilder.CreateIndex(
                name: "IX_grades_SchoolId_SubjectId",
                table: "grades",
                columns: new[] { "SchoolId", "SubjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_grades_SchoolId_TermId",
                table: "grades",
                columns: new[] { "SchoolId", "TermId" });

            migrationBuilder.CreateIndex(
                name: "UX_grades_single_entry",
                table: "grades",
                columns: new[] { "SchoolId", "StudentId", "SubjectId", "TermId", "EvaluationType", "IsDeleted" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_terms_SchoolId_SchoolYearId_Order_IsDeleted",
                table: "terms",
                columns: new[] { "SchoolId", "SchoolYearId", "Order", "IsDeleted" },
                unique: true);

            // --- Isolation multi-tenant (AGENTS.md règle #2) : les DEUX tables ---
            foreach (var table in new[] { "terms", "grades" })
            {
                migrationBuilder.Sql($"ALTER TABLE {table} ENABLE ROW LEVEL SECURITY;");

                // USING      -> lignes visibles en lecture (SELECT, et cibles d'UPDATE/DELETE).
                // WITH CHECK -> lignes autorisées en écriture : interdit d'écrire pour une autre école.
                migrationBuilder.Sql($"""
                    CREATE POLICY {table}_tenant_isolation ON {table}
                        USING ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid)
                        WITH CHECK ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid);
                    """);
            }

            // Droits du rôle applicatif. Le GRANT ON ALL TABLES d'une migration antérieure ne couvrait
            // pas des tables qui n'existaient pas encore.
            migrationBuilder.Sql($"""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{AppRole}') THEN
                        EXECUTE 'GRANT SELECT, INSERT, UPDATE ON terms TO {AppRole}';
                        EXECUTE 'GRANT SELECT, INSERT, UPDATE ON grades TO {AppRole}';
                    ELSE
                        RAISE WARNING 'Rôle % absent : l''application ne pourra pas accéder aux tables de notation.', '{AppRole}';
                    END IF;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in new[] { "grades", "terms" })
            {
                migrationBuilder.Sql($"DROP POLICY IF EXISTS {table}_tenant_isolation ON {table};");
            }

            migrationBuilder.DropTable(
                name: "grades");

            migrationBuilder.DropTable(
                name: "terms");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_subjects_SchoolId_Id",
                table: "subjects");
        }
    }
}
