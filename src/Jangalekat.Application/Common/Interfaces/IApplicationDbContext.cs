using Jangalekat.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Jangalekat.Application.Common.Interfaces;

/// <summary>
/// Abstraction du DbContext exposée à Application, implémentée par Jangalekat.Persistence.
/// Application ne référence jamais EF Core directement en dehors de ce contrat minimal.
/// </summary>
public interface IApplicationDbContext
{
    DbSet<School> Schools { get; }
    DbSet<Student> Students { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
