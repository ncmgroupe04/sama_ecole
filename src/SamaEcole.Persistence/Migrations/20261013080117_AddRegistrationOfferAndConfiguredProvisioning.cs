using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Offre par tranche d'effectif à l'inscription : colonnes de la demande (profil, internat, cycles cochés,
    /// tranche — NULL/false pour toute demande antérieure, qui garde la grille historique) et fonction de
    /// provisionnement d'une école NEUVE déjà configurée.
    ///
    /// MÊME PROBLÈME et MÊME SOLUTION que provision_tenant_subscription (migration AddTenantSubscriptionProvisioning) :
    /// `tenant_subscriptions` et `school_settings` sont sous RLS et un Super Admin n'a aucun schoolId de session.
    /// provision_configured_tenant_subscription, SECURITY DEFINER, `search_path` figé, accordée au seul rôle
    /// applicatif, crée la souscription directement <c>Active</c> avec les choix de l'inscription ET aligne la ligne
    /// de réglages que provision_school_director vient de créer (profil, quatre modules, cycles gérés) — dans la
    /// transaction de l'approbation, donc tout ou rien.
    ///
    /// GARDÉE : elle refuse (renvoie NULL) toute école qui possède déjà une souscription vivante, et n'écrit les
    /// réglages que s'ils n'ont encore AUCUN profil (<c>ProfileEtablissement IS NULL</c>, l'état d'une école neuve) :
    /// détournée avec un schoolId arbitraire, elle ne peut ni reconfigurer ni écraser une école en exploitation.
    /// </summary>
    public partial class AddRegistrationOfferAndConfiguredProvisioning : Migration
    {
        private const string AppRole = "sama_ecole_app";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Cycles",
                table: "school_registration_requests",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HasBoarding",
                table: "school_registration_requests",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Profile",
                table: "school_registration_requests",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Tier",
                table: "school_registration_requests",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.Sql("""
                CREATE FUNCTION provision_configured_tenant_subscription(
                    p_school_id uuid,
                    p_profile_type text,
                    p_establishment_profile text,
                    p_tier text,
                    p_max_students integer,
                    p_soft_limit integer,
                    p_pedagogy boolean,
                    p_finance boolean,
                    p_internat boolean,
                    p_coran boolean,
                    p_managed_cycles text)
                RETURNS uuid
                LANGUAGE plpgsql
                SECURITY DEFINER
                SET search_path = public
                AS $$
                DECLARE
                    v_id uuid;
                    v_settings_updated integer;
                BEGIN
                    INSERT INTO tenant_subscriptions (
                        "Id", "SchoolId", "ProfileType", "StudentQuotaTier", "MaxStudentLimit", "SoftQuotaLimit", "Status",
                        "IsPedagogyEnabled", "IsFinanceEnabled", "IsInternatEnabled", "IsCoranModuleEnabled",
                        "CreatedAt", "IsDeleted")
                    SELECT gen_random_uuid(), p_school_id, p_profile_type, p_tier, p_max_students, p_soft_limit, 'Active',
                           p_pedagogy, p_finance, p_internat, p_coran, NOW(), FALSE
                    -- LA garde : amorçage d'une souscription VIERGE uniquement.
                    WHERE NOT EXISTS (
                        SELECT 1 FROM tenant_subscriptions t WHERE t."SchoolId" = p_school_id AND NOT t."IsDeleted"
                    )
                    RETURNING "Id" INTO v_id;

                    IF v_id IS NULL THEN
                        RETURN NULL;
                    END IF;

                    -- Les réglages d'une école neuve n'ont encore aucun profil (provision_school_director les crée
                    -- avec NULL) : c'est cette condition qui interdit de reconfigurer une école en exploitation.
                    UPDATE school_settings
                       SET "ProfileEtablissement" = p_establishment_profile,
                           "IsPedagogyEnabled" = p_pedagogy,
                           "IsFinanceEnabled" = p_finance,
                           "IsInternatEnabled" = p_internat,
                           "IsCoranModuleEnabled" = p_coran,
                           "ManagedCycles" = p_managed_cycles,
                           "UpdatedAt" = NOW()
                     WHERE "SchoolId" = p_school_id AND NOT "IsDeleted" AND "ProfileEtablissement" IS NULL;

                    GET DIAGNOSTICS v_settings_updated = ROW_COUNT;

                    -- Souscription Active sans réglages alignés = sidebar en désaccord avec la souscription : on
                    -- annule plutôt que de laisser un état à moitié configuré (la transaction de l'approbation saute).
                    IF v_settings_updated = 0 THEN
                        RAISE EXCEPTION 'Réglages introuvables ou déjà configurés pour l''école %.', p_school_id;
                    END IF;

                    RETURN v_id;
                END
                $$;
                """);

            migrationBuilder.Sql($"""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{AppRole}') THEN
                        -- Contourner la RLS ne doit jamais être un droit par défaut : retiré à PUBLIC, accordé
                        -- nommément au seul rôle applicatif.
                        EXECUTE 'REVOKE ALL ON FUNCTION provision_configured_tenant_subscription(uuid, text, text, text, integer, integer, boolean, boolean, boolean, boolean, text) FROM PUBLIC';
                        EXECUTE 'GRANT EXECUTE ON FUNCTION provision_configured_tenant_subscription(uuid, text, text, text, integer, integer, boolean, boolean, boolean, boolean, text) TO {AppRole}';
                    ELSE
                        RAISE WARNING 'Rôle % absent : l''approbation d''une offre par tranche ne pourra pas provisionner l''école.', '{AppRole}';
                    END IF;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS provision_configured_tenant_subscription(uuid, text, text, text, integer, integer, boolean, boolean, boolean, boolean, text);");

            migrationBuilder.DropColumn(
                name: "Cycles",
                table: "school_registration_requests");

            migrationBuilder.DropColumn(
                name: "HasBoarding",
                table: "school_registration_requests");

            migrationBuilder.DropColumn(
                name: "Profile",
                table: "school_registration_requests");

            migrationBuilder.DropColumn(
                name: "Tier",
                table: "school_registration_requests");
        }
    }
}
