using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Ticket JGK-C01 — table `school_years`.
    ///
    /// Trois choses qu'EF Core ne génère PAS et qu'il faut poser à la main :
    ///
    ///   1. La policy RLS. Une table tenant protégée par le seul Global Query Filter EF Core n'est pas
    ///      protégée (AGENTS.md règle #2 : les DEUX, jamais une seule). `school_years` rejoint donc
    ///      TenantTables aux côtés de students, classrooms, school_settings, subscriptions et
    ///      matricule_sequences.
    ///
    ///   2. Les GRANT pour le rôle applicatif. Le `GRANT ... ON ALL TABLES` de la migration
    ///      EnableRowLevelSecurity ne vaut que pour les tables existant À CE MOMENT-LÀ : une table
    ///      créée plus tard n'hérite de rien, et l'application se heurterait à un « permission denied
    ///      for table school_years » au premier appel. DELETE n'est volontairement pas accordé :
    ///      aucune suppression physique de donnée métier (AGENTS.md règle #6), le soft delete est un
    ///      UPDATE.
    ///
    ///   3. Rien d'autre — mais un point mérite d'être souligné, car il est le cœur du ticket :
    ///      l'index UX_school_years_single_active, lui, EST généré par EF (il est déclaré dans
    ///      SchoolYearConfiguration). C'est un index unique PARTIEL sur (SchoolId) restreint aux
    ///      lignes actives et non supprimées : il rend « deux années actives dans une même école »
    ///      structurellement impossible, y compris entre deux transactions concurrentes qui auraient
    ///      toutes deux lu « aucune année active » avant d'écrire. La règle « une seule année active à
    ///      la fois » tient donc dans la BASE, et pas seulement dans un `if` du Handler.
    /// </summary>
    public partial class AddSchoolYears : Migration
    {
        private const string Table = "school_years";
        private const string AppRole = "sama_ecole_app";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "school_years",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    Label = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_school_years", x => x.Id);
                    table.ForeignKey(
                        name: "FK_school_years_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_school_years_SchoolId_Label_IsDeleted",
                table: "school_years",
                columns: new[] { "SchoolId", "Label", "IsDeleted" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_school_years_single_active",
                table: "school_years",
                column: "SchoolId",
                unique: true,
                filter: "\"IsActive\" AND NOT \"IsDeleted\"");

            // --- Isolation multi-tenant (AGENTS.md règle #2) ---
            migrationBuilder.Sql($"ALTER TABLE {Table} ENABLE ROW LEVEL SECURITY;");

            // USING      -> lignes visibles en lecture (SELECT, et cibles d'UPDATE/DELETE).
            // WITH CHECK -> lignes autorisées en écriture : interdit d'INSÉRER une année pour une autre
            //               école, ou de déplacer une année existante vers une autre école.
            migrationBuilder.Sql($"""
                CREATE POLICY {Table}_tenant_isolation ON {Table}
                    USING ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid)
                    WITH CHECK ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid);
                """);

            // Droits sur CETTE table : le GRANT ON ALL TABLES d'une migration antérieure ne pouvait pas
            // la couvrir, elle n'existait pas encore. Le rôle est créé hors migration
            // (docker/postgres/init en dev et CI) : s'il est absent, on n'échoue pas la migration — on
            // avertit, exactement comme le fait EnableRowLevelSecurity.
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
                name: "school_years");
        }
    }
}
