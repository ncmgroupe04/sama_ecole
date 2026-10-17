using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Franco-Arabe Bloc 5b : saisies du cahier de roulement (<c>roll_book_entries</c>, une par élève et demi-journée).
    /// La RLS est ajoutée à la main (AGENTS.md règle #2) ; le rôle applicatif n'a ni DELETE. La table référence students,
    /// classrooms et school_years en RESTRICT : elle est inscrite dans reset_school_data (avant students) et dans
    /// delete_school_year (avant l'année). Ancre absente ou ambiguë : la migration ÉCHOUE ; étape déjà présente : rien.
    /// </summary>
    public partial class AddRollBookEntries : Migration
    {
        private const string Table = "roll_book_entries";

        private const string AppRole = "sama_ecole_app";

        // reset_school_data : les saisies précèdent students (FK RESTRICT) ; même ancre que le journal Hizb.
        private const string StudentsAnchor = "['students',";

        private const string ResetInsert =
            "['roll_book_entries', 'Saisies du cahier de roulement'],\n                        ";

        // delete_school_year : les saisies de l'année partent avant l'année elle-même ; même ancre que les fermetures.
        private const string YearAnchor = "DELETE FROM school_years";

        private const string YearStep =
            "DELETE FROM roll_book_entries\n" +
            "    WHERE \"SchoolYearId\" = p_school_year_id AND \"SchoolId\" = p_school_id;\n" +
            "    GET DIAGNOSTICS v_deleted = ROW_COUNT;\n" +
            "    label := 'Saisies du cahier de roulement'; rows_deleted := v_deleted; RETURN NEXT;\n" +
            "\n" +
            "    ";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "roll_book_entries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolYearId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClassroomId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    HalfDay = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    LateMinutes = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    RecordedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_roll_book_entries", x => x.Id);
                    table.CheckConstraint("CK_roll_book_entries_late_minutes", "\"LateMinutes\" BETWEEN 0 AND 600");
                    table.CheckConstraint("CK_roll_book_entries_minutes_need_late", "\"Status\" = 'Late' OR \"LateMinutes\" = 0");
                    table.ForeignKey(
                        name: "FK_roll_book_entries_classrooms_SchoolId_ClassroomId",
                        columns: x => new { x.SchoolId, x.ClassroomId },
                        principalTable: "classrooms",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_roll_book_entries_school_years_SchoolId_SchoolYearId",
                        columns: x => new { x.SchoolId, x.SchoolYearId },
                        principalTable: "school_years",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_roll_book_entries_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_roll_book_entries_students_SchoolId_StudentId",
                        columns: x => new { x.SchoolId, x.StudentId },
                        principalTable: "students",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_roll_book_entries_SchoolId_ClassroomId_Date",
                table: "roll_book_entries",
                columns: new[] { "SchoolId", "ClassroomId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_roll_book_entries_SchoolId_SchoolYearId",
                table: "roll_book_entries",
                columns: new[] { "SchoolId", "SchoolYearId" });

            migrationBuilder.CreateIndex(
                name: "IX_roll_book_entries_SchoolId_StudentId_Date_HalfDay",
                table: "roll_book_entries",
                columns: new[] { "SchoolId", "StudentId", "Date", "HalfDay" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            // --- Isolation multi-tenant (AGENTS.md règle #2) : EF ne génère jamais la RLS ---
            migrationBuilder.Sql($"ALTER TABLE \"{Table}\" ENABLE ROW LEVEL SECURITY;");

            migrationBuilder.Sql($$"""
                CREATE POLICY {{Table}}_tenant_isolation ON "{{Table}}"
                    USING ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid)
                    WITH CHECK ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid);
                """);

            // SELECT / INSERT / UPDATE, jamais DELETE : le retour au statut calculé est une suppression logique
            // (règle #6). Les purges passent par reset_school_data / delete_school_year (SECURITY DEFINER).
            migrationBuilder.Sql($$"""
                DO $inner$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{{AppRole}}') THEN
                        EXECUTE 'GRANT SELECT, INSERT, UPDATE ON "{{Table}}" TO {{AppRole}}';
                    ELSE
                        RAISE WARNING 'Rôle % absent : l''application ne pourra pas lire la table {{Table}}.', '{{AppRole}}';
                    END IF;
                END
                $inner$;
                """);

            // --- Purges : la table référence students, classrooms et school_years (FK RESTRICT) ---
            migrationBuilder.Sql(InsertBefore("reset_school_data(uuid)", StudentsAnchor, ResetInsert, Table));
            migrationBuilder.Sql(InsertBefore("delete_school_year(uuid, uuid)", YearAnchor, YearStep, Table));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(Remove("delete_school_year(uuid, uuid)", YearStep + YearAnchor, YearAnchor, Table));
            migrationBuilder.Sql(Remove("reset_school_data(uuid)", ResetInsert + StudentsAnchor, StudentsAnchor, Table));

            migrationBuilder.Sql($"DROP POLICY IF EXISTS {Table}_tenant_isolation ON \"{Table}\";");

            migrationBuilder.DropTable(
                name: "roll_book_entries");
        }

        // Même technique que AddHizbEvaluationJournal : on lit la définition COURANTE de la fonction (pg_get_functiondef),
        // on y insère UNE étape à un endroit ancré, et on la recrée. Ancre absente ou ambiguë : la migration ÉCHOUE ;
        // étape déjà présente : elle ne fait rien.
        private static string InsertBefore(string function, string anchor, string inserted, string marker) => $$"""
            DO $patch$
            DECLARE
                v_def    text;
                v_anchor text := $a${{anchor}}$a$;
                v_ins    text := $i${{inserted}}$i$;
            BEGIN
                v_def := pg_get_functiondef('{{function}}'::regprocedure);

                IF position('{{marker}}' IN v_def) > 0 THEN
                    RETURN;
                END IF;

                IF array_length(string_to_array(v_def, v_anchor), 1) <> 2 THEN
                    RAISE EXCEPTION '{{function}} : ancre % introuvable ou ambiguë — la fonction a changé de forme, corriger cette migration.', v_anchor;
                END IF;

                EXECUTE replace(v_def, v_anchor, v_ins || v_anchor);
            END
            $patch$;
            """;

        private static string Remove(string function, string with, string without, string marker) => $$"""
            DO $patch$
            DECLARE
                v_def text;
            BEGIN
                v_def := pg_get_functiondef('{{function}}'::regprocedure);

                IF position('{{marker}}' IN v_def) = 0 THEN
                    RETURN;
                END IF;

                EXECUTE replace(v_def, $w${{with}}$w$, $o${{without}}$o$);
            END
            $patch$;
            """;
    }
}
