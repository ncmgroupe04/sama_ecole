using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCashierSessionDiscrepancy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ActualCashAmount",
                table: "cashier_sessions",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscrepancyAmount",
                table: "cashier_sessions",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DiscrepancyReason",
                table: "cashier_sessions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ExpectedCashAmount",
                table: "cashier_sessions",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActualCashAmount",
                table: "cashier_sessions");

            migrationBuilder.DropColumn(
                name: "DiscrepancyAmount",
                table: "cashier_sessions");

            migrationBuilder.DropColumn(
                name: "DiscrepancyReason",
                table: "cashier_sessions");

            migrationBuilder.DropColumn(
                name: "ExpectedCashAmount",
                table: "cashier_sessions");
        }
    }
}
