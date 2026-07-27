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

        // DateOnly directement, pas ToDateTime(...) : voir le commentaire équivalent
        // d'ExpireOverdueSubscriptionsAsync — un DateTime "Unspecified" se lie en `timestamp`, que
        // Postgres ne convertit PAS implicitement vers le `date` attendu par la fonction.
        command.Parameters.Add(new NpgsqlParameter("expiresAt", NpgsqlTypes.NpgsqlDbType.Date) { Value = expiresAt });
        command.Parameters.AddWithValue("promoCodeId", (object?)promoCodeId ?? DBNull.Value);

        var result = await command.ExecuteScalarAsync(cancellationToken);

        // NULL = aucun abonnement pour cette école — rien à modifier (voir la garde WHERE de la
        // fonction SQL), pas une erreur technique.
        return result is Guid;
    }

    public async Task<IReadOnlyList<Guid>> ExpireOverdueSubscriptionsAsync(
        DateOnly asOf, CancellationToken cancellationToken)
    {
        await using var command = await CreateCommandAsync(
            "SELECT * FROM expire_overdue_subscriptions(@asOf)", cancellationToken);

        // DateOnly directement (mappé nativement sur `date` par Npgsql) plutôt que ToDateTime(...) :
        // un DateTime "Unspecified" s'y résout en `timestamp`, que Postgres ne convertit PAS
        // implicitement vers `date` pour la résolution de surcharge d'un appel de fonction — d'où
        // "function expire_overdue_subscriptions(timestamp without time zone) does not exist".
        command.Parameters.Add(new NpgsqlParameter("asOf", NpgsqlTypes.NpgsqlDbType.Date) { Value = asOf });

        var schoolIds = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            schoolIds.Add(reader.GetGuid(0));
        }

        return schoolIds;
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
