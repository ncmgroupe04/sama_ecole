using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddArrivalTimeToEntryTickets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PreviousLateMinutes",
                table: "student_attendances",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreviousStatus",
                table: "student_attendances",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "ArrivalTime",
                table: "LateArrivals",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid[]>(
                name: "MissedScheduleSlotIds",
                table: "LateArrivals",
                type: "uuid[]",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TotalMinutes",
                table: "LateArrivals",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PreviousLateMinutes",
                table: "student_attendances");

            migrationBuilder.DropColumn(
                name: "PreviousStatus",
                table: "student_attendances");

            migrationBuilder.DropColumn(
                name: "ArrivalTime",
                table: "LateArrivals");

            migrationBuilder.DropColumn(
                name: "MissedScheduleSlotIds",
                table: "LateArrivals");

            migrationBuilder.DropColumn(
                name: "TotalMinutes",
                table: "LateArrivals");
        }
    }
}
