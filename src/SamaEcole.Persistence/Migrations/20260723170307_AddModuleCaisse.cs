using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddModuleCaisse : Migration
    {
        private static readonly string[] TenantTables = ["cashier_sessions", "payment_breakdowns"];
        private const string AppRole = "sama_ecole_app";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CashierSessionId",
                table: "payments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "payments",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ReferencePeriod",
                table: "payments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "cashier_sessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    CashierId = table.Column<Guid>(type: "uuid", nullable: false),
                    OpenedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OpeningBalance = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    ClosingBalance = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
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
                    table.PrimaryKey("PK_cashier_sessions", x => x.Id);
                    table.UniqueConstraint("AK_cashier_sessions_SchoolId_Id", x => new { x.SchoolId, x.Id });
                    table.ForeignKey(
                        name: "FK_cashier_sessions_users_CashierId",
                        column: x => x.CashierId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payment_breakdowns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    FeeCategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    AmountAllocated = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
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
                    table.PrimaryKey("PK_payment_breakdowns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_payment_breakdowns_fee_categories_FeeCategoryId",
                        column: x => x.FeeCategoryId,
                        principalTable: "fee_categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_payment_breakdowns_payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_payments_SchoolId_CashierSessionId",
                table: "payments",
                columns: new[] { "SchoolId", "CashierSessionId" });

            migrationBuilder.CreateIndex(
                name: "IX_cashier_sessions_CashierId",
                table: "cashier_sessions",
                column: "CashierId");

            migrationBuilder.CreateIndex(
                name: "UX_cashier_sessions_SchoolId_CashierId_Open",
                table: "cashier_sessions",
                columns: new[] { "SchoolId", "CashierId" },
                unique: true,
                filter: "\"Status\" = 'Open'");

            migrationBuilder.CreateIndex(
                name: "IX_payment_breakdowns_FeeCategoryId",
                table: "payment_breakdowns",
                column: "FeeCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_payment_breakdowns_PaymentId",
                table: "payment_breakdowns",
                column: "PaymentId");

            migrationBuilder.AddForeignKey(
                name: "FK_payments_cashier_sessions_SchoolId_CashierSessionId",
                table: "payments",
                columns: new[] { "SchoolId", "CashierSessionId" },
                principalTable: "cashier_sessions",
                principalColumns: new[] { "SchoolId", "Id" },
                onDelete: ReferentialAction.Restrict);

            // --- Isolation multi-tenant (AGENTS.md règle #2) ---
            foreach (var table in TenantTables)
            {
                migrationBuilder.Sql($"ALTER TABLE {table} ENABLE ROW LEVEL SECURITY;");

                migrationBuilder.Sql($"""
                    CREATE POLICY {table}_tenant_isolation ON {table}
                        USING ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid)
                        WITH CHECK ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid);
                    """);

                migrationBuilder.Sql($"""
                    DO $$
                    BEGIN
                        IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{AppRole}') THEN
                            EXECUTE 'GRANT SELECT, INSERT, UPDATE ON {table} TO {AppRole}';
                        ELSE
                            RAISE WARNING 'Rôle % absent : l''application ne pourra pas lire la table {table}.', '{AppRole}';
                        END IF;
                    END
                    $$;
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in TenantTables)
            {
                migrationBuilder.Sql($"DROP POLICY IF EXISTS {table}_tenant_isolation ON {table};");
            }

            migrationBuilder.DropForeignKey(
                name: "FK_payments_cashier_sessions_SchoolId_CashierSessionId",
                table: "payments");

            migrationBuilder.DropTable(
                name: "cashier_sessions");

            migrationBuilder.DropTable(
                name: "payment_breakdowns");

            migrationBuilder.DropIndex(
                name: "IX_payments_SchoolId_CashierSessionId",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "CashierSessionId",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "ReferencePeriod",
                table: "payments");
        }
    }
}
