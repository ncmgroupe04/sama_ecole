using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Console Super Admin — écran Abonnements &amp; Facturation (vue plateforme, jusqu'ici démo) et
    /// bouton « Infiltrer » des Établissements. Même problème et même solution que
    /// AddPlatformAdminViews : le Super Admin n'a AUCUN SchoolId propre, la RLS de
    /// `subscriptions`/`subscription_payments`/`users` ne laisse donc passer aucune ligne pour sa
    /// session (AGENTS.md règle #2).
    ///
    ///   - `v_platform_subscriptions` (vue, security_invoker = false, OWNER sama_ecole) : une ligne par
    ///     école ayant un abonnement, avec son dernier paiement CONFIRMÉ le cas échéant — alimente
    ///     GET /admin/platform/subscriptions.
    ///   - `auth_find_active_director_by_school` (fonction SECURITY DEFINER, même famille que
    ///     auth_find_user_by_email/auth_find_user_by_id, migration AddAuthentication) : retrouve le
    ///     compte Directeur ACTIF d'une école cible, nécessaire à la fois pour émettre un jeton
    ///     d'impersonation (« Infiltrer ») et pour adresser un rappel de paiement (« Relancer »).
    ///     Ne renvoie RIEN pour un compte suspendu/bloqué/supprimé : ni l'un ni l'autre bouton ne doit
    ///     agir sur un compte qui ne serait plus valide.
    /// </summary>
    public partial class AddPlatformSubscriptionsAndImpersonation : Migration
    {
        private const string AppRole = "sama_ecole_app";
        private const string OwnerRole = "sama_ecole";
        private const string DirectorLookupFunctionSignature = "auth_find_active_director_by_school(uuid)";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ---------------------------------------------------------------- Abonnements (GET /admin/platform/subscriptions)
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

            AlterOwnerIfRoleExists(migrationBuilder, "VIEW", "public.v_platform_subscriptions");

            migrationBuilder.Sql($"""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{AppRole}') THEN
                        EXECUTE 'GRANT SELECT ON public.v_platform_subscriptions TO {AppRole}';
                    ELSE
                        RAISE WARNING 'Rôle % absent : l''écran Abonnements du Super Admin sera inaccessible.', '{AppRole}';
                    END IF;
                END
                $$;
                """);

            // ---------------------------------------------------------------- Directeur d'une école cible (Infiltrer / Relancer)
            migrationBuilder.Sql("""
                CREATE FUNCTION public.auth_find_active_director_by_school(p_school_id uuid)
                RETURNS TABLE (
                    "Id" uuid,
                    "SchoolId" uuid,
                    "Email" text,
                    "PasswordHash" text,
                    "FullName" text,
                    "Role" text,
                    "Status" text,
                    "AccessFailedCount" integer,
                    "LockoutEndAt" timestamptz
                )
                LANGUAGE sql
                SECURITY DEFINER
                SET search_path = public
                AS $$
                    SELECT u."Id", u."SchoolId", u."Email"::text, u."PasswordHash"::text, u."FullName"::text,
                           u."Role"::text, u."Status"::text, u."AccessFailedCount", u."LockoutEndAt"
                    FROM users u
                    WHERE u."SchoolId" = p_school_id
                      AND u."Role" = 'Directeur'
                      AND u."Status" = 'Active'
                      AND u."IsDeleted" = FALSE
                    ORDER BY u."CreatedAt"
                    LIMIT 1;
                $$;
                """);

            AlterOwnerIfRoleExists(migrationBuilder, "FUNCTION", $"public.{DirectorLookupFunctionSignature}");

            migrationBuilder.Sql($"""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{AppRole}') THEN
                        EXECUTE 'REVOKE ALL ON FUNCTION {DirectorLookupFunctionSignature} FROM PUBLIC';
                        EXECUTE 'GRANT EXECUTE ON FUNCTION {DirectorLookupFunctionSignature} TO {AppRole}';
                    ELSE
                        RAISE WARNING 'Rôle % absent : Infiltrer/Relancer seront inutilisables.', '{AppRole}';
                    END IF;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"DROP FUNCTION IF EXISTS {DirectorLookupFunctionSignature};");
            migrationBuilder.Sql("DROP VIEW IF EXISTS public.v_platform_subscriptions;");
        }

        private static void AlterOwnerIfRoleExists(MigrationBuilder migrationBuilder, string objectKind, string objectName)
        {
            migrationBuilder.Sql($"""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{OwnerRole}') THEN
                        EXECUTE 'ALTER {objectKind} {objectName} OWNER TO {OwnerRole}';
                    END IF;
                END
                $$;
                """);
        }
    }
}
