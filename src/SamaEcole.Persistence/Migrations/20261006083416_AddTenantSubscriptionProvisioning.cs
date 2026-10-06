using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Provisionnement de la souscription commerciale d'une NOUVELLE école (<c>tenant_subscriptions</c>).
    ///
    /// MÊME PROBLÈME et MÊME SOLUTION que provision_subscription (migration AddSubscriptionProvisioning) :
    /// la table est sous policy RLS et un Super Admin n'a aucun schoolId de session, donc son INSERT serait
    /// rejeté. Une fonction SECURITY DEFINER, `search_path` figé sur public, accordée au seul rôle
    /// applicatif, et GARDÉE : elle refuse d'agir sur une école qui possède déjà une souscription vivante —
    /// détournée avec un schoolId arbitraire, elle ne peut ni écraser ni dupliquer celle d'autrui.
    ///
    /// La ligne créée est en <c>PendingOnboarding</c> : modules désactivés, et profil/tranche PROVISOIRES
    /// (EnseignementGeneral / Tier1_150) — les colonnes sont NOT NULL et les CHECK exigent un plafond
    /// positif. Ces valeurs ne servent à rien tant que l'école est en Onboarding (rien n'est accessible, et
    /// aucun élève ne peut être créé) ; SelectProfileCommand les remplace par le choix réel du Directeur.
    ///
    /// Aucun changement de schéma : cette migration ne pose QUE la fonction et son GRANT.
    /// </summary>
    public partial class AddTenantSubscriptionProvisioning : Migration
    {
        private const string AppRole = "sama_ecole_app";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE FUNCTION provision_tenant_subscription(p_school_id uuid)
                RETURNS uuid
                LANGUAGE sql
                SECURITY DEFINER
                SET search_path = public
                AS $$
                    INSERT INTO tenant_subscriptions (
                        "Id", "SchoolId", "ProfileType", "StudentQuotaTier", "MaxStudentLimit", "SoftQuotaLimit", "Status",
                        "IsPedagogyEnabled", "IsFinanceEnabled", "IsInternatEnabled", "IsCoranModuleEnabled",
                        "CreatedAt", "IsDeleted")
                    SELECT gen_random_uuid(), p_school_id, 'EnseignementGeneral', 'Tier1_150', 150, 160, 'PendingOnboarding',
                           FALSE, FALSE, FALSE, FALSE, NOW(), FALSE
                    -- LA garde : amorçage d'une souscription VIERGE uniquement.
                    WHERE NOT EXISTS (
                        SELECT 1 FROM tenant_subscriptions t WHERE t."SchoolId" = p_school_id AND NOT t."IsDeleted"
                    )
                    RETURNING "Id";
                $$;
                """);

            migrationBuilder.Sql($"""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{AppRole}') THEN
                        -- Contourner la RLS ne doit jamais être un droit par défaut : retiré à PUBLIC, accordé
                        -- nommément au seul rôle applicatif.
                        EXECUTE 'REVOKE ALL ON FUNCTION provision_tenant_subscription(uuid) FROM PUBLIC';
                        EXECUTE 'GRANT EXECUTE ON FUNCTION provision_tenant_subscription(uuid) TO {AppRole}';
                    ELSE
                        RAISE WARNING 'Rôle % absent : la création d''une école ne pourra pas provisionner sa souscription.', '{AppRole}';
                    END IF;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS provision_tenant_subscription(uuid);");
        }
    }
}
