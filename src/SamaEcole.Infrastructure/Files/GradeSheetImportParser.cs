using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using FluentValidation.Results;

namespace SamaEcole.Infrastructure.Files;

/// <summary>
/// Reconnaît les colonnes par le TEXTE de leur en-tête, jamais par leur position : la ligne d'en-tête
/// est donc OBLIGATOIRE (contrairement à l'ancien import à 2 colonnes) — c'est elle qui garantit
/// qu'aucune note n'atterrit sur la mauvaise épreuve si l'enseignant a réordonné les colonnes.
/// </summary>
public class GradeSheetImportParser : IGradeSheetImportParser
{
    private enum ColumnPurpose { Matricule, Devoir1, Devoir2, Composition }

    private const int MaxHeaderSearchRows = 5;

    public IReadOnlyList<GradeSheetRow> Parse(byte[] fileContent, string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (extension is not (".xlsx" or ".xls"))
        {
            throw new ValidationException([
                new ValidationFailure("File", "Format de fichier non pris en charge : utilisez un fichier .xlsx ou .xls.")
            ]);
        }

        IXLWorksheet worksheet;
        try
        {
            using var stream = new MemoryStream(fileContent);
            using var workbook = new XLWorkbook(stream);
            worksheet = workbook.Worksheets.First();

            return ParseWorksheet(worksheet);
        }
        catch (ValidationException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new ValidationException([
                new ValidationFailure("File", "Le fichier Excel n'a pas pu être lu : vérifiez qu'il n'est pas corrompu ou protégé par mot de passe.")
            ]);
        }
    }

    private static List<GradeSheetRow> ParseWorksheet(IXLWorksheet worksheet)
    {
        var usedRows = worksheet.RowsUsed().ToList();
        if (usedRows.Count == 0)
        {
            throw new ValidationException([
                new ValidationFailure("File", "Le fichier ne contient aucune ligne de données.")
            ]);
        }

        var (headerRowNumber, columns) = ResolveColumns(usedRows);

        var rows = new List<GradeSheetRow>();
        foreach (var xlRow in usedRows.Where(r => r.RowNumber() > headerRowNumber))
        {
            var matricule = CellText(xlRow, columns, ColumnPurpose.Matricule).Trim();
            var devoir1 = CellText(xlRow, columns, ColumnPurpose.Devoir1).Trim();
            var devoir2 = CellText(xlRow, columns, ColumnPurpose.Devoir2).Trim();
            var composition = CellText(xlRow, columns, ColumnPurpose.Composition).Trim();

            // Ligne entièrement vide (espacement dans le fichier) : ignorée, pas une ligne en erreur.
            if (matricule.Length == 0 && devoir1.Length == 0 && devoir2.Length == 0 && composition.Length == 0)
            {
                continue;
            }

            rows.Add(new GradeSheetRow(xlRow.RowNumber(), matricule, devoir1, devoir2, composition));
        }

        return rows;
    }

    private static string CellText(IXLRow row, Dictionary<ColumnPurpose, int> columns, ColumnPurpose purpose) =>
        columns.TryGetValue(purpose, out var column) ? row.Cell(column).GetString() : "";

    /// <summary>
    /// Cherche la ligne d'en-tête dans les <see cref="MaxHeaderSearchRows"/> premières lignes utilisées
    /// (une éventuelle ligne de consigne au-dessus n'y fait donc pas obstacle), puis mappe chaque colonne
    /// reconnue par le TEXTE de son en-tête. Une colonne « Nom &amp; Prénom » ou toute autre colonne non
    /// reconnue est simplement ignorée à la lecture — seule sa présence sur la feuille exportée compte.
    /// </summary>
    private static (int HeaderRowNumber, Dictionary<ColumnPurpose, int> Columns) ResolveColumns(List<IXLRow> usedRows)
    {
        foreach (var candidateRow in usedRows.Take(MaxHeaderSearchRows))
        {
            var columns = new Dictionary<ColumnPurpose, int>();
            var duplicates = new HashSet<ColumnPurpose>();

            foreach (var cell in candidateRow.CellsUsed())
            {
                var purpose = PurposeFor(NormalizeHeader(cell.GetString()));
                if (purpose is null) continue;

                if (!columns.TryAdd(purpose.Value, cell.Address.ColumnNumber))
                {
                    duplicates.Add(purpose.Value);
                }
            }

            if (!columns.ContainsKey(ColumnPurpose.Matricule))
            {
                continue;
            }

            if (duplicates.Count > 0)
            {
                throw new ValidationException([
                    new ValidationFailure("File",
                        $"Plusieurs colonnes correspondent à la même épreuve ({string.Join(", ", duplicates)}) : le fichier ne peut pas être importé.")
                ]);
            }

            if (!columns.Keys.Any(p => p is ColumnPurpose.Devoir1 or ColumnPurpose.Devoir2 or ColumnPurpose.Composition))
            {
                throw new ValidationException([
                    new ValidationFailure("File",
                        "Aucune colonne de note trouvée (Devoir 1, Devoir 2 ou Composition) : vérifiez les en-têtes du fichier.")
                ]);
            }

            return (candidateRow.RowNumber(), columns);
        }

        throw new ValidationException([
            new ValidationFailure("File",
                "Aucune colonne « Matricule » trouvée : la ligne d'en-tête est obligatoire (utilisez la feuille exportée depuis l'application).")
        ]);
    }

    private static ColumnPurpose? PurposeFor(string normalizedHeader) => normalizedHeader switch
    {
        "MATRICULE" => ColumnPurpose.Matricule,
        "DEVOIR1" => ColumnPurpose.Devoir1,
        "DEVOIR2" => ColumnPurpose.Devoir2,
        "COMPOSITION" => ColumnPurpose.Composition,
        _ => null
    };

    /// <summary>
    /// « Devoir 1 (/20) » → « DEVOIR1 » : tout ce qui suit une parenthèse ouvrante est ignoré (le
    /// barème affiché n'est qu'indicatif), les accents et espaces retirés — pour que « Devoir 1 »,
    /// « DEVOIR 1 » et « Devoir1 » désignent tous la même colonne.
    /// </summary>
    private static string NormalizeHeader(string rawHeader)
    {
        var beforeParenthesis = rawHeader.Split('(')[0];
        var upper = beforeParenthesis.Trim().ToUpperInvariant();
        var withoutDiacritics = RemoveDiacritics(upper);

        return new string(withoutDiacritics.Where(char.IsLetterOrDigit).ToArray());
    }

    private static string RemoveDiacritics(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder();

        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(ch);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
