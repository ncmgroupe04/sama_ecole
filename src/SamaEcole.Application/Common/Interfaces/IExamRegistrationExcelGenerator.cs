using SamaEcole.Application.Exams.Queries.GetExamRegistrationExport;

namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Export Excel (.xlsx) du relevé d'inscription d'une session d'examen. Implémentation côté
/// Infrastructure (ClosedXML), même convention qu'<see cref="IRevenueReportExcelGenerator"/> :
/// Application ne référence aucune bibliothèque tierce.
/// </summary>
public interface IExamRegistrationExcelGenerator
{
    byte[] Generate(IReadOnlyList<ExamRegistrationRow> rows, string schoolName, string sessionLabel);
}
