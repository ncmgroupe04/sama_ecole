using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Rend PARTIEL l'index unique des attributions enseignant : il ne compte plus que les lignes
    /// vivantes (« NOT IsDeleted »).
    ///
    /// <para>
    /// Le retrait d'une attribution est une suppression LOGIQUE (AGENTS.md règle #6) : la ligne reste
    /// en base. L'index, lui, ne connaissait pas la colonne IsDeleted — il continuait donc de réserver
    /// la place pour une attribution que l'utilisateur croyait avoir supprimée. Conséquence pour un
    /// établissement : une fois la matière retirée à un enseignant dans une classe, il devenait
    /// IMPOSSIBLE de la lui rendre pour l'année scolaire en cours, et aucun écran ne permettait de
    /// sortir de cette impasse.
    /// </para>
    ///
    /// <para>
    /// Migration strictement additive côté données : aucune ligne n'est écrite, seul l'index est
    /// reconstruit. Elle est réversible tant qu'aucun doublon « vivant + retiré » n'a été créé
    /// entre-temps — c'est précisément ce que le nouvel index autorise, donc un retour arrière sur une
    /// base déjà exploitée échouera, comme il se doit, plutôt que de perdre une attribution.
    /// </para>
    /// </summary>
    public partial class MakeTeacherAssignmentUniqueIndexPartial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_teacher_assignments_TeacherId_ClassroomId_SubjectId_SchoolY~",
                table: "teacher_assignments");

            migrationBuilder.CreateIndex(
                name: "IX_teacher_assignments_TeacherId_ClassroomId_SubjectId_SchoolY~",
                table: "teacher_assignments",
                columns: new[] { "TeacherId", "ClassroomId", "SubjectId", "SchoolYearId" },
                unique: true,
                filter: "NOT \"IsDeleted\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_teacher_assignments_TeacherId_ClassroomId_SubjectId_SchoolY~",
                table: "teacher_assignments");

            migrationBuilder.CreateIndex(
                name: "IX_teacher_assignments_TeacherId_ClassroomId_SubjectId_SchoolY~",
                table: "teacher_assignments",
                columns: new[] { "TeacherId", "ClassroomId", "SubjectId", "SchoolYearId" },
                unique: true);
        }
    }
}
