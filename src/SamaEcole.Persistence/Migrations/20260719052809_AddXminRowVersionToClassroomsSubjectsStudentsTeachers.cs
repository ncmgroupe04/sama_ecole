using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Enregistre dans l'historique EF le nouveau jeton de concurrence xmin sur classrooms, subjects,
    /// students, teachers (AGENTS.md règle #5 — voir ClassroomConfiguration et consorts). Les
    /// opérations ci-dessous ne produisent AUCUN DDL réel : Npgsql reconnaît une colonne nommée
    /// "xmin" comme la colonne système PostgreSQL déjà présente sur chaque table, et l'omet du script
    /// SQL généré (vérifié via `dotnet ef migrations script`) — même mécanisme que AddGrades et
    /// AddEnrollments, où xmin faisait déjà partie du CREATE TABLE initial.
    /// </summary>
    public partial class AddXminRowVersionToClassroomsSubjectsStudentsTeachers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "teachers",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "subjects",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "students",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "classrooms",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "xmin",
                table: "teachers");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "subjects");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "students");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "classrooms");
        }
    }
}
