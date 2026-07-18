using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Reports;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Implémentation QuestPDF de <see cref="IAttendanceReportPdfGenerator"/> (ticket JGK-R03). Sans état :
/// un singleton suffit, comme les autres générateurs PDF.
/// </summary>
public class AttendanceReportPdfGenerator : IAttendanceReportPdfGenerator
{
    static AttendanceReportPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(AttendanceReportExportModel model)
        => new AttendanceReportDocument(model).GeneratePdf();
}
