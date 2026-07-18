namespace SamaEcole.Application.Common.Exceptions;

/// <summary>
/// Levée quand l'agrégateur de paiement (ticket JGK-I05, ex. PayDunya) refuse ou est injoignable lors
/// de la création d'une facture. Traduite en HTTP 502 par SamaEcole.Web : le serveur, agissant comme
/// passerelle vers l'agrégateur, a reçu une réponse invalide ou aucune réponse — jamais une exception
/// brute (AGENTS.md règle #9).
/// </summary>
public class PaymentProviderException(string message) : Exception(message);
