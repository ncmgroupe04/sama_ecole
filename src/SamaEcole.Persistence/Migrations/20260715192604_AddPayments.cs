using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Ticket JGK-F02 — caisse : table `payments` (encaissements) + colonne `AmountPaid` sur
    /// `enrollments` (cumul encaissé, le solde = TotalDue − AmountPaid).
    ///
    /// Ce qu'EF Core ne génère PAS et qu'il faut poser à la main (comme AddEnrollments / AddFees) :
    ///   1. La policy RLS de la table tenant `payments` (AGENTS.md règle #2), en plus du filtre EF Core.
    ///   2. Les GRANT du rôle applicatif : SELECT, INSERT, UPDATE — jamais DELETE (soft delete, règle #6).
    ///
    /// Aucun verrou optimiste n'est posé sur `payments` : le solde vit sur `enrollments`, dont la colonne
    /// système `xmin` sérialise deux encaissements concurrents (règle #5) — aucun DDL pour elle.
    /// </summary>
    public partial class AddPayments : Migration
    {
        private const string AppRole = "sama_ecole_app";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AmountPaid",
                table: "enrollments",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnrollmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    Method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    BalanceAfter = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    ReceiptNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ReceivedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaidAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
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
                    table.PrimaryKey("PK_payments", x => x.Id);
                    table.CheckConstraint("CK_payments_amount_positive", "\"Amount\" > 0");
                    table.ForeignKey(
                        name: "FK_payments_enrollments_SchoolId_EnrollmentId",
                        columns: x => new { x.SchoolId, x.EnrollmentId },
                        principalTable: "enrollments",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_payments_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_payments_SchoolId_EnrollmentId",
                table: "payments",
                columns: new[] { "SchoolId", "EnrollmentId" });

            migrationBuilder.CreateIndex(
                name: "UX_payments_receipt_number",
                table: "payments",
                columns: new[] { "SchoolId", "ReceiptNumber" },
                unique: true);

            // --- Isolation multi-tenant (AGENTS.md règle #2) : la nouvelle table `payments` ---
            migrationBuilder.Sql("ALTER TABLE payments ENABLE ROW LEVEL SECURITY;");

            // USING      -> lignes visibles en lecture (SELECT, et cibles d'UPDATE).
            // WITH CHECK -> lignes autorisées en écriture : interdit d'encaisser pour une autre école.
            migrationBuilder.Sql("""
                CREATE POLICY payments_tenant_isolation ON payments
                    USING ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid)
                    WITH CHECK ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid);
                """);

            // Droits du rôle applicatif : encaisser (INSERT) et corriger un statut (UPDATE), jamais
            // supprimer physiquement (soft delete, règle #6). Le GRANT ON ALL TABLES d'une migration
            // antérieure ne couvrait pas `payments`, qui n'existait pas encore.
            migrationBuilder.Sql($"""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{AppRole}') THEN
                        EXECUTE 'GRANT SELECT, INSERT, UPDATE ON payments TO {AppRole}';
                    ELSE
                        RAISE WARNING 'Rôle % absent : l''application ne pourra pas accéder aux paiements.', '{AppRole}';
                    END IF;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS payments_tenant_isolation ON payments;");

            migrationBuilder.DropTable(
                name: "payments");

            migrationBuilder.DropColumn(
                name: "AmountPaid",
                table: "enrollments");
        }
    }
}
