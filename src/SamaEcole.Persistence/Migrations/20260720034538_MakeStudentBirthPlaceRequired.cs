using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MakeStudentBirthPlaceRequired : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Feature E — lieu de naissance obligatoire. Backfill AVANT de poser NOT NULL : aucune ligne
            // existante ne doit rester NULL, sinon l'ALTER échouerait. « Non renseigné » (plutôt qu'une
            // chaîne vide) signale explicitement les fiches antérieures à corriger. En Development le
            // seeder ne crée aucun élève (voir DbSeeder), ce backfill ne concerne donc que d'éventuelles
            // données réelles en production. Pas de DEFAULT posé sur la colonne : l'application fournit
            // toujours la valeur (validée non vide), et un DEFAULT désynchroniserait le model snapshot.
            migrationBuilder.Sql("UPDATE students SET \"BirthPlace\" = 'Non renseigné' WHERE \"BirthPlace\" IS NULL;");

            migrationBuilder.AlterColumn<string>(
                name: "BirthPlace",
                table: "students",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "BirthPlace",
                table: "students",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);
        }
    }
}
