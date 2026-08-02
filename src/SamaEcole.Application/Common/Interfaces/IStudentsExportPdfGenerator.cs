using SamaEcole.Application.Students;

namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Rend la liste des élèves en PDF (GET /students/export/pdf). Même convention que les autres
/// générateurs PDF (IAttendanceReportPdfGenerator…) : moteur QuestPDF côté Infrastructure, générateur
/// pur et synchrone (les données sont déjà résolues dans <see cref="StudentsExportModel"/>).
/// </summary>
public interface IStudentsExportPdfGenerator
{
    byte[] Generate(StudentsExportModel model);
}
