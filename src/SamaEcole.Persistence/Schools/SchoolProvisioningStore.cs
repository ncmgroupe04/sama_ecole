using System.Data;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace SamaEcole.Persistence.Schools;

/// <summary>
/// Ticket JGK-B01 — passe par la fonction SECURITY DEFINER provision_school_director (migration
/// AddSchoolProvisioning). Un INSERT EF classique serait rejeté par la policy RLS de `users` : un
/// Super Admin n'a pas de schoolId, sa session ne satisfait le WITH CHECK d'aucune ligne.
/// </summary>
public class SchoolProvisioningStore(ApplicationDbContext dbContext) : ISchoolProvisioningStore
{
    public async Task<Guid?> CreateInitialDirectorAsync(
        Guid schoolId,
        string email,
        string passwordHash,
        string fullName,
        Role role,
        CancellationToken cancellationToken)
    {
        await using var command = await CreateCommandAsync(
            "SELECT provision_school_director(@schoolId, @email, @passwordHash, @fullName, @role)",
            cancellationToken);

        command.Parameters.AddWithValue("schoolId", schoolId);
        command.Parameters.AddWithValue("email", email);
        command.Parameters.AddWithValue("passwordHash", passwordHash);
        command.Parameters.AddWithValue("fullName", fullName);
        command.Parameters.AddWithValue("role", role.ToString());

        var result = await command.ExecuteScalarAsync(cancellationToken);

        // NULL = l'établissement a déjà un utilisateur. La fonction refuse alors d'agir : c'est sa
        // garde anti-escalade, pas une erreur technique.
        return result is Guid id ? id : null;
    }

    public async Task<Guid?> CreateInitialSubscriptionAsync(
        Guid schoolId,
        SubscriptionPlan plan,
        SubscriptionStatus status,
        CancellationToken cancellationToken)
    {
        await using var command = await CreateCommandAsync(
            "SELECT provision_subscription(@schoolId, @plan, @status)",
            cancellationToken);

        command.Parameters.AddWithValue("schoolId", schoolId);
        command.Parameters.AddWithValue("plan", plan.ToString());
        command.Parameters.AddWithValue("status", status.ToString());

        var result = await command.ExecuteScalarAsync(cancellationToken);

        // NULL = l'établissement a déjà un abonnement. La fonction refuse alors d'agir : garde
        // anti-escalade, pas une erreur technique.
        return result is Guid id ? id : null;
    }

    public async Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken)
    {
        // Réutilise la fonction du chemin de login : elle voit TOUS les comptes, toutes écoles
        // confondues, ce qu'une requête EF sous RLS ne pourrait pas faire.
        await using var command = await CreateCommandAsync(
            "SELECT EXISTS (SELECT 1 FROM auth_find_user_by_email(@email))",
            cancellationToken);

        command.Parameters.AddWithValue("email", email);

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

        // Indispensable : la création se fait dans la transaction ouverte par le Handler. Sans cela,
        // le Directeur serait écrit hors transaction et survivrait à l'annulation de l'école.
        command.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction() as NpgsqlTransaction;

        return command;
    }
}
