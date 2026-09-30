using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Onboarding / Setup Wizard — profil d'établissement choisi par le Directeur (voir
    /// ProfileEtablissement, SamaEcole.Domain.Enums.CommonEnums). Colonne NULLABLE et SANS défaut au
    /// niveau colonne : une école neuve (provision_school_director, migration AddSchoolSettings) reçoit
    /// donc NULL pour cette colonne par simple absence de valeur — c'est ce qui déclenche la redirection
    /// cliente vers /onboarding.
    ///
    /// BACKFILL SILENCIEUX (décision produit actée) : toute ligne school_settings déjà existante à cette
    /// migration appartient forcément à une école ANTÉRIEURE à cette fonctionnalité — jamais à une école
    /// neuve créée après elle. On la bascule donc explicitement sur General, pour ne JAMAIS rediriger un
    /// Directeur déjà en production vers l'Onboarding à son prochain login.
    /// </summary>
    public partial class AddProfileEtablissement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProfileEtablissement",
                table: "school_settings",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE school_settings SET "ProfileEtablissement" = 'General'
                WHERE "ProfileEtablissement" IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProfileEtablissement",
                table: "school_settings");
        }
    }
}
