using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSmsNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SmsCreditBalance",
                table: "school_settings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "SmsOnAttendanceAlert",
                table: "school_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SmsOnDuesReminder",
                table: "school_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SmsOnPaymentReceipt",
                table: "school_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "sms_messages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    Recipient = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Body = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Trigger = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ProviderMessageId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SegmentCount = table.Column<int>(type: "integer", nullable: false),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: true),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
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
                    table.PrimaryKey("PK_sms_messages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sms_messages_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sms_messages_students_SchoolId_StudentId",
                        columns: x => new { x.SchoolId, x.StudentId },
                        principalTable: "students",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sms_messages_SchoolId_SentAt",
                table: "sms_messages",
                columns: new[] { "SchoolId", "SentAt" });

            migrationBuilder.CreateIndex(
                name: "IX_sms_messages_SchoolId_StudentId",
                table: "sms_messages",
                columns: new[] { "SchoolId", "StudentId" });

            // --- Isolation multi-tenant (AGENTS.md règle #2) ---
            // EF ne génère JAMAIS ces policies : elles s'écrivent à la main, comme dans
            // AddBuildingsAndRooms. Une table tenant protégée par le seul filtre EF fuit dès la
            // première requête SQL brute — RlsCoverageTests échoue si celle-ci manque.
            //
            // `school_settings` est déjà sous policy depuis AddSchoolSettings : les quatre colonnes
            // ajoutées ci-dessus en héritent, rien à poser de plus pour elles.
            const string appRole = "sama_ecole_app";

            migrationBuilder.Sql("ALTER TABLE \"sms_messages\" ENABLE ROW LEVEL SECURITY;");

            migrationBuilder.Sql("""
                CREATE POLICY sms_messages_tenant_isolation ON "sms_messages"
                    USING ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid)
                    WITH CHECK ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid);
                """);

            // SELECT/INSERT/UPDATE uniquement — aucun DELETE : le soft delete n'émet jamais de SQL
            // DELETE (AGENTS.md règle #6).
            migrationBuilder.Sql($$"""
                DO $inner$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{{appRole}}') THEN
                        EXECUTE 'GRANT SELECT, INSERT, UPDATE ON "sms_messages" TO {{appRole}}';
                    ELSE
                        RAISE WARNING 'Rôle % absent : l''application ne pourra pas lire la table sms_messages.', '{{appRole}}';
                    END IF;
                END
                $inner$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sms_messages");

            migrationBuilder.DropColumn(
                name: "SmsCreditBalance",
                table: "school_settings");

            migrationBuilder.DropColumn(
                name: "SmsOnAttendanceAlert",
                table: "school_settings");

            migrationBuilder.DropColumn(
                name: "SmsOnDuesReminder",
                table: "school_settings");

            migrationBuilder.DropColumn(
                name: "SmsOnPaymentReceipt",
                table: "school_settings");
        }
    }
}
