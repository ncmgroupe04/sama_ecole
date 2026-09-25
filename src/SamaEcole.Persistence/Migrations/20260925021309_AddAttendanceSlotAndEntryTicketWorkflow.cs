using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendanceSlotAndEntryTicketWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "EntryTicketId",
                table: "student_attendances",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AcceptedAt",
                table: "LateArrivals",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AcceptedByUserId",
                table: "LateArrivals",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CancelledAt",
                table: "LateArrivals",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CancelledByUserId",
                table: "LateArrivals",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PreviousLateMinutes",
                table: "LateArrivals",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreviousStatus",
                table: "LateArrivals",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "LateArrivals",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TargetScheduleSlotId",
                table: "LateArrivals",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ScheduleSlotId",
                table: "attendance_sheets",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_student_attendances_EntryTicketId",
                table: "student_attendances",
                column: "EntryTicketId");

            migrationBuilder.CreateIndex(
                name: "IX_LateArrivals_TargetScheduleSlotId",
                table: "LateArrivals",
                column: "TargetScheduleSlotId");

            migrationBuilder.CreateIndex(
                name: "UX_LateArrivals_ActiveTicket",
                table: "LateArrivals",
                columns: new[] { "StudentId", "TargetScheduleSlotId", "Date" },
                unique: true,
                filter: "\"Status\" IN ('Issued', 'Accepted') AND NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_attendance_sheets_ScheduleSlotId",
                table: "attendance_sheets",
                column: "ScheduleSlotId");

            migrationBuilder.AddForeignKey(
                name: "FK_attendance_sheets_ScheduleSlots_ScheduleSlotId",
                table: "attendance_sheets",
                column: "ScheduleSlotId",
                principalTable: "ScheduleSlots",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_LateArrivals_ScheduleSlots_TargetScheduleSlotId",
                table: "LateArrivals",
                column: "TargetScheduleSlotId",
                principalTable: "ScheduleSlots",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_student_attendances_LateArrivals_EntryTicketId",
                table: "student_attendances",
                column: "EntryTicketId",
                principalTable: "LateArrivals",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_attendance_sheets_ScheduleSlots_ScheduleSlotId",
                table: "attendance_sheets");

            migrationBuilder.DropForeignKey(
                name: "FK_LateArrivals_ScheduleSlots_TargetScheduleSlotId",
                table: "LateArrivals");

            migrationBuilder.DropForeignKey(
                name: "FK_student_attendances_LateArrivals_EntryTicketId",
                table: "student_attendances");

            migrationBuilder.DropIndex(
                name: "IX_student_attendances_EntryTicketId",
                table: "student_attendances");

            migrationBuilder.DropIndex(
                name: "IX_LateArrivals_TargetScheduleSlotId",
                table: "LateArrivals");

            migrationBuilder.DropIndex(
                name: "UX_LateArrivals_ActiveTicket",
                table: "LateArrivals");

            migrationBuilder.DropIndex(
                name: "IX_attendance_sheets_ScheduleSlotId",
                table: "attendance_sheets");

            migrationBuilder.DropColumn(
                name: "EntryTicketId",
                table: "student_attendances");

            migrationBuilder.DropColumn(
                name: "AcceptedAt",
                table: "LateArrivals");

            migrationBuilder.DropColumn(
                name: "AcceptedByUserId",
                table: "LateArrivals");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "LateArrivals");

            migrationBuilder.DropColumn(
                name: "CancelledByUserId",
                table: "LateArrivals");

            migrationBuilder.DropColumn(
                name: "PreviousLateMinutes",
                table: "LateArrivals");

            migrationBuilder.DropColumn(
                name: "PreviousStatus",
                table: "LateArrivals");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "LateArrivals");

            migrationBuilder.DropColumn(
                name: "TargetScheduleSlotId",
                table: "LateArrivals");

            migrationBuilder.DropColumn(
                name: "ScheduleSlotId",
                table: "attendance_sheets");
        }
    }
}
