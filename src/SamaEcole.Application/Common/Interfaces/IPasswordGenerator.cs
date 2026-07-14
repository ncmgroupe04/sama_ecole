namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Génère le mot de passe initial du Directeur (ticket JGK-B01).
/// Il n'est JAMAIS choisi par l'appelant ni renvoyé dans la réponse HTTP : il ne transite que par
/// l'e-mail envoyé au Directeur.
/// </summary>
public interface IPasswordGenerator
{
    /// <summary>Conforme à la politique de docs/Volume_7_Security.md §2.</summary>
    string Generate();
}
