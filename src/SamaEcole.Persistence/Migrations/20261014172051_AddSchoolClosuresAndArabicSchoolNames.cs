using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Franco-Arabe Bloc 5a : noms arabes de l'établissement (<c>schools.NameAr</c>, <c>InspectionAcademieAr</c>,
    /// <c>InspectionEducationFormationAr</c> — nullables, sans défaut requis, donc sans risque pour
    /// <c>provision_school_director</c>) et calendrier de fermeture (<c>school_closures</c>).
    /// La RLS est ajoutée à la main (AGENTS.md règle #2) ; le rôle applicatif n'a ni DELETE. La fonction
    /// <c>delete_school_year</c> est corrigée pour retirer les fermetures de l'année avant l'année elle-même
    /// (FK RESTRICT) ; <c>reset_school_data</c> garde les années, donc leur calendrier. Ancre absente ou
    /// ambiguë : la migration ÉCHOUE ; étape déjà présente : elle ne fait rien.
    /// </summary>
    public partial class AddSchoolClosuresAndArabicSchoolNames : Migration
    {
        private const string Table = "school_closures";

        private const string AppRole = "sama_ecole_app";

        // delete_school_year : l'étape précède la suppression de l'année elle-même (FK RESTRICT vers school_years) et ne
        // vise que l'année demandée. Elle finit par l'indentation de l'ancre pour que Down() restitue le texte d'origine.
        private const string YearAnchor = "DELETE FROM school_years";

        private const string YearStep =
            "DELETE FROM school_closures\n" +
            "    WHERE \"SchoolYearId\" = p_school_year_id AND \"SchoolId\" = p_school_id;\n" +
            "    GET DIAGNOSTICS v_deleted = ROW_COUNT;\n" +
            "    label := 'Fermetures du calendrier'; rows_deleted := v_deleted; RETURN NEXT;\n" +
            "\n" +
            "    ";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InspectionAcademieAr",
                table: "schools",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InspectionEducationFormationAr",
                table: "schools",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NameAr",
                table: "schools",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "school_closures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolYearId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Label = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    LabelAr = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
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
                    table.PrimaryKey("PK_school_closures", x => x.Id);
                    table.CheckConstraint("CK_school_closures_period", "\"EndDate\" >= \"StartDate\"");
                    table.ForeignKey(
                        name: "FK_school_closures_school_years_SchoolId_SchoolYearId",
                        columns: x => new { x.SchoolId, x.SchoolYearId },
                        principalTable: "school_years",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_school_closures_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_school_closures_SchoolId_SchoolYearId_StartDate",
                table: "school_closures",
                columns: new[] { "SchoolId", "SchoolYearId", "StartDate" });

            // --- Isolation multi-tenant (AGENTS.md règle #2) : EF ne génère jamais la RLS ---
            migrationBuilder.Sql($"ALTER TABLE \"{Table}\" ENABLE ROW LEVEL SECURITY;");

            migrationBuilder.Sql($$"""
                CREATE POLICY {{Table}}_tenant_isolation ON "{{Table}}"
                    USING ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid)
                    WITH CHECK ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid);
                """);

            // SELECT / INSERT / UPDATE, jamais DELETE : la suppression est logique (règle #6). La purge
            // d'une année passe par delete_school_year (SECURITY DEFINER), pas par un GRANT DELETE.
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

            // --- Suppression d'une année scolaire : ses fermetures partent avant elle (FK RESTRICT) ---
            // reset_school_data n'est PAS touchée : elle conserve les années scolaires (et donc leur
            // calendrier), comme les trimestres.
            migrationBuilder.Sql(InsertBefore("delete_school_year(uuid, uuid)", YearAnchor, YearStep, Table));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(Remove("delete_school_year(uuid, uuid)", YearStep + YearAnchor, YearAnchor, Table));

            migrationBuilder.Sql($"DROP POLICY IF EXISTS {Table}_tenant_isolation ON \"{Table}\";");

            migrationBuilder.DropTable(
                name: "school_closures");

            migrationBuilder.DropColumn(
                name: "InspectionAcademieAr",
                table: "schools");

            migrationBuilder.DropColumn(
                name: "InspectionEducationFormationAr",
                table: "schools");

            migrationBuilder.DropColumn(
                name: "NameAr",
                table: "schools");
        }

        // Même technique que AddHizbEvaluationJournal : on lit la définition COURANTE de la fonction (pg_get_functiondef),
        // on y insère UNE étape à un endroit ancré, et on la recrée.
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
