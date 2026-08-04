using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Classes PASSERELLES / ACCÉLÉRÉES (option) : une année scolaire qui valide DEUX niveaux successifs
    /// (« CI-CP », « 6e-5e »), pour les parcours d'intégration des élèves venus des écoles coraniques.
    ///
    /// Strictement ADDITIVE et non destructive : deux colonnes ajoutées à `classrooms`, aucune donnée
    /// existante lue, réécrite ni contrainte. `IsAccelerated` est NOT NULL avec un DEFAULT false, ce qui
    /// renseigne les classes déjà en base sans downtime ; `TargetLevel` est nullable. Une classe existante
    /// se retrouve donc exactement dans l'état « option non utilisée », et tout le comportement d'avant.
    ///
    /// AUCUNE policy RLS à ajouter : `classrooms` est déjà inscrite dans TenantTables et protégée par la
    /// migration AddClassrooms. Une policy PostgreSQL porte sur les LIGNES, pas sur les colonnes — les
    /// deux nouvelles colonnes héritent de l'isolation existante (AGENTS.md règle #2).
    /// </summary>
    public partial class AddAcceleratedClassrooms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsAccelerated",
                table: "classrooms",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "TargetLevel",
                table: "classrooms",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsAccelerated",
                table: "classrooms");

            migrationBuilder.DropColumn(
                name: "TargetLevel",
                table: "classrooms");
        }
    }
}
