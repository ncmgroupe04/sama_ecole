using System.Data;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace SamaEcole.Persistence.Subscriptions;

/// <summary>
/// Ticket JGK-I06 — passe par les fonctions SECURITY DEFINER find_subscription_payment /
/// confirm_subscription_payment (migration AddSubscriptionPaymentConfirmation). L'acteur webhook est
/// ANONYME : aucune session, donc aucun SchoolId — les policies RLS de subscription_payments/subscriptions
/// rejetteraient toute lecture ou écriture EF classique, exactement le même problème que
/// SchoolProvisioningStore pour la création du Directeur/abonnement (JGK-B01/I03).
/// </summary>
public class SubscriptionPaymentStore(ApplicationDbContext dbContext) : ISubscriptionPaymentStore
{
    public async Task<SubscriptionPaymentLookup?> FindAsync(Guid internalPaymentId, CancellationToken cancellationToken)
    {
        await using var command = await CreateCommandAsync(
            """
            SELECT provider_transaction_ref, amount, status
            FROM find_subscription_payment(@internalPaymentId)
            """,
            cancellationToken);

        command.Parameters.AddWithValue("internalPaymentId", internalPaymentId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new SubscriptionPaymentLookup(
            ProviderTransactionRef: reader.GetString(0),
            Amount: reader.GetDecimal(1),
            Status: Enum.Parse<SubscriptionPaymentStatus>(reader.GetString(2)));
    }

    public async Task<SubscriptionPaymentConfirmation?> ConfirmAsync(
        Guid internalPaymentId,
        SubscriptionPaymentStatus finalStatus,
        string webhookPayloadJson,
        CancellationToken cancellationToken)
    {
        await using var command = await CreateCommandAsync(
            """
            SELECT was_processed, school_id, school_name, director_user_id, director_email,
                   director_full_name, new_expires_at
            FROM confirm_subscription_payment(@internalPaymentId, @finalStatus, @webhookPayload::jsonb)
            """,
            cancellationToken);

        command.Parameters.AddWithValue("internalPaymentId", internalPaymentId);
        command.Parameters.AddWithValue("finalStatus", finalStatus.ToString());
        command.Parameters.AddWithValue("webhookPayload", webhookPayloadJson);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        // was_processed = FALSE : la garde SQL (WHERE Status = 'Initiated') n'a rien trouvé à
        // transitionner — référence déjà traitée par un appel concurrent. Idempotence garantie côté
        // base, pas seulement par le contrôle applicatif du Handler (voir la fonction SQL elle-même).
        if (!reader.GetBoolean(0))
        {
            return null;
        }

        return new SubscriptionPaymentConfirmation(
            SchoolId: reader.GetGuid(1),
            SchoolName: reader.GetString(2),
            DirectorUserId: reader.GetGuid(3),
            DirectorEmail: reader.GetString(4),
            DirectorFullName: reader.GetString(5),
            NewExpiresAt: reader.IsDBNull(6) ? null : DateOnly.FromDateTime(reader.GetDateTime(6)));
    }

    private async Task<NpgsqlCommand> CreateCommandAsync(string sql, CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();

        if (connection.State is not ConnectionState.Open)
        {
            await dbContext.Database.OpenConnectionAsync(cancellationToken);
        }

        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction() as NpgsqlTransaction;

        return command;
    }
}
