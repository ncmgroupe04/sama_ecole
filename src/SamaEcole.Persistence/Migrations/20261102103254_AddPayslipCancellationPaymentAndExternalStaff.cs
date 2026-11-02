using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayslipCancellationPaymentAndExternalStaff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_fiche_paies_SchoolId_EmployeeContractId_Month_Year",
                table: "fiche_paies");

            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "fiche_paies",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CancelledAt",
                table: "fiche_paies",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CancelledByUserId",
                table: "fiche_paies",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DisbursementId",
                table: "fiche_paies",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PaidAt",
                table: "fiche_paies",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PaidByUserId",
                table: "fiche_paies",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaidWith",
                table: "fiche_paies",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReplacedByFichePaieId",
                table: "fiche_paies",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalFullName",
                table: "employee_contracts",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalJobTitle",
                table: "employee_contracts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalPhone",
                table: "employee_contracts",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_fiche_paies_SchoolId_EmployeeContractId_Month_Year",
                table: "fiche_paies",
                columns: new[] { "SchoolId", "EmployeeContractId", "Month", "Year" },
                unique: true,
                filter: "\"CancelledAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_fiche_paies_SchoolId_EmployeeContractId_Month_Year",
                table: "fiche_paies");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "fiche_paies");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "fiche_paies");

            migrationBuilder.DropColumn(
                name: "CancelledByUserId",
                table: "fiche_paies");

            migrationBuilder.DropColumn(
                name: "DisbursementId",
                table: "fiche_paies");

            migrationBuilder.DropColumn(
                name: "PaidAt",
                table: "fiche_paies");

            migrationBuilder.DropColumn(
                name: "PaidByUserId",
                table: "fiche_paies");

            migrationBuilder.DropColumn(
                name: "PaidWith",
                table: "fiche_paies");

            migrationBuilder.DropColumn(
                name: "ReplacedByFichePaieId",
                table: "fiche_paies");

            migrationBuilder.DropColumn(
                name: "ExternalFullName",
                table: "employee_contracts");

            migrationBuilder.DropColumn(
                name: "ExternalJobTitle",
                table: "employee_contracts");

            migrationBuilder.DropColumn(
                name: "ExternalPhone",
                table: "employee_contracts");

            migrationBuilder.CreateIndex(
                name: "IX_fiche_paies_SchoolId_EmployeeContractId_Month_Year",
                table: "fiche_paies",
                columns: new[] { "SchoolId", "EmployeeContractId", "Month", "Year" },
                unique: true);
        }
    }
}
