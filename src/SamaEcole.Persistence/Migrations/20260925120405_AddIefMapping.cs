using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// Cartographie IEF (Évolution N°7) : statut « Transféré » des inscriptions (IsTransferredIn, PreviousSchoolName —
    /// défaut false, les inscriptions existantes restent « Nouveau ») et table tenant grade_age_norms (tranches d'âge
    /// propres à l'école, le modèle national restant en code).
    ///
    /// Policy RLS + GRANT ajoutés à la main (AGENTS.md règle #2) ; aucun DELETE (règle #6). grade_age_norms est un
    /// PARAMÉTRAGE : comme school_settings, elle survit à « Réinitialiser les données » (aucune FK vers une table
    /// purgée), donc les fonctions de purge ne changent pas.
    /// </summary>
    public partial class AddIefMapping : Migration
    {
        private const string AppRole = "sama_ecole_app";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsTransferredIn",
                table: "enrollments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PreviousSchoolName",
                table: "enrollments",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "grade_age_norms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    GradeLevel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    MinAge = table.Column<int>(type: "integer", nullable: false),
                    MaxAge = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_grade_age_norms", x => x.Id);
                    table.CheckConstraint("CK_grade_age_norms_range", "\"MinAge\" >= 0 AND \"MaxAge\" >= \"MinAge\" AND \"MaxAge\" <= 30");
                    table.ForeignKey(
                        name: "FK_grade_age_norms_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_grade_age_norms_level",
                table: "grade_age_norms",
                columns: new[] { "SchoolId", "GradeLevel" },
                unique: true,
                filter: "NOT \"IsDeleted\"");

            // --- Isolation multi-tenant (AGENTS.md règle #2) ---
            migrationBuilder.Sql("ALTER TABLE \"grade_age_norms\" ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("""
                CREATE POLICY grade_age_norms_tenant_isolation ON "grade_age_norms"
                    USING ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid)
                    WITH CHECK ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid);
                """);
            migrationBuilder.Sql($$"""
                DO $inner$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{{AppRole}}') THEN
                        EXECUTE 'GRANT SELECT, INSERT, UPDATE ON "grade_age_norms" TO {{AppRole}}';
                    ELSE
                        RAISE WARNING 'Rôle % absent : l''application ne pourra pas lire la table grade_age_norms.', '{{AppRole}}';
                    END IF;
                END
                $inner$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS grade_age_norms_tenant_isolation ON \"grade_age_norms\";");

            migrationBuilder.DropTable(
                name: "grade_age_norms");

            migrationBuilder.DropColumn(
                name: "IsTransferredIn",
                table: "enrollments");

            migrationBuilder.DropColumn(
                name: "PreviousSchoolName",
                table: "enrollments");
        }
    }
}
