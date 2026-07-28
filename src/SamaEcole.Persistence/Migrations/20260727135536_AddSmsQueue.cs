using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Transforme l'historique des SMS en FILE D'ATTENTE : le déclencheur métier inscrit le message,
    /// SmsQueueProcessor le remet au fournisseur hors requête, l'accusé de réception (DLR) le conclut.
    ///
    /// Aucune reprise de données : avant cette migration aucune ligne n'a le statut « Pending », les
    /// envois historiques restent donc tels quels (DispatchedAt nul pour eux, ce qui est exact — ils
    /// n'ont jamais transité par une file).
    ///
    /// Les trois fonctions ci-dessous sont en SECURITY DEFINER pour une raison précise : le worker et
    /// le webhook DLR s'exécutent SANS TENANT (ni JWT, ni app.current_school_id), et les policies RLS
    /// de `sms_messages` et `school_settings` leur masqueraient toutes les lignes. L'alternative —
    /// faire tourner l'application sous le rôle propriétaire — désactiverait la RLS de la base
    /// entière en silence (AGENTS.md règle #2). On préfère donc trois fonctions au périmètre étroit,
    /// exécutables par le seul rôle applicatif, comme grant_complimentary_subscription.
    /// </summary>
    public partial class AddSmsQueue : Migration
    {
        private const string AppRole = "sama_ecole_app";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AttemptCount",
                table: "sms_messages",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeliveredAt",
                table: "sms_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DispatchedAt",
                table: "sms_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextAttemptAt",
                table: "sms_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_sms_messages_ProviderMessageId",
                table: "sms_messages",
                column: "ProviderMessageId",
                filter: "\"ProviderMessageId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_sms_messages_queue",
                table: "sms_messages",
                column: "NextAttemptAt",
                filter: "\"Status\" = 'Pending'");

            // ── Réclamation d'un lot ────────────────────────────────────────────────────────────
            // FOR UPDATE SKIP LOCKED : deux instances de l'application (ou deux tours qui se
            // chevauchent) se partagent la file sans jamais se voir attribuer la même ligne. Sans
            // lui, une montée en charge à deux répliques enverrait chaque alerte EN DOUBLE au parent.
            //
            // Le bail (NextAttemptAt repoussé) prend le relais du verrou dès la transaction terminée :
            // le message part chez le fournisseur hors transaction, et rien ne doit le rendre à
            // nouveau éligible tant que cet appel n'a pas rendu son verdict — ou expiré.
            migrationBuilder.Sql("""
                CREATE FUNCTION claim_pending_sms(p_batch_size integer, p_lease_seconds integer)
                RETURNS TABLE (
                    id uuid,
                    school_id uuid,
                    recipient text,
                    body text,
                    segment_count integer,
                    attempt_count integer)
                LANGUAGE sql
                SECURITY DEFINER
                SET search_path = public
                AS $fn$
                    UPDATE sms_messages
                    SET "AttemptCount" = sms_messages."AttemptCount" + 1,
                        "NextAttemptAt" = NOW() + make_interval(secs => p_lease_seconds),
                        "UpdatedAt" = NOW()
                    WHERE "Id" IN (
                        SELECT "Id"
                        FROM sms_messages
                        WHERE "Status" = 'Pending'
                          AND "IsDeleted" = FALSE
                          AND "NextAttemptAt" <= NOW()
                        ORDER BY "NextAttemptAt"
                        LIMIT p_batch_size
                        FOR UPDATE SKIP LOCKED
                    )
                    RETURNING "Id", "SchoolId", "Recipient"::text, "Body"::text,
                              "SegmentCount", "AttemptCount";
                $fn$;
                """);

            // ── Issue d'une tentative ───────────────────────────────────────────────────────────
            // L'arbitrage « on retente / on abandonne » vit ICI et non dans le worker : l'abandon
            // s'accompagne du RECRÉDITAGE du solde, et les deux doivent tomber dans la même
            // transaction. Réparties entre C# et SQL, une panne au mauvais moment laisserait l'école
            // débitée d'un SMS jamais parti.
            migrationBuilder.Sql("""
                CREATE FUNCTION settle_sms_attempt(
                    p_id uuid,
                    p_is_sent boolean,
                    p_provider_message_id text,
                    p_failure_reason text,
                    p_max_attempts integer)
                RETURNS void
                LANGUAGE plpgsql
                SECURITY DEFINER
                SET search_path = public
                AS $fn$
                DECLARE
                    v_school_id uuid;
                    v_segments integer;
                    v_attempts integer;
                BEGIN
                    SELECT "SchoolId", "SegmentCount", "AttemptCount"
                    INTO v_school_id, v_segments, v_attempts
                    FROM sms_messages
                    WHERE "Id" = p_id;

                    IF NOT FOUND THEN
                        RETURN;
                    END IF;

                    IF p_is_sent THEN
                        UPDATE sms_messages
                        SET "Status" = 'Sent',
                            "DispatchedAt" = NOW(),
                            "ProviderMessageId" = p_provider_message_id,
                            "FailureReason" = NULL,
                            "NextAttemptAt" = NULL,
                            "UpdatedAt" = NOW()
                        WHERE "Id" = p_id;
                        RETURN;
                    END IF;

                    IF v_attempts >= p_max_attempts THEN
                        UPDATE sms_messages
                        SET "Status" = 'Failed',
                            "FailureReason" = p_failure_reason,
                            "NextAttemptAt" = NULL,
                            "UpdatedAt" = NOW()
                        WHERE "Id" = p_id;

                        -- Le solde a été débité à la mise en file : ce qui ne partira jamais est rendu.
                        UPDATE school_settings
                        SET "SmsCreditBalance" = "SmsCreditBalance" + v_segments,
                            "UpdatedAt" = NOW()
                        WHERE "SchoolId" = v_school_id;
                        RETURN;
                    END IF;

                    -- Report exponentiel (30s, 1min, 2min…) PLAFONNÉ à une heure : une panne longue
                    -- de l'agrégateur ne doit ni marteler son API, ni repousser l'alerte au lendemain.
                    UPDATE sms_messages
                    SET "FailureReason" = p_failure_reason,
                        "NextAttemptAt" =
                            NOW() + make_interval(secs => LEAST(POWER(2, v_attempts)::integer * 30, 3600)),
                        "UpdatedAt" = NOW()
                    WHERE "Id" = p_id;
                END;
                $fn$;
                """);

            // ── Accusé de réception (DLR) ───────────────────────────────────────────────────────
            // Ne s'applique QU'À un message au statut « Sent » : un agrégateur rejoue volontiers ses
            // accusés, et cette condition rend l'opération idempotente sans table de déduplication.
            // Un accusé négatif recrédite, exactement comme un abandon.
            migrationBuilder.Sql("""
                CREATE FUNCTION apply_sms_delivery_receipt(
                    p_provider_message_id text,
                    p_is_delivered boolean,
                    p_failure_reason text)
                RETURNS boolean
                LANGUAGE plpgsql
                SECURITY DEFINER
                SET search_path = public
                AS $fn$
                DECLARE
                    v_id uuid;
                    v_school_id uuid;
                    v_segments integer;
                BEGIN
                    SELECT "Id", "SchoolId", "SegmentCount"
                    INTO v_id, v_school_id, v_segments
                    FROM sms_messages
                    WHERE "ProviderMessageId" = p_provider_message_id
                      AND "Status" = 'Sent'
                      AND "IsDeleted" = FALSE;

                    IF NOT FOUND THEN
                        RETURN FALSE;
                    END IF;

                    IF p_is_delivered THEN
                        UPDATE sms_messages
                        SET "Status" = 'Delivered',
                            "DeliveredAt" = NOW(),
                            "UpdatedAt" = NOW()
                        WHERE "Id" = v_id;
                    ELSE
                        UPDATE sms_messages
                        SET "Status" = 'Failed',
                            "FailureReason" = p_failure_reason,
                            "UpdatedAt" = NOW()
                        WHERE "Id" = v_id;

                        UPDATE school_settings
                        SET "SmsCreditBalance" = "SmsCreditBalance" + v_segments,
                            "UpdatedAt" = NOW()
                        WHERE "SchoolId" = v_school_id;
                    END IF;

                    RETURN TRUE;
                END;
                $fn$;
                """);

            // REVOKE ... FROM PUBLIC d'abord : sans lui, PostgreSQL accorde EXECUTE à tout le monde
            // par défaut, ce qui viderait de son sens le périmètre étroit de ces fonctions.
            migrationBuilder.Sql($"""
                DO $grant$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{AppRole}') THEN
                        EXECUTE 'REVOKE ALL ON FUNCTION claim_pending_sms(integer, integer) FROM PUBLIC';
                        EXECUTE 'REVOKE ALL ON FUNCTION settle_sms_attempt(uuid, boolean, text, text, integer) FROM PUBLIC';
                        EXECUTE 'REVOKE ALL ON FUNCTION apply_sms_delivery_receipt(text, boolean, text) FROM PUBLIC';

                        EXECUTE 'GRANT EXECUTE ON FUNCTION claim_pending_sms(integer, integer) TO {AppRole}';
                        EXECUTE 'GRANT EXECUTE ON FUNCTION settle_sms_attempt(uuid, boolean, text, text, integer) TO {AppRole}';
                        EXECUTE 'GRANT EXECUTE ON FUNCTION apply_sms_delivery_receipt(text, boolean, text) TO {AppRole}';
                    ELSE
                        RAISE WARNING 'Rôle % absent : la file des SMS ne sera pas dépilée.', '{AppRole}';
                    END IF;
                END
                $grant$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS apply_sms_delivery_receipt(text, boolean, text);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS settle_sms_attempt(uuid, boolean, text, text, integer);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS claim_pending_sms(integer, integer);");

            migrationBuilder.DropIndex(
                name: "IX_sms_messages_ProviderMessageId",
                table: "sms_messages");

            migrationBuilder.DropIndex(
                name: "IX_sms_messages_queue",
                table: "sms_messages");

            migrationBuilder.DropColumn(
                name: "AttemptCount",
                table: "sms_messages");

            migrationBuilder.DropColumn(
                name: "DeliveredAt",
                table: "sms_messages");

            migrationBuilder.DropColumn(
                name: "DispatchedAt",
                table: "sms_messages");

            migrationBuilder.DropColumn(
                name: "NextAttemptAt",
                table: "sms_messages");
        }
    }
}
