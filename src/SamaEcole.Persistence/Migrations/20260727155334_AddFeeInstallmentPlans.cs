using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFeeInstallmentPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DebtorReminderThresholdDays",
                table: "school_settings",
                type: "integer",
                nullable: false,
                defaultValue: 7);

            migrationBuilder.CreateTable(
                name: "debtor_reminder_batches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClassroomId = table.Column<Guid>(type: "uuid", nullable: false),
                    ThresholdDays = table.Column<int>(type: "integer", nullable: false),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SentByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("PK_debtor_reminder_batches", x => x.Id);
                    table.UniqueConstraint("AK_debtor_reminder_batches_SchoolId_Id", x => new { x.SchoolId, x.Id });
                    table.ForeignKey(
                        name: "FK_debtor_reminder_batches_classrooms_SchoolId_ClassroomId",
                        columns: x => new { x.SchoolId, x.ClassroomId },
                        principalTable: "classrooms",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_debtor_reminder_batches_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fee_installment_plans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnrollmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedFromClassroomTemplate = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_fee_installment_plans", x => x.Id);
                    table.UniqueConstraint("AK_fee_installment_plans_SchoolId_Id", x => new { x.SchoolId, x.Id });
                    table.ForeignKey(
                        name: "FK_fee_installment_plans_enrollments_SchoolId_EnrollmentId",
                        columns: x => new { x.SchoolId, x.EnrollmentId },
                        principalTable: "enrollments",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fee_installment_plans_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "debtor_reminder_batch_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    DebtorReminderBatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnrollmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    GuardianPhone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    RemainingBalance = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    DaysOverdue = table.Column<int>(type: "integer", nullable: false),
                    SmsMessageId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_debtor_reminder_batch_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_debtor_reminder_batch_items_debtor_reminder_batches_SchoolI~",
                        columns: x => new { x.SchoolId, x.DebtorReminderBatchId },
                        principalTable: "debtor_reminder_batches",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_debtor_reminder_batch_items_enrollments_SchoolId_Enrollment~",
                        columns: x => new { x.SchoolId, x.EnrollmentId },
                        principalTable: "enrollments",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_debtor_reminder_batch_items_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fee_installments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    FeeInstallmentPlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    SequenceNo = table.Column<int>(type: "integer", nullable: false),
                    Label = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
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
                    table.PrimaryKey("PK_fee_installments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_fee_installments_fee_installment_plans_SchoolId_FeeInstallm~",
                        columns: x => new { x.SchoolId, x.FeeInstallmentPlanId },
                        principalTable: "fee_installment_plans",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fee_installments_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_debtor_reminder_batch_items_SchoolId_DebtorReminderBatchId",
                table: "debtor_reminder_batch_items",
                columns: new[] { "SchoolId", "DebtorReminderBatchId" });

            migrationBuilder.CreateIndex(
                name: "IX_debtor_reminder_batch_items_SchoolId_EnrollmentId",
                table: "debtor_reminder_batch_items",
                columns: new[] { "SchoolId", "EnrollmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_debtor_reminder_batches_SchoolId_ClassroomId",
                table: "debtor_reminder_batches",
                columns: new[] { "SchoolId", "ClassroomId" });

            migrationBuilder.CreateIndex(
                name: "IX_debtor_reminder_batches_SchoolId_Status_GeneratedAt",
                table: "debtor_reminder_batches",
                columns: new[] { "SchoolId", "Status", "GeneratedAt" });

            migrationBuilder.CreateIndex(
                name: "UX_fee_installment_plans_single_active_per_enrollment",
                table: "fee_installment_plans",
                columns: new[] { "SchoolId", "EnrollmentId" },
                unique: true,
                filter: "\"Status\" = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_fee_installments_SchoolId_FeeInstallmentPlanId_SequenceNo",
                table: "fee_installments",
                columns: new[] { "SchoolId", "FeeInstallmentPlanId", "SequenceNo" },
                unique: true);

            // --- Isolation multi-tenant (AGENTS.md règle #2) ---
            // EF ne génère JAMAIS ces policies : elles s'écrivent à la main, comme dans
            // AddBuildingsAndRooms. Une table tenant protégée par le seul filtre EF fuit dès la
            // première requête SQL brute — RlsCoverageTests échoue si l'une d'elles manque.
            string[] tenantTables =
            [
                "fee_installment_plans", "fee_installments",
                "debtor_reminder_batches", "debtor_reminder_batch_items"
            ];
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
            string[] tenantTables =
            [
                "fee_installment_plans", "fee_installments",
                "debtor_reminder_batches", "debtor_reminder_batch_items"
            ];
            foreach (var table in tenantTables)
            {
                migrationBuilder.Sql($"DROP POLICY IF EXISTS {table}_tenant_isolation ON \"{table}\";");
            }

            migrationBuilder.DropTable(
                name: "debtor_reminder_batch_items");

            migrationBuilder.DropTable(
                name: "fee_installments");

            migrationBuilder.DropTable(
                name: "debtor_reminder_batches");

            migrationBuilder.DropTable(
                name: "fee_installment_plans");

            migrationBuilder.DropColumn(
                name: "DebtorReminderThresholdDays",
                table: "school_settings");
        }
    }
}
