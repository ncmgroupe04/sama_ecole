namespace SamaEcole.Application.Common.Exceptions;

/// <summary>
/// Levée quand la signature d'un webhook de paiement (ticket JGK-I06) est absente ou invalide.
/// Traduite en HTTP 401 par SamaEcole.Web — docs/Volume_7_Security.md §12bis : « Une signature absente
/// ou invalide entraîne un rejet 401… sans aucune exception de contournement en environnement de
/// développement ». AUCUN traitement (recherche de paiement, appel de confirmation) n'a lieu avant
/// cette vérification.
/// </summary>
public class InvalidWebhookSignatureException(string message) : Exception(message);
