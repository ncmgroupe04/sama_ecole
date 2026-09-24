using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSchoolWorkingDays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WorkingDays",
                table: "school_settings",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "Monday,Tuesday,Wednesday,Thursday,Friday,Saturday");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WorkingDays",
                table: "school_settings");
        }
    }
}
