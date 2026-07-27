using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Module Tarification, Réductions &amp; Offres Promotionnelles (Super Admin). `promo_codes` est
    /// une table PLATEFORME (comme `schools`/`subscriptions`) : aucun `SchoolId`, donc aucune policy
    /// RLS — un CRUD EF classique du Super Admin y suffit (voir CreateSchoolCommandHandler pour le
    /// même raisonnement sur `schools`).
    ///
    /// `subscriptions`, en revanche, EST sous RLS avec policy `WITH CHECK` sur `SchoolId` (migration
    /// AddSubscriptionProvisioning) : un Super Admin, sans SchoolId, ne peut pas UPDATE une ligne
    /// existante par un simple EF SaveChanges — même mur que pour l'INSERT initial. D'où
    /// grant_complimentary_subscription, copie du gabarit provision_subscription mais pour MODIFIER
    /// un abonnement déjà provisionné (attribution manuelle d'un accès offert).
    /// </summary>
    public partial class AddPromoCodesAndComplimentaryAccess : Migration
    {
        private const string AppRole = "sama_ecole_app";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PromoCodeId",
                table: "subscriptions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "PromoDiscountEndsAt",
                table: "subscriptions",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "promo_codes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DiscountType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DiscountValue = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    DurationMonths = table.Column<int>(type: "integer", nullable: true),
                    MaxUses = table.Column<int>(type: "integer", nullable: true),
                    CurrentUses = table.Column<int>(type: "integer", nullable: false),
                    StartDateUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndDateUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_promo_codes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_subscriptions_PromoCodeId",
                table: "subscriptions",
                column: "PromoCodeId");

            migrationBuilder.CreateIndex(
                name: "IX_promo_codes_Code",
                table: "promo_codes",
                column: "Code",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_subscriptions_promo_codes_PromoCodeId",
                table: "subscriptions",
                column: "PromoCodeId",
                principalTable: "promo_codes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
                CREATE FUNCTION grant_complimentary_subscription(
                    p_school_id uuid,
                    p_plan text,
                    p_status text,
                    p_expires_at date,
                    p_promo_code_id uuid)
                RETURNS uuid
                LANGUAGE sql
                SECURITY DEFINER
                SET search_path = public
                AS $$
                    UPDATE subscriptions
                    SET "Plan" = p_plan,
                        "Status" = p_status,
                        "ExpiresAt" = p_expires_at,
                        "PromoCodeId" = p_promo_code_id,
                        "PromoDiscountEndsAt" = p_expires_at,
                        "UpdatedAt" = NOW()
                    -- Cible une ligne EXISTANTE uniquement : cette fonction n'amorce jamais un
                    -- abonnement (voir provision_subscription pour l'amorçage), elle ne fait que
                    -- MODIFIER celui d'une école déjà provisionnée.
                    WHERE "SchoolId" = p_school_id AND "IsDeleted" = FALSE
                    RETURNING "Id";
                $$;
                """);

            migrationBuilder.Sql($"""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{AppRole}') THEN
                        EXECUTE 'REVOKE ALL ON FUNCTION grant_complimentary_subscription(uuid, text, text, date, uuid) FROM PUBLIC';
                        EXECUTE 'GRANT EXECUTE ON FUNCTION grant_complimentary_subscription(uuid, text, text, date, uuid) TO {AppRole}';
                    ELSE
                        RAISE WARNING 'Rôle % absent : l''attribution d''un accès offert ne fonctionnera pas.', '{AppRole}';
                    END IF;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS grant_complimentary_subscription(uuid, text, text, date, uuid);");

            migrationBuilder.DropForeignKey(
                name: "FK_subscriptions_promo_codes_PromoCodeId",
                table: "subscriptions");

            migrationBuilder.DropTable(
                name: "promo_codes");

            migrationBuilder.DropIndex(
                name: "IX_subscriptions_PromoCodeId",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "PromoCodeId",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "PromoDiscountEndsAt",
                table: "subscriptions");
        }
    }
}
