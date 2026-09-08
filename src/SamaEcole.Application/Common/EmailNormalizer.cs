namespace SamaEcole.Application.Common;

/// <summary>
/// Forme CANONIQUE d'une adresse e-mail lorsqu'elle sert d'IDENTIFIANT DE COMPTE
/// (<c>users.Email</c>, <c>school_registration_requests.DirectorEmail</c>) — docs/Volume_3_DDS.md §5.2 :
/// « un email n'appartient qu'à un compte sur toute la plateforme ».
///
/// Un seul point de passage, pour que tous les chemins de création (inscription self-service,
/// création directe par le Super Admin, ajout de personnel par un Directeur) écrivent la MÊME valeur.
/// Sans lui, <c>Directeur@ecole.sn</c> et <c>directeur@ecole.sn</c> étaient deux comptes distincts :
/// l'index unique <c>IX_users_Email</c> (btree sensible à la casse) les laissait tous deux passer,
/// alors que le login (<c>auth_find_user_by_email</c>, <c>lower() = lower()</c>) les confondait — d'où
/// une connexion non déterministe entre les deux écoles.
///
/// N'est PAS destiné aux e-mails de simple contact (<c>schools.Email</c>, e-mail du tuteur d'un élève),
/// qui restent en texte libre : là, réécrire la casse d'une donnée saisie volontairement n'a aucun sens.
/// </summary>
public static class EmailNormalizer
{
    /// <summary>
    /// Espaces de bord retirés et casse repliée en minuscules (invariant de culture — un e-mail est de
    /// l'ASCII, jamais soumis aux règles de casse du turc &amp; co). Une entrée <c>null</c> ou vide
    /// ressort en chaîne vide : c'est au validateur de format, en amont, de refuser un e-mail manquant.
    /// </summary>
    public static string Normalize(string? email) =>
        (email ?? string.Empty).Trim().ToLowerInvariant();
}
