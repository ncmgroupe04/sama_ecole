using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEvaluationPeriodType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CustomPeriodCount",
                table: "school_settings",
                type: "integer",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<string>(
                name: "EvaluationPeriodType",
                table: "school_settings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Trimester");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CustomPeriodCount",
                table: "school_settings");

            migrationBuilder.DropColumn(
                name: "EvaluationPeriodType",
                table: "school_settings");
        }
    }
}
