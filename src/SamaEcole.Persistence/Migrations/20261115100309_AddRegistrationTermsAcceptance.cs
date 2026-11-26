using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRegistrationTermsAcceptance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TermsAcceptedAt",
                table: "school_registration_requests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TermsAcceptedIp",
                table: "school_registration_requests",
                type: "character varying(45)",
                maxLength: 45,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TermsVersion",
                table: "school_registration_requests",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TermsAcceptedAt",
                table: "school_registration_requests");

            migrationBuilder.DropColumn(
                name: "TermsAcceptedIp",
                table: "school_registration_requests");

            migrationBuilder.DropColumn(
                name: "TermsVersion",
                table: "school_registration_requests");
        }
    }
}
