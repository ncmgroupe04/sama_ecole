using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Inscrit <c>subject_coefficient_overrides</c> (migration AddSubjectCoefficientOverrides) dans les
    /// DEUX fonctions de purge : <c>reset_school_data</c> (« Réinitialiser les données ») et
    /// <c>delete_school_year</c> (suppression d'une année scolaire en mode test). La table porte des FK
    /// RESTRICT vers <c>subjects</c>, <c>classrooms</c> et <c>school_years</c> : sans cette migration,
    /// ces deux opérations échoueraient en 23503 — le bug déjà rencontré pour Examens, Inventaire,
    /// Cahier de texte et Coran (voir AddQuranModuleToResetSchoolData).
    ///
    /// TECHNIQUE — différente de ces migrations-là, et voulue : au lieu de recopier en entier le corps de
    /// chaque fonction (150 lignes à transcrire sans faute), on lit sa définition COURANTE
    /// (<c>pg_get_functiondef</c>), on y insère UNE étape à un endroit ancré, et on la recrée. Le corps
    /// existant est donc conservé tel quel par construction, quelle que soit la migration qui l'a écrit en
    /// dernier. Si l'ancre est introuvable ou ambiguë, la migration ÉCHOUE plutôt que de patcher au
    /// hasard ; si l'étape est déjà là, elle ne fait rien (idempotente). Les privilèges (REVOKE PUBLIC,
    /// GRANT EXECUTE au rôle applicatif) survivent à un CREATE OR REPLACE. Down() retire exactement ce
    /// qu'Up() a inséré.
    ///
    /// À propos : une future migration qui RECOPIERAIT le corps complet d'une de ces fonctions à partir
    /// d'une version antérieure supprimerait silencieusement cette étape — c'est ce que
    /// Every_Restrict_Foreign_Key_Into_A_Purged_Table_Must_Come_From_A_Purged_Table_Too détecte pour
    /// reset_school_data, et DeleteSchoolYearTests / CoefficientOverrideIsolationTests pour l'année.
    /// </summary>
    public partial class AddSubjectCoefficientOverridesToPurges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // reset_school_data : la ligne est insérée juste AVANT « subjects » (donc avant « classrooms »,
            // qui vient après) — la FK impose d'effacer l'enfant avant ses deux parents.
            migrationBuilder.Sql("""
                DO $patch$
                DECLARE
                    v_def    text;
                    v_anchor text := '[''subjects'',';
                BEGIN
                    v_def := pg_get_functiondef('reset_school_data(uuid)'::regprocedure);

                    IF position('subject_coefficient_overrides' IN v_def) > 0 THEN
                        RETURN;
                    END IF;

                    IF array_length(string_to_array(v_def, v_anchor), 1) <> 2 THEN
                        RAISE EXCEPTION 'reset_school_data : ancre % introuvable ou ambiguë — la fonction a changé de forme, corriger cette migration.', v_anchor;
                    END IF;

                    EXECUTE replace(v_def, v_anchor,
                        '[''subject_coefficient_overrides'', ''Surcharges de coefficients''],' || chr(10)
                        || '                        ' || v_anchor);
                END
                $patch$;
                """);

            // delete_school_year : l'étape est insérée juste AVANT la suppression de l'année elle-même
            // (FK RESTRICT vers school_years). Elle ne vise que l'année demandée, jamais les autres.
            migrationBuilder.Sql("""
                DO $patch$
                DECLARE
                    v_def    text;
                    v_anchor text := 'DELETE FROM school_years';
                    v_step   text := $step$DELETE FROM subject_coefficient_overrides
                    WHERE "SchoolYearId" = p_school_year_id AND "SchoolId" = p_school_id;
                    GET DIAGNOSTICS v_deleted = ROW_COUNT;
                    label := 'Surcharges de coefficients'; rows_deleted := v_deleted; RETURN NEXT;

                    $step$;
                BEGIN
                    v_def := pg_get_functiondef('delete_school_year(uuid, uuid)'::regprocedure);

                    IF position('subject_coefficient_overrides' IN v_def) > 0 THEN
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $patch$
                DECLARE
                    v_def    text;
                    v_insert text := '[''subject_coefficient_overrides'', ''Surcharges de coefficients''],' || chr(10)
                                     || '                        ';
                BEGIN
                    v_def := pg_get_functiondef('reset_school_data(uuid)'::regprocedure);

                    IF position('subject_coefficient_overrides' IN v_def) = 0 THEN
                        RETURN;
                    END IF;

                    EXECUTE replace(v_def, v_insert, '');
                END
                $patch$;
                """);

            migrationBuilder.Sql("""
                DO $patch$
                DECLARE
                    v_def  text;
                    v_step text := $step$DELETE FROM subject_coefficient_overrides
                    WHERE "SchoolYearId" = p_school_year_id AND "SchoolId" = p_school_id;
                    GET DIAGNOSTICS v_deleted = ROW_COUNT;
                    label := 'Surcharges de coefficients'; rows_deleted := v_deleted; RETURN NEXT;

                    $step$;
                BEGIN
                    v_def := pg_get_functiondef('delete_school_year(uuid, uuid)'::regprocedure);

                    IF position('subject_coefficient_overrides' IN v_def) = 0 THEN
                        RETURN;
                    END IF;

                    EXECUTE replace(v_def, v_step, '');
                END
                $patch$;
                """);
        }
    }
}
