using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Ajout des URLs d'images pour la signature du directeur, du caissier et du cachet officiel
    /// (SchoolSettings) pour injection dynamique dans les reçus et bulletins.
    /// </summary>
    public partial class AddSignaturesToSchoolSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CashierSignatureUrl",
                table: "school_settings",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DirectorSignatureUrl",
                table: "school_settings",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OfficialStampUrl",
                table: "school_settings",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CashierSignatureUrl",
                table: "school_settings");

            migrationBuilder.DropColumn(
                name: "DirectorSignatureUrl",
                table: "school_settings");

            migrationBuilder.DropColumn(
                name: "OfficialStampUrl",
                table: "school_settings");
        }
    }
}
