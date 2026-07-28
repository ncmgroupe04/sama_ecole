namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Récupère les octets du logo d'un établissement à partir de l'URL saisie par le Directeur
/// (<c>School.LogoUrl</c>), pour l'incruster dans le reçu PDF officiel (ticket JGK-E02).
///
/// L'URL vient d'un utilisateur (le Directeur) : la récupérer côté serveur est une surface SSRF.
/// L'implémentation (SamaEcole.Infrastructure) DOIT donc filtrer les adresses internes, borner la
/// taille et le temps, et n'accepter qu'une vraie image. Le contrat est volontairement « best effort » :
/// toute anomalie (URL absente, hôte injoignable, contenu non-image, réseau interne) rend <c>null</c> —
/// le reçu se génère alors sans logo, jamais en erreur (le reçu est un document officiel, il doit
/// toujours s'émettre).
/// </summary>
public interface ISchoolLogoProvider
{
    Task<byte[]?> TryFetchAsync(string? logoUrl, CancellationToken cancellationToken);
}
