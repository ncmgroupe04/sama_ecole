using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Renomme la note grades."EvaluationType" = 'Devoir' en 'Devoir1' (harmonisation import/export
    /// Excel des notes — ajout d'un second devoir, Devoir2, à côté de Composition).
    ///
    /// Migration de DONNÉES, volontairement sans schéma : grades."EvaluationType" est déjà une colonne
    /// character varying(20) sans contrainte CHECK (GradeConfiguration.HasConversion&lt;string&gt;()), et
    /// l'index unique UX_grades_single_entry inclut déjà EvaluationType — une troisième valeur (Devoir2)
    /// y ouvre automatiquement son propre créneau, sans rien à modifier ici. Elle s'applique à tous les
    /// tenants (rôle propriétaire, hors RLS) : c'est bien l'intention, le renommage touche tout le monde.
    /// </summary>
    public partial class AddDevoir2EvaluationType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE grades SET "EvaluationType" = 'Devoir1' WHERE "EvaluationType" = 'Devoir';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Aucune note Devoir2 n'existait avant cette migration : la rétrogradation se limite à
            // l'inverse exact du renommage. Une éventuelle note Devoir2 saisie depuis resterait en base
            // sous un type que l'ancien code ne connaît pas — un downgrade de schéma n'a de toute façon
            // pas vocation à préserver des données créées après lui.
            migrationBuilder.Sql(
                """
                UPDATE grades SET "EvaluationType" = 'Devoir' WHERE "EvaluationType" = 'Devoir1';
                """);
        }
    }
}
