using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTeacherBirthDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Nouveau champ obligatoire (formulaire Enseignant) sur une table déjà peuplée : ajoutée
            // nullable, backfillée, puis verrouillée en NOT NULL — même séquence que
            // MakeStudentBirthPlaceRequired. 1900-01-01, sentinelle manifestement fausse, signale les
            // fiches enseignant antérieures à corriger (l'application fournit toujours une vraie valeur
            // désormais). Pas de DEFAULT posé sur la colonne au final : un DEFAULT désynchroniserait le
            // model snapshot, et l'application n'en a plus besoin une fois le backfill fait.
            migrationBuilder.AddColumn<DateOnly>(
                name: "BirthDate",
                table: "teachers",
                type: "date",
                nullable: true);

            migrationBuilder.Sql("UPDATE teachers SET \"BirthDate\" = DATE '1900-01-01' WHERE \"BirthDate\" IS NULL;");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "BirthDate",
                table: "teachers",
                type: "date",
                nullable: false,
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BirthDate",
                table: "teachers");
        }
    }
}
