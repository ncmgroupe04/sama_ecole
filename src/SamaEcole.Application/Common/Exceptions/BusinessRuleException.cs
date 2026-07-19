namespace SamaEcole.Application.Common.Exceptions;

/// <summary>
/// Levée quand une règle métier interdit une opération sur une ressource par ailleurs valide et
/// existante (ex. supprimer une classe encore liée à des élèves, annuler une inscription déjà
/// encaissée). Traduite en HTTP 409 par SamaEcole.Web : c'est l'ÉTAT de la ressource qui bloque
/// l'opération, pas la forme de la requête (ValidationException, 422) ni une écriture concurrente
/// (ConcurrencyConflictException, également 409 mais pour une cause différente).
/// </summary>
public class BusinessRuleException(string message) : Exception(message);
