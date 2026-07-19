using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Console Super Admin — agrégats plateforme (dashboard) et journal d'audit toutes écoles
    /// confondues (activité). LE PROBLÈME, comme pour provision_school_director (AddSchoolProvisioning)
    /// et append_audit_log (AddAuditLogAppendFunction) : le Super Admin n'a AUCUN SchoolId propre, donc
    /// la RLS de `schools`/`users`/`subscriptions`/`subscription_payments`/`audit_logs` ne laisse passer
    /// AUCUNE ligne pour sa session, quelle que soit l'école visée (AGENTS.md règle #2).
    ///
    /// LA SOLUTION, ici en deux variantes du même principe (contourner via le rôle PROPRIÉTAIRE, jamais
    /// en affaiblissant les policies existantes) :
    ///   - une VUE `WITH (security_invoker = false)` (comportement par défaut de PostgreSQL, explicité
    ///     ici) : une vue s'exécute avec les droits de son PROPRIÉTAIRE, pas de l'appelant — tant que le
    ///     propriétaire est `sama_ecole` (exempté de RLS sur les tables qu'il possède), la vue voit tout,
    ///     quel que soit le rôle qui la SELECT (à condition d'avoir GRANT SELECT dessus).
    ///   - une FONCTION `SECURITY DEFINER`, même mécanisme, pour une requête paramétrée (pagination).
    ///
    /// `sama_ecole` (rôle propriétaire) n'existe PAS forcément sous ce nom dans tous les environnements
    /// (ex. Testcontainers en tests fonctionnels, où l'owner de migration porte un autre nom) : les
    /// ALTER ... OWNER TO sont donc gardés par une vérification d'existence, comme le fait déjà
    /// EnableRowLevelSecurity pour `sama_ecole_app`. En production, la migration tourne déjà AVEC ce
    /// rôle (DesignTimeDbContextFactory) : l'ALTER y est un no-op auto-référentiel, gardé pour la même
    /// robustesse et pour rester the source de vérité explicite demandée.
    /// </summary>
    public partial class AddPlatformAdminViews : Migration
    {
        private const string AppRole = "sama_ecole_app";
        private const string OwnerRole = "sama_ecole";
        private const string ActivityFunctionSignature = "get_global_audit_logs(int, int)";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ---------------------------------------------------------------- Dashboard (GET /admin/platform/dashboard)
            migrationBuilder.Sql("""
                CREATE VIEW public.v_platform_dashboard_stats
                WITH (security_invoker = false) AS
                SELECT
                    (SELECT COUNT(*)::integer FROM schools WHERE NOT "IsDeleted") AS "TotalSchools",
                    (SELECT COUNT(*)::integer FROM users WHERE NOT "IsDeleted") AS "TotalUsers",
                    -- Seuls les paiements CONFIRMÉS comptent comme revenu (AGENTS.md règle #11) :
                    -- Initiated/Failed ne sont jamais de l'argent réellement encaissé.
                    (SELECT COALESCE(SUM("Amount"), 0::numeric)
                     FROM subscription_payments
                     WHERE "Status" = 'Confirmed' AND NOT "IsDeleted") AS "TotalRevenue",
                    (SELECT COUNT(*)::integer FROM subscriptions
                     WHERE "Status" = 'Active' AND NOT "IsDeleted") AS "ActiveSubscriptions";
                """);

            AlterOwnerIfRoleExists(migrationBuilder, "VIEW", "public.v_platform_dashboard_stats");

            migrationBuilder.Sql($"""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{AppRole}') THEN
                        EXECUTE 'GRANT SELECT ON public.v_platform_dashboard_stats TO {AppRole}';
                    ELSE
                        RAISE WARNING 'Rôle % absent : le tableau de bord plateforme du Super Admin sera inaccessible.', '{AppRole}';
                    END IF;
                END
                $$;
                """);

            // ---------------------------------------------------------------- Activité (GET /admin/platform/activity)
            migrationBuilder.Sql("""
                CREATE FUNCTION public.get_global_audit_logs(limit_val int, offset_val int)
                RETURNS TABLE (
                    "Id" uuid,
                    "SchoolId" uuid,
                    "SchoolName" text,
                    "UserId" uuid,
                    "ActorFullName" text,
                    "Module" text,
                    "Action" text,
                    "Success" boolean,
                    "FailureReason" text,
                    "IpAddress" text,
                    "OccurredAt" timestamptz,
                    "TotalCount" integer)
                LANGUAGE sql
                SECURITY DEFINER
                SET search_path = public
                AS $$
                    -- COUNT(*) OVER() : total AVANT LIMIT/OFFSET, porté sur chaque ligne — évite un
                    -- second aller-retour dédié au total pour la pagination.
                    SELECT
                        a."Id", a."SchoolId", s."Name", a."UserId", u."FullName",
                        a."Module", a."Action", a."Success", a."FailureReason", a."IpAddress", a."OccurredAt",
                        COUNT(*) OVER()::integer AS "TotalCount"
                    FROM audit_logs a
                    JOIN schools s ON s."Id" = a."SchoolId"
                    JOIN users u ON u."Id" = a."UserId"
                    WHERE NOT a."IsDeleted"
                    ORDER BY a."OccurredAt" DESC
                    LIMIT limit_val OFFSET offset_val;
                $$;
                """);

            AlterOwnerIfRoleExists(migrationBuilder, "FUNCTION", $"public.{ActivityFunctionSignature}");

            migrationBuilder.Sql($"""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{AppRole}') THEN
                        -- Contourner la RLS ne doit jamais être un droit par défaut : on le retire à
                        -- PUBLIC avant de l'accorder nommément au seul rôle applicatif.
                        EXECUTE 'REVOKE ALL ON FUNCTION {ActivityFunctionSignature} FROM PUBLIC';
                        EXECUTE 'GRANT EXECUTE ON FUNCTION {ActivityFunctionSignature} TO {AppRole}';
                    ELSE
                        RAISE WARNING 'Rôle % absent : le journal d''activité plateforme du Super Admin sera inaccessible.', '{AppRole}';
                    END IF;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"DROP FUNCTION IF EXISTS {ActivityFunctionSignature};");
            migrationBuilder.Sql("DROP VIEW IF EXISTS public.v_platform_dashboard_stats;");
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
