namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Génère le prochain numéro de table d'une session d'examen, à l'intérieur de la même transaction
/// que l'attribution (AGENTS.md règle #3, même contrat que <see cref="IMatriculeGenerator"/>).
/// L'implémentation (SamaEcole.Persistence) garantit l'absence de trou/doublon sous concurrence par
/// verrou de ligne transactionnel sur la session — jamais une séquence PostgreSQL, qui ne se
/// rembobinerait pas en cas de rollback.
/// </summary>
public interface IExamCandidateNumberGenerator
{
    Task<string> GenerateNextAsync(Guid examSessionId, CancellationToken cancellationToken);
}
