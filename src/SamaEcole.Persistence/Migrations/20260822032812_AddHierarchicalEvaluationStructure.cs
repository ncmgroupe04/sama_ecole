using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Structure d'évaluation HIÉRARCHIQUE et modulable (grilles APC du primaire) : un domaine
    /// (« Lang &amp; Com. », « Français ») porte des activités (« P. Alphabétique », « Ressources »),
    /// chacune avec son propre barème — 10, 16, 24, 40, 60 selon la grille de l'école — et son rang
    /// d'affichage. Les deux entêtes de colonnes du bulletin (« Domaines »/« Activités » au CI-CP,
    /// « Activités »/« Contrôles » au CE1-CE2) deviennent eux aussi un réglage.
    ///
    /// STRICTEMENT ADDITIVE. Les cinq colonnes sont nullables ou à défaut neutre : une matière existante
    /// se retrouve exactement dans l'état « option non utilisée » — pas de domaine, barème du cycle
    /// (MaxScore NULL, voir GradeCalculator.EffectiveMaxScore), rang 0 qui laisse le nom départager
    /// comme avant. Aucune donnée n'est lue ni réécrite.
    ///
    /// LE POINT DÉLICAT est l'index unique. Il portait sur (SchoolId, Level, Name, IsDeleted) ; il doit
    /// désormais inclure ParentSubjectId, sans quoi « Ressources » ne pourrait pas exister à la fois sous
    /// « Français » et sous « Maths » dans la même grille. Mais PostgreSQL considère par défaut deux NULL
    /// comme DISTINCTS : ParentSubjectId étant NULL pour toute matière de premier niveau, l'index aurait
    /// alors cessé d'interdire deux « Maths » au même niveau — la garantie même qu'il portait avant.
    /// `NULLS NOT DISTINCT` (annotation Npgsql:NullsDistinct = false ; PostgreSQL 15+, l'image du projet
    /// est postgres:16) la rétablit à l'identique.
    ///
    /// AUCUNE policy RLS à ajouter : `subjects` est déjà inscrite dans TenantTables et protégée par la
    /// migration AddSubjects. Une policy PostgreSQL porte sur les LIGNES, pas sur les colonnes — les
    /// nouvelles colonnes héritent de l'isolation existante (AGENTS.md règle #2). La clé étrangère
    /// auto-référencée reste bornée au même tenant par cette policy, et par les contrôles applicatifs de
    /// SubjectHierarchyGuard (un domaine d'une autre école est structurellement introuvable).
    ///
    /// La FK est en Restrict : supprimer un domaine qui porte encore des activités les laisserait
    /// orphelines — invisibles sur le bulletin sans le moindre message. DeleteSubjectCommandHandler
    /// refuse ce cas en amont, avec un message qui dit quoi faire.
    /// </summary>
    public partial class AddHierarchicalEvaluationStructure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_subjects_SchoolId_Level_Name_IsDeleted",
                table: "subjects");

            migrationBuilder.AddColumn<string>(
                name: "Column1Header",
                table: "subjects",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Column2Header",
                table: "subjects",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DisplayOrder",
                table: "subjects",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "MaxScore",
                table: "subjects",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ParentSubjectId",
                table: "subjects",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_subjects_ParentSubjectId",
                table: "subjects",
                column: "ParentSubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_subjects_SchoolId_Level_ParentSubjectId_Name_IsDeleted",
                table: "subjects",
                columns: new[] { "SchoolId", "Level", "ParentSubjectId", "Name", "IsDeleted" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.AddForeignKey(
                name: "FK_subjects_subjects_ParentSubjectId",
                table: "subjects",
                column: "ParentSubjectId",
                principalTable: "subjects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_subjects_subjects_ParentSubjectId",
                table: "subjects");

            migrationBuilder.DropIndex(
                name: "IX_subjects_ParentSubjectId",
                table: "subjects");

            migrationBuilder.DropIndex(
                name: "IX_subjects_SchoolId_Level_ParentSubjectId_Name_IsDeleted",
                table: "subjects");

            migrationBuilder.DropColumn(
                name: "Column1Header",
                table: "subjects");

            migrationBuilder.DropColumn(
                name: "Column2Header",
                table: "subjects");

            migrationBuilder.DropColumn(
                name: "DisplayOrder",
                table: "subjects");

            migrationBuilder.DropColumn(
                name: "MaxScore",
                table: "subjects");

            migrationBuilder.DropColumn(
                name: "ParentSubjectId",
                table: "subjects");

            migrationBuilder.CreateIndex(
                name: "IX_subjects_SchoolId_Level_Name_IsDeleted",
                table: "subjects",
                columns: new[] { "SchoolId", "Level", "Name", "IsDeleted" },
                unique: true);
        }
    }
}
