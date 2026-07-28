using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Ticket JGK-G02 — table `mentions` (seuils de moyenne configurables). Ce qu'EF Core ne génère PAS
    /// et qu'il faut poser à la main (voir AddSubjects) : la policy RLS et le GRANT du rôle applicatif.
    /// </summary>
    public partial class AddMentions : Migration
    {
        private const string Table = "mentions";
        private const string AppRole = "sama_ecole_app";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mentions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    Label = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    MinAverage = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
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
                    table.PrimaryKey("PK_mentions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_mentions_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_mentions_SchoolId_Label_IsDeleted",
                table: "mentions",
                columns: new[] { "SchoolId", "Label", "IsDeleted" },
                unique: true);

            // --- Isolation multi-tenant (AGENTS.md règle #2) ---
            migrationBuilder.Sql($"ALTER TABLE {Table} ENABLE ROW LEVEL SECURITY;");

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
                        RAISE WARNING 'Rôle % absent : l''application ne pourra pas accéder à la table {Table}.', '{AppRole}';
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
                name: "mentions");
        }
    }
}
