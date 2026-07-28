namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Point de passage OBLIGATOIRE pour toute clé de cache applicatif (Redis, prévu dès la V1 —
/// docs/Volume_6_Dev_Guide.md — mais pas encore consommé en code à ce jour) portant sur une donnée de
/// tenant. La RLS PostgreSQL (AGENTS.md règle #2) et le Global Query Filter EF Core ne protègent QUE
/// PostgreSQL : un cache applicatif est une troisième surface entièrement hors de leur portée. Une clé
/// de cache qui oublie le SchoolId (ex. <c>"grading-scale"</c> au lieu de <c>"school:{id}:grading-scale"</c>)
/// ferait fuiter la donnée d'une école vers toutes les autres dès la première écriture concurrente — voir
/// docs/Volume_3_DDS.md §2.5.
///
/// Composer systématiquement les clés via <see cref="BuildKey"/> plutôt qu'une interpolation de chaîne
/// manuelle : c'est le SEUL moyen de garantir qu'aucune clé ne peut être posée sans SchoolId, y compris
/// par erreur d'inattention plusieurs années après l'écriture de ce commentaire.
/// </summary>
public interface ITenantCacheKeyFactory
{
    /// <summary>
    /// Préfixe <paramref name="key"/> par le SchoolId du tenant courant (jamais un paramètre de requête
    /// modifiable par le client — AGENTS.md règle #10). Lève <see cref="UnauthorizedAccessException"/>
    /// si aucun tenant n'est résolu : fail closed, jamais de clé "globale" accidentelle faute de contexte.
    /// </summary>
    string BuildKey(string key);
}
