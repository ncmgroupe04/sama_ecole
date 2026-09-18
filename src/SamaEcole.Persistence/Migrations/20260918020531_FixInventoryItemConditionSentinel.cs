using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Retire le défaut généré côté base sur <c>inventory_items."Condition"</c>. Il faisait double
    /// emploi avec le défaut C# de <c>InventoryItem.Condition</c> (<c>= ItemCondition.Bon</c>), seul
    /// chemin de création de ce lot passant par EF — et l'ambiguïté entre les deux (Neuf, valeur CLR
    /// 0 de l'enum, ne pouvait plus être distinguée d'une valeur non renseignée sans sentinelle
    /// dédiée) déclenchait <c>PendingModelChangesWarning</c>, qui bloque <c>dotnet ef database
    /// update</c> (donc potentiellement le Job Cloud Run de migration) et le démarrage des suites
    /// Integration/Functional. Voir InventoryItemConfiguration.
    /// </summary>
    public partial class FixInventoryItemConditionSentinel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Condition",
                table: "inventory_items",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldDefaultValue: "Bon");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Condition",
                table: "inventory_items",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Bon",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);
        }
    }
}
