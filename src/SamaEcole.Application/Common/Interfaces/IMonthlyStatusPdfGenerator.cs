using SamaEcole.Application.Finance.Queries.GetMonthlyStatus;

namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// PDF de la Situation des mensualités (même convention que IInventoryReportPdfGenerator : QuestPDF côté
/// Infrastructure, générateur pur). <paramref name="status"/> limite le document à UNE catégorie
/// (« paid », « partial », « unpaid ») ; null = les trois.
/// </summary>
public interface IMonthlyStatusPdfGenerator
{
    byte[] Generate(MonthlyStatusDto report, string schoolName, string? classroomName, string? status);
}
