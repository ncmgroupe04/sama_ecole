using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Ticket JGK-H01 — journal d'audit centralisé (docs/Volume_7_Security.md §7).
    ///
    /// Table tenant standard (SchoolId non nul, contrairement à `users` : docs/Volume_3_DDS.md §4.6 ne
    /// classe PAS AuditLogs parmi les entités hors périmètre SchoolId de §2.3 — seule PlatformAuditLogs,
    /// une table distincte hors MVP, l'est) : policy RLS + Global Query Filter (règle #2).
    ///
    /// APPEND-ONLY IMPOSÉ PAR LA BASE, comme user_status_history : docs/Volume_7_Security.md §7 exige
    /// un journal « consultable mais jamais modifiable, y compris par un administrateur ». Le rôle
    /// applicatif ne reçoit donc que SELECT et INSERT — UPDATE et DELETE lui sont explicitement retirés.
    /// </summary>
    public partial class AddAuditLogs : Migration
    {
        private const string AppRole = "sama_ecole_app";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Module = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Success = table.Column<bool>(type: "boolean", nullable: false),
                    FailureReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IpAddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
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
                    table.PrimaryKey("PK_audit_logs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_audit_logs_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    // FK directe vers schools (pas seulement via users) : l'acteur d'une entrée d'audit
                    // (ex. le Super Admin, SchoolId nul) peut être d'une école DIFFÉRENTE de celle
                    // désignée par l'entrée (ex. CreateSchoolCommand — l'école qu'il vient de créer).
                    table.ForeignKey(
                        name: "FK_audit_logs_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_SchoolId_OccurredAt",
                table: "audit_logs",
                columns: new[] { "SchoolId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_UserId",
                table: "audit_logs",
                column: "UserId");

            // Isolation multi-tenant, identique aux autres tables tenant (migration EnableRowLevelSecurity).
            migrationBuilder.Sql("ALTER TABLE audit_logs ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("""
                CREATE POLICY audit_logs_tenant_isolation ON audit_logs
                    USING ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid)
                    WITH CHECK ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid);
                """);

            migrationBuilder.Sql($"""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{AppRole}') THEN
                        -- On repart de zéro : les ALTER DEFAULT PRIVILEGES ont pu accorder UPDATE/DELETE.
                        EXECUTE 'REVOKE ALL ON audit_logs FROM {AppRole}';
                        EXECUTE 'GRANT SELECT, INSERT ON audit_logs TO {AppRole}';
                    ELSE
                        RAISE WARNING 'Rôle % absent : le journal d''audit sera inaccessible à l''application.', '{AppRole}';
                    END IF;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS audit_logs_tenant_isolation ON audit_logs;");

            migrationBuilder.DropTable(
                name: "audit_logs");
        }
    }
}
