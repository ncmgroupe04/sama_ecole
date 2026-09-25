using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// Seuils du conseil de classe (Évolution N°7) : six colonnes numeric(4,2) sur school_settings, avec les valeurs
    /// de la spécification MEN en défaut de base — les lignes existantes les reçoivent, aucune donnée à migrer.
    /// Pas de nouvelle table, donc aucune policy RLS à ajouter (school_settings est déjà protégée).
    /// </summary>
    public partial class AddCouncilRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CouncilEliminatoryGrade",
                table: "school_settings",
                type: "numeric(4,2)",
                precision: 4,
                scale: 2,
                nullable: false,
                defaultValue: 5m);

            migrationBuilder.AddColumn<decimal>(
                name: "CouncilEncouragementsMin",
                table: "school_settings",
                type: "numeric(4,2)",
                precision: 4,
                scale: 2,
                nullable: false,
                defaultValue: 12m);

            migrationBuilder.AddColumn<decimal>(
                name: "CouncilFelicitationsMin",
                table: "school_settings",
                type: "numeric(4,2)",
                precision: 4,
                scale: 2,
                nullable: false,
                defaultValue: 14m);

            migrationBuilder.AddColumn<decimal>(
                name: "CouncilHonorRollMin",
                table: "school_settings",
                type: "numeric(4,2)",
                precision: 4,
                scale: 2,
                nullable: false,
                defaultValue: 12m);

            migrationBuilder.AddColumn<decimal>(
                name: "CouncilPromotionMin",
                table: "school_settings",
                type: "numeric(4,2)",
                precision: 4,
                scale: 2,
                nullable: false,
                defaultValue: 10m);

            migrationBuilder.AddColumn<decimal>(
                name: "CouncilRepeatMin",
                table: "school_settings",
                type: "numeric(4,2)",
                precision: 4,
                scale: 2,
                nullable: false,
                defaultValue: 8.5m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CouncilEliminatoryGrade",
                table: "school_settings");

            migrationBuilder.DropColumn(
                name: "CouncilEncouragementsMin",
                table: "school_settings");

            migrationBuilder.DropColumn(
                name: "CouncilFelicitationsMin",
                table: "school_settings");

            migrationBuilder.DropColumn(
                name: "CouncilHonorRollMin",
                table: "school_settings");

            migrationBuilder.DropColumn(
                name: "CouncilPromotionMin",
                table: "school_settings");

            migrationBuilder.DropColumn(
                name: "CouncilRepeatMin",
                table: "school_settings");
        }
    }
}
