using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Refonte du reçu d'inscription (A5 paysage) : mentions légales de l'en-tête et ventilation de
    /// l'encaissement du jour.
    ///
    /// • <c>schools</c> — e-mail de contact, NINEA et RCCM, imprimés dans l'en-tête du reçu.
    /// • <c>enrollment_fee_lines</c> — part réellement encaissée au guichet et nombre de mois couverts.
    ///   DEFAULT 0 : les inscriptions antérieures valent « rien encaissé à l'inscription », ce qui est
    ///   exact — leurs versements sont passés par la Caisse et ont leurs propres reçus.
    ///
    /// Aucune nouvelle table : les deux tables sont déjà couvertes par leurs policies RLS (AGENTS.md
    /// règle #2), il n'y a donc rien à ajouter à TenantTables.
    /// </summary>
    public partial class AddSchoolLegalMentionsAndEnrollmentCollection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "schools",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Ninea",
                table: "schools",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RegistreCommerce",
                table: "schools",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AmountCollected",
                table: "enrollment_fee_lines",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "MonthsCollected",
                table: "enrollment_fee_lines",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Email",
                table: "schools");

            migrationBuilder.DropColumn(
                name: "Ninea",
                table: "schools");

            migrationBuilder.DropColumn(
                name: "RegistreCommerce",
                table: "schools");

            migrationBuilder.DropColumn(
                name: "AmountCollected",
                table: "enrollment_fee_lines");

            migrationBuilder.DropColumn(
                name: "MonthsCollected",
                table: "enrollment_fee_lines");
        }
    }
}
