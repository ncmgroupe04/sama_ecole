using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Lot F2 (contract) : supprime <c>enrollments.BoardingStatus</c> et <c>enrollments.RoomId</c>, avec leur clé étrangère composite vers
    /// <c>rooms</c> et leur index. Depuis la réconciliation du lot C (<c>ReconcileBoardingWithLegacyModel</c>) plus rien ne les lisait ni ne les
    /// écrivait : le séjour vit dans <c>boarding_enrollments</c>. Le code ne les mappait plus qu'en propriétés fantômes depuis le lot F1.
    ///
    /// <b>PRÉREQUIS DE DÉPLOIEMENT.</b> Toutes les instances doivent être au moins en F1 : une instance antérieure mappe encore ces colonnes et
    /// échouerait sur chaque requête touchant <c>enrollments</c>. Avant de déployer : exporter les deux colonnes et passer la porte de sécurité
    /// (voir la description de la PR F2 et <c>DropLegacyBoardingColumnsMigrationTests.GateSql</c>).
    ///
    /// <c>Down()</c> restitue les colonnes (même définition qu'à <c>AddInternatBoarding</c>) puis RE-DÉRIVE leur valeur des séjours ACTIFS :
    /// <c>BoardingStatus</c> = régime du séjour, <c>RoomId</c> = chambre du lit, seulement si cette chambre existe aussi dans <c>rooms</c> (une chambre
    /// créée par la nouvelle API n'y a pas de ligne : la clé étrangère l'interdit). C'est une approximation documentée, pas une restitution exacte
    /// de l'état de l'ancien écran, dont la valeur figée n'est de toute façon plus la référence depuis le lot C.
    /// </summary>
    public partial class DropLegacyBoardingColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_enrollments_rooms_SchoolId_RoomId",
                table: "enrollments");

            migrationBuilder.DropIndex(
                name: "IX_enrollments_SchoolId_RoomId",
                table: "enrollments");

            migrationBuilder.DropColumn(
                name: "BoardingStatus",
                table: "enrollments");

            migrationBuilder.DropColumn(
                name: "RoomId",
                table: "enrollments");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BoardingStatus",
                table: "enrollments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Externe");

            migrationBuilder.AddColumn<Guid>(
                name: "RoomId",
                table: "enrollments",
                type: "uuid",
                nullable: true);

            // Re-dérivation AVANT de recréer la clé étrangère. Un séjour actif donne le régime ; la chambre n'est posée que si elle existe aussi
            // dans l'ancienne table rooms (même Id : chambres reprises au lot A). Les autres élèves restent « Externe » sans chambre.
            migrationBuilder.Sql("""
                UPDATE enrollments e
                SET "BoardingStatus" = be."Regime",
                    "RoomId" = (
                        SELECT r."Id"
                        FROM beds b
                        JOIN dormitory_rooms dr ON dr."SchoolId" = b."SchoolId" AND dr."Id" = b."DormitoryRoomId"
                        JOIN rooms r ON r."SchoolId" = dr."SchoolId" AND r."Id" = dr."Id"
                        WHERE b."SchoolId" = be."SchoolId" AND b."Id" = be."BedId")
                FROM boarding_enrollments be
                WHERE be."SchoolId" = e."SchoolId" AND be."EnrollmentId" = e."Id" AND be."IsActive" AND NOT be."IsDeleted";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_enrollments_SchoolId_RoomId",
                table: "enrollments",
                columns: new[] { "SchoolId", "RoomId" });

            migrationBuilder.AddForeignKey(
                name: "FK_enrollments_rooms_SchoolId_RoomId",
                table: "enrollments",
                columns: new[] { "SchoolId", "RoomId" },
                principalTable: "rooms",
                principalColumns: new[] { "SchoolId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }
    }
}
