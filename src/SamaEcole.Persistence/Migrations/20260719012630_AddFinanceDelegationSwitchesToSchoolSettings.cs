using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Matrice d'autorisation "Photoshop" — délégation Finance, par école : AllowFinanceToModifyFees
    /// (PUT /finance/fees/{id}) et AllowFinanceToDeleteFees (DELETE /finance/fees/{id} et
    /// /finance/fee-categories/{id}), lues par CanModifyFeesHandler/CanDeleteFeesHandler
    /// (SamaEcole.Web.Authorization). Même gabarit que AddAllowSecretaryToManageGradingToSchoolSettings :
    /// défaut à FALSE au niveau colonne, provision_school_director n'a pas besoin d'être réémise, la
    /// délégation reste fermée tant que le Directeur ne l'active pas explicitement.
    /// </summary>
    public partial class AddFinanceDelegationSwitchesToSchoolSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowFinanceToDeleteFees",
                table: "school_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AllowFinanceToModifyFees",
                table: "school_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowFinanceToDeleteFees",
                table: "school_settings");

            migrationBuilder.DropColumn(
                name: "AllowFinanceToModifyFees",
                table: "school_settings");
        }
    }
}
