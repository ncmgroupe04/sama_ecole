using ClosedXML.Excel;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Infrastructure.Files;

/// <summary>
/// Feuille de notes Excel : une ligne de consigne, une ligne d'en-tête, une ligne par élève —
/// Matricule et Nom &amp; Prénom pré-remplis et VERROUILLÉS (protection de feuille sans mot de passe :
/// un garde-fou anti-erreur de frappe, pas un secret), seules les trois colonnes de notes se modifient.
/// L'ordre des colonnes n'a aucune importance à la réimportation (GradeSheetImportParser mappe par nom
/// d'en-tête) — il est fixe ici uniquement pour la lisibilité du fichier généré.
/// </summary>
public class GradeSheetExcelGenerator : IGradeSheetExcelGenerator
{
    private const int MatriculeColumn = 1;
    private const int FullNameColumn = 2;
    private const int Devoir1Column = 3;
    private const int Devoir2Column = 4;
    private const int CompositionColumn = 5;
    private const int HeaderRow = 2;
    private const int FirstDataRow = 3;

    public byte[] Generate(IReadOnlyList<GradeSheetStudentRow> rows, decimal gradingScale)
    {
        // « 40 » et non « 40,00 » : le barème vient d'une colonne numeric(5,2), et son affichage brut
        // ferait lire « DEVOIR 1 (/40,00) » à l'enseignant.
        var scale = gradingScale.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',');

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Notes");

        sheet.Cell(1, 1).Value =
            $"Saisir des notes entre 0 et {scale}. Laisser vide en cas d'absence. " +
            "Ne modifiez pas les colonnes Matricule et Nom & Prénom.";
        sheet.Range(1, 1, 1, CompositionColumn).Merge();
        sheet.Cell(1, 1).Style.Font.Italic = true;
        sheet.Cell(1, 1).Style.Font.FontColor = XLColor.FromHtml("#475569");
        sheet.Row(1).Height = 30;
        sheet.Cell(1, 1).Style.Alignment.WrapText = true;
        sheet.Cell(1, 1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

        var headers = new[]
        {
            "MATRICULE", "NOM & PRÉNOM", $"DEVOIR 1 (/{scale})", $"DEVOIR 2 (/{scale})", $"COMPOSITION (/{scale})"
        };
        for (var col = 0; col < headers.Length; col++)
        {
            var header = sheet.Cell(HeaderRow, col + 1);
            header.Value = headers[col];
            header.Style.Font.Bold = true;
            header.Style.Fill.BackgroundColor = XLColor.FromHtml("#EEF2FF");
        }

        for (var i = 0; i < rows.Count; i++)
        {
            var row = FirstDataRow + i;
            var student = rows[i];

            sheet.Cell(row, MatriculeColumn).Value = student.Matricule;
            sheet.Cell(row, FullNameColumn).Value = student.FullName;
            if (student.Devoir1 is { } d1) sheet.Cell(row, Devoir1Column).Value = d1;
            if (student.Devoir2 is { } d2) sheet.Cell(row, Devoir2Column).Value = d2;
            if (student.Composition is { } c) sheet.Cell(row, CompositionColumn).Value = c;
        }

        var lastRow = Math.Max(FirstDataRow, FirstDataRow + rows.Count - 1);

        // Validation Excel : garde-fou visuel côté enseignant, la borne réelle reste appliquée
        // côté serveur à l'import (GradingScaleGuard) — jamais la seule ligne de défense.
        foreach (var col in new[] { Devoir1Column, Devoir2Column, CompositionColumn })
        {
            // ClosedXML exprime ses bornes de validation en double : la conversion est explicite plutôt
            // que subie, et sans perte utile ici (un barème est un petit nombre à deux décimales).
            sheet.Range(FirstDataRow, col, lastRow, col)
                .CreateDataValidation().Decimal.Between(0, (double)gradingScale);
        }

        sheet.Columns(1, headers.Length).AdjustToContents();
        sheet.SheetView.FreezeRows(HeaderRow);

        // Verrouillage : la feuille entière part "locked" par défaut (comportement Excel standard), on
        // déverrouille explicitement les seules cellules de notes avant de protéger la feuille — ce qui
        // rend Matricule et Nom & Prénom non éditables, sans mot de passe.
        foreach (var col in new[] { Devoir1Column, Devoir2Column, CompositionColumn })
        {
            sheet.Range(FirstDataRow, col, lastRow, col).Style.Protection.SetLocked(false);
        }
        sheet.Protect();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
