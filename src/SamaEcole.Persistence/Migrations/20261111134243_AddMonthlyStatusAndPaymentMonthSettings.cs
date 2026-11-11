using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMonthlyStatusAndPaymentMonthSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowFinanceToViewMonthlyStatus",
                table: "school_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AskPaymentMonthAtCashier",
                table: "school_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "FirstMonthlyPaymentCoversLastMonth",
                table: "school_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Month",
                table: "payment_breakdowns",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_payment_breakdowns_month_range",
                table: "payment_breakdowns",
                sql: "\"Month\" IS NULL OR (\"Month\" BETWEEN 1 AND 12)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_payment_breakdowns_month_range",
                table: "payment_breakdowns");

            migrationBuilder.DropColumn(
                name: "AllowFinanceToViewMonthlyStatus",
                table: "school_settings");

            migrationBuilder.DropColumn(
                name: "AskPaymentMonthAtCashier",
                table: "school_settings");

            migrationBuilder.DropColumn(
                name: "FirstMonthlyPaymentCoversLastMonth",
                table: "school_settings");

            migrationBuilder.DropColumn(
                name: "Month",
                table: "payment_breakdowns");
        }
    }
}
