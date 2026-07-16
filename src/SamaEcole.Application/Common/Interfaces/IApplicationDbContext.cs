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

    /// <summary>Matières et coefficients par niveau (ticket JGK-C03). Le coefficient pilote les bulletins.</summary>
    DbSet<Subject> Subjects { get; }

    /// <summary>Catégories de frais paramétrables (ticket JGK-F01) : inscription, mensualité, cantine…</summary>
    DbSet<FeeCategory> FeeCategories { get; }

    /// <summary>Barème : le montant d'une catégorie pour une classe (ticket JGK-F01). Verrou optimiste xmin.</summary>
    DbSet<ClassFee> ClassFees { get; }

    /// <summary>Journal append-only des changements de barème (ticket JGK-F01) : on y AJOUTE, jamais plus.</summary>
    DbSet<FeeChangeHistory> FeeChangeHistory { get; }

    /// <summary>Inscriptions/réinscriptions (ticket JGK-E01). Portent le TotalDue calculé. Verrou optimiste xmin.</summary>
    DbSet<Enrollment> Enrollments { get; }

    /// <summary>Détail figé des frais d'une inscription (ticket JGK-E01) : l'instantané du barème pour le reçu.</summary>
    DbSet<EnrollmentFeeLine> EnrollmentFeeLines { get; }

    /// <summary>Encaissements de caisse (ticket JGK-F02). Le solde vit sur l'inscription (verrou optimiste xmin).</summary>
    DbSet<Payment> Payments { get; }

    DbSet<User> Users { get; }
    DbSet<Subscription> Subscriptions { get; }

    /// <summary>Journal append-only des changements de statut (ticket JGK-A05) : on y AJOUTE, jamais plus.</summary>
    DbSet<UserStatusHistory> UserStatusHistory { get; }

    /// <summary>Paramètres d'établissement (ticket JGK-B02).</summary>
    DbSet<SchoolSettings> SchoolSettings { get; }

    /// <summary>Journal d'audit append-only (ticket JGK-H01) : on y AJOUTE, jamais plus.</summary>
    DbSet<AuditLog> AuditLogs { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Positionne le jeton de concurrence (xmin) ATTENDU par le client sur une entité déjà suivie,
    /// pour que le prochain SaveChangesAsync refuse en 409 si la ligne a changé entre-temps
    /// (AGENTS.md règle #5). Encapsule l'API de suivi d'EF Core : Application n'a pas à connaître xmin.
    /// </summary>
    void SetOriginalConcurrencyToken<TEntity>(TEntity entity, uint expectedVersion) where TEntity : class;

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
