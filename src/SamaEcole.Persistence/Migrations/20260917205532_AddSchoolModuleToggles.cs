using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Feature flags par établissement (SchoolModule) : le Directeur active/désactive Pédagogie,
    /// Finance, Internat et Coran depuis Paramètres › Modules, indépendamment de la formule
    /// d'abonnement (Feature/PlanFeatures, axe distinct). Migration purement additive.
    ///
    /// Pédagogie et Finance défaillent à TRUE au niveau colonne (socle métier déjà livré, activé par
    /// défaut) : toute école déjà en base au moment de la migration garde un accès complet, aucun
    /// script de backfill n'est nécessaire. Internat et Coran défaillent à FALSE : aucun module
    /// n'existe encore derrière ces deux réglages (réglage anticipé, sans effet aujourd'hui).
    /// </summary>
    public partial class AddSchoolModuleToggles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsCoranModuleEnabled",
                table: "school_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsFinanceEnabled",
                table: "school_settings",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsInternatEnabled",
                table: "school_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsPedagogyEnabled",
                table: "school_settings",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsCoranModuleEnabled",
                table: "school_settings");

            migrationBuilder.DropColumn(
                name: "IsFinanceEnabled",
                table: "school_settings");

            migrationBuilder.DropColumn(
                name: "IsInternatEnabled",
                table: "school_settings");

            migrationBuilder.DropColumn(
                name: "IsPedagogyEnabled",
                table: "school_settings");
        }
    }
}
