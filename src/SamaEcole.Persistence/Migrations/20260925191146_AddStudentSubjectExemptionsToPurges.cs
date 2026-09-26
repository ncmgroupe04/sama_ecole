using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Inscrit <c>student_subject_exemptions</c> (migration AddStudentSubjectExemptions) dans les DEUX fonctions
    /// de purge : <c>reset_school_data</c> et <c>delete_school_year</c>. La table porte des FK RESTRICT vers
    /// <c>students</c>, <c>subjects</c> et <c>school_years</c> : sans cette migration, ces deux opérations
    /// échoueraient en 23503.
    ///
    /// Même technique que <c>AddSubjectCoefficientOverridesToPurges</c> : on lit la définition COURANTE
    /// (<c>pg_get_functiondef</c>), on y insère UNE étape à un endroit ancré, et on la recrée. Ancre absente ou
    /// ambiguë : la migration ÉCHOUE ; étape déjà présente : elle ne fait rien. <c>Down()</c> retire exactement
    /// le texte qu'<c>Up()</c> a inséré (mêmes constantes), ce qui rend le retour arrière exact.
    /// </summary>
    public partial class AddStudentSubjectExemptionsToPurges : Migration
    {
        // reset_school_data : la ligne précède « enrollments », qui précède elle-même students, subjects et
        // school_years (les trois parents en RESTRICT de la dispense).
        private const string ResetAnchor = "['enrollments',";

        private const string ResetInsert =
            "['student_subject_exemptions', 'Dispenses de matières'],\n                        ";

        // delete_school_year : la dispense est rattachée à une année ; l'étape précède la suppression des
        // inscriptions de cette année et ne vise jamais une autre année. Elle finit par l'indentation de
        // l'ancre, pour que Down() restitue le texte d'origine à l'octet près.
        private const string YearAnchor = "DELETE FROM enrollments";

        private const string YearStep =
            "DELETE FROM student_subject_exemptions\n" +
            "    WHERE \"SchoolYearId\" = p_school_year_id AND \"SchoolId\" = p_school_id;\n" +
            "    GET DIAGNOSTICS v_deleted = ROW_COUNT;\n" +
            "    label := 'Dispenses de matières'; rows_deleted := v_deleted; RETURN NEXT;\n" +
            "\n" +
            "    ";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(InsertBefore("reset_school_data(uuid)", ResetAnchor, ResetInsert));
            migrationBuilder.Sql(InsertBefore("delete_school_year(uuid, uuid)", YearAnchor, YearStep));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(Remove("reset_school_data(uuid)", ResetInsert + ResetAnchor, ResetAnchor));
            migrationBuilder.Sql(Remove("delete_school_year(uuid, uuid)", YearStep + YearAnchor, YearAnchor));
        }

        // Textes passés dans des littéraux SQL dollar-quotés : aucun échappement d'apostrophe à gérer.
        private static string InsertBefore(string function, string anchor, string inserted) => $$"""
            DO $patch$
            DECLARE
                v_def    text;
                v_anchor text := $a${{anchor}}$a$;
                v_ins    text := $i${{inserted}}$i$;
            BEGIN
                v_def := pg_get_functiondef('{{function}}'::regprocedure);

                IF position('student_subject_exemptions' IN v_def) > 0 THEN
                    RETURN;
                END IF;

                IF array_length(string_to_array(v_def, v_anchor), 1) <> 2 THEN
                    RAISE EXCEPTION '{{function}} : ancre % introuvable ou ambiguë — la fonction a changé de forme, corriger cette migration.', v_anchor;
                END IF;

                EXECUTE replace(v_def, v_anchor, v_ins || v_anchor);
            END
            $patch$;
            """;

        private static string Remove(string function, string with, string without) => $$"""
            DO $patch$
            DECLARE
                v_def text;
            BEGIN
                v_def := pg_get_functiondef('{{function}}'::regprocedure);

                IF position('student_subject_exemptions' IN v_def) = 0 THEN
                    RETURN;
                END IF;

                EXECUTE replace(v_def, $w${{with}}$w$, $o${{without}}$o$);
            END
            $patch$;
            """;
    }
}
