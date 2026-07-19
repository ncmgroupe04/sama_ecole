using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Distinction du conseil (Blâme… Félicitations) et observations, par élève et par trimestre — pour
    /// le bulletin (ticket JGK-G03). Mêmes deux ajouts manuels que toute table tenant (voir
    /// AddSubjects/AddAttendance) : policy RLS (AGENTS.md règle #2) et GRANT du rôle applicatif, sans
    /// DELETE (soft delete = UPDATE, règle #6).
    /// </summary>
    public partial class AddReportCardRemarks : Migration
    {
        private static readonly string[] TenantTables = ["report_card_remarks"];
        private const string AppRole = "sama_ecole_app";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "report_card_remarks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    TermId = table.Column<Guid>(type: "uuid", nullable: false),
                    DisciplinaryMention = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Observations = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
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
                    table.PrimaryKey("PK_report_card_remarks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_report_card_remarks_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_report_card_remarks_students_SchoolId_StudentId",
                        columns: x => new { x.SchoolId, x.StudentId },
                        principalTable: "students",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_report_card_remarks_terms_SchoolId_TermId",
                        columns: x => new { x.SchoolId, x.TermId },
                        principalTable: "terms",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_report_card_remarks_SchoolId_TermId",
                table: "report_card_remarks",
                columns: new[] { "SchoolId", "TermId" });

            migrationBuilder.CreateIndex(
                name: "UX_report_card_remarks_single_entry",
                table: "report_card_remarks",
                columns: new[] { "SchoolId", "StudentId", "TermId", "IsDeleted" },
                unique: true);

            // --- Isolation multi-tenant (AGENTS.md règle #2) ---
            foreach (var table in TenantTables)
            {
                migrationBuilder.Sql($"ALTER TABLE {table} ENABLE ROW LEVEL SECURITY;");

                // USING      -> lignes visibles en lecture (SELECT, et cibles d'UPDATE/DELETE).
                // WITH CHECK -> lignes autorisées en écriture : interdit d'INSÉRER pour une autre
                //               école, ou de déplacer une ligne existante vers une autre école.
                migrationBuilder.Sql($"""
                    CREATE POLICY {table}_tenant_isolation ON {table}
                        USING ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid)
                        WITH CHECK ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid);
                    """);

                migrationBuilder.Sql($"""
                    DO $$
                    BEGIN
                        IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{AppRole}') THEN
                            EXECUTE 'GRANT SELECT, INSERT, UPDATE ON {table} TO {AppRole}';
                        ELSE
                            RAISE WARNING 'Rôle % absent : l''application ne pourra pas lire la table {table}.', '{AppRole}';
                        END IF;
                    END
                    $$;
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in TenantTables)
            {
                migrationBuilder.Sql($"DROP POLICY IF EXISTS {table}_tenant_isolation ON {table};");
            }

            migrationBuilder.DropTable(
                name: "report_card_remarks");
        }
    }
}
