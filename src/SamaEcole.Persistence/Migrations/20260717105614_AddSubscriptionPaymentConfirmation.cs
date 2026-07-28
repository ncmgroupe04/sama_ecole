using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Ticket JGK-I06 — traitement du webhook de paiement et activation de l'abonnement.
    ///
    /// L'acteur webhook est ANONYME (aucune session, donc aucun SchoolId) : `subscription_payments` et
    /// `subscriptions` sont toutes deux sous policy RLS, un SELECT ou un UPDATE EF classique n'y verrait
    /// donc AUCUNE ligne. Même contournement que pour l'inscription (JGK-B01/I03) : deux fonctions
    /// SECURITY DEFINER, chacune avec sa propre garde anti-abus.
    ///
    /// DEUX fonctions plutôt qu'une, pour une raison de CORRECTION, pas de style : entre la lecture
    /// (find_subscription_payment) et l'écriture (confirm_subscription_payment), le Handler appelle
    /// l'agrégateur de paiement — un aller-retour réseau potentiellement lent. Ouvrir une transaction et
    /// tenir un verrou de ligne PostgreSQL pendant cet appel externe serait dangereux (contention,
    /// épuisement du pool de connexions). La lecture est donc SANS verrou (juste une consultation), et
    /// c'est la clause `WHERE "Status" = 'Initiated'` de l'UPDATE, atomique par nature, qui garantit
    /// l'idempotence — pas un verrou détenu pendant l'appel réseau.
    ///
    /// confirm_subscription_payment fait TOUT dans une seule fonction (transition du paiement, calcul de
    /// la nouvelle date d'expiration, activation de l'abonnement, ET récupération des informations du
    /// Directeur pour l'e-mail de confirmation) : le Handler, anonyme, ne pourrait relire ni `users` ni
    /// `subscriptions` séparément après coup — tout ce dont il a besoin doit sortir d'ici en une fois.
    /// </summary>
    public partial class AddSubscriptionPaymentConfirmation : Migration
    {
        private const string AppRole = "sama_ecole_app";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE FUNCTION find_subscription_payment(p_internal_payment_id uuid)
                RETURNS TABLE (
                    provider_transaction_ref text,
                    amount numeric,
                    status text
                )
                LANGUAGE sql
                SECURITY DEFINER
                SET search_path = public
                AS $$
                    SELECT sp."ProviderTransactionRef"::text, sp."Amount", sp."Status"::text
                    FROM subscription_payments sp
                    WHERE sp."Id" = p_internal_payment_id
                      AND sp."IsDeleted" = FALSE;
                $$;
                """);

            migrationBuilder.Sql("""
                CREATE FUNCTION confirm_subscription_payment(
                    p_internal_payment_id uuid,
                    p_final_status text,
                    p_webhook_payload jsonb)
                RETURNS TABLE (
                    was_processed boolean,
                    school_id uuid,
                    school_name text,
                    director_user_id uuid,
                    director_email text,
                    director_full_name text,
                    new_expires_at date
                )
                LANGUAGE plpgsql
                SECURITY DEFINER
                SET search_path = public
                AS $$
                DECLARE
                    v_school_id uuid;
                    v_billing_period text;
                    v_current_expires_at date;
                    v_new_expires_at date;
                BEGIN
                    -- Garde d'idempotence : SEULE une ligne encore au statut Initiated peut transitionner.
                    -- Un rejeu du webhook (référence déjà Confirmed/Failed) ne touche RIEN — c'est cette
                    -- clause, pas un verrou tenu depuis la lecture précédente, qui protège contre le
                    -- double traitement (voir le commentaire de classe de cette migration).
                    UPDATE subscription_payments
                    SET "Status" = p_final_status,
                        "ConfirmedAt" = NOW(),
                        "WebhookPayloadRaw" = p_webhook_payload,
                        "UpdatedAt" = NOW()
                    WHERE "Id" = p_internal_payment_id
                      AND "Status" = 'Initiated'
                    RETURNING "SchoolId", "BillingPeriod"::text INTO v_school_id, v_billing_period;

                    IF v_school_id IS NULL THEN
                        RETURN QUERY SELECT FALSE, NULL::uuid, NULL::text, NULL::uuid, NULL::text, NULL::text, NULL::date;
                        RETURN;
                    END IF;

                    IF p_final_status = 'Confirmed' THEN
                        SELECT "ExpiresAt" INTO v_current_expires_at FROM subscriptions WHERE "SchoolId" = v_school_id;

                        -- Prolonge depuis la date la plus tardive entre aujourd'hui et l'expiration
                        -- actuelle : un renouvellement PAYÉ EN AVANCE ne doit jamais raccourcir la
                        -- période déjà acquise (Volume 1 §11.6, cycle de renouvellement).
                        v_new_expires_at := (GREATEST(COALESCE(v_current_expires_at, CURRENT_DATE), CURRENT_DATE)
                            + CASE WHEN v_billing_period = 'Yearly' THEN INTERVAL '1 year' ELSE INTERVAL '1 month' END)::date;

                        UPDATE subscriptions
                        SET "Status" = 'Active',
                            "ExpiresAt" = v_new_expires_at,
                            "UpdatedAt" = NOW()
                        WHERE "SchoolId" = v_school_id;
                    END IF;

                    -- Un seul Directeur par établissement dans ce parcours (créé par l'approbation,
                    -- JGK-I03) : LIMIT 1 documente cette hypothèse plutôt que de la laisser implicite.
                    RETURN QUERY
                        SELECT TRUE, s."Id", s."Name"::text, u."Id", u."Email"::text, u."FullName"::text, v_new_expires_at
                        FROM schools s
                        JOIN users u ON u."SchoolId" = s."Id" AND u."Role" = 'Directeur' AND u."IsDeleted" = FALSE
                        WHERE s."Id" = v_school_id AND s."IsDeleted" = FALSE
                        LIMIT 1;
                END;
                $$;
                """);

            migrationBuilder.Sql($"""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{AppRole}') THEN
                        EXECUTE 'REVOKE ALL ON FUNCTION find_subscription_payment(uuid) FROM PUBLIC';
                        EXECUTE 'REVOKE ALL ON FUNCTION confirm_subscription_payment(uuid, text, jsonb) FROM PUBLIC';

                        EXECUTE 'GRANT EXECUTE ON FUNCTION find_subscription_payment(uuid) TO {AppRole}';
                        EXECUTE 'GRANT EXECUTE ON FUNCTION confirm_subscription_payment(uuid, text, jsonb) TO {AppRole}';
                    ELSE
                        RAISE WARNING 'Rôle % absent : le traitement du webhook de paiement ne fonctionnera pas.', '{AppRole}';
                    END IF;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS confirm_subscription_payment(uuid, text, jsonb);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS find_subscription_payment(uuid);");
        }
    }
}
