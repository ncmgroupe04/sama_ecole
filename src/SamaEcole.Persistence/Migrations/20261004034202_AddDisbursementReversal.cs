using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDisbursementReversal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ReversalOfId",
                table: "Disbursements",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Disbursements_ReversalOfId",
                table: "Disbursements",
                column: "ReversalOfId",
                unique: true,
                filter: "\"ReversalOfId\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_disbursements_reversal_negative",
                table: "Disbursements",
                sql: "\"ReversalOfId\" IS NULL OR \"Amount\" < 0");

            migrationBuilder.AddForeignKey(
                name: "FK_Disbursements_Disbursements_ReversalOfId",
                table: "Disbursements",
                column: "ReversalOfId",
                principalTable: "Disbursements",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Disbursements_Disbursements_ReversalOfId",
                table: "Disbursements");

            migrationBuilder.DropIndex(
                name: "IX_Disbursements_ReversalOfId",
                table: "Disbursements");

            migrationBuilder.DropCheckConstraint(
                name: "CK_disbursements_reversal_negative",
                table: "Disbursements");

            migrationBuilder.DropColumn(
                name: "ReversalOfId",
                table: "Disbursements");
        }
    }
}
