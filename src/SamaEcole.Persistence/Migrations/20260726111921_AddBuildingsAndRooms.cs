using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBuildingsAndRooms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "buildings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("PK_buildings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_buildings_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "rooms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Capacity = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "SalleDeClasse"),
                    BuildingId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_rooms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_rooms_buildings_BuildingId",
                        column: x => x.BuildingId,
                        principalTable: "buildings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_rooms_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_buildings_SchoolId",
                table: "buildings",
                column: "SchoolId");

            migrationBuilder.CreateIndex(
                name: "IX_buildings_SchoolId_Name_IsDeleted",
                table: "buildings",
                columns: new[] { "SchoolId", "Name", "IsDeleted" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_rooms_BuildingId",
                table: "rooms",
                column: "BuildingId");

            migrationBuilder.CreateIndex(
                name: "IX_rooms_SchoolId_BuildingId",
                table: "rooms",
                columns: new[] { "SchoolId", "BuildingId" });

            migrationBuilder.CreateIndex(
                name: "IX_rooms_SchoolId_BuildingId_Name_IsDeleted",
                table: "rooms",
                columns: new[] { "SchoolId", "BuildingId", "Name", "IsDeleted" },
                unique: true);

            // --- Isolation multi-tenant (AGENTS.md règle #2) ---
            // EF ne génère JAMAIS ces policies : elles s'écrivent à la main, comme dans
            // AddDocumentsModuleEntities. Une table tenant protégée par le seul filtre EF fuit dès la
            // première requête SQL brute — RlsCoverageTests échoue si l'une d'elles manque.
            string[] tenantTables = ["buildings", "rooms"];
            string appRole = "sama_ecole_app";
            foreach (var table in tenantTables)
            {
                migrationBuilder.Sql($"ALTER TABLE \"{table}\" ENABLE ROW LEVEL SECURITY;");

                migrationBuilder.Sql($$"""
                    CREATE POLICY {{table}}_tenant_isolation ON "{{table}}"
                        USING ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid)
                        WITH CHECK ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid);
                    """);

                // SELECT/INSERT/UPDATE uniquement — aucun DELETE : le soft delete n'émet jamais de SQL
                // DELETE (AGENTS.md règle #6).
                migrationBuilder.Sql($$"""
                    DO $inner$
                    BEGIN
                        IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{{appRole}}') THEN
                            EXECUTE 'GRANT SELECT, INSERT, UPDATE ON "{{table}}" TO {{appRole}}';
                        ELSE
                            RAISE WARNING 'Rôle % absent : l''application ne pourra pas lire la table {{table}}.', '{{appRole}}';
                        END IF;
                    END
                    $inner$;
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            string[] tenantTables = ["buildings", "rooms"];
            foreach (var table in tenantTables)
            {
                migrationBuilder.Sql($"DROP POLICY IF EXISTS {table}_tenant_isolation ON \"{table}\";");
            }

            migrationBuilder.DropTable(
                name: "rooms");

            migrationBuilder.DropTable(
                name: "buildings");
        }
    }
}
