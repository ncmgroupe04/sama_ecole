using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// Séries, matières et options (Évolution N°6) — deux tables tenant : class_subjects (programme d'une classe)
    /// et student_subject_enrollments (option retenue par un élève pour une année).
    ///
    /// EF ne génère jamais les policies RLS : ajoutées à la main ci-dessous (AGENTS.md règle #2), comme pour
    /// AddSubjectCoefficientOverrides. RlsCoverageTests échoue si elle manque. UPDATE accordé (une matière se
    /// désactive, un choix se supprime logiquement), aucun DELETE (règle #6).
    ///
    /// Les deux fonctions de purge sont corrigées dans la MÊME migration, par la technique de
    /// AddSubjectCoefficientOverridesToPurges (lecture de la définition courante, insertion ancrée, échec si
    /// l'ancre est introuvable ou ambiguë, idempotence) : sans elles, « Réinitialiser les données » et la
    /// suppression d'une année échoueraient en 23503 (FK RESTRICT vers students, subjects, classrooms, school_years).
    /// </summary>
    public partial class AddClassSubjectsAndOptions : Migration
    {
        private static readonly string[] TenantTables = ["class_subjects", "student_subject_enrollments"];

        private const string AppRole = "sama_ecole_app";

        // reset_school_data : les choix d'options avant « students » (leur parent), le programme des classes avant
        // « subject_coefficient_overrides » — donc avant « subjects » et « classrooms », qui viennent après.
        private const string ResetEnrollmentsLine =
            "['student_subject_enrollments', 'Options choisies par les élèves'],";
        private const string ResetClassSubjectsLine =
            "['class_subjects', 'Matières des classes'],";

        // delete_school_year : seuls les choix d'options sont rattachés à une année (le programme ne l'est pas).
        private const string YearStep = """
            DELETE FROM student_subject_enrollments
                    WHERE "SchoolYearId" = p_school_year_id AND "SchoolId" = p_school_id;
                    GET DIAGNOSTICS v_deleted = ROW_COUNT;
                    label := 'Options choisies par les élèves'; rows_deleted := v_deleted; RETURN NEXT;

                    
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "class_subjects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClassroomId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    OptionGroup = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    IsCustom = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_class_subjects", x => x.Id);
                    table.UniqueConstraint("AK_class_subjects_SchoolId_Id", x => new { x.SchoolId, x.Id });
                    table.ForeignKey(
                        name: "FK_class_subjects_classrooms_SchoolId_ClassroomId",
                        columns: x => new { x.SchoolId, x.ClassroomId },
                        principalTable: "classrooms",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_class_subjects_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_class_subjects_subjects_SchoolId_SubjectId",
                        columns: x => new { x.SchoolId, x.SubjectId },
                        principalTable: "subjects",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "student_subject_enrollments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClassSubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolYearId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_student_subject_enrollments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_student_subject_enrollments_class_subjects_SchoolId_ClassSu~",
                        columns: x => new { x.SchoolId, x.ClassSubjectId },
                        principalTable: "class_subjects",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_student_subject_enrollments_school_years_SchoolId_SchoolYea~",
                        columns: x => new { x.SchoolId, x.SchoolYearId },
                        principalTable: "school_years",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_student_subject_enrollments_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_student_subject_enrollments_students_SchoolId_StudentId",
                        columns: x => new { x.SchoolId, x.StudentId },
                        principalTable: "students",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_class_subjects_SchoolId",
                table: "class_subjects",
                column: "SchoolId");

            migrationBuilder.CreateIndex(
                name: "IX_class_subjects_SchoolId_ClassroomId",
                table: "class_subjects",
                columns: new[] { "SchoolId", "ClassroomId" });

            migrationBuilder.CreateIndex(
                name: "IX_class_subjects_SchoolId_SubjectId",
                table: "class_subjects",
                columns: new[] { "SchoolId", "SubjectId" });

            migrationBuilder.CreateIndex(
                name: "UX_class_subjects_classroom_subject",
                table: "class_subjects",
                columns: new[] { "ClassroomId", "SubjectId" },
                unique: true,
                filter: "NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_student_subject_enrollments_SchoolId",
                table: "student_subject_enrollments",
                column: "SchoolId");

            migrationBuilder.CreateIndex(
                name: "IX_student_subject_enrollments_SchoolId_ClassSubjectId",
                table: "student_subject_enrollments",
                columns: new[] { "SchoolId", "ClassSubjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_student_subject_enrollments_SchoolId_SchoolYearId",
                table: "student_subject_enrollments",
                columns: new[] { "SchoolId", "SchoolYearId" });

            migrationBuilder.CreateIndex(
                name: "IX_student_subject_enrollments_SchoolId_StudentId",
                table: "student_subject_enrollments",
                columns: new[] { "SchoolId", "StudentId" });

            migrationBuilder.CreateIndex(
                name: "UX_student_subject_enrollments_choice",
                table: "student_subject_enrollments",
                columns: new[] { "StudentId", "ClassSubjectId", "SchoolYearId" },
                unique: true,
                filter: "NOT \"IsDeleted\"");

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

            // --- Purges : reset_school_data ---
            migrationBuilder.Sql(PatchResetSchoolData("['students',", ResetEnrollmentsLine, "student_subject_enrollments"));
            migrationBuilder.Sql(PatchResetSchoolData("['subject_coefficient_overrides',", ResetClassSubjectsLine, "class_subjects"));

            // --- Purges : delete_school_year (étape insérée juste avant la suppression de l'année) ---
            migrationBuilder.Sql($$"""
                DO $patch$
                DECLARE
                    v_def    text;
                    v_anchor text := 'DELETE FROM school_years';
                    v_step   text := $step${{YearStep}}$step$;
                BEGIN
                    v_def := pg_get_functiondef('delete_school_year(uuid, uuid)'::regprocedure);

                    IF position('student_subject_enrollments' IN v_def) > 0 THEN
                        RETURN;
                    END IF;

                    IF array_length(string_to_array(v_def, v_anchor), 1) <> 2 THEN
                        RAISE EXCEPTION 'delete_school_year : ancre % introuvable ou ambiguë — la fonction a changé de forme, corriger cette migration.', v_anchor;
                    END IF;

                    EXECUTE replace(v_def, v_anchor, v_step || v_anchor);
                END
                $patch$;
                """);
        }

        /// <summary>
        /// Insère <paramref name="line"/> juste avant <paramref name="anchor"/> dans reset_school_data. Échoue si
        /// l'ancre est introuvable ou ambiguë ; ne fait rien si <paramref name="marker"/> y figure déjà.
        /// </summary>
        private static string PatchResetSchoolData(string anchor, string line, string marker) => $$"""
            DO $patch$
            DECLARE
                v_def    text;
                v_anchor text := {{Quote(anchor)}};
            BEGIN
                v_def := pg_get_functiondef('reset_school_data(uuid)'::regprocedure);

                IF position({{Quote("'" + marker + "'")}} IN v_def) > 0 THEN
                    RETURN;
                END IF;

                IF array_length(string_to_array(v_def, v_anchor), 1) <> 2 THEN
                    RAISE EXCEPTION 'reset_school_data : ancre % introuvable ou ambiguë — la fonction a changé de forme, corriger cette migration.', v_anchor;
                END IF;

                EXECUTE replace(v_def, v_anchor,
                    {{Quote(line)}} || chr(10) || '                        ' || v_anchor);
            END
            $patch$;
            """;

        /// <summary>Retire exactement ce que <see cref="PatchResetSchoolData"/> a inséré.</summary>
        private static string UnpatchResetSchoolData(string line) => $$"""
            DO $patch$
            DECLARE
                v_def text;
            BEGIN
                v_def := pg_get_functiondef('reset_school_data(uuid)'::regprocedure);
                EXECUTE replace(v_def, {{Quote(line)}} || chr(10) || '                        ', '');
            END
            $patch$;
            """;

        /// <summary>Littéral SQL : apostrophes doublées.</summary>
        private static string Quote(string value) => "'" + value.Replace("'", "''") + "'";

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(UnpatchResetSchoolData(ResetClassSubjectsLine));
            migrationBuilder.Sql(UnpatchResetSchoolData(ResetEnrollmentsLine));

            migrationBuilder.Sql($$"""
                DO $patch$
                DECLARE
                    v_def  text;
                    v_step text := $step${{YearStep}}$step$;
                BEGIN
                    v_def := pg_get_functiondef('delete_school_year(uuid, uuid)'::regprocedure);
                    EXECUTE replace(v_def, v_step, '');
                END
                $patch$;
                """);

            foreach (var table in TenantTables)
            {
                migrationBuilder.Sql($"DROP POLICY IF EXISTS {table}_tenant_isolation ON \"{table}\";");
            }

            migrationBuilder.DropTable(
                name: "student_subject_enrollments");

            migrationBuilder.DropTable(
                name: "class_subjects");
        }
    }
}
