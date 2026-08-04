using SamaEcole.Application.Teachers;

namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Rend la liste des enseignants en PDF (GET /teachers/export/pdf). Même convention que
/// <see cref="IStudentsExportPdfGenerator"/> : moteur QuestPDF côté Infrastructure, générateur pur et
/// synchrone (les données sont déjà résolues dans <see cref="TeachersExportModel"/>).
/// </summary>
public interface ITeachersExportPdfGenerator
{
    byte[] Generate(TeachersExportModel model);
}
