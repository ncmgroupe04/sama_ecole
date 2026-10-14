using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Paiement d'abonnement par le compte marchand Wave de la plateforme (SubscriptionPaymentMethod.WaveMerchant).
    /// Un lien marchand statique n'émet aucun webhook : c'est le Super Admin qui rapproche la demande de son relevé
    /// Wave et la valide (écran Abonnements &amp; Facturation). Il n'a AUCUN SchoolId propre : `subscription_payments`
    /// et `users` sont sous RLS, une requête EF classique n'y verrait donc rien — même contournement que
    /// AddSubscriptionPaymentConfirmation (fonction SECURITY DEFINER, EXECUTE réservé au rôle applicatif).
    ///
    /// `list_pending_wave_payments` ne renvoie QUE les lignes Provider = 'WaveMerchant' et Status = 'Initiated' :
    /// la validation (ConfirmAsync) ne s'applique donc jamais à un paiement d'agrégateur, dont seul le webhook signé
    /// peut décider (AGENTS.md règle #11). Aucune colonne ni table nouvelle : le moyen de paiement est stocké en texte.
    /// </summary>
    public partial class AddPendingWavePaymentsFunction : Migration
    {
        private const string AppRole = "sama_ecole_app";
        private const string FunctionSignature = "list_pending_wave_payments()";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE FUNCTION list_pending_wave_payments()
                RETURNS TABLE (
                    payment_id uuid,
                    school_id uuid,
                    school_name text,
                    director_full_name text,
                    director_email text,
                    director_phone text,
                    amount numeric,
                    billing_period text,
                    reference text,
                    initiated_at timestamptz
                )
                LANGUAGE sql
                SECURITY DEFINER
                SET search_path = public
                AS $$
                    SELECT sp."Id", sp."SchoolId", s."Name"::text,
                           d."FullName"::text, d."Email"::text,
                           -- Le téléphone n'est saisi qu'à la demande d'inscription (users.Phone reste vide à l'approbation).
                           COALESCE(d."Phone", (
                               SELECT rr."DirectorPhone"
                               FROM school_registration_requests rr
                               WHERE rr."CreatedSchoolId" = sp."SchoolId" AND rr."IsDeleted" = FALSE
                               ORDER BY rr."CreatedAt"
                               LIMIT 1))::text,
                           sp."Amount", sp."BillingPeriod"::text, sp."ProviderTransactionRef"::text, sp."InitiatedAt"
                    FROM subscription_payments sp
                    JOIN schools s ON s."Id" = sp."SchoolId"
                    LEFT JOIN LATERAL (
                        SELECT u."FullName", u."Email", u."Phone"
                        FROM users u
                        WHERE u."SchoolId" = sp."SchoolId"
                          AND u."Role" = 'Directeur'
                          AND u."IsDeleted" = FALSE
                        ORDER BY u."CreatedAt"
                        LIMIT 1
                    ) d ON TRUE
                    WHERE sp."Provider" = 'WaveMerchant'
                      AND sp."Status" = 'Initiated'
                      AND sp."ProviderTransactionRef" IS NOT NULL
                      AND sp."IsDeleted" = FALSE
                    ORDER BY sp."InitiatedAt";
                $$;
                """);

            migrationBuilder.Sql($"""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{AppRole}') THEN
                        EXECUTE 'REVOKE ALL ON FUNCTION {FunctionSignature} FROM PUBLIC';
                        EXECUTE 'GRANT EXECUTE ON FUNCTION {FunctionSignature} TO {AppRole}';
                    ELSE
                        RAISE WARNING 'Rôle % absent : la validation des paiements Wave sera inaccessible.', '{AppRole}';
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
