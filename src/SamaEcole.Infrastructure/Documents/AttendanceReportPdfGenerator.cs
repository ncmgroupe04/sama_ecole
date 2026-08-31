using Microsoft.Extensions.Logging;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Reports;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Implémentation QuestPDF de <see cref="IAttendanceReportPdfGenerator"/> (ticket JGK-R03). Passe par
/// <see cref="PdfRenderGuard"/> : un rendu vide LÈVE une exception journalisée au lieu de renvoyer un
/// tableau de 0 octet.
/// </summary>
public class AttendanceReportPdfGenerator(ILogger<AttendanceReportPdfGenerator>? logger = null) : IAttendanceReportPdfGenerator
{
    static AttendanceReportPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(AttendanceReportExportModel model) =>
        PdfRenderGuard.Render(
            logger,
            $"rapport de présences (école {model.SchoolName}, {model.StartDate:yyyy-MM-dd} → {model.EndDate:yyyy-MM-dd})",
            () => new AttendanceReportDocument(model).GeneratePdf());
}
