using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Students;
using Microsoft.Extensions.Logging;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Implémentation QuestPDF de <see cref="IStudentsExportPdfGenerator"/>. Sans état : un singleton
/// suffit, comme les autres générateurs PDF.
///
/// Ce document n'a NI logo NI cachet (pièce de travail interne) : aucun repli « sans logo » n'a donc
/// de sens ici. En revanche, comme <see cref="PaymentReceiptPdfGenerator"/> et
/// <see cref="DailyClosingReportPdfGenerator"/>, on refuse de renvoyer un tableau d'octets vide sans
/// bruit : si QuestPDF produisait un document de 0 octet, on LÈVE — le middleware d'exception en fait
/// un 500 normalisé journalisé (cause visible dans les logs), jamais un 200 au corps vide qui bloque
/// l'aperçu client sur « document vide (0 octet) » sans laisser de trace.
/// </summary>
public class StudentsExportPdfGenerator(ILogger<StudentsExportPdfGenerator> logger) : IStudentsExportPdfGenerator
{
    static StudentsExportPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(StudentsExportModel model)
    {
        byte[] pdf;
        try
        {
            pdf = new StudentsExportDocument(model).GeneratePdf();
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Échec de génération de l'export PDF des élèves (école {SchoolName}, classe {ClassName}, {Count} élève(s)).",
                model.SchoolName, model.ClassName ?? "toutes", model.Students.Count);
            throw;
        }

        if (pdf is null || pdf.Length == 0)
        {
            throw new InvalidOperationException(
                $"L'export PDF des élèves généré est vide (école {model.SchoolName}, {model.Students.Count} élève(s)).");
        }

        return pdf;
    }
}
