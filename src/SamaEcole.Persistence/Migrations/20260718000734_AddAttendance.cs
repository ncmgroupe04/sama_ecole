using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Ticket JGK-D06 — module Présences : tables `attendance_sheets` et `student_attendances`, plus le
    /// lien `teachers.UserId` (fiche RH ↔ compte de connexion) qui permet de borner la saisie de l'appel
    /// aux classes assignées de l'enseignant connecté.
    ///
    /// Mêmes deux ajouts manuels que toute table tenant (voir AddSubjects/AddTeachers) : policy RLS
    /// (AGENTS.md règle #2) et GRANT du rôle applicatif, sans DELETE (soft delete = UPDATE, règle #6).
    /// La colonne `teachers.UserId` n'a besoin de rien de plus : `teachers` porte déjà sa policy RLS.
    /// </summary>
    public partial class AddAttendance : Migration
    {
        private static readonly string[] TenantTables = ["attendance_sheets", "student_attendances"];
        private const string AppRole = "sama_ecole_app";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "teachers",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "attendance_sheets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClassroomId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolYearId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Period = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TakenByUserId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_attendance_sheets", x => x.Id);
                    table.UniqueConstraint("AK_attendance_sheets_SchoolId_Id", x => new { x.SchoolId, x.Id });
                    table.ForeignKey(
                        name: "FK_attendance_sheets_classrooms_SchoolId_ClassroomId",
                        columns: x => new { x.SchoolId, x.ClassroomId },
                        principalTable: "classrooms",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_attendance_sheets_school_years_SchoolId_SchoolYearId",
                        columns: x => new { x.SchoolId, x.SchoolYearId },
                        principalTable: "school_years",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_attendance_sheets_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_attendance_sheets_subjects_SchoolId_SubjectId",
                        columns: x => new { x.SchoolId, x.SubjectId },
                        principalTable: "subjects",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "student_attendances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttendanceSheetId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    LateMinutes = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_student_attendances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_student_attendances_attendance_sheets_SchoolId_AttendanceSh~",
                        columns: x => new { x.SchoolId, x.AttendanceSheetId },
                        principalTable: "attendance_sheets",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_student_attendances_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_student_attendances_students_SchoolId_StudentId",
                        columns: x => new { x.SchoolId, x.StudentId },
                        principalTable: "students",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_teachers_UserId",
                table: "teachers",
                column: "UserId",
                unique: true,
                filter: "\"UserId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_attendance_sheets_SchoolId",
                table: "attendance_sheets",
                column: "SchoolId");

            migrationBuilder.CreateIndex(
                name: "IX_attendance_sheets_SchoolId_ClassroomId_SubjectId_Date_Period",
                table: "attendance_sheets",
                columns: new[] { "SchoolId", "ClassroomId", "SubjectId", "Date", "Period" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_attendance_sheets_SchoolId_SchoolYearId",
                table: "attendance_sheets",
                columns: new[] { "SchoolId", "SchoolYearId" });

            migrationBuilder.CreateIndex(
                name: "IX_attendance_sheets_SchoolId_SubjectId",
                table: "attendance_sheets",
                columns: new[] { "SchoolId", "SubjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_student_attendances_AttendanceSheetId_StudentId",
                table: "student_attendances",
                columns: new[] { "AttendanceSheetId", "StudentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_student_attendances_SchoolId",
                table: "student_attendances",
                column: "SchoolId");

            migrationBuilder.CreateIndex(
                name: "IX_student_attendances_SchoolId_AttendanceSheetId",
                table: "student_attendances",
                columns: new[] { "SchoolId", "AttendanceSheetId" });

            migrationBuilder.CreateIndex(
                name: "IX_student_attendances_SchoolId_StudentId",
                table: "student_attendances",
                columns: new[] { "SchoolId", "StudentId" });

            migrationBuilder.AddForeignKey(
                name: "FK_teachers_users_UserId",
                table: "teachers",
                column: "UserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

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

            migrationBuilder.DropForeignKey(
                name: "FK_teachers_users_UserId",
                table: "teachers");

            migrationBuilder.DropTable(
                name: "student_attendances");

            migrationBuilder.DropTable(
                name: "attendance_sheets");

            migrationBuilder.DropIndex(
                name: "IX_teachers_UserId",
                table: "teachers");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "teachers");
        }
    }
}
