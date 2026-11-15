namespace SamaEcole.Application.Exports;

/// <summary>
/// Écrit l'archive « Exporter mes données » de l'établissement COURANT dans un flux : un fichier ZIP contenant un CSV par table
/// d'établissement (toutes les données de l'école, jamais celles d'une autre : Global Query Filter + RLS), un manifeste JSON et un
/// LISEZMOI. Implémentée dans SamaEcole.Persistence, qui connaît le modèle EF ; l'Application ne dépend que de ce contrat.
///
/// Ne contient JAMAIS de secret : hachages de mots de passe, jetons, clés. <see cref="SensitiveColumnRules"/> en est la liste de
/// refus, et un test refuse toute nouvelle colonne au nom suspect qui ne figurerait pas dans cette liste.
/// </summary>
public interface ISchoolDataExporter
{
    Task<SchoolDataExportManifest> ExportAsync(Stream destination, CancellationToken cancellationToken);
}

/// <param name="SchoolName">Nom de l'établissement exporté.</param>
/// <param name="GeneratedAtUtc">Horodatage de l'export.</param>
/// <param name="Tables">Une entrée par table exportée (y compris les tables vides, avec 0 ligne).</param>
public sealed record SchoolDataExportManifest(
    string SchoolName,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<SchoolDataExportTable> Tables)
{
    public int TotalRows => Tables.Sum(t => t.RowCount);
}

public sealed record SchoolDataExportTable(string Name, string? File, int RowCount, int ColumnCount);

/// <summary>Règles de refus : tables et colonnes qui ne sortent jamais de la plateforme, même vers le Directeur.</summary>
public static class SensitiveColumnRules
{
    /// <summary>Tables entièrement exclues : jetons d'authentification, sans valeur pour l'établissement.</summary>
    public static readonly IReadOnlySet<string> ExcludedTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "refresh_tokens",
        "password_reset_tokens"
    };

    /// <summary>Colonnes exclues par leur nom exact (insensible à la casse).</summary>
    public static readonly IReadOnlySet<string> ExcludedColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "PasswordHash",
        "TokenHash",
        "WebhookSecret",
        "ApiKey",
        "SecurityStamp",
        // État de verrouillage de compte : donnée de sécurité de la plateforme, sans valeur pour l'établissement.
        "AccessFailedCount",
        "LockoutEndAt"
    };

    private static readonly System.Text.RegularExpressions.Regex WordPattern =
        new("[A-Z]+(?![a-z])|[A-Z]?[a-z0-9]+", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly HashSet<string> SensitiveWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "passwd", "secret", "hash", "apikey", "token", "salt", "credential", "credentials"
    };

    /// <summary>
    /// Un nom de colonne qui ressemble à un secret doit être traité explicitement (exclu ou autorisé ici, après revue). Détection par MOTS
    /// entiers du nom PascalCase : « AllowSecretaryToManageGrading » n'est pas suspect (« Secretary » n'est pas « secret »), « TokenHash » l'est.
    /// </summary>
    public static bool LooksSensitive(string columnName) =>
        WordPattern.Matches(columnName).Any(m => SensitiveWords.Contains(m.Value));

    /// <summary>Colonnes au nom suspect mais revues et autorisées (aucun secret : donnée métier). Vide à ce jour.</summary>
    public static readonly IReadOnlySet<string> ReviewedAllowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}
