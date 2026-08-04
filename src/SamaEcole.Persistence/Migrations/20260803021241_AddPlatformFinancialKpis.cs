using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformFinancialKpis : Migration
    {
        private const string AppRole = "sama_ecole_app";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW IF EXISTS public.v_platform_dashboard_stats;");
            
            migrationBuilder.Sql("""
                CREATE VIEW public.v_platform_dashboard_stats
                WITH (security_invoker = false) AS
                SELECT
                    (SELECT COUNT(*)::integer FROM schools WHERE NOT "IsDeleted") AS "TotalSchools",
                    (SELECT COUNT(*)::integer FROM users WHERE NOT "IsDeleted") AS "TotalUsers",
                    (SELECT COALESCE(SUM("Amount"), 0::numeric)
                     FROM subscription_payments
                     WHERE "Status" = 'Confirmed' AND NOT "IsDeleted") AS "TotalRevenue",
                    (SELECT COUNT(*)::integer FROM subscriptions
                     WHERE "Status" = 'Active' AND NOT "IsDeleted") AS "ActiveSubscriptions",
                    (SELECT COUNT(*)::integer FROM schools WHERE "Status" = 'Active' AND NOT "IsDeleted") AS "ActiveSchools",
                    -- MRR = somme, sur les écoles dont l'abonnement est ACTUELLEMENT Active, du montant de
                    -- leur dernier paiement confirmé, ramené à un équivalent mensuel (Yearly / 12). Le
                    -- filtre sur sub."Status" = 'Active' est déterminant : sans lui, une école dont
                    -- l'abonnement a expiré ou a été résilié après son dernier paiement resterait comptée
                    -- indéfiniment (le paiement, lui, reste confirmé pour toujours).
                    (SELECT COALESCE(SUM(
                        CASE WHEN lastpay."BillingPeriod" = 'Yearly' THEN lastpay."Amount" / 12 ELSE lastpay."Amount" END
                    ), 0::numeric)
                     FROM subscriptions sub
                     JOIN LATERAL (
                         SELECT sp."Amount", sp."BillingPeriod"
                         FROM subscription_payments sp
                         WHERE sp."SchoolId" = sub."SchoolId" AND sp."Status" = 'Confirmed' AND NOT sp."IsDeleted"
                         ORDER BY sp."ConfirmedAt" DESC NULLS LAST
                         LIMIT 1
                     ) lastpay ON TRUE
                     WHERE sub."Status" = 'Active' AND NOT sub."IsDeleted") AS "MRR",
                    -- Cashflow prévisionnel 30j = montant du dernier paiement confirmé des écoles dont
                    -- l'abonnement Active ÉCHOIT dans les 30 prochains jours (ExpiresAt, PAS ConfirmedAt :
                    -- c'est un montant À VENIR, pas un historique des encaissements passés).
                    (SELECT COALESCE(SUM(lastpay."Amount"), 0::numeric)
                     FROM subscriptions sub
                     JOIN LATERAL (
                         SELECT sp."Amount"
                         FROM subscription_payments sp
                         WHERE sp."SchoolId" = sub."SchoolId" AND sp."Status" = 'Confirmed' AND NOT sp."IsDeleted"
                         ORDER BY sp."ConfirmedAt" DESC NULLS LAST
                         LIMIT 1
                     ) lastpay ON TRUE
                     WHERE sub."Status" = 'Active' AND NOT sub."IsDeleted"
                       AND sub."ExpiresAt" IS NOT NULL
                       AND sub."ExpiresAt" BETWEEN CURRENT_DATE AND CURRENT_DATE + INTERVAL '30 days'
                    ) AS "ForecastedRevenue30Days";
                """);

            migrationBuilder.Sql($"""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'sama_ecole') THEN
                        EXECUTE 'ALTER VIEW public.v_platform_dashboard_stats OWNER TO sama_ecole';
                    END IF;
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{AppRole}') THEN
                        EXECUTE 'GRANT SELECT ON public.v_platform_dashboard_stats TO {AppRole}';
                    END IF;
                END
                $$;
                """);

            migrationBuilder.Sql("DROP VIEW IF EXISTS public.v_platform_subscriptions;");
            
            migrationBuilder.Sql("""
                CREATE VIEW public.v_platform_subscriptions
                WITH (security_invoker = false) AS
                SELECT
                    s."Id" AS "SchoolId",
                    s."Name" AS "SchoolName",
                    sub."Plan"::text AS "Plan",
                    sub."Status"::text AS "Status",
                    sub."ExpiresAt" AS "ExpiresAt",
                    lastpay."Amount" AS "LastPaymentAmountXof",
                    lastpay."ConfirmedAt" AS "LastPaymentAt",
                    lastpay."BillingPeriod"::text AS "LastPaymentBillingPeriod"
                FROM schools s
                JOIN subscriptions sub ON sub."SchoolId" = s."Id" AND NOT sub."IsDeleted"
                LEFT JOIN LATERAL (
                    SELECT sp."Amount", sp."ConfirmedAt", sp."BillingPeriod"
                    FROM subscription_payments sp
                    WHERE sp."SchoolId" = s."Id"
                      AND sp."Status" = 'Confirmed'
                      AND NOT sp."IsDeleted"
                    ORDER BY sp."ConfirmedAt" DESC NULLS LAST
                    LIMIT 1
                ) lastpay ON TRUE
                WHERE NOT s."IsDeleted";
                """);

            migrationBuilder.Sql($"""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'sama_ecole') THEN
                        EXECUTE 'ALTER VIEW public.v_platform_subscriptions OWNER TO sama_ecole';
                    END IF;
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{AppRole}') THEN
                        EXECUTE 'GRANT SELECT ON public.v_platform_subscriptions TO {AppRole}';
                    END IF;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restore previous version of v_platform_dashboard_stats
            migrationBuilder.Sql("DROP VIEW IF EXISTS public.v_platform_dashboard_stats;");
            
            migrationBuilder.Sql("""
                CREATE VIEW public.v_platform_dashboard_stats
                WITH (security_invoker = false) AS
                SELECT
                    (SELECT COUNT(*)::integer FROM schools WHERE NOT "IsDeleted") AS "TotalSchools",
                    (SELECT COUNT(*)::integer FROM users WHERE NOT "IsDeleted") AS "TotalUsers",
                    (SELECT COALESCE(SUM("Amount"), 0::numeric)
                     FROM subscription_payments
                     WHERE "Status" = 'Confirmed' AND NOT "IsDeleted") AS "TotalRevenue",
                    (SELECT COUNT(*)::integer FROM subscriptions
                     WHERE "Status" = 'Active' AND NOT "IsDeleted") AS "ActiveSubscriptions";
                """);

            migrationBuilder.Sql($"""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'sama_ecole') THEN
                        EXECUTE 'ALTER VIEW public.v_platform_dashboard_stats OWNER TO sama_ecole';
                    END IF;
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{AppRole}') THEN
                        EXECUTE 'GRANT SELECT ON public.v_platform_dashboard_stats TO {AppRole}';
                    END IF;
                END
                $$;
                """);

            // Restore previous version of v_platform_subscriptions
            migrationBuilder.Sql("DROP VIEW IF EXISTS public.v_platform_subscriptions;");

            migrationBuilder.Sql("""
                CREATE VIEW public.v_platform_subscriptions
                WITH (security_invoker = false) AS
                SELECT
                    s."Id" AS "SchoolId",
                    s."Name" AS "SchoolName",
                    sub."Plan"::text AS "Plan",
                    sub."Status"::text AS "Status",
                    sub."ExpiresAt" AS "ExpiresAt",
                    lastpay."Amount" AS "LastPaymentAmountXof",
                    lastpay."ConfirmedAt" AS "LastPaymentAt"
                FROM schools s
                JOIN subscriptions sub ON sub."SchoolId" = s."Id" AND NOT sub."IsDeleted"
                LEFT JOIN LATERAL (
                    SELECT sp."Amount", sp."ConfirmedAt"
                    FROM subscription_payments sp
                    WHERE sp."SchoolId" = s."Id"
                      AND sp."Status" = 'Confirmed'
                      AND NOT sp."IsDeleted"
                    ORDER BY sp."ConfirmedAt" DESC NULLS LAST
                    LIMIT 1
                ) lastpay ON TRUE
                WHERE NOT s."IsDeleted";
                """);

            migrationBuilder.Sql($"""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'sama_ecole') THEN
                        EXECUTE 'ALTER VIEW public.v_platform_subscriptions OWNER TO sama_ecole';
                    END IF;
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{AppRole}') THEN
                        EXECUTE 'GRANT SELECT ON public.v_platform_subscriptions TO {AppRole}';
                    END IF;
                END
                $$;
                """);
        }
    }
}
