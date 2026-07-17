using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Ticket JGK-G01 (correctif) — les trimestres ne sont générés QUE dans
    /// CreateSchoolYearCommandHandler, à la CRÉATION d'une SchoolYear. Toute année scolaire créée
    /// avant l'introduction de cette logique (ou par un autre chemin que la commande, ex. données de
    /// démonstration antérieures) reste donc sans trimestre pour toujours — l'écran de saisie de
    /// notes ne peut alors rien proposer.
    ///
    /// Ce correctif rejoue EXACTEMENT le même découpage que BuildTerms (CreateSchoolYearCommandHandler) :
    /// trois tranches consécutives de la période, la dernière absorbant le reste de la division
    /// entière. Ne touche que les années scolaires n'ayant AUCUN trimestre — jamais celles déjà
    /// pourvues, qu'elles l'aient été par la commande ou par une exécution précédente de ce correctif.
    /// </summary>
    public partial class BackfillMissingTerms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    y RECORD;
                    total_days INT;
                    chunk INT;
                    first_end DATE;
                    second_start DATE;
                    second_end DATE;
                    third_start DATE;
                BEGIN
                    FOR y IN
                        SELECT sy."Id", sy."SchoolId", sy."StartDate", sy."EndDate"
                        FROM school_years sy
                        WHERE NOT EXISTS (SELECT 1 FROM terms t WHERE t."SchoolYearId" = sy."Id")
                    LOOP
                        total_days := (y."EndDate" - y."StartDate") + 1;
                        chunk := total_days / 3;
                        first_end := y."StartDate" + (chunk - 1);
                        second_start := first_end + 1;
                        second_end := second_start + (chunk - 1);
                        third_start := second_end + 1;

                        INSERT INTO terms
                            ("Id", "SchoolId", "SchoolYearId", "Label", "Order", "StartDate", "EndDate", "CreatedAt", "IsDeleted")
                        VALUES
                            (gen_random_uuid(), y."SchoolId", y."Id", '1er trimestre', 1, y."StartDate", first_end, now(), false),
                            (gen_random_uuid(), y."SchoolId", y."Id", '2e trimestre', 2, second_start, second_end, now(), false),
                            (gen_random_uuid(), y."SchoolId", y."Id", '3e trimestre', 3, third_start, y."EndDate", now(), false);
                    END LOOP;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Pas de retour arrière : on ne peut pas distinguer un trimestre backfillé ici d'un
            // trimestre déjà créé normalement par CreateSchoolYearCommandHandler après coup — les
            // supprimer risquerait d'effacer de vraies notes saisies entre-temps (AGENTS.md règle #6).
        }
    }
}
