using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDashboardPerfIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_schedule_slots_SchoolId_DayOfWeek",
                table: "ScheduleSlots",
                columns: new[] { "SchoolId", "DayOfWeek" });

            migrationBuilder.CreateIndex(
                name: "IX_payments_SchoolId_Status_PaidAt",
                table: "payments",
                columns: new[] { "SchoolId", "Status", "PaidAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_schedule_slots_SchoolId_DayOfWeek",
                table: "ScheduleSlots");

            migrationBuilder.DropIndex(
                name: "IX_payments_SchoolId_Status_PaidAt",
                table: "payments");
        }
    }
}
