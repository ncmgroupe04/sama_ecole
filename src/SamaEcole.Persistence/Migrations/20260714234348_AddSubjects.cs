using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Ticket JGK-C03 — table `subjects`.
    ///
    /// Deux choses qu'EF Core ne génère PAS et qu'il faut poser à la main (voir AddClassrooms pour le
    /// raisonnement détaillé) :
    ///
    ///   1. La policy RLS. Une table tenant protégée par le seul Global Query Filter EF Core n'est pas
    ///      protégée (AGENTS.md règle #2). `subjects` rejoint TenantTables aux côtés de students,
    ///      classrooms, school_years, school_settings, subscriptions et matricule_sequences.
    ///
    ///   2. Le GRANT pour le rôle applicatif — le GRANT ON ALL TABLES d'une migration antérieure ne
    ///      couvrait pas une table qui n'existait pas encore. DELETE n'est pas accordé : le soft
    ///      delete est un UPDATE (AGENTS.md règle #6).
    /// </summary>
    public partial class AddSubjects : Migration
    {
        private const string Table = "subjects";
        private const string AppRole = "sama_ecole_app";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "subjects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Level = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Coefficient = table.Column<decimal>(type: "numeric(4,2)", precision: 4, scale: 2, nullable: false),
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
                    table.PrimaryKey("PK_subjects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_subjects_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_subjects_SchoolId",
                table: "subjects",
                column: "SchoolId");

            migrationBuilder.CreateIndex(
                name: "IX_subjects_SchoolId_Level_Name_IsDeleted",
                table: "subjects",
                columns: new[] { "SchoolId", "Level", "Name", "IsDeleted" },
                unique: true);

            // --- Isolation multi-tenant (AGENTS.md règle #2) ---
            migrationBuilder.Sql($"ALTER TABLE {Table} ENABLE ROW LEVEL SECURITY;");

            // USING      -> lignes visibles en lecture (SELECT, et cibles d'UPDATE/DELETE).
            // WITH CHECK -> lignes autorisées en écriture : interdit d'INSÉRER une matière pour une
            //               autre école, ou de déplacer une matière existante vers une autre école.
            migrationBuilder.Sql($"""
                CREATE POLICY {Table}_tenant_isolation ON {Table}
                    USING ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid)
                    WITH CHECK ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid);
                """);

            migrationBuilder.Sql($"""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{AppRole}') THEN
                        EXECUTE 'GRANT SELECT, INSERT, UPDATE ON {Table} TO {AppRole}';
                    ELSE
                        RAISE WARNING 'Rôle % absent : l''application ne pourra pas lire la table {Table}.', '{AppRole}';
                    END IF;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"DROP POLICY IF EXISTS {Table}_tenant_isolation ON {Table};");

            migrationBuilder.DropTable(
                name: "subjects");
        }
    }
}
