namespace SamaEcole.Application.Users.Common;

/// <summary>
/// Politique de mot de passe (docs/Volume_7_Security.md §2) : minimum 12 caractères, au moins une
/// majuscule, une minuscule, un chiffre, un caractère spécial ; ni suite évidente ni donnée
/// personnelle. Partagée entre la création de compte et la réinitialisation — les deux seuls
/// endroits où un humain saisit un mot de passe (le mot de passe généré de JGK-B01 n'y est pas
/// soumis : un générateur ne produit ni "123456" ni le nom de l'école).
/// </summary>
public static class PasswordPolicy
{
    private static readonly string[] ForbiddenSequences =
        ["123456", "azerty", "qwerty", "password", "motdepasse", "abcdef"];

    public static IEnumerable<string> Validate(string password, params string?[] personalTerms)
    {
        if (password.Length < 12)
            yield return "Le mot de passe doit contenir au moins 12 caractères.";

        if (!password.Any(char.IsUpper))
            yield return "Le mot de passe doit contenir au moins une majuscule.";

        if (!password.Any(char.IsLower))
            yield return "Le mot de passe doit contenir au moins une minuscule.";

        if (!password.Any(char.IsDigit))
            yield return "Le mot de passe doit contenir au moins un chiffre.";

        if (password.Length > 0 && password.All(char.IsLetterOrDigit))
            yield return "Le mot de passe doit contenir au moins un caractère spécial.";

        var lowered = password.ToLowerInvariant();

        if (ForbiddenSequences.Any(lowered.Contains))
            yield return "Le mot de passe ne doit pas contenir de suite évidente (ex. 123456, password).";

        // Découpé en mots individuels ("Awa", "Ndiaye"), pas la phrase entière : un mot de passe
        // « AwaNdiaye2026! » ne contient nulle part la sous-chaîne "awa ndiaye" (avec l'espace du nom
        // complet), mais contient bien "awa" — c'est CE mot-là qui doit être détecté.
        var personalWords = personalTerms
            .Where(term => !string.IsNullOrWhiteSpace(term))
            .SelectMany(term => term!.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(word => word.Length >= 3);

        if (personalWords.Any(word => lowered.Contains(word.ToLowerInvariant())))
            yield return "Le mot de passe ne doit pas contenir votre nom ou celui de l'établissement.";
    }
}
