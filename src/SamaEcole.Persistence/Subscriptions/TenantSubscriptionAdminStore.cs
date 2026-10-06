using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Subscriptions;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Persistence.Subscriptions;

/// <summary>
/// Passe par les fonctions SECURITY DEFINER get_tenant_subscription / update_tenant_subscription (migration
/// AddTenantSubscriptionAdminFunctions) — même raison que SubscriptionAdminStore : <c>tenant_subscriptions</c>
/// est sous RLS, le Super Admin n'a pas de SchoolId à présenter à la policy.
/// </summary>
public class TenantSubscriptionAdminStore(ApplicationDbContext dbContext) : ITenantSubscriptionAdminStore
{
    public async Task<TenantSubscriptionDto?> GetAsync(Guid schoolId, CancellationToken cancellationToken)
    {
        await using var command = await CreateCommandAsync(
            "SELECT * FROM get_tenant_subscription(@schoolId)", cancellationToken);

        command.Parameters.AddWithValue("schoolId", schoolId);

        return await ReadSingleAsync(command, cancellationToken);
    }

    public async Task<TenantSubscriptionDto?> UpdateAsync(
        Guid schoolId,
        StudentQuotaTier? tier,
        int? maxStudentLimit,
        int? softQuotaLimit,
        TenantSubscriptionStatus? status,
        Guid updatedBy,
        CancellationToken cancellationToken)
    {
        await using var command = await CreateCommandAsync(
            "SELECT * FROM update_tenant_subscription(@schoolId, @tier, @max, @soft, @status, @updatedBy)",
            cancellationToken);

        command.Parameters.AddWithValue("schoolId", schoolId);

        // Types explicites : un paramètre NULL non typé ne résout pas la surcharge de la fonction.
        command.Parameters.Add(new NpgsqlParameter("tier", NpgsqlDbType.Text) { Value = (object?)tier?.ToString() ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("max", NpgsqlDbType.Integer) { Value = (object?)maxStudentLimit ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("soft", NpgsqlDbType.Integer) { Value = (object?)softQuotaLimit ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("status", NpgsqlDbType.Text) { Value = (object?)status?.ToString() ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("updatedBy", NpgsqlDbType.Text) { Value = updatedBy.ToString() });

        return await ReadSingleAsync(command, cancellationToken);
    }

    private static async Task<TenantSubscriptionDto?> ReadSingleAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new TenantSubscriptionDto(
            Id: reader.GetGuid(reader.GetOrdinal("Id")),
            SchoolId: reader.GetGuid(reader.GetOrdinal("SchoolId")),
            ProfileType: reader.GetString(reader.GetOrdinal("ProfileType")),
            StudentQuotaTier: reader.GetString(reader.GetOrdinal("StudentQuotaTier")),
            MaxStudentLimit: reader.GetInt32(reader.GetOrdinal("MaxStudentLimit")),
            SoftQuotaLimit: reader.GetInt32(reader.GetOrdinal("SoftQuotaLimit")),
            Status: reader.GetString(reader.GetOrdinal("Status")),
            IsPedagogyEnabled: reader.GetBoolean(reader.GetOrdinal("IsPedagogyEnabled")),
            IsFinanceEnabled: reader.GetBoolean(reader.GetOrdinal("IsFinanceEnabled")),
            IsInternatEnabled: reader.GetBoolean(reader.GetOrdinal("IsInternatEnabled")),
            IsCoranModuleEnabled: reader.GetBoolean(reader.GetOrdinal("IsCoranModuleEnabled")));
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
