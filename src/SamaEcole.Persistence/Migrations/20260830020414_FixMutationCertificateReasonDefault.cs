using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Retire le DEFAULT base de <c>student_mutation_certificates.Reason</c>. La valeur CLR par
    /// défaut de l'enum <c>StudentMutationReason</c> (Demenagement = 0) est aussi celle qu'EF Core
    /// interprète comme « propriété non affectée » : avec un défaut base (« Autre »), une mutation
    /// réellement pour « Déménagement » aurait été enregistrée « Autre » sans erreur. Le Handler
    /// fournit toujours Reason — aucun défaut n'est nécessaire. Additive et réversible.
    /// </summary>
    public partial class FixMutationCertificateReasonDefault : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Reason",
                table: "student_mutation_certificates",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30,
                oldDefaultValue: "Autre");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Reason",
                table: "student_mutation_certificates",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Autre",
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30);
        }
    }
}
