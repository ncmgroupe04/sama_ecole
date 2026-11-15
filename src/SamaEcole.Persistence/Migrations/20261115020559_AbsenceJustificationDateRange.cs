using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AbsenceJustificationDateRange : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AbsenceJustificationId",
                table: "student_attendances",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<DateOnly>(
                name: "AuthorizedReturnDate",
                table: "AbsenceJustifications",
                type: "date",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "EndDate",
                table: "AbsenceJustifications",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<DateOnly>(
                name: "StartDate",
                table: "AbsenceJustifications",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            // Les justificatifs existants (une seule date) deviennent des billets d'un jour : aucune donnée perdue.
            migrationBuilder.Sql(@"UPDATE ""AbsenceJustifications"" SET ""StartDate"" = (""Date"" AT TIME ZONE 'UTC')::date, ""EndDate"" = (""Date"" AT TIME ZONE 'UTC')::date;");

            migrationBuilder.DropColumn(
                name: "Date",
                table: "AbsenceJustifications");

            migrationBuilder.CreateIndex(
                name: "IX_student_attendances_AbsenceJustificationId",
                table: "student_attendances",
                column: "AbsenceJustificationId");

            migrationBuilder.CreateIndex(
                name: "IX_AbsenceJustifications_SchoolId_StartDate_EndDate",
                table: "AbsenceJustifications",
                columns: new[] { "SchoolId", "StartDate", "EndDate" });

            migrationBuilder.AddForeignKey(
                name: "FK_student_attendances_AbsenceJustifications_AbsenceJustificat~",
                table: "student_attendances",
                column: "AbsenceJustificationId",
                principalTable: "AbsenceJustifications",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_student_attendances_AbsenceJustifications_AbsenceJustificat~",
                table: "student_attendances");

            migrationBuilder.DropIndex(
                name: "IX_student_attendances_AbsenceJustificationId",
                table: "student_attendances");

            migrationBuilder.DropIndex(
                name: "IX_AbsenceJustifications_SchoolId_StartDate_EndDate",
                table: "AbsenceJustifications");

            migrationBuilder.DropColumn(
                name: "AbsenceJustificationId",
                table: "student_attendances");

            migrationBuilder.DropColumn(
                name: "EndDate",
                table: "AbsenceJustifications");

            migrationBuilder.DropColumn(
                name: "StartDate",
                table: "AbsenceJustifications");

            migrationBuilder.AlterColumn<DateTime>(
                name: "AuthorizedReturnDate",
                table: "AbsenceJustifications",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "Date",
                table: "AbsenceJustifications",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));
        }
    }
}
