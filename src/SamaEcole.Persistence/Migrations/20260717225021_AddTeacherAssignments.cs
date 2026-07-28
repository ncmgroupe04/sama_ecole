using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Ticket JGK-D04 — table `teacher_assignments`.
    ///
    /// Mêmes deux ajouts manuels que toute table tenant (voir AddSubjects/AddTeachers pour le
    /// raisonnement détaillé) : policy RLS (AGENTS.md règle #2) et GRANT du rôle applicatif, sans
    /// DELETE (le soft delete est un UPDATE, règle #6).
    /// </summary>
    public partial class AddTeacherAssignments : Migration
    {
        private const string Table = "teacher_assignments";
        private const string AppRole = "sama_ecole_app";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "teacher_assignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeacherId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClassroomId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolYearId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_teacher_assignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_teacher_assignments_classrooms_SchoolId_ClassroomId",
                        columns: x => new { x.SchoolId, x.ClassroomId },
                        principalTable: "classrooms",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_teacher_assignments_school_years_SchoolId_SchoolYearId",
                        columns: x => new { x.SchoolId, x.SchoolYearId },
                        principalTable: "school_years",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_teacher_assignments_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_teacher_assignments_subjects_SchoolId_SubjectId",
                        columns: x => new { x.SchoolId, x.SubjectId },
                        principalTable: "subjects",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_teacher_assignments_teachers_SchoolId_TeacherId",
                        columns: x => new { x.SchoolId, x.TeacherId },
                        principalTable: "teachers",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_teacher_assignments_SchoolId",
                table: "teacher_assignments",
                column: "SchoolId");

            migrationBuilder.CreateIndex(
                name: "IX_teacher_assignments_SchoolId_ClassroomId",
                table: "teacher_assignments",
                columns: new[] { "SchoolId", "ClassroomId" });

            migrationBuilder.CreateIndex(
                name: "IX_teacher_assignments_SchoolId_SchoolYearId",
                table: "teacher_assignments",
                columns: new[] { "SchoolId", "SchoolYearId" });

            migrationBuilder.CreateIndex(
                name: "IX_teacher_assignments_SchoolId_SubjectId",
                table: "teacher_assignments",
                columns: new[] { "SchoolId", "SubjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_teacher_assignments_SchoolId_TeacherId",
                table: "teacher_assignments",
                columns: new[] { "SchoolId", "TeacherId" });

            migrationBuilder.CreateIndex(
                name: "IX_teacher_assignments_TeacherId_ClassroomId_SubjectId_SchoolY~",
                table: "teacher_assignments",
                columns: new[] { "TeacherId", "ClassroomId", "SubjectId", "SchoolYearId" },
                unique: true);

            // --- Isolation multi-tenant (AGENTS.md règle #2) ---
            migrationBuilder.Sql($"ALTER TABLE {Table} ENABLE ROW LEVEL SECURITY;");

            migrationBuilder.Sql($"""
                CREATE POLICY {Table}_tenant_isolation ON {Table}
                    USING ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid)
                    WITH CHECK ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid);
                """);

            migrationBuilder.Sql($"""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{AppRole}') THEN
                        EXECUTE 'GRANT SELECT, INSERT, UPDATE ON {Table} TO {AppRole}';
                    ELSE
                        RAISE WARNING 'Rôle % absent : l''application ne pourra pas lire la table {Table}.', '{AppRole}';
                    END IF;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"DROP POLICY IF EXISTS {Table}_tenant_isolation ON {Table};");

            migrationBuilder.DropTable(
                name: "teacher_assignments");
        }
    }
}
