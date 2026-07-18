using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Grades;
using FluentValidation.Results;

namespace SamaEcole.Infrastructure.Files;

/// <summary>
/// Lit un fichier d'import de notes CSV ou Excel (deux colonnes : matricule, note). Aucune validation
/// MÉTIER ici (barème, appartenance à la classe...) — seulement la structure du fichier : c'est
/// ImportGradesCommandHandler qui résout chaque ligne contre l'état en base.
///
/// Une ligne d'en-tête (« Matricule;Note ») est acceptée mais jamais EXIGÉE : si la 2e colonne de la
/// première ligne ne ressemble pas à une note, elle est traitée comme en-tête et ignorée — l'enseignant
/// n'a pas à se souvenir d'une règle de format supplémentaire.
/// </summary>
public class GradeImportFileParser : IGradeImportFileParser
{
    public IReadOnlyList<GradeImportFileRow> Parse(byte[] fileContent, string fileName)
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
                new ValidationFailure("File", "Le fichier ne contient aucune ligne de données.")
            ]);
        }

        return rows;
    }

    private static List<GradeImportFileRow> ParseCsv(byte[] fileContent)
    {
        // BOM UTF-8 éventuel (Excel FR en écrit un systématiquement, voir AttendanceReportCsv) : à
        // retirer avant lecture, sinon il s'accroche au premier champ de la première ligne.
        var text = Encoding.UTF8.GetString(fileContent).TrimStart((char)0xFEFF);

        // Découpage qui PRÉSERVE le numéro de ligne réel (y compris les lignes vides), pour que les
        // messages d'erreur pointent la même ligne que ce que l'enseignant voit dans son fichier.
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        // Le point-virgule est la convention francophone de cette app (AttendanceReportCsv : la
        // virgule sert de séparateur décimal, pas de séparateur de colonnes, dans Excel FR). On ne
        // bascule sur la virgule que si aucune ligne non vide n'en contient.
        var firstNonEmpty = lines.Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
        var delimiter = firstNonEmpty is not null && firstNonEmpty.Contains(';') ? ';' : ',';

        var rows = new List<GradeImportFileRow>();
        var isFirstDataLine = true;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0) continue;

            // Un seul split, sur la PREMIÈRE occurrence : une note en notation FR ("15,5") avec le
            // séparateur virgule ne doit jamais être coupée en trois colonnes.
            var separatorIndex = line.IndexOf(delimiter);
            if (separatorIndex < 0) continue;

            var matricule = Unquote(line[..separatorIndex]);
            var value = Unquote(line[(separatorIndex + 1)..]);

            // Ligne d'en-tête auto-détectée sur la première ligne de DONNÉES (pas la première ligne du
            // fichier au sens brut : une ligne vide en tête ne doit pas fausser la détection).
            if (isFirstDataLine)
            {
                isFirstDataLine = false;
                if (!LooksNumeric(value)) continue;
            }

            rows.Add(new GradeImportFileRow(i + 1, matricule, value));
        }

        return rows;
    }

    private static List<GradeImportFileRow> ParseExcel(byte[] fileContent)
    {
        try
        {
            using var stream = new MemoryStream(fileContent);
            using var workbook = new XLWorkbook(stream);
            var worksheet = workbook.Worksheets.First();

            var rows = new List<GradeImportFileRow>();
            var isFirstUsedRow = true;

            foreach (var xlRow in worksheet.RowsUsed())
            {
                var matricule = xlRow.Cell(1).GetString().Trim();
                var value = xlRow.Cell(2).GetString().Trim();

                if (matricule.Length == 0 && value.Length == 0) continue;

                if (isFirstUsedRow)
                {
                    isFirstUsedRow = false;
                    if (!LooksNumeric(value)) continue;
                }

                rows.Add(new GradeImportFileRow(xlRow.RowNumber(), matricule, value));
            }

            return rows;
        }
        catch (Exception)
        {
            throw new ValidationException([
                new ValidationFailure("File", "Le fichier Excel n'a pas pu être lu : vérifiez qu'il n'est pas corrompu ou protégé par mot de passe.")
            ]);
        }
    }

    private static bool LooksNumeric(string value)
    {
        var normalized = value.IndexOf(',') >= 0 && value.IndexOf('.') < 0 ? value.Replace(',', '.') : value;
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out _);
    }

    private static string Unquote(string field)
    {
        field = field.Trim();
        return field.Length >= 2 && field[0] == '"' && field[^1] == '"'
            ? field[1..^1].Replace("\"\"", "\"")
            : field;
    }
}
