using SamaEcole.Application.Reports;

namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Rend un rapport d'assiduité en PDF (ticket JGK-R03). Même convention que les autres générateurs PDF
/// (IReportCardPdfGenerator…) : moteur QuestPDF côté Infrastructure, générateur pur et synchrone (les
/// données sont déjà résolues dans <see cref="AttendanceReportExportModel"/>).
/// </summary>
public interface IAttendanceReportPdfGenerator
{
    byte[] Generate(AttendanceReportExportModel model);
}
