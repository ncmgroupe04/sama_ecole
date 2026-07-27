using System.Data;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace SamaEcole.Persistence.Subscriptions;

/// <summary>
/// Passe par la fonction SECURITY DEFINER grant_complimentary_subscription (migration
/// AddPromoCodesAndComplimentaryAccess) — même raison que SchoolProvisioningStore : `subscriptions`
/// est sous RLS, le Super Admin n'a pas de SchoolId à présenter au WITH CHECK d'un UPDATE EF direct.
/// </summary>
public class SubscriptionAdminStore(ApplicationDbContext dbContext) : ISubscriptionAdminStore
{
    public async Task<bool> GrantComplimentaryAccessAsync(
        Guid schoolId,
        SubscriptionPlan plan,
        DateOnly expiresAt,
        Guid? promoCodeId,
        CancellationToken cancellationToken)
    {
        await using var command = await CreateCommandAsync(
            "SELECT grant_complimentary_subscription(@schoolId, @plan, @status, @expiresAt, @promoCodeId)",
            cancellationToken);

        command.Parameters.AddWithValue("schoolId", schoolId);
        command.Parameters.AddWithValue("plan", plan.ToString());
        command.Parameters.AddWithValue("status", SubscriptionStatus.Active.ToString());
        command.Parameters.AddWithValue("expiresAt", expiresAt.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("promoCodeId", (object?)promoCodeId ?? DBNull.Value);

        var result = await command.ExecuteScalarAsync(cancellationToken);

        // NULL = aucun abonnement pour cette école — rien à modifier (voir la garde WHERE de la
        // fonction SQL), pas une erreur technique.
        return result is Guid;
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
