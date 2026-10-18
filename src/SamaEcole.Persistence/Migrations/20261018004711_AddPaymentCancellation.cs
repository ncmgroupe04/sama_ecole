using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentCancellation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CancellationOutcome",
                table: "payments",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "payments",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CancellationSessionId",
                table: "payments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CancelledAt",
                table: "payments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CancelledByUserId",
                table: "payments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_payments_SchoolId_CancellationSessionId",
                table: "payments",
                columns: new[] { "SchoolId", "CancellationSessionId" },
                filter: "\"CancellationSessionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_payments_SchoolId_CancelledAt",
                table: "payments",
                columns: new[] { "SchoolId", "CancelledAt" },
                filter: "\"CancelledAt\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_payments_SchoolId_CancellationSessionId",
                table: "payments");

            migrationBuilder.DropIndex(
                name: "IX_payments_SchoolId_CancelledAt",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "CancellationOutcome",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "CancellationSessionId",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "CancelledByUserId",
                table: "payments");
        }
    }
}
