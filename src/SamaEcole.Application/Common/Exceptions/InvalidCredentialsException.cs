namespace SamaEcole.Application.Common.Exceptions;

/// <summary>
/// Échec d'authentification → HTTP 401 (docs/Volume_4_API_Design.md §0.4, ticket JGK-A04).
///
/// Le message est volontairement IDENTIQUE pour « e-mail inconnu », « mot de passe faux » et
/// « compte verrouillé/suspendu » : distinguer les cas permettrait d'énumérer les comptes existants
/// de la plateforme. Le motif réel est journalisé côté serveur, jamais renvoyé au client.
/// </summary>
public class InvalidCredentialsException()
    : Exception("Identifiants invalides.");