namespace SamaEcole.Application.ReportCards;

/// <summary>
/// Un nom de classe/élève/trimestre peut contenir des espaces ou des séparateurs de chemin (jamais
/// garanti « propre » côté saisie) — partagé par <c>GetClassReportCardsZipQuery</c> et
/// <c>GetClassReportCardsPdfQuery</c> pour produire un nom de fichier/entrée ZIP valide sur tous les
/// systèmes d'exploitation.
/// </summary>
public static class ClassBulletinsFileNaming
{
    public static string Sanitize(string value)
    {
        var sanitized = value;
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            sanitized = sanitized.Replace(invalid, '_');
        }

        return sanitized.Replace(' ', '_');
    }
}
