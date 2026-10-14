using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Grille tarifaire par tranche d'effectif, éditable par le Super Admin. TABLE PLATEFORME (comme
    /// promo_codes) : aucun SchoolId, donc ni Global Query Filter ni policy RLS.
    ///
    /// Sème la grille de départ validée le 2026-10-13 (mensuel / annuel = 10 mois payés, 2 offerts) avec
    /// des valeurs écrites EN DUR : une migration ne dépend jamais du code vivant. Aucune majoration Internat
    /// au départ (0) : le Super Admin la fixera depuis l'écran Tarification. Le rôle applicatif ne reçoit que
    /// SELECT et UPDATE : les lignes sont créées par cette migration, jamais par l'application.
    /// </summary>
    public partial class AddQuotaTierPrices : Migration
    {
        private const string Table = "quota_tier_prices";
        private const string AppRole = "sama_ecole_app";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "quota_tier_prices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Tier = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    MonthlyXof = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    AnnualXof = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    BoardingSurchargeMonthlyXof = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    BoardingSurchargeAnnualXof = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
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
                    table.PrimaryKey("PK_quota_tier_prices", x => x.Id);
                    table.CheckConstraint("CK_quota_tier_prices_prices_positive", "\"MonthlyXof\" > 0 AND \"AnnualXof\" > 0");
                    table.CheckConstraint("CK_quota_tier_prices_surcharges_not_negative", "\"BoardingSurchargeMonthlyXof\" >= 0 AND \"BoardingSurchargeAnnualXof\" >= 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_quota_tier_prices_Tier",
                table: "quota_tier_prices",
                column: "Tier",
                unique: true);

            migrationBuilder.Sql("""
                INSERT INTO quota_tier_prices
                    ("Id", "Tier", "MonthlyXof", "AnnualXof", "BoardingSurchargeMonthlyXof", "BoardingSurchargeAnnualXof",
                     "CreatedAt", "IsDeleted")
                VALUES
                    (gen_random_uuid(), 'Tier_100',   5000,  50000, 0, 0, now(), false),
                    (gen_random_uuid(), 'Tier1_150', 10000, 100000, 0, 0, now(), false),
                    (gen_random_uuid(), 'Tier_250',  15000, 150000, 0, 0, now(), false),
                    (gen_random_uuid(), 'Tier2_400', 25000, 250000, 0, 0, now(), false);
                """);

            migrationBuilder.Sql($$"""
                DO $inner$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{{AppRole}}') THEN
                        EXECUTE 'GRANT SELECT, UPDATE ON "{{Table}}" TO {{AppRole}}';
                    ELSE
                        RAISE WARNING 'Rôle % absent : l''application ne pourra pas lire la table {{Table}}.', '{{AppRole}}';
                    END IF;
                END
                $inner$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "quota_tier_prices");
        }
    }
}
