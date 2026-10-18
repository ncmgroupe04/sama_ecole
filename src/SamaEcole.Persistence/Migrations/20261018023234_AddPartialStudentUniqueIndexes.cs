using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPartialStudentUniqueIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_students_SchoolId_IenNumber",
                table: "students");

            migrationBuilder.DropIndex(
                name: "IX_students_SchoolId_Matricule",
                table: "students");

            migrationBuilder.CreateIndex(
                name: "IX_students_SchoolId_IenNumber",
                table: "students",
                columns: new[] { "SchoolId", "IenNumber" },
                unique: true,
                filter: "\"IenNumber\" IS NOT NULL AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_students_SchoolId_Matricule",
                table: "students",
                columns: new[] { "SchoolId", "Matricule" },
                unique: true,
                filter: "\"IsDeleted\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_students_SchoolId_IenNumber",
                table: "students");

            migrationBuilder.DropIndex(
                name: "IX_students_SchoolId_Matricule",
                table: "students");

            migrationBuilder.CreateIndex(
                name: "IX_students_SchoolId_IenNumber",
                table: "students",
                columns: new[] { "SchoolId", "IenNumber" },
                unique: true,
                filter: "\"IenNumber\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_students_SchoolId_Matricule",
                table: "students",
                columns: new[] { "SchoolId", "Matricule" },
                unique: true);
        }
    }
}
