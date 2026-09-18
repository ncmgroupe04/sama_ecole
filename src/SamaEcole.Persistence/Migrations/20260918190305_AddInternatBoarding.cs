using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInternatBoarding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsBoardingFee",
                table: "fee_categories",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "BoardingStatus",
                table: "enrollments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Externe");

            migrationBuilder.AddColumn<Guid>(
                name: "RoomId",
                table: "enrollments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_rooms_SchoolId_Id",
                table: "rooms",
                columns: new[] { "SchoolId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_enrollments_SchoolId_RoomId",
                table: "enrollments",
                columns: new[] { "SchoolId", "RoomId" });

            migrationBuilder.AddForeignKey(
                name: "FK_enrollments_rooms_SchoolId_RoomId",
                table: "enrollments",
                columns: new[] { "SchoolId", "RoomId" },
                principalTable: "rooms",
                principalColumns: new[] { "SchoolId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_enrollments_rooms_SchoolId_RoomId",
                table: "enrollments");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_rooms_SchoolId_Id",
                table: "rooms");

            migrationBuilder.DropIndex(
                name: "IX_enrollments_SchoolId_RoomId",
                table: "enrollments");

            migrationBuilder.DropColumn(
                name: "IsBoardingFee",
                table: "fee_categories");

            migrationBuilder.DropColumn(
                name: "BoardingStatus",
                table: "enrollments");

            migrationBuilder.DropColumn(
                name: "RoomId",
                table: "enrollments");
        }
    }
}
