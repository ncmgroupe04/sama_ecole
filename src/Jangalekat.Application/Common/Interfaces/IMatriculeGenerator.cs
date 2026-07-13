namespace Jangalekat.Application.Common.Interfaces;

/// <summary>
/// Génère le prochain matricule séquentiel pour une école donnée, à l'intérieur de la même
/// transaction que l'insertion (AGENTS.md règle #3, ticket JGK-D01). L'implémentation
/// (Jangalekat.Persistence) doit garantir l'absence de trou/doublon sous concurrence
/// (ex. verrou transactionnel ou séquence PostgreSQL dédiée par école).
/// </summary>
public interface IMatriculeGenerator
{
    Task<string> GenerateNextStudentMatriculeAsync(Guid schoolId, CancellationToken cancellationToken);
    Task<string> GenerateNextTeacherMatriculeAsync(Guid schoolId, CancellationToken cancellationToken);
}
