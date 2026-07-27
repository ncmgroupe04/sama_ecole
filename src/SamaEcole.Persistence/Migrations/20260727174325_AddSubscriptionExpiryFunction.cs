using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Ticket JGK-B03 — passage automatique en lecture seule à expiration. `subscriptions` est sous
    /// RLS (migration AddSubscriptionProvisioning) et SubscriptionLifecycleHostedService, qui tourne
    /// hors requête HTTP, ne porte aucun tenant : basculer TOUS les abonnements Actifs expirés, toutes
    /// écoles confondues, exige donc la même fonction SECURITY DEFINER que
    /// grant_complimentary_subscription (migration AddPromoCodesAndComplimentaryAccess), copiée pour
    /// UPDATE en masse plutôt qu'une seule ligne ciblée.
    /// </summary>
    public partial class AddSubscriptionExpiryFunction : Migration
    {
        private const string AppRole = "sama_ecole_app";
        private const string OwnerRole = "sama_ecole";
        private const string FunctionSignature = "expire_overdue_subscriptions(date)";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE FUNCTION public.expire_overdue_subscriptions(p_as_of date)
                RETURNS TABLE ("SchoolId" uuid)
                LANGUAGE sql
                SECURITY DEFINER
                SET search_path = public
                AS $$
                    UPDATE subscriptions
                    SET "Status" = 'ReadOnly',
                        "UpdatedAt" = NOW()
                    WHERE "Status" = 'Active'
                      AND "ExpiresAt" IS NOT NULL
                      AND "ExpiresAt" < p_as_of
                      AND "IsDeleted" = FALSE
                    RETURNING "SchoolId";
                $$;
                """);

            migrationBuilder.Sql($"""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{OwnerRole}') THEN
                        EXECUTE 'ALTER FUNCTION public.{FunctionSignature} OWNER TO {OwnerRole}';
                    END IF;
                END
                $$;
                """);

            migrationBuilder.Sql($"""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{AppRole}') THEN
                        EXECUTE 'REVOKE ALL ON FUNCTION {FunctionSignature} FROM PUBLIC';
                        EXECUTE 'GRANT EXECUTE ON FUNCTION {FunctionSignature} TO {AppRole}';
                    ELSE
                        RAISE WARNING 'Rôle % absent : le passage automatique en lecture seule ne fonctionnera pas.', '{AppRole}';
                    END IF;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"DROP FUNCTION IF EXISTS {FunctionSignature};");
        }
    }
}
