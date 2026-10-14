using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Subscriptions;
using SamaEcole.Application.Subscriptions.Changes;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Persistence.Subscriptions;

/// <summary>
/// Passe par les fonctions SECURITY DEFINER list_subscription_change_requests, resolve_subscription_change_request et
/// list_over_quota_schools (migration AddSubscriptionChangeRequests) — même raison que TenantSubscriptionAdminStore : les
/// tables concernées sont sous RLS, le Super Admin n'a pas de SchoolId à présenter à la policy.
/// </summary>
public class SubscriptionChangeAdminStore(ApplicationDbContext dbContext) : ISubscriptionChangeAdminStore
{
    public async Task<IReadOnlyList<SubscriptionChangeRequest>> ListAsync(
        SubscriptionChangeStatus? status, CancellationToken cancellationToken)
    {
        var statusText = status?.ToString();

        // Types explicites (casts) : un paramètre NULL non typé ne résout pas la signature de la fonction. La fonction
        // contourne la RLS ; IgnoreQueryFilters écarte le filtre EF « SchoolId = tenant », qui n'a pas de sens ici.
        return await dbContext.SubscriptionChangeRequests
            .FromSqlInterpolated($"SELECT * FROM list_subscription_change_requests({statusText}::text, NULL::uuid)")
            .IgnoreQueryFilters()
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<SubscriptionChangeRequest?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await dbContext.SubscriptionChangeRequests
            .FromSqlInterpolated($"SELECT * FROM list_subscription_change_requests(NULL::text, {id}::uuid)")
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<Guid?> ResolveAsync(
        Guid id, bool approve, Guid reviewedBy, string? note, CancellationToken cancellationToken)
    {
        await using var command = await CreateCommandAsync(
            "SELECT resolve_subscription_change_request(@id, @approve, @reviewer, @note)", cancellationToken);

        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("approve", approve);
        command.Parameters.AddWithValue("reviewer", reviewedBy);
        command.Parameters.Add(new NpgsqlParameter("note", NpgsqlDbType.Text) { Value = (object?)note ?? DBNull.Value });

        try
        {
            var result = await command.ExecuteScalarAsync(cancellationToken);

            // NULL = la demande n'existe pas ou n'est plus en attente : la fonction refuse d'agir.
            return result is Guid schoolId ? schoolId : null;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.RaiseException)
        {
            // Les gardes de la fonction lèvent des codes stables (voir la migration) : on les traduit en messages lisibles.
            throw ex.MessageText switch
            {
                "STALE_CHANGE_REQUEST" => new BusinessRuleException(
                    "L'abonnement de cette école a changé depuis la demande (profil, tranche ou cycles) : elle ne peut plus être appliquée telle quelle. Rejetez-la et demandez au Directeur d'en envoyer une nouvelle.",
                    "STALE_CHANGE_REQUEST"),
                "TARGET_TIER_TOO_SMALL" => new BusinessRuleException(
                    "L'effectif actuel de l'école dépasse déjà la tranche demandée : la demande ne peut pas être appliquée.",
                    "TARGET_TIER_TOO_SMALL"),
                "CYCLE_HAS_CLASSROOMS" => new BusinessRuleException(
                    "Un cycle retiré par cette demande contient encore des classes : elles doivent être supprimées ou déplacées d'abord.",
                    "CYCLE_HAS_CLASSROOMS"),
                _ => ex
            };
        }
    }

    public async Task<IReadOnlyList<OverQuotaSchoolDto>> ListOverQuotaSchoolsAsync(CancellationToken cancellationToken)
    {
        await using var command = await CreateCommandAsync("SELECT * FROM list_over_quota_schools()", cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var schools = new List<OverQuotaSchoolDto>();

        while (await reader.ReadAsync(cancellationToken))
        {
            var count = reader.GetInt64(reader.GetOrdinal("student_count"));
            var max = reader.GetInt32(reader.GetOrdinal("max_students"));
            var soft = reader.GetInt32(reader.GetOrdinal("soft_limit"));
            var sinceOrdinal = reader.GetOrdinal("over_quota_since");

            schools.Add(new OverQuotaSchoolDto(
                reader.GetGuid(reader.GetOrdinal("school_id")),
                reader.GetString(reader.GetOrdinal("school_name")),
                reader.GetString(reader.GetOrdinal("tier")),
                max,
                soft,
                count,
                // Même verdict que StudentQuotaEvaluator : tolérance entamée, ou dépassée (création refusée).
                count <= soft ? nameof(StudentQuotaState.InTolerance) : nameof(StudentQuotaState.Exceeded),
                await reader.IsDBNullAsync(sinceOrdinal, cancellationToken)
                    ? null
                    : reader.GetFieldValue<DateTimeOffset>(sinceOrdinal)));
        }

        return schools;
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
