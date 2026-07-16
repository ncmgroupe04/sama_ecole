using System.Data;
using SamaEcole.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace SamaEcole.Persistence.AuditLogs;

/// <summary>
/// Ticket JGK-H01 — passe par la fonction SECURITY DEFINER append_audit_log (migration
/// AddAuditLogAppendFunction). Un INSERT EF classique serait rejeté par la policy RLS de audit_logs
/// quand l'appelant n'a pas SON PROPRE SchoolId (connexion avant JWT, ou Super Admin) — même
/// raisonnement que SchoolProvisioningStore/AuthStore pour la table users.
/// </summary>
public class AuditLogStore(ApplicationDbContext dbContext) : IAuditLogStore
{
    public async Task AppendAsync(
        Guid schoolId,
        Guid userId,
        string module,
        string action,
        bool success,
        string? failureReason,
        string? ipAddress,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();

        if (connection.State is not ConnectionState.Open)
        {
            await dbContext.Database.OpenConnectionAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT append_audit_log(@schoolId, @userId, @module, @action, @success, @failureReason, @ipAddress, @occurredAt)";

        command.Parameters.AddWithValue("schoolId", schoolId);
        command.Parameters.AddWithValue("userId", userId);
        command.Parameters.AddWithValue("module", module);
        command.Parameters.AddWithValue("action", action);
        command.Parameters.AddWithValue("success", success);
        command.Parameters.AddWithValue("failureReason", (object?)failureReason ?? DBNull.Value);
        command.Parameters.AddWithValue("ipAddress", (object?)ipAddress ?? DBNull.Value);
        command.Parameters.AddWithValue("occurredAt", occurredAt);

        // Indispensable si un appelant a déjà ouvert une transaction (ex. tests d'intégration qui
        // rollback) : sans cela, l'écriture d'audit se ferait hors transaction et survivrait à une
        // annulation que l'appelant attendait totale.
        command.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction() as NpgsqlTransaction;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
