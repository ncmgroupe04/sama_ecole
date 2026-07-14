using System.Data;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace SamaEcole.Persistence.Auth;

/// <summary>
/// Ticket JGK-A04. Deux régimes d'accès, pour une raison précise :
///
///   * users          -> protégée par une policy RLS sur SchoolId (ticket JGK-A03). Le login n'a pas
///                       encore de tenant : une requête EF n'y verrait AUCUNE ligne. On passe donc par
///                       les fonctions SECURITY DEFINER auth_find_user_by_* / auth_touch_login, qui
///                       s'exécutent avec les droits du propriétaire. Le rôle applicatif a EXECUTE sur
///                       ces trois fonctions et RIEN d'autre : la brèche est réduite à ces opérations.
///
///   * refresh_tokens -> aucune RLS (aucune donnée d'établissement, cf. entité RefreshToken).
///                       Accès EF Core classique.
/// </summary>
public class AuthStore(ApplicationDbContext dbContext, TimeProvider timeProvider) : IAuthStore
{
    public async Task<AuthUser?> FindUserByEmailAsync(string email, CancellationToken cancellationToken)
    {
        await using var command = await CreateCommandAsync("SELECT * FROM auth_find_user_by_email(@p)", cancellationToken);
        command.Parameters.AddWithValue("p", email);

        return await ReadUserAsync(command, cancellationToken);
    }

    public async Task<AuthUser?> FindUserByIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var command = await CreateCommandAsync("SELECT * FROM auth_find_user_by_id(@p)", cancellationToken);
        command.Parameters.AddWithValue("p", userId);

        return await ReadUserAsync(command, cancellationToken);
    }

    public async Task RecordLoginAttemptAsync(
        Guid userId,
        bool success,
        int maxFailedAttempts,
        int lockoutMinutes,
        CancellationToken cancellationToken)
    {
        await using var command = await CreateCommandAsync(
            "SELECT auth_touch_login(@userId, @success, @maxFailed, @lockoutMinutes)", cancellationToken);

        command.Parameters.AddWithValue("userId", userId);
        command.Parameters.AddWithValue("success", success);
        command.Parameters.AddWithValue("maxFailed", maxFailedAttempts);
        command.Parameters.AddWithValue("lockoutMinutes", lockoutMinutes);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task StoreRefreshTokenAsync(
        Guid userId,
        string tokenHash,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        dbContext.RefreshTokens.Add(new RefreshToken
        {
            UserId = userId,
            TokenHash = tokenHash,
            ExpiresAt = expiresAt
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<StoredRefreshToken?> FindRefreshTokenAsync(string tokenHash, CancellationToken cancellationToken)
    {
        return await dbContext.RefreshTokens
            .AsNoTracking()
            .Where(t => t.TokenHash == tokenHash)
            .Select(t => new StoredRefreshToken(t.Id, t.UserId, t.ExpiresAt, t.RevokedAt))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task RevokeRefreshTokenAsync(Guid tokenId, CancellationToken cancellationToken)
    {
        await dbContext.RefreshTokens
            .Where(t => t.Id == tokenId && t.RevokedAt == null)
            .ExecuteUpdateAsync(
                s => s.SetProperty(t => t.RevokedAt, timeProvider.GetUtcNow()),
                cancellationToken);
    }

    public async Task RevokeAllRefreshTokensAsync(Guid userId, CancellationToken cancellationToken)
    {
        await dbContext.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(
                s => s.SetProperty(t => t.RevokedAt, timeProvider.GetUtcNow()),
                cancellationToken);
    }

    private async Task<NpgsqlCommand> CreateCommandAsync(string sql, CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();

        if (connection.State is not ConnectionState.Open)
        {
            // Passe par EF : l'ouverture déclenche TenantConnectionInterceptor, qui positionne
            // app.current_school_id (vide ici, puisque le login n'a pas encore de tenant).
            await dbContext.Database.OpenConnectionAsync(cancellationToken);
        }

        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction() as NpgsqlTransaction;

        return command;
    }

    private static async Task<AuthUser?> ReadUserAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new AuthUser(
            Id: reader.GetGuid(reader.GetOrdinal("Id")),
            SchoolId: GetNullableGuid(reader, "SchoolId"),
            Email: reader.GetString(reader.GetOrdinal("Email")),
            PasswordHash: reader.GetString(reader.GetOrdinal("PasswordHash")),
            FullName: reader.GetString(reader.GetOrdinal("FullName")),
            Role: Enum.Parse<Role>(reader.GetString(reader.GetOrdinal("Role"))),
            Status: Enum.Parse<EntityStatus>(reader.GetString(reader.GetOrdinal("Status"))),
            AccessFailedCount: reader.GetInt32(reader.GetOrdinal("AccessFailedCount")),
            LockoutEndAt: GetNullableDate(reader, "LockoutEndAt"));
    }

    private static Guid? GetNullableGuid(NpgsqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);
    }

    private static DateTimeOffset? GetNullableDate(NpgsqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);
    }
}