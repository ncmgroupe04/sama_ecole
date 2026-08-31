namespace SamaEcole.Application.Common.Exceptions;

/// <summary>
/// Levée quand une règle métier interdit une opération sur une ressource par ailleurs valide et
/// existante (ex. supprimer une classe encore liée à des élèves, annuler une inscription déjà
/// encaissée). Traduite en HTTP 409 par SamaEcole.Web : c'est l'ÉTAT de la ressource qui bloque
/// l'opération, pas la forme de la requête (ValidationException, 422) ni une écriture concurrente
/// (ConcurrencyConflictException, également 409 mais pour une cause différente).
///
/// <paramref name="code"/> est OPTIONNEL : sans lui, l'API renvoie le code générique
/// <c>BUSINESS_RULE_VIOLATION</c> (comportement historique, inchangé). Avec lui, l'API renvoie ce
/// code STABLE — utile quand le client doit distinguer plusieurs refus 409 pour réagir
/// différemment (ex. <c>RESET_UNAVAILABLE_LIVE_MODE</c> vs <c>ALREADY_LIVE</c>).
/// </summary>
public class BusinessRuleException(string message, string? code = null) : Exception(message)
{
    /// <summary>Code d'erreur stable destiné au client, ou <c>null</c> pour le code générique.</summary>
    public string? Code { get; } = code;
}
