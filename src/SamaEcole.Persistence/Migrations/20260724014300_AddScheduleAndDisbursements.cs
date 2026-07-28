using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduleAndDisbursements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Disbursements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Category = table.Column<string>(type: "text", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    PaymentMethod = table.Column<string>(type: "text", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Beneficiary = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    ReceiptUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("PK_Disbursements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScheduleSlots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeacherId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClassroomId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    DayOfWeek = table.Column<string>(type: "text", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    RoomNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    IsTeacherSubmitted = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_ScheduleSlots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScheduleSlots_classrooms_ClassroomId",
                        column: x => x.ClassroomId,
                        principalTable: "classrooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScheduleSlots_subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScheduleSlots_teachers_TeacherId",
                        column: x => x.TeacherId,
                        principalTable: "teachers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Disbursements_SchoolId_Date",
                table: "Disbursements",
                columns: new[] { "SchoolId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleSlots_ClassroomId",
                table: "ScheduleSlots",
                column: "ClassroomId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleSlots_SchoolId_ClassroomId",
                table: "ScheduleSlots",
                columns: new[] { "SchoolId", "ClassroomId" });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleSlots_SchoolId_TeacherId",
                table: "ScheduleSlots",
                columns: new[] { "SchoolId", "TeacherId" });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleSlots_SubjectId",
                table: "ScheduleSlots",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleSlots_TeacherId",
                table: "ScheduleSlots",
                column: "TeacherId");

            // --- Isolation multi-tenant (AGENTS.md règle #2) ---
            string[] tenantTables = ["Disbursements", "ScheduleSlots"];
            foreach (var table in tenantTables)
            {
                migrationBuilder.Sql($"ALTER TABLE \"{table}\" ENABLE ROW LEVEL SECURITY;");

                migrationBuilder.Sql($"""
                    CREATE POLICY {table}_tenant_isolation ON "{table}"
                        USING ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid)
                        WITH CHECK ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid);
                    """);

                migrationBuilder.Sql($"""
                    DO $$
                    BEGIN
                        IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'sama_ecole_app') THEN
                            EXECUTE 'GRANT SELECT, INSERT, UPDATE, DELETE ON "{table}" TO sama_ecole_app';
                        ELSE
                            RAISE WARNING 'Rôle % absent : l''application ne pourra pas lire la table {table}.', 'sama_ecole_app';
                        END IF;
                    END
                    $$;
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            string[] tenantTables = ["Disbursements", "ScheduleSlots"];
            foreach (var table in tenantTables)
            {
                migrationBuilder.Sql($"DROP POLICY IF EXISTS {table}_tenant_isolation ON \"{table}\";");
            }

            migrationBuilder.DropTable(
                name: "Disbursements");

            migrationBuilder.DropTable(
                name: "ScheduleSlots");
        }
    }
}
