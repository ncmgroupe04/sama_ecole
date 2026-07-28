using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Ticket JGK-A05 — journal des changements de statut de compte.
    ///
    /// Table tenant : policy RLS + Global Query Filter comme toute donnée d'école (règle #2).
    ///
    /// APPEND-ONLY IMPOSÉ PAR LA BASE : docs/Volume_7_Security.md §7 exige un journal « consultable
    /// mais jamais modifiable, y compris par un administrateur ». Le rôle applicatif ne reçoit donc
    /// que SELECT et INSERT — UPDATE et DELETE lui sont explicitement RETIRÉS. C'est nécessaire :
    /// docker/postgres/init accorde par ALTER DEFAULT PRIVILEGES un SELECT/INSERT/UPDATE/DELETE sur
    /// toute table future, ce qui rendrait ce journal réécrivable si on ne révoquait rien ici.
    /// Résultat : même un bug applicatif ou une injection SQL ne peut pas réécrire l'histoire.
    /// </summary>
    public partial class AddUserStatusHistory : Migration
    {
        private const string AppRole = "sama_ecole_app";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_status_history",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreviousStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    NewStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ChangedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
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
                    table.PrimaryKey("PK_user_status_history", x => x.Id);
                    table.ForeignKey(
                        name: "FK_user_status_history_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_user_status_history_UserId_ChangedAt",
                table: "user_status_history",
                columns: new[] { "UserId", "ChangedAt" });

            // Isolation multi-tenant, identique aux autres tables tenant (migration EnableRowLevelSecurity).
            migrationBuilder.Sql("ALTER TABLE user_status_history ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("""
                CREATE POLICY user_status_history_tenant_isolation ON user_status_history
                    USING ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid)
                    WITH CHECK ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid);
                """);

            migrationBuilder.Sql($"""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{AppRole}') THEN
                        -- On repart de zéro : les ALTER DEFAULT PRIVILEGES ont pu accorder UPDATE/DELETE.
                        EXECUTE 'REVOKE ALL ON user_status_history FROM {AppRole}';
                        EXECUTE 'GRANT SELECT, INSERT ON user_status_history TO {AppRole}';
                    ELSE
                        RAISE WARNING 'Rôle % absent : le journal de statuts sera inaccessible à l''application.', '{AppRole}';
                    END IF;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS user_status_history_tenant_isolation ON user_status_history;");

            migrationBuilder.DropTable(
                name: "user_status_history");
        }
    }
}
