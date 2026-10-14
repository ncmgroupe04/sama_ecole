using SamaEcole.Application.Boarding.Documents.MonthlyAttendanceSummary;

namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Rend le récapitulatif mensuel du pointage d'une session (A4 portrait), sur le même contrat que
/// <see cref="IDormitoryRegisterPdfGenerator"/>. <paramref name="logo"/> est <c>null</c> quand l'établissement n'en a pas.
/// </summary>
public interface IMonthlyAttendanceSummaryPdfGenerator
{
    byte[] Generate(MonthlyAttendanceSummaryReportDto report, byte[]? logo);
}
