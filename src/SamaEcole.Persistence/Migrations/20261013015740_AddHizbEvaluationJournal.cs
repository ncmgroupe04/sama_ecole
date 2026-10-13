using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Journal append-only des évaluations Hizb (<c>student_hizb_evaluations</c>) : historique et courbe d'évolution. Comme pour
    /// AddDaaraHalqaAndHizbTracking, EF ne génère jamais la RLS : elle est ajoutée à la main (AGENTS.md règle #2). Le rôle
    /// applicatif n'a que SELECT et INSERT. La table est inscrite dans reset_school_data (FK RESTRICT vers students). Une ligne de
    /// départ est créée pour chaque état courant déjà saisi ; le passé antérieur n'est pas reconstituable.
    /// </summary>
    public partial class AddHizbEvaluationJournal : Migration
    {
        private const string Table = "student_hizb_evaluations";

        private const string AppRole = "sama_ecole_app";

        // reset_school_data : le journal précède students (FK RESTRICT) ; même ancre que le suivi par Hizb.
        private const string StudentsAnchor = "['students',";

        private const string JournalInsert =
            "['student_hizb_evaluations', 'Journal des évaluations Hizb'],\n                        ";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "student_hizb_evaluations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    HizbNumber = table.Column<int>(type: "integer", nullable: false),
                    QuartersBefore = table.Column<int>(type: "integer", nullable: false),
                    QuartersAfter = table.Column<int>(type: "integer", nullable: false),
                    Rating = table.Column<int>(type: "integer", nullable: true),
                    TotalQuartersAfter = table.Column<int>(type: "integer", nullable: false),
                    EvaluatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EvaluatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    InstructorId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsBaseline = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_student_hizb_evaluations", x => x.Id);
                    table.CheckConstraint("CK_student_hizb_evaluations_hizb_range", "\"HizbNumber\" BETWEEN 1 AND 60");
                    table.CheckConstraint("CK_student_hizb_evaluations_quarters_range", "\"QuartersBefore\" BETWEEN 0 AND 4 AND \"QuartersAfter\" BETWEEN 0 AND 4");
                    table.CheckConstraint("CK_student_hizb_evaluations_rating_needs_quarters", "\"QuartersAfter\" > 0 OR \"Rating\" IS NULL");
                    table.CheckConstraint("CK_student_hizb_evaluations_rating_range", "\"Rating\" IS NULL OR \"Rating\" BETWEEN 1 AND 5");
                    table.CheckConstraint("CK_student_hizb_evaluations_total_range", "\"TotalQuartersAfter\" BETWEEN 0 AND 240");
                    table.ForeignKey(
                        name: "FK_student_hizb_evaluations_instructors_SchoolId_InstructorId",
                        columns: x => new { x.SchoolId, x.InstructorId },
                        principalTable: "instructors",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_student_hizb_evaluations_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_student_hizb_evaluations_students_SchoolId_StudentId",
                        columns: x => new { x.SchoolId, x.StudentId },
                        principalTable: "students",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_student_hizb_evaluations_instructor_time",
                table: "student_hizb_evaluations",
                columns: new[] { "SchoolId", "InstructorId", "EvaluatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_student_hizb_evaluations_student_time",
                table: "student_hizb_evaluations",
                columns: new[] { "SchoolId", "StudentId", "EvaluatedAt" });

            // --- Reprise : une ligne de départ par état courant déjà saisi ---
            // Le passé antérieur n'est pas reconstituable : le journal commence ici. `TotalQuartersAfter` est le cumul des
            // quarts de l'élève dans l'ordre des dates d'évaluation, de sorte que la courbe démarre au bon niveau. La date
            // retenue est la dernière évaluation connue (à défaut, la date de mise à jour puis de création de la ligne).
            // Exécutée AVANT l'activation de la RLS : la migration tourne sous le rôle propriétaire.
            migrationBuilder.Sql("""
                INSERT INTO student_hizb_evaluations
                    ("Id", "SchoolId", "StudentId", "HizbNumber", "QuartersBefore", "QuartersAfter", "Rating",
                     "TotalQuartersAfter", "EvaluatedAt", "EvaluatedByUserId", "InstructorId", "IsBaseline",
                     "CreatedAt", "IsDeleted")
                SELECT gen_random_uuid(), h."SchoolId", h."StudentId", h."HizbNumber", 0, h."CompletedQuarters", h."Rating",
                       SUM(h."CompletedQuarters") OVER (
                           PARTITION BY h."StudentId"
                           ORDER BY COALESCE(h."LastEvaluatedAt", h."UpdatedAt", h."CreatedAt"), h."HizbNumber"),
                       COALESCE(h."LastEvaluatedAt", h."UpdatedAt", h."CreatedAt"),
                       NULL, s."InstructorId", TRUE, now(), FALSE
                FROM student_hizb_statuses h
                JOIN students s ON s."SchoolId" = h."SchoolId" AND s."Id" = h."StudentId"
                WHERE h."CompletedQuarters" > 0 AND NOT h."IsDeleted";
                """);

            // --- Isolation multi-tenant (AGENTS.md règle #2) ---
            migrationBuilder.Sql($"ALTER TABLE \"{Table}\" ENABLE ROW LEVEL SECURITY;");

            migrationBuilder.Sql($$"""
                CREATE POLICY {{Table}}_tenant_isolation ON "{{Table}}"
                    USING ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid)
                    WITH CHECK ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid);
                """);

            // APPEND-ONLY : SELECT et INSERT seulement. Ni UPDATE ni DELETE, même logique : le journal fait foi, une saisie
            // fausse se corrige par une nouvelle saisie.
            migrationBuilder.Sql($$"""
                DO $inner$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{{AppRole}}') THEN
                        EXECUTE 'GRANT SELECT, INSERT ON "{{Table}}" TO {{AppRole}}';
                    ELSE
                        RAISE WARNING 'Rôle % absent : l''application ne pourra pas lire la table {{Table}}.', '{{AppRole}}';
                    END IF;
                END
                $inner$;
                """);

            // --- Purge « Réinitialiser les données » : le journal précède students (FK RESTRICT) ---
            migrationBuilder.Sql(InsertBefore("reset_school_data(uuid)", StudentsAnchor, JournalInsert, Table));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(Remove("reset_school_data(uuid)", JournalInsert + StudentsAnchor, StudentsAnchor, Table));

            migrationBuilder.Sql($"DROP POLICY IF EXISTS {Table}_tenant_isolation ON \"{Table}\";");

            migrationBuilder.DropTable(
                name: "student_hizb_evaluations");
        }

        // Même technique que AddDaaraHalqaAndHizbTracking : on lit la définition COURANTE de la fonction (pg_get_functiondef),
        // on y insère UNE étape à un endroit ancré, et on la recrée. Ancre absente ou ambiguë : la migration ÉCHOUE ; étape déjà
        // présente : elle ne fait rien.
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
