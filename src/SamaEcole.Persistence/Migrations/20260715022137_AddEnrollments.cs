using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Ticket JGK-E01 — inscriptions : tables `enrollments` et `enrollment_fee_lines`, plus le réglage
    /// `TuitionMonthsPerYear` (nombre de mensualités par an) ajouté aux paramètres d'établissement.
    ///
    /// Ce qu'EF Core ne génère PAS et qu'il faut poser à la main (comme pour AddFees) :
    ///   1. La policy RLS des DEUX tables tenant (AGENTS.md règle #2), en plus du Global Query Filter.
    ///   2. Les GRANT du rôle applicatif sur ces tables : SELECT, INSERT, UPDATE — jamais DELETE
    ///      (soft delete, règle #6).
    ///
    /// La colonne `xmin` d'`enrollments` est le jeton de verrou optimiste (règle #5) sur TotalDue :
    /// colonne SYSTÈME de PostgreSQL, aucun DDL généré pour elle.
    ///
    /// La valeur par défaut du nouveau réglage est 9 (la convention sénégalaise : rentrée d'octobre,
    /// fin en juin), synchronisée avec SchoolSettingsDefaults.TuitionMonthsPerYear (Domain).
    /// </summary>
    public partial class AddEnrollments : Migration
    {
        private const string AppRole = "sama_ecole_app";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TuitionMonthsPerYear",
                table: "school_settings",
                type: "integer",
                nullable: false,
                defaultValue: 9);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_students_SchoolId_Id",
                table: "students",
                columns: new[] { "SchoolId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_school_years_SchoolId_Id",
                table: "school_years",
                columns: new[] { "SchoolId", "Id" });

            migrationBuilder.CreateTable(
                name: "enrollments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolYearId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClassroomId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TotalDue = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    EnrolledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
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
                    table.PrimaryKey("PK_enrollments", x => x.Id);
                    table.UniqueConstraint("AK_enrollments_SchoolId_Id", x => new { x.SchoolId, x.Id });
                    table.ForeignKey(
                        name: "FK_enrollments_classrooms_SchoolId_ClassroomId",
                        columns: x => new { x.SchoolId, x.ClassroomId },
                        principalTable: "classrooms",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_enrollments_school_years_SchoolId_SchoolYearId",
                        columns: x => new { x.SchoolId, x.SchoolYearId },
                        principalTable: "school_years",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_enrollments_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_enrollments_students_SchoolId_StudentId",
                        columns: x => new { x.SchoolId, x.StudentId },
                        principalTable: "students",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "enrollment_fee_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnrollmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    FeeCategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Designation = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    IsRecurring = table.Column<bool>(type: "boolean", nullable: false),
                    UnitAmount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    Months = table.Column<int>(type: "integer", nullable: false),
                    LineTotal = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
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
                    table.PrimaryKey("PK_enrollment_fee_lines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_enrollment_fee_lines_enrollments_SchoolId_EnrollmentId",
                        columns: x => new { x.SchoolId, x.EnrollmentId },
                        principalTable: "enrollments",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_enrollment_fee_lines_fee_categories_SchoolId_FeeCategoryId",
                        columns: x => new { x.SchoolId, x.FeeCategoryId },
                        principalTable: "fee_categories",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_enrollment_fee_lines_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_enrollment_fee_lines_SchoolId_EnrollmentId",
                table: "enrollment_fee_lines",
                columns: new[] { "SchoolId", "EnrollmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_enrollment_fee_lines_SchoolId_FeeCategoryId",
                table: "enrollment_fee_lines",
                columns: new[] { "SchoolId", "FeeCategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_enrollments_SchoolId_ClassroomId",
                table: "enrollments",
                columns: new[] { "SchoolId", "ClassroomId" });

            migrationBuilder.CreateIndex(
                name: "IX_enrollments_SchoolId_SchoolYearId",
                table: "enrollments",
                columns: new[] { "SchoolId", "SchoolYearId" });

            migrationBuilder.CreateIndex(
                name: "UX_enrollments_single_active_per_year",
                table: "enrollments",
                columns: new[] { "SchoolId", "StudentId", "SchoolYearId" },
                unique: true,
                filter: "NOT \"IsDeleted\" AND \"Status\" <> 'Cancelled'");

            // --- Isolation multi-tenant (AGENTS.md règle #2) : les DEUX nouvelles tables ---
            foreach (var table in new[] { "enrollments", "enrollment_fee_lines" })
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

            // Droits du rôle applicatif sur les nouvelles tables : le GRANT ON ALL TABLES d'une migration
            // antérieure ne couvrait pas des tables qui n'existaient pas encore. Modifiables (statut,
            // correction historisée du montant par le Secrétariat) mais jamais supprimables physiquement
            // (soft delete, règle #6).
            migrationBuilder.Sql($"""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{AppRole}') THEN
                        EXECUTE 'GRANT SELECT, INSERT, UPDATE ON enrollments TO {AppRole}';
                        EXECUTE 'GRANT SELECT, INSERT, UPDATE ON enrollment_fee_lines TO {AppRole}';
                    ELSE
                        RAISE WARNING 'Rôle % absent : l''application ne pourra pas accéder aux inscriptions.', '{AppRole}';
                    END IF;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in new[] { "enrollment_fee_lines", "enrollments" })
            {
                migrationBuilder.Sql($"DROP POLICY IF EXISTS {table}_tenant_isolation ON {table};");
            }

            migrationBuilder.DropTable(
                name: "enrollment_fee_lines");

            migrationBuilder.DropTable(
                name: "enrollments");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_students_SchoolId_Id",
                table: "students");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_school_years_SchoolId_Id",
                table: "school_years");

            migrationBuilder.DropColumn(
                name: "TuitionMonthsPerYear",
                table: "school_settings");
        }
    }
}
