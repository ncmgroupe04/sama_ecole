using System.Text;
using ClosedXML.Excel;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Students;
using FluentValidation.Results;

namespace SamaEcole.Infrastructure.Files;

/// <summary>
/// Lit un fichier d'import d'élèves CSV ou Excel (neuf colonnes fixes, voir StudentImportFileRow).
/// Aucune validation MÉTIER ici (format de date, existence de la classe...) — seulement la structure du
/// fichier : c'est ImportStudentsCommandHandler qui résout chaque ligne.
///
/// La première ligne est TOUJOURS un en-tête, jamais des données (voir IStudentImportFileParser) : pas
/// d'heuristique de détection comme GradeImportFileParser, le modèle fourni en comporte toujours un.
/// </summary>
public class StudentImportFileParser : IStudentImportFileParser
{
    /// <summary>Nombre de colonnes attendu (voir StudentImportFileRow) — les colonnes manquantes en fin
    /// de ligne (tuteur non renseigné) sont complétées par des chaînes vides, jamais une erreur.</summary>
    private const int ColumnCount = 9;

    public IReadOnlyList<StudentImportFileRow> Parse(byte[] fileContent, string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();

        var rows = extension switch
        {
            ".csv" => ParseCsv(fileContent),
            ".xlsx" or ".xls" => ParseExcel(fileContent),
            _ => throw new ValidationException([
                new ValidationFailure("File", "Format de fichier non pris en charge : utilisez un fichier .csv ou .xlsx.")
            ])
        };

        if (rows.Count == 0)
        {
            throw new ValidationException([
                new ValidationFailure("File", "Le fichier ne contient aucune ligne d'élève (au-delà de l'en-tête).")
            ]);
        }

        return rows;
    }

    private static List<StudentImportFileRow> ParseCsv(byte[] fileContent)
    {
        // BOM UTF-8 éventuel (Excel FR en écrit un systématiquement) : à retirer avant lecture.
        var text = Encoding.UTF8.GetString(fileContent).TrimStart((char)0xFEFF);
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        var firstNonEmpty = lines.Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
        var delimiter = firstNonEmpty is not null && firstNonEmpty.Contains(';') ? ';' : ',';

        var rows = new List<StudentImportFileRow>();
        var headerSkipped = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Trim().Length == 0) continue;

            // La première ligne NON VIDE est l'en-tête, quelle que soit sa position brute dans le
            // fichier (une ligne vide en tête du fichier ne doit pas décaler la détection).
            if (!headerSkipped)
            {
                headerSkipped = true;
                continue;
            }

            var fields = SplitCsvLine(line, delimiter);
            while (fields.Count < ColumnCount) fields.Add("");

            rows.Add(new StudentImportFileRow(
                i + 1, fields[0], fields[1], fields[2], fields[3], fields[4], fields[5], fields[6], fields[7], fields[8]));
        }

        return rows;
    }

    /// <summary>Découpage CSV respectant les champs entre guillemets (pouvant contenir le délimiteur ou des guillemets échappés « "" »).</summary>
    private static List<string> SplitCsvLine(string line, char delimiter)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (c == '"')
            {
                inQuotes = true;
            }
            else if (c == delimiter)
            {
                fields.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        fields.Add(current.ToString().Trim());
        return fields;
    }

    private static List<StudentImportFileRow> ParseExcel(byte[] fileContent)
    {
        try
        {
            using var stream = new MemoryStream(fileContent);
            using var workbook = new XLWorkbook(stream);
            var worksheet = workbook.Worksheets.First();

            var rows = new List<StudentImportFileRow>();
            var headerSkipped = false;

            foreach (var xlRow in worksheet.RowsUsed())
            {
                var fields = new string[ColumnCount];
                for (var col = 0; col < ColumnCount; col++)
                {
                    fields[col] = CellText(xlRow.Cell(col + 1));
                }

                if (fields.All(f => f.Length == 0)) continue;

                if (!headerSkipped)
                {
                    headerSkipped = true;
                    continue;
                }

                rows.Add(new StudentImportFileRow(
                    xlRow.RowNumber(), fields[0], fields[1], fields[2], fields[3], fields[4], fields[5], fields[6], fields[7], fields[8]));
            }

            return rows;
        }
        catch (Exception ex) when (ex is not ValidationException)
        {
            throw new ValidationException([
                new ValidationFailure("File", "Le fichier Excel n'a pas pu être lu : vérifiez qu'il n'est pas corrompu ou protégé par mot de passe.")
            ]);
        }
    }

    /// <summary>
    /// Une cellule de date de naissance peut être un VRAI type date Excel (widget calendrier utilisé à
    /// la saisie) ou du texte simple ("12/03/2015") : dans les deux cas, on ramène à la même
    /// représentation texte « jj/mm/aaaa » — la validation de format elle-même reste dans le Handler,
    /// pas ici (le parseur reste structurel, comme GradeImportFileParser).
    /// </summary>
    private static string CellText(IXLCell cell) =>
        cell.DataType == XLDataType.DateTime
            ? cell.GetDateTime().ToString("dd/MM/yyyy")
            : cell.GetString().Trim();
}
