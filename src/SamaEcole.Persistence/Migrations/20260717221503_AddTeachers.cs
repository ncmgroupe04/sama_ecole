using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Ticket JGK-D03 — tables `teachers` et `teacher_subjects`.
    ///
    /// Deux choses qu'EF Core ne génère PAS et qu'il faut poser à la main (voir AddSubjects pour le
    /// raisonnement détaillé) :
    ///
    ///   1. La policy RLS sur les DEUX tables. `teachers` et `teacher_subjects` rejoignent TenantTables
    ///      aux côtés de students, classrooms, school_years, subjects, school_settings, subscriptions
    ///      et matricule_sequences (AGENTS.md règle #2).
    ///
    ///   2. Le GRANT pour le rôle applicatif — DELETE n'est pas accordé : le soft delete est un UPDATE
    ///      (AGENTS.md règle #6).
    /// </summary>
    public partial class AddTeachers : Migration
    {
        private const string TeachersTable = "teachers";
        private const string TeacherSubjectsTable = "teacher_subjects";
        private const string AppRole = "sama_ecole_app";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "teachers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    Matricule = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    FullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Phone = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
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
                    table.PrimaryKey("PK_teachers", x => x.Id);
                    table.UniqueConstraint("AK_teachers_SchoolId_Id", x => new { x.SchoolId, x.Id });
                    table.ForeignKey(
                        name: "FK_teachers_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "teacher_subjects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeacherId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_teacher_subjects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_teacher_subjects_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_teacher_subjects_subjects_SchoolId_SubjectId",
                        columns: x => new { x.SchoolId, x.SubjectId },
                        principalTable: "subjects",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_teacher_subjects_teachers_SchoolId_TeacherId",
                        columns: x => new { x.SchoolId, x.TeacherId },
                        principalTable: "teachers",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_teacher_subjects_SchoolId",
                table: "teacher_subjects",
                column: "SchoolId");

            migrationBuilder.CreateIndex(
                name: "IX_teacher_subjects_SchoolId_SubjectId",
                table: "teacher_subjects",
                columns: new[] { "SchoolId", "SubjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_teacher_subjects_SchoolId_TeacherId",
                table: "teacher_subjects",
                columns: new[] { "SchoolId", "TeacherId" });

            migrationBuilder.CreateIndex(
                name: "IX_teacher_subjects_TeacherId_SubjectId",
                table: "teacher_subjects",
                columns: new[] { "TeacherId", "SubjectId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_teachers_SchoolId",
                table: "teachers",
                column: "SchoolId");

            migrationBuilder.CreateIndex(
                name: "IX_teachers_SchoolId_Matricule",
                table: "teachers",
                columns: new[] { "SchoolId", "Matricule" },
                unique: true);

            // --- Isolation multi-tenant (AGENTS.md règle #2) ---
            foreach (var table in new[] { TeachersTable, TeacherSubjectsTable })
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
            migrationBuilder.Sql($"DROP POLICY IF EXISTS {TeacherSubjectsTable}_tenant_isolation ON {TeacherSubjectsTable};");
            migrationBuilder.Sql($"DROP POLICY IF EXISTS {TeachersTable}_tenant_isolation ON {TeachersTable};");

            migrationBuilder.DropTable(
                name: "teacher_subjects");

            migrationBuilder.DropTable(
                name: "teachers");
        }
    }
}
