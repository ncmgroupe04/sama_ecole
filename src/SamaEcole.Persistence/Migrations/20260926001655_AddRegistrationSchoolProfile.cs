using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRegistrationSchoolProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CycleProfile",
                table: "school_registration_requests",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Primaire");

            migrationBuilder.AddColumn<string>(
                name: "Ownership",
                table: "school_registration_requests",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Private");

            migrationBuilder.AddColumn<string>(
                name: "SizeTier",
                table: "school_registration_requests",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CycleProfile",
                table: "school_registration_requests");

            migrationBuilder.DropColumn(
                name: "Ownership",
                table: "school_registration_requests");

            migrationBuilder.DropColumn(
                name: "SizeTier",
                table: "school_registration_requests");
        }
    }
}
