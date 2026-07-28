using System.Data;
using SamaEcole.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace SamaEcole.Persistence.Notifications;

/// <summary>
/// Implémentation d'<see cref="ISmsQueueStore"/> par les fonctions SECURITY DEFINER de la migration
/// AddSmsQueue — même idiome que <see cref="Subscriptions.SubscriptionAdminStore"/> : la RLS reste
/// active, seul un périmètre étroit et nommé lui échappe.
///
/// SQL brut plutôt qu'EF : ces trois opérations sont exactement ce qu'EF ne sait pas exprimer — une
/// réclamation concurrente (FOR UPDATE SKIP LOCKED) et deux transitions qui doivent rester atomiques
/// avec le recréditage du solde.
///
/// Les erreurs Npgsql ne remontent pas telles quelles à la couche Application (voir la doc de
/// SmsQueueProcessor) : le worker ne fait que journaliser et repasser au tour suivant.
/// </summary>
public class SmsQueueStore(ApplicationDbContext dbContext) : ISmsQueueStore
{
    public async Task<IReadOnlyList<QueuedSmsMessage>> ClaimPendingAsync(
        int batchSize, TimeSpan lease, CancellationToken cancellationToken)
    {
        await using var command = await CreateCommandAsync(
            "SELECT id, school_id, recipient, body, segment_count, attempt_count "
            + "FROM claim_pending_sms(@batchSize, @leaseSeconds)",
            cancellationToken);

        command.Parameters.AddWithValue("batchSize", batchSize);
        command.Parameters.AddWithValue("leaseSeconds", (int)lease.TotalSeconds);

        var claimed = new List<QueuedSmsMessage>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            claimed.Add(new QueuedSmsMessage(
                Id: reader.GetGuid(0),
                SchoolId: reader.GetGuid(1),
                Recipient: reader.GetString(2),
                Body: reader.GetString(3),
                SegmentCount: reader.GetInt32(4),
                AttemptCount: reader.GetInt32(5)));
        }

        return claimed;
    }

    public async Task SettleAttemptAsync(
        Guid messageId,
        bool isSent,
        string? providerMessageId,
        string? failureReason,
        int maxAttempts,
        CancellationToken cancellationToken)
    {
        await using var command = await CreateCommandAsync(
            "SELECT settle_sms_attempt(@id, @isSent, @providerMessageId, @failureReason, @maxAttempts)",
            cancellationToken);

        command.Parameters.AddWithValue("id", messageId);
        command.Parameters.AddWithValue("isSent", isSent);
        command.Parameters.AddWithValue("providerMessageId", (object?)providerMessageId ?? DBNull.Value);
        command.Parameters.AddWithValue("failureReason", (object?)failureReason ?? DBNull.Value);
        command.Parameters.AddWithValue("maxAttempts", maxAttempts);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> ApplyDeliveryReceiptAsync(
        string providerMessageId,
        bool isDelivered,
        string? failureReason,
        CancellationToken cancellationToken)
    {
        await using var command = await CreateCommandAsync(
            "SELECT apply_sms_delivery_receipt(@providerMessageId, @isDelivered, @failureReason)",
            cancellationToken);

        command.Parameters.AddWithValue("providerMessageId", providerMessageId);
        command.Parameters.AddWithValue("isDelivered", isDelivered);
        command.Parameters.AddWithValue("failureReason", (object?)failureReason ?? DBNull.Value);

        return await command.ExecuteScalarAsync(cancellationToken) is true;
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
