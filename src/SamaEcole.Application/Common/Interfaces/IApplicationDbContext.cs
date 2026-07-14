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
    DbSet<Classroom> Classrooms { get; }

    /// <summary>Années scolaires (ticket JGK-C01) : le pivot des inscriptions, des frais et des bulletins.</summary>
    DbSet<SchoolYear> SchoolYears { get; }

    DbSet<User> Users { get; }
    DbSet<Subscription> Subscriptions { get; }

    /// <summary>Journal append-only des changements de statut (ticket JGK-A05) : on y AJOUTE, jamais plus.</summary>
    DbSet<UserStatusHistory> UserStatusHistory { get; }

    /// <summary>Paramètres d'établissement (ticket JGK-B02).</summary>
    DbSet<SchoolSettings> SchoolSettings { get; }

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
