using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Ticket JGK-C02 — table `classrooms`.
    ///
    /// Trois choses qu'EF Core ne génère PAS et qu'il faut poser à la main :
    ///
    ///   1. La policy RLS. Une table tenant protégée par le seul Global Query Filter EF Core n'est
    ///      pas protégée (AGENTS.md règle #2 : les DEUX, jamais une seule). `classrooms` rejoint donc
    ///      TenantTables aux côtés de students, subscriptions et matricule_sequences.
    ///
    ///   2. Les GRANT pour le rôle applicatif. Le `GRANT ... ON ALL TABLES` de la migration
    ///      EnableRowLevelSecurity ne vaut que pour les tables existant À CE MOMENT-LÀ : une table
    ///      créée plus tard n'hérite de rien, et l'application se heurterait à un « permission denied
    ///      for table classrooms » au premier appel.
    ///
    ///   3. La clé étrangère COMPOSITE (SchoolId, ClassroomId) depuis students, générée par EF grâce
    ///      à la configuration : elle rend structurellement impossible qu'un élève d'une école
    ///      référence la classe d'une autre. La RLS masque une telle ligne, mais ne l'empêche pas
    ///      d'exister — sa clause WITH CHECK ne contrôle que le SchoolId de la ligne écrite.
    /// </summary>
    public partial class AddClassrooms : Migration
    {
        private const string Table = "classrooms";
        private const string AppRole = "sama_ecole_app";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "classrooms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Level = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Capacity = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_classrooms", x => x.Id);
                    table.UniqueConstraint("AK_classrooms_SchoolId_Id", x => new { x.SchoolId, x.Id });
                    table.ForeignKey(
                        name: "FK_classrooms_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_students_SchoolId_ClassroomId",
                table: "students",
                columns: new[] { "SchoolId", "ClassroomId" });

            migrationBuilder.CreateIndex(
                name: "IX_classrooms_SchoolId",
                table: "classrooms",
                column: "SchoolId");

            migrationBuilder.CreateIndex(
                name: "IX_classrooms_SchoolId_Name_IsDeleted",
                table: "classrooms",
                columns: new[] { "SchoolId", "Name", "IsDeleted" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_students_classrooms_SchoolId_ClassroomId",
                table: "students",
                columns: new[] { "SchoolId", "ClassroomId" },
                principalTable: "classrooms",
                principalColumns: new[] { "SchoolId", "Id" },
                onDelete: ReferentialAction.Restrict);

            // --- Isolation multi-tenant (AGENTS.md règle #2) ---
            migrationBuilder.Sql($"ALTER TABLE {Table} ENABLE ROW LEVEL SECURITY;");

            // USING      -> lignes visibles en lecture (SELECT, et cibles d'UPDATE/DELETE).
            // WITH CHECK -> lignes autorisées en écriture : interdit d'INSÉRER une classe pour une
            //               autre école, ou de déplacer une classe existante vers une autre école.
            migrationBuilder.Sql($"""
                CREATE POLICY {Table}_tenant_isolation ON {Table}
                    USING ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid)
                    WITH CHECK ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid);
                """);

            // Droits sur CETTE table : le GRANT ON ALL TABLES d'une migration antérieure ne pouvait
            // pas la couvrir, elle n'existait pas encore. Le rôle est créé hors migration
            // (docker/postgres/init en dev et CI) : s'il est absent, on n'échoue pas la migration —
            // on avertit, exactement comme le fait EnableRowLevelSecurity.
            migrationBuilder.Sql($"""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{AppRole}') THEN
                        EXECUTE 'GRANT SELECT, INSERT, UPDATE, DELETE ON {Table} TO {AppRole}';
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

            migrationBuilder.DropForeignKey(
                name: "FK_students_classrooms_SchoolId_ClassroomId",
                table: "students");

            migrationBuilder.DropTable(
                name: "classrooms");

            migrationBuilder.DropIndex(
                name: "IX_students_SchoolId_ClassroomId",
                table: "students");
        }
    }
}
