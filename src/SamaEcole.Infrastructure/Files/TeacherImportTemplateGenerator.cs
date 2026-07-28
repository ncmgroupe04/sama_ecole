using ClosedXML.Excel;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Infrastructure.Files;

/// <summary>
/// Modèle Excel d'import d'enseignants : en-tête + une ligne d'exemple sur la feuille « Enseignants »,
/// plus une feuille « Matières » qui liste les VRAIES matières de l'école courante au format
/// « Nom (Niveau) » — un même nom de matière pouvant exister à plusieurs niveaux (voir
/// Domain.Entities.Subject), cette feuille sert de référence à copier-coller dans la colonne Matières,
/// plutôt qu'une liste déroulante (qui ne gère pas nativement une saisie multi-valeurs).
/// </summary>
public class TeacherImportTemplateGenerator : ITeacherImportTemplateGenerator
{
    private static readonly string[] Headers =
    [
        "Nom complet", "Email", "Téléphone", "Date de naissance (jj/mm/aaaa)", "Lieu de naissance",
        "Matières (séparées par une virgule)"
    ];

    public byte[] Generate(IReadOnlyList<(string Name, string Level)> subjects)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Enseignants");

        for (var col = 0; col < Headers.Length; col++)
        {
            var header = sheet.Cell(1, col + 1);
            header.Value = Headers[col];
            header.Style.Font.Bold = true;
            header.Style.Fill.BackgroundColor = XLColor.FromHtml("#EEF2FF");
        }

        // Ligne d'exemple : montre le format attendu (date, matières multiples) sans être une donnée à
        // importer telle quelle — l'utilisateur la remplace par ses propres enseignants avant l'envoi.
        var exampleSubjects = subjects.Count > 0
            ? string.Join(", ", subjects.Take(2).Select(s => $"{s.Name} ({s.Level})"))
            : "Mathématiques (Primaire)";

        sheet.Cell(2, 1).Value = "Moussa Fall";
        sheet.Cell(2, 2).Value = "moussa.fall@example.com";
        sheet.Cell(2, 3).Value = "+221771234567";
        sheet.Cell(2, 4).Value = "15/05/1985";
        sheet.Cell(2, 5).Value = "Dakar";
        sheet.Cell(2, 6).Value = exampleSubjects;

        sheet.Columns(1, Headers.Length).AdjustToContents();

        if (subjects.Count > 0)
        {
            // Feuille de référence : les matières RÉELLES de l'école, au format exact à reprendre dans
            // la colonne Matières — un nom partagé par plusieurs niveaux (« Mathématiques » au Primaire
            // ET au Collège) est ainsi désambiguïsé sans que l'utilisateur ait à le deviner.
            var subjectSheet = workbook.Worksheets.Add("Matières");
            subjectSheet.Cell(1, 1).Value = "Matières de votre établissement (à copier-coller dans la colonne Matières)";
            subjectSheet.Cell(1, 1).Style.Font.Bold = true;

            for (var i = 0; i < subjects.Count; i++)
            {
                subjectSheet.Cell(i + 2, 1).Value = $"{subjects[i].Name} ({subjects[i].Level})";
            }
            subjectSheet.Column(1).AdjustToContents();
        }

        sheet.SheetView.FreezeRows(1);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
