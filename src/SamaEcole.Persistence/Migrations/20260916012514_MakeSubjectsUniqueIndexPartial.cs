using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Rend PARTIEL l'index unique des matières : il ne compte plus que les lignes vivantes
    /// (« NOT IsDeleted »), même convention que MakeTeacherAssignmentUniqueIndexPartial.
    ///
    /// <para>
    /// IsDeleted était auparavant DANS la clé de l'index (pas en filtre), pour permettre de recréer une
    /// matière de même nom après archivage. Mais ça plafonnait aussi à UNE seule matière archivée par
    /// clé naturelle (SchoolId, Level, ParentSubjectId, Name) : <c>reset_school_data</c> détache tous
    /// les enfants d'un coup (<c>ParentSubjectId = NULL</c>, y compris sur des lignes déjà supprimées)
    /// avant de les effacer, et deux matières archivées finissant avec la même clé après ce détachement
    /// violaient l'unicité en pleine purge — alors qu'aucune des deux n'est plus vivante.
    /// </para>
    ///
    /// <para>
    /// Migration strictement additive côté données : aucune ligne n'est écrite, seul l'index est
    /// reconstruit. Réversible tant qu'aucun doublon « vivant + archivé(s) multiples » n'a été créé
    /// entre-temps — précisément ce que le nouvel index autorise, donc un retour arrière sur une base
    /// déjà exploitée échouera, comme il se doit, plutôt que de perdre une matière.
    /// </para>
    /// </summary>
    public partial class MakeSubjectsUniqueIndexPartial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_subjects_SchoolId_Level_ParentSubjectId_Name_IsDeleted",
                table: "subjects");

            migrationBuilder.CreateIndex(
                name: "IX_subjects_SchoolId_Level_ParentSubjectId_Name",
                table: "subjects",
                columns: new[] { "SchoolId", "Level", "ParentSubjectId", "Name" },
                unique: true,
                filter: "NOT \"IsDeleted\"")
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_subjects_SchoolId_Level_ParentSubjectId_Name",
                table: "subjects");

            migrationBuilder.CreateIndex(
                name: "IX_subjects_SchoolId_Level_ParentSubjectId_Name_IsDeleted",
                table: "subjects",
                columns: new[] { "SchoolId", "Level", "ParentSubjectId", "Name", "IsDeleted" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
        }
    }
}
