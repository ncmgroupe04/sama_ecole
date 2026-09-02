using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddParentSummonsOutcome : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ClosedAt",
                table: "ParentSummons",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ClosedByUserId",
                table: "ParentSummons",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OutcomeNotes",
                table: "ParentSummons",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            // defaultValue REPRIS À LA MAIN : l'échafaudage EF proposait la chaîne vide, qui n'est
            // pas un ParentSummonsStatus. Toute convocation antérieure au 02/09/2026 serait relue
            // en `""` et ferait lever la conversion enum ↔ texte à la première lecture du registre.
            // « Scheduled » est le seul remplissage juste : ces convocations n'ont effectivement
            // jamais reçu de suite. La valeur par défaut reste ensuite en base pour les rares
            // insertions hors EF (restauration, correction SQL), où elle dit la même chose.
            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "ParentSummons",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Scheduled");

            migrationBuilder.CreateIndex(
                name: "IX_parent_summons_pending",
                table: "ParentSummons",
                columns: new[] { "SchoolId", "ScheduledAt" },
                filter: "\"Status\" = 'Scheduled'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_parent_summons_pending",
                table: "ParentSummons");

            migrationBuilder.DropColumn(
                name: "ClosedAt",
                table: "ParentSummons");

            migrationBuilder.DropColumn(
                name: "ClosedByUserId",
                table: "ParentSummons");

            migrationBuilder.DropColumn(
                name: "OutcomeNotes",
                table: "ParentSummons");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "ParentSummons");
        }
    }
}
