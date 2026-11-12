using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Validation d'un paiement Wave marchand : le Super Admin saisit l'identifiant de la transaction retrouvée sur son
    /// relevé. Un lien marchand statique ne porte aucune référence — sans cet identifiant, un seul règlement pouvait
    /// servir à activer plusieurs écoles. L'index unique partiel en fait une pièce à usage unique.
    ///
    /// `confirm_wave_subscription_payment` pose l'identifiant PUIS délègue à `confirm_subscription_payment` (même
    /// transaction : si l'index unique refuse l'identifiant, rien n'est confirmé ni prolongé). Elle n'agit que sur
    /// Provider = 'WaveMerchant' et Status = 'Initiated' : un paiement d'agrégateur ne passe jamais par ici (règle #11).
    /// </summary>
    public partial class AddWaveTransactionReference : Migration
    {
        private const string AppRole = "sama_ecole_app";
        private const string FunctionSignature = "confirm_wave_subscription_payment(uuid, text, jsonb)";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExternalTransactionRef",
                table: "subscription_payments",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_subscription_payments_external_transaction_ref",
                table: "subscription_payments",
                column: "ExternalTransactionRef",
                unique: true,
                filter: "\"ExternalTransactionRef\" IS NOT NULL");

            migrationBuilder.Sql("""
                CREATE FUNCTION confirm_wave_subscription_payment(
                    p_internal_payment_id uuid,
                    p_transaction_ref text,
                    p_webhook_payload jsonb
                )
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
                BEGIN
                    -- Garde : seule une demande Wave encore en attente reçoit un identifiant. Une violation de l'index
                    -- unique (identifiant déjà utilisé) lève 23505 et annule toute la fonction — rien n'est confirmé.
                    UPDATE subscription_payments
                    SET "ExternalTransactionRef" = p_transaction_ref,
                        "UpdatedAt" = NOW()
                    WHERE "Id" = p_internal_payment_id
                      AND "Provider" = 'WaveMerchant'
                      AND "Status" = 'Initiated';

                    IF NOT FOUND THEN
                        RETURN QUERY SELECT FALSE, NULL::uuid, NULL::text, NULL::uuid, NULL::text, NULL::text, NULL::date;
                        RETURN;
                    END IF;

                    RETURN QUERY SELECT * FROM confirm_subscription_payment(p_internal_payment_id, 'Confirmed', p_webhook_payload);
                END;
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

            migrationBuilder.DropIndex(
                name: "UX_subscription_payments_external_transaction_ref",
                table: "subscription_payments");

            migrationBuilder.DropColumn(
                name: "ExternalTransactionRef",
                table: "subscription_payments");
        }
    }
}
