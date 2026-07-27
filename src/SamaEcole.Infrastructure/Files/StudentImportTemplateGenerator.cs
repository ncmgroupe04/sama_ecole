using ClosedXML.Excel;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Infrastructure.Files;

/// <summary>
/// Modèle Excel d'import d'élèves : en-tête + une ligne d'exemple sur la feuille « Élèves », plus une
/// feuille « Classes » qui sert À LA FOIS de référence visible pour l'utilisateur et de SOURCE de la
/// liste déroulante de la colonne Classe — l'école voit ses propres classes, jamais une liste inventée.
/// </summary>
public class StudentImportTemplateGenerator : IStudentImportTemplateGenerator
{
    private static readonly string[] Headers =
    [
        "Nom complet", "Date de naissance (jj/mm/aaaa)", "Lieu de naissance", "Genre (M/F)",
        "Classe", "Nom du tuteur", "Téléphone du tuteur", "E-mail du tuteur", "Adresse"
    ];

    public byte[] Generate(IReadOnlyList<string> classroomNames)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Élèves");

        for (var col = 0; col < Headers.Length; col++)
        {
            var header = sheet.Cell(1, col + 1);
            header.Value = Headers[col];
            header.Style.Font.Bold = true;
            header.Style.Fill.BackgroundColor = XLColor.FromHtml("#EEF2FF");
        }

        // Ligne d'exemple : montre le format attendu (date, genre) sans être une donnée à importer telle
        // quelle — l'utilisateur la remplace par ses propres élèves avant l'envoi.
        sheet.Cell(2, 1).Value = "Awa Ndiaye";
        sheet.Cell(2, 2).Value = "12/03/2015";
        sheet.Cell(2, 3).Value = "Dakar";
        sheet.Cell(2, 4).Value = "F";
        sheet.Cell(2, 5).Value = classroomNames.Count > 0 ? classroomNames[0] : "CM2 A";
        sheet.Cell(2, 6).Value = "Moussa Ndiaye";
        sheet.Cell(2, 7).Value = "+221771234567";
        sheet.Cell(2, 8).Value = "moussa.ndiaye@example.com";
        sheet.Cell(2, 9).Value = "Cité Keur Gorgui, Dakar";

        sheet.Columns(1, Headers.Length).AdjustToContents();

        // Liste déroulante Genre : littéraux directs, pas besoin d'une plage source.
        sheet.Range("D2:D1000").SetDataValidation().List("M,F");

        if (classroomNames.Count > 0)
        {
            // Feuille de référence : les classes RÉELLES de l'école, visibles ET utilisées comme source
            // de la liste déroulante Classe — évite la faute de frappe, la source d'erreur la plus
            // fréquente d'un import de masse.
            var classSheet = workbook.Worksheets.Add("Classes");
            classSheet.Cell(1, 1).Value = "Classes de votre établissement";
            classSheet.Cell(1, 1).Style.Font.Bold = true;

            for (var i = 0; i < classroomNames.Count; i++)
            {
                classSheet.Cell(i + 2, 1).Value = classroomNames[i];
            }
            classSheet.Column(1).AdjustToContents();

            var classRange = classSheet.Range(2, 1, classroomNames.Count + 1, 1);
            sheet.Range("E2:E1000").SetDataValidation().List(classRange);
        }

        sheet.SheetView.FreezeRows(1);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
