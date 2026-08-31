using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Bascule « bac à sable → exploitation réelle » : une seule colonne, <c>WentLiveAt</c> sur
    /// <c>schools</c>. <c>NULL</c> = mode test (la « Zone de danger » peut purger les données d'essai
    /// autant de fois qu'il faut) ; DATÉ = mode réel (l'horodatage du passage EXPLICITE du Directeur —
    /// la purge devient indisponible, invariant d'immuabilité comptable).
    ///
    /// Additive et réversible. Toutes les écoles existantes démarrent à <c>NULL</c> (mode test) : la
    /// décision de passer en mode réel n'appartient qu'au Directeur, jamais à une migration ni à un
    /// effet de bord (première clôture, première inscription…). Aucune donnée n'est lue ni réécrite.
    /// </summary>
    public partial class AddSchoolWentLiveAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "WentLiveAt",
                table: "schools",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WentLiveAt",
                table: "schools");
        }
    }
}
