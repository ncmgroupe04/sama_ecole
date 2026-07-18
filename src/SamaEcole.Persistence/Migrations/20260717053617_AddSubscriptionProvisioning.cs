using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Ticket JGK-I03 — création de l'abonnement initial à l'approbation d'une demande self-service.
    ///
    /// MÊME PROBLÈME que provision_school_director (migration AddSchoolProvisioning) : `subscriptions`
    /// est sous policy RLS (migration EnableRowLevelSecurity), et un Super Admin n'a AUCUN schoolId — sa
    /// session ne satisfait le WITH CHECK d'aucune ligne, son INSERT est rejeté. Sans issue, il ne peut
    /// pas doter d'un abonnement l'école qu'il vient d'approuver.
    ///
    /// MÊME SOLUTION, et même garde : une fonction SECURITY DEFINER qui REFUSE d'agir si l'établissement
    /// possède déjà un abonnement. Elle ne sait donc qu'amorcer un abonnement vierge — même détournée avec
    /// un schoolId arbitraire, elle ne peut ni écraser ni dupliquer l'abonnement d'autrui.
    ///
    /// `search_path` figé sur public : sinon un rôle capable de créer un schéma pourrait y glisser une
    /// fausse table `subscriptions` et détourner l'exécution, qui tourne avec les droits du PROPRIÉTAIRE.
    ///
    /// Aucun changement de schéma ici (la valeur d'enum AwaitingPayment est stockée en texte, sans
    /// contrainte CHECK) : cette migration ne pose QUE la fonction et son GRANT.
    /// </summary>
    public partial class AddSubscriptionProvisioning : Migration
    {
        private const string AppRole = "sama_ecole_app";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE FUNCTION provision_subscription(
                    p_school_id uuid,
                    p_plan text,
                    p_status text)
                RETURNS uuid
                LANGUAGE sql
                SECURITY DEFINER
                SET search_path = public
                AS $$
                    INSERT INTO subscriptions ("Id", "SchoolId", "Plan", "ExpiresAt", "Status",
                                               "CreatedAt", "IsDeleted")
                    -- ExpiresAt reste NULL : aucune date d'expiration tant que le premier paiement n'est
                    -- pas confirmé (docs/Volume_1_Cahier_des_Charges.md §11.5, ticket JGK-I06).
                    SELECT gen_random_uuid(), p_school_id, p_plan, NULL, p_status, NOW(), FALSE
                    -- LA garde : amorçage d'un abonnement VIERGE uniquement. Un établissement qui en a
                    -- déjà un n'est plus à provisionner, et cette porte doit lui rester fermée.
                    WHERE NOT EXISTS (
                        SELECT 1 FROM subscriptions s WHERE s."SchoolId" = p_school_id
                    )
                    RETURNING "Id";
                $$;
                """);

            migrationBuilder.Sql($"""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{AppRole}') THEN
                        -- Contourner la RLS ne doit jamais être un droit par défaut : on le retire à
                        -- PUBLIC avant de l'accorder nommément au seul rôle applicatif.
                        EXECUTE 'REVOKE ALL ON FUNCTION provision_subscription(uuid, text, text) FROM PUBLIC';
                        EXECUTE 'GRANT EXECUTE ON FUNCTION provision_subscription(uuid, text, text) TO {AppRole}';
                    ELSE
                        RAISE WARNING 'Rôle % absent : l''approbation d''une demande d''inscription ne fonctionnera pas.', '{AppRole}';
                    END IF;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS provision_subscription(uuid, text, text);");
        }
    }
}
