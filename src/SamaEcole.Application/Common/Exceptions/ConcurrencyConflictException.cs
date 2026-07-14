namespace SamaEcole.Application.Common.Exceptions;

/// <summary>
/// Levée quand une écriture concurrente est détectée sur une entité à verrouillage optimiste
/// (Note, Paiement, Frais — voir AGENTS.md règle #5). Traduite en HTTP 409 par SamaEcole.Web,
/// jamais en écrasement silencieux.
/// </summary>
public class ConcurrencyConflictException(string entityName, object key)
    : Exception($"L'entité '{entityName}' ({key}) a été modifiée par un autre utilisateur entre-temps.");
