using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Recalcule <c>classrooms."Cycle"</c> depuis <c>"Level"</c> pour les classes DÉJÀ en base.
    ///
    /// La colonne avait été ajoutée par AddCycleToClassrooms avec un DEFAULT « College », mais aucune
    /// commande ne l'a jamais renseignée : toutes les classes de tous les établissements sont restées
    /// sur ce défaut, y compris celles de niveau Primaire. Conséquences observées sur un bulletin de
    /// CM2 : en-tête « COLLÈGE DE », notes sur /20 au lieu de /10, et moyenne pondérée par des
    /// coefficients que le primaire n'utilise pas.
    ///
    /// Corriger le code (ClassroomCycle, branché sur Create/UpdateClassroomCommandHandler) ne suffit
    /// pas : sans cette reprise, seules les classes créées ou modifiées APRÈS le déploiement seraient
    /// correctes. La règle appliquée ici est exactement celle de ClassroomCycle.CycleFor.
    ///
    /// Migration de DONNÉES, volontairement sans schéma : elle s'applique à tous les tenants (elle
    /// tourne sous le rôle propriétaire, hors RLS), ce qui est bien l'intention — le défaut erroné les
    /// touche tous.
    /// </summary>
    public partial class BackfillClassroomCycleFromLevel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Les niveaux non reconnus gardent leur valeur (ELSE "Cycle") : on ne devine pas un cycle
            // pour une nomenclature inconnue, et basculer par erreur une classe du secondaire en /10
            // réinterpréterait des notes DÉJÀ saisies sur /20. Les variantes accentuées et non
            // accentuées sont listées explicitement : l'extension unaccent n'est pas garantie présente.
            migrationBuilder.Sql("""
                UPDATE classrooms SET "Cycle" = CASE
                    WHEN upper("Level") IN ('CRÈCHE', 'CRECHE', 'MATERNELLE', 'PRÉSCOLAIRE', 'PRESCOLAIRE')
                        THEN 'Maternelle'
                    WHEN upper("Level") IN ('PRIMAIRE', 'ÉLÉMENTAIRE', 'ELEMENTAIRE',
                                            'ÉCOLE ÉLÉMENTAIRE', 'ECOLE ELEMENTAIRE',
                                            'ÉCOLE PRIMAIRE', 'ECOLE PRIMAIRE')
                        THEN 'Primaire'
                    WHEN upper("Level") IN ('COLLÈGE', 'COLLEGE', 'MOYEN', 'CEM')
                        THEN 'College'
                    WHEN upper("Level") IN ('LYCÉE', 'LYCEE', 'SECONDAIRE')
                        THEN 'Lycee'
                    ELSE "Cycle"
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Volontairement VIDE. La valeur d'avant reprise était « College » pour absolument toutes
            // les lignes — la restaurer écraserait aussi les cycles corrects saisis depuis, et
            // rétablirait le bug qu'on vient de corriger. Un retour arrière du schéma n'a de toute
            // façon rien à défaire ici : cette migration ne touche aucune structure.
        }
    }
}
