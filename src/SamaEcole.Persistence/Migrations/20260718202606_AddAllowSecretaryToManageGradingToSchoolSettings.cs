using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Ticket JGK-G02 — bascule, par école, de la délégation de la notation (barème, matières,
    /// mentions) au Secrétariat ; lue par CanManageGradingScaleHandler (SamaEcole.Web.Authorization),
    /// jamais par un rôle codé en dur. Défaut à FALSE au niveau colonne (comme TuitionMonthsPerYear,
    /// migration AddEnrollments) : provision_school_director n'a pas besoin d'être réémise, son INSERT
    /// ne liste pas cette colonne et reçoit donc le défaut — la délégation reste fermée tant que le
    /// Directeur ne l'active pas explicitement.
    /// </summary>
    public partial class AddAllowSecretaryToManageGradingToSchoolSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowSecretaryToManageGrading",
                table: "school_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowSecretaryToManageGrading",
                table: "school_settings");
        }
    }
}
