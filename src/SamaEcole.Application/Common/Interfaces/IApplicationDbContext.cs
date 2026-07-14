using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Abstraction du DbContext exposée à Application, implémentée par SamaEcole.Persistence.
/// Application ne référence jamais EF Core directement en dehors de ce contrat minimal.
/// </summary>
public interface IApplicationDbContext
{
    DbSet<School> Schools { get; }
    DbSet<Student> Students { get; }
    DbSet<User> Users { get; }
    DbSet<Subscription> Subscriptions { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Exécute <paramref name="operation"/> dans UNE seule transaction (la crée si aucune n'est
    /// déjà ouverte). Indispensable dès qu'un matricule est généré : il doit l'être dans la même
    /// transaction que l'insertion, sans quoi un échec d'enregistrement laisserait un numéro
    /// consommé — donc un trou dans la numérotation (AGENTS.md règle #3).
    /// </summary>
    Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken);
}
