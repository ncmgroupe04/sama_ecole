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

    public async Task<AuthUser?> FindActiveDirectorForSchoolAsync(Guid schoolId, CancellationToken cancellationToken)
    {
        await using var command = await CreateCommandAsync(
            "SELECT * FROM auth_find_active_director_by_school(@p)", cancellationToken);
        command.Parameters.AddWithValue("p", schoolId);

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

    public async Task<int> RevokeAllRefreshTokensAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await dbContext.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(
                s => s.SetProperty(t => t.RevokedAt, timeProvider.GetUtcNow()),
                cancellationToken);
    }

    public async Task<int> RevokeAllRefreshTokensForSchoolAsync(Guid schoolId, CancellationToken cancellationToken)
    {
        await using var command = await CreateCommandAsync(
            "SELECT revoke_refresh_tokens_by_school(@schoolId)", cancellationToken);
        command.Parameters.AddWithValue("schoolId", schoolId);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is int count ? count : 0;
    }

    public async Task StorePasswordResetTokenAsync(
        Guid userId,
        string tokenHash,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        // Périme les demandes encore en cours AVANT d'enregistrer la nouvelle : un compte n'a jamais
        // qu'un seul lien de réinitialisation valide à la fois.
        await RevokeOutstandingResetTokensAsync(userId, cancellationToken);

        dbContext.PasswordResetTokens.Add(new PasswordResetToken
        {
            UserId = userId,
            TokenHash = tokenHash,
            ExpiresAt = expiresAt
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<StoredPasswordResetToken?> FindPasswordResetTokenAsync(
        string tokenHash, CancellationToken cancellationToken)
    {
        return await dbContext.PasswordResetTokens
            .AsNoTracking()
            .Where(t => t.TokenHash == tokenHash)
            .Select(t => new StoredPasswordResetToken(t.Id, t.UserId, t.ExpiresAt, t.UsedAt, t.RevokedAt))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<int> CompletePasswordResetAsync(
        Guid tokenId, Guid userId, string newPasswordHash, CancellationToken cancellationToken)
    {
        return await dbContext.ExecuteInTransactionAsync(async ct =>
        {
            await SetPasswordHashAsync(userId, newPasswordHash, ct);

            var now = timeProvider.GetUtcNow();

            // Usage unique.
            await dbContext.PasswordResetTokens
                .Where(t => t.Id == tokenId && t.UsedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedAt, now), ct);

            // Le mot de passe vient de changer : les autres demandes en cours n'ont plus lieu d'être,
            // les laisser valides rouvrirait le compte à qui détient un lien plus ancien.
            await RevokeOutstandingResetTokensAsync(userId, ct);

            return await RevokeAllRefreshTokensAsync(userId, ct);
        }, cancellationToken);
    }

    public async Task<int> ChangePasswordAsync(
        Guid userId, string newPasswordHash, CancellationToken cancellationToken)
    {
        return await dbContext.ExecuteInTransactionAsync(async ct =>
        {
            await SetPasswordHashAsync(userId, newPasswordHash, ct);

            // Même raisonnement que CompletePasswordResetAsync : un lien de réinitialisation encore
            // valide ne doit pas pouvoir contourner le mot de passe qu'on vient de choisir soi-même.
            await RevokeOutstandingResetTokensAsync(userId, ct);

            return await RevokeAllRefreshTokensAsync(userId, ct);
        }, cancellationToken);
    }

    /// <summary>
    /// users est sous RLS, et ni le login (aucun tenant) ni un changement de mot de passe (le Super
    /// Admin qui change le sien n'a lui non plus aucun SchoolId de session) ne peuvent compter sur la
    /// policy standard : l'écriture passe par la fonction SECURITY DEFINER dédiée, une requête EF ne
    /// verrait aucune ligne et l'UPDATE serait un silencieux « 0 ligne modifiée ».
    /// </summary>
    private async Task SetPasswordHashAsync(Guid userId, string newPasswordHash, CancellationToken cancellationToken)
    {
        await using var command = await CreateCommandAsync(
            "SELECT auth_set_password_hash(@userId, @hash)", cancellationToken);

        command.Parameters.AddWithValue("userId", userId);
        command.Parameters.AddWithValue("hash", newPasswordHash);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task RevokeOutstandingResetTokensAsync(Guid userId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        await dbContext.PasswordResetTokens
            .Where(t => t.UserId == userId && t.UsedAt == null && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), cancellationToken);
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