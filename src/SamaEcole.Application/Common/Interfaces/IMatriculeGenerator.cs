namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Génère le prochain matricule séquentiel pour une école donnée, à l'intérieur de la même
/// transaction que l'insertion (AGENTS.md règle #3, ticket JGK-D01). L'implémentation
/// (SamaEcole.Persistence) doit garantir l'absence de trou/doublon sous concurrence
/// (ex. verrou transactionnel ou séquence PostgreSQL dédiée par école).
/// </summary>
public interface IMatriculeGenerator
{
    Task<string> GenerateNextStudentMatriculeAsync(Guid schoolId, CancellationToken cancellationToken);
    Task<string> GenerateNextTeacherMatriculeAsync(Guid schoolId, CancellationToken cancellationToken);

    /// <summary>
    /// Prochain numéro de reçu d'inscription (ticket JGK-E02, ex. « REC-2025-0002 »). Même contrat que
    /// les matricules : appelé DANS la transaction d'inscription, gapless et unique par établissement.
    /// </summary>
    Task<string> GenerateNextReceiptNumberAsync(Guid schoolId, CancellationToken cancellationToken);
}
