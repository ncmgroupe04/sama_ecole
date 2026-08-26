using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeContractPayoutMethod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PayoutAccountReference",
                table: "employee_contracts",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayoutMethod",
                table: "employee_contracts",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Cash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PayoutAccountReference",
                table: "employee_contracts");

            migrationBuilder.DropColumn(
                name: "PayoutMethod",
                table: "employee_contracts");
        }
    }
}
