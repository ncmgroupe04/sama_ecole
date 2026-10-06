using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Lecture et modification d'une souscription commerciale (<c>tenant_subscriptions</c>) PAR LE SUPER ADMIN.
    ///
    /// MÊME PROBLÈME que provision_tenant_subscription (migration AddTenantSubscriptionProvisioning) : la table
    /// est sous policy RLS et un Super Admin n'a aucun schoolId de session — ni son SELECT ni son UPDATE EF ne
    /// verraient la ligne. MÊME SOLUTION : deux fonctions SECURITY DEFINER, `search_path` figé sur public,
    /// `EXECUTE` retiré à PUBLIC et accordé au seul rôle applicatif.
    ///
    /// <c>get_tenant_subscription</c> lit UNE école. <c>update_tenant_subscription</c> ne touche qu'à la
    /// tranche, aux plafonds et au statut — jamais au profil ni aux modules, qui restent le choix de l'école —
    /// et ne modifie que la ligne VIVANTE de l'école visée (une école sans ligne renvoie un ensemble vide).
    /// Chaque paramètre NULL signifie « inchangé ». Les règles métier (transitions de statut, cohérence
    /// tranche/plafond) vivent dans UpdateSubscriptionTierCommandHandler ; les CHECK de la table restent le
    /// filet si une valeur incohérente passait.
    ///
    /// Aucun changement de schéma : cette migration ne pose QUE les fonctions et leurs GRANT.
    /// </summary>
    public partial class AddTenantSubscriptionAdminFunctions : Migration
    {
        private const string AppRole = "sama_ecole_app";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE FUNCTION get_tenant_subscription(p_school_id uuid)
                RETURNS SETOF tenant_subscriptions
                LANGUAGE sql
                STABLE
                SECURITY DEFINER
                SET search_path = public
                AS $$
                    SELECT * FROM tenant_subscriptions t
                    WHERE t."SchoolId" = p_school_id AND NOT t."IsDeleted";
                $$;
                """);

            migrationBuilder.Sql("""
                CREATE FUNCTION update_tenant_subscription(
                    p_school_id uuid,
                    p_tier text,
                    p_max_students integer,
                    p_soft_limit integer,
                    p_status text,
                    p_updated_by text)
                RETURNS SETOF tenant_subscriptions
                LANGUAGE sql
                SECURITY DEFINER
                SET search_path = public
                AS $$
                    UPDATE tenant_subscriptions t
                       SET "StudentQuotaTier" = COALESCE(p_tier, t."StudentQuotaTier"),
                           "MaxStudentLimit"  = COALESCE(p_max_students, t."MaxStudentLimit"),
                           "SoftQuotaLimit"   = COALESCE(p_soft_limit, t."SoftQuotaLimit"),
                           "Status"           = COALESCE(p_status, t."Status"),
                           "UpdatedAt"        = NOW(),
                           "UpdatedBy"        = p_updated_by
                     WHERE t."SchoolId" = p_school_id AND NOT t."IsDeleted"
                    RETURNING t.*;
                $$;
                """);

            migrationBuilder.Sql($"""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{AppRole}') THEN
                        EXECUTE 'REVOKE ALL ON FUNCTION get_tenant_subscription(uuid) FROM PUBLIC';
                        EXECUTE 'GRANT EXECUTE ON FUNCTION get_tenant_subscription(uuid) TO {AppRole}';
                        EXECUTE 'REVOKE ALL ON FUNCTION update_tenant_subscription(uuid, text, integer, integer, text, text) FROM PUBLIC';
                        EXECUTE 'GRANT EXECUTE ON FUNCTION update_tenant_subscription(uuid, text, integer, integer, text, text) TO {AppRole}';
                    ELSE
                        RAISE WARNING 'Rôle % absent : le Super Admin ne pourra pas gérer les souscriptions commerciales.', '{AppRole}';
                    END IF;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS update_tenant_subscription(uuid, text, integer, integer, text, text);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS get_tenant_subscription(uuid);");
        }
    }
}
