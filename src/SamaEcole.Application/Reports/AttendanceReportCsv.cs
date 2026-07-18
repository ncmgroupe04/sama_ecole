using System.Globalization;
using System.Text;

namespace SamaEcole.Application.Reports;

/// <summary>
/// Sérialise un rapport d'assiduité en CSV (ticket JGK-R03). Composant PUR (aucune dépendance de
/// provider) — donc testable en test unitaire, sans base ni QuestPDF.
///
/// Choix assumés pour une ouverture propre dans un tableur francophone :
///   * séparateur point-virgule (Excel FR interprète la virgule comme décimale, pas comme séparateur) ;
///   * BOM UTF-8 en tête, pour que les accents s'affichent correctement à l'ouverture ;
///   * taux exprimé en pourcentage à décimale virgule (ex. « 66,67 »), lisible ET numérique en FR.
/// </summary>
public static class AttendanceReportCsv
{
    private const char Separator = ';';

    public static byte[] Build(AttendanceReportExportModel model)
    {
        var sb = new StringBuilder();

        // En-tête de contexte (le fichier se lit seul, hors de tout écran).
        sb.Append("Rapport d'assiduité").Append('\n');
        Line(sb, "Période", $"{Format(model.StartDate)} au {Format(model.EndDate)}");
        Line(sb, "Classe", model.ClassName ?? "Toutes les classes");
        Line(sb, "Taux moyen", model.AverageAttendanceRate is { } avg ? FormatPercent(avg) : "—");
        sb.Append('\n');

        // Ligne d'en-tête des colonnes.
        sb.Append(string.Join(Separator, new[]
        {
            "Matricule", "Nom", "Classe", "Appels", "Présents", "Retards",
            "Minutes de retard", "Absences justifiées", "Absences non justifiées", "Taux de présence (%)"
        })).Append('\n');

        foreach (var s in model.Students)
        {
            sb.Append(string.Join(Separator, new[]
            {
                Escape(s.Matricule),
                Escape(s.FullName),
                Escape(s.ClassroomName),
                s.TotalCalls.ToString(CultureInfo.InvariantCulture),
                s.Present.ToString(CultureInfo.InvariantCulture),
                s.Late.ToString(CultureInfo.InvariantCulture),
                s.TotalLateMinutes.ToString(CultureInfo.InvariantCulture),
                s.JustifiedAbsences.ToString(CultureInfo.InvariantCulture),
                s.UnjustifiedAbsences.ToString(CultureInfo.InvariantCulture),
                FormatPercent(s.AttendanceRate)
            })).Append('\n');
        }

        // BOM UTF-8 explicite (GetBytes ne le préfixe pas) : indispensable pour les accents dans Excel FR.
        var text = sb.ToString();
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text)];
    }

    private static void Line(StringBuilder sb, string label, string value) =>
        sb.Append(Escape(label)).Append(Separator).Append(Escape(value)).Append('\n');

    /// <summary>Taux 0..1 → pourcentage à décimale virgule, sans décimales inutiles (« 100 », « 66,67 »).</summary>
    private static string FormatPercent(decimal rate) =>
        Math.Round(rate * 100m, 2, MidpointRounding.AwayFromZero)
            .ToString("0.##", CultureInfo.InvariantCulture)
            .Replace('.', ',');

    private static string Format(DateOnly date) => date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    /// <summary>
    /// Échappement CSV : un champ contenant le séparateur, un guillemet ou un saut de ligne est encadré
    /// de guillemets, les guillemets internes étant doublés. Sans cela, un nom composé casserait les colonnes.
    /// </summary>
    private static string Escape(string field)
    {
        if (field.IndexOf(Separator) < 0 && !field.Contains('"') && !field.Contains('\n') && !field.Contains('\r'))
        {
            return field;
        }

        return $"\"{field.Replace("\"", "\"\"")}\"";
    }
}
