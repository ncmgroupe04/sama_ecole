using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Réconcilie UNE FOIS les séjours Internat avec l'ancien modèle (<c>enrollments.BoardingStatus/RoomId</c>) : la
    /// reprise du lot A (<c>AddBoardingDormitoryModel</c>) était un instantané, et l'ancien écran a continué d'écrire
    /// depuis. L'ancien modèle prévaut pour les séjours ; la structure (pavillons, chambres, lits) est additive.
    /// Le script est <see cref="BoardingReconciliationSql.Script"/>, testé tel quel par <c>BoardingReconciliationTests</c>.
    ///
    /// NE JAMAIS REJOUER après la bascule : les colonnes héritées ne sont plus maintenues, le rejouer fermerait de vrais
    /// séjours. C'est aussi pourquoi <c>Down()</c> est volontairement vide et qu'aucune fonction SQL durable n'est créée.
    /// PRÉREQUIS DE DÉPLOIEMENT : arrêter toutes les anciennes instances AVANT d'appliquer cette migration, sinon une
    /// instance du lot A/B écrirait encore des colonnes que plus personne ne lit.
    /// </summary>
    public partial class ReconcileBoardingWithLegacyModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
            => migrationBuilder.Sql(BoardingReconciliationSql.Script);

        /// <inheritdoc />
        // Données seulement : rien à défaire. Les tables restent celles du lot A.
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
