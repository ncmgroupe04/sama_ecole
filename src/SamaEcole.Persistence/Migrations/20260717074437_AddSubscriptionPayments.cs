using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Ticket JGK-I05 — paiements d'abonnement (`subscription_payments`).
    ///
    /// Table TENANT normale (SchoolId non nul) : le Directeur l'écrit lui-même, avec son propre
    /// SchoolId de session — contrairement à `subscriptions`/`users`, AUCUNE fonction SECURITY DEFINER
    /// n'est nécessaire ici, un INSERT EF classique satisfait la policy RLS (AGENTS.md règle #2 : les
    /// DEUX protections, Global Query Filter + policy RLS, jamais une seule).
    ///
    /// GRANT restreint à SELECT/INSERT/UPDATE (jamais DELETE, soft delete uniquement — règle #6).
    /// UPDATE reste nécessaire : c'est lui que le futur traitement webhook (JGK-I06) utilisera pour
    /// faire passer une ligne d'Initiated à Confirmed/Failed — mais UNIQUEMENT via la fonction
    /// SECURITY DEFINER que ce ticket-là posera (l'acteur webhook, anonyme, n'a pas de schoolId).
    /// </summary>
    public partial class AddSubscriptionPayments : Migration
    {
        private const string AppRole = "sama_ecole_app";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "subscription_payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    BillingPeriod = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ProviderTransactionRef = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    WebhookPayloadRaw = table.Column<string>(type: "jsonb", nullable: true),
                    InitiatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("PK_subscription_payments", x => x.Id);
                    table.CheckConstraint("CK_subscription_payments_amount_positive", "\"Amount\" > 0");
                    table.ForeignKey(
                        name: "FK_subscription_payments_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_subscription_payments_subscriptions_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalTable: "subscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_subscription_payments_SchoolId_InitiatedAt",
                table: "subscription_payments",
                columns: new[] { "SchoolId", "InitiatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_subscription_payments_SubscriptionId",
                table: "subscription_payments",
                column: "SubscriptionId");

            migrationBuilder.CreateIndex(
                name: "UX_subscription_payments_provider_transaction_ref",
                table: "subscription_payments",
                column: "ProviderTransactionRef",
                unique: true,
                filter: "\"ProviderTransactionRef\" IS NOT NULL");

            // Isolation multi-tenant, identique aux autres tables tenant (migration EnableRowLevelSecurity).
            migrationBuilder.Sql("ALTER TABLE subscription_payments ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("""
                CREATE POLICY subscription_payments_tenant_isolation ON subscription_payments
                    USING ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid)
                    WITH CHECK ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid);
                """);

            migrationBuilder.Sql($"""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{AppRole}') THEN
                        -- Repart de zéro : les ALTER DEFAULT PRIVILEGES de docker/postgres/init ont pu
                        -- accorder DELETE. Modifiable (le futur webhook JGK-I06 fera passer Initiated ->
                        -- Confirmed/Failed) mais jamais supprimable physiquement (soft delete, règle #6).
                        EXECUTE 'REVOKE ALL ON subscription_payments FROM {AppRole}';
                        EXECUTE 'GRANT SELECT, INSERT, UPDATE ON subscription_payments TO {AppRole}';
                    ELSE
                        RAISE WARNING 'Rôle % absent : les paiements d''abonnement seront inaccessibles à l''application.', '{AppRole}';
                    END IF;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS subscription_payments_tenant_isolation ON subscription_payments;");

            migrationBuilder.DropTable(
                name: "subscription_payments");
        }
    }
}
