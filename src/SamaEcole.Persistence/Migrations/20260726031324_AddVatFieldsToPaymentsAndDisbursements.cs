using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVatFieldsToPaymentsAndDisbursements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "VatAmount",
                table: "payments",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "VatRate",
                table: "payments",
                type: "numeric(5,4)",
                precision: 5,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "VatAmount",
                table: "Disbursements",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "VatRate",
                table: "Disbursements",
                type: "numeric(5,4)",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_payments_vat_rate_range",
                table: "payments",
                sql: "\"VatRate\" IS NULL OR (\"VatRate\" >= 0 AND \"VatRate\" <= 1)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_disbursements_vat_rate_range",
                table: "Disbursements",
                sql: "\"VatRate\" IS NULL OR (\"VatRate\" >= 0 AND \"VatRate\" <= 1)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_payments_vat_rate_range",
                table: "payments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_disbursements_vat_rate_range",
                table: "Disbursements");

            migrationBuilder.DropColumn(
                name: "VatAmount",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "VatRate",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "VatAmount",
                table: "Disbursements");

            migrationBuilder.DropColumn(
                name: "VatRate",
                table: "Disbursements");
        }
    }
}
