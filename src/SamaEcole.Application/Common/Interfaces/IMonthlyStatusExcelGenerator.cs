using SamaEcole.Application.Finance.Queries.GetMonthlyStatus;

namespace SamaEcole.Application.Common.Interfaces;

/// <summary>Export Excel de la Situation des mensualités — même contrat que IDebtorAgingExcelGenerator.</summary>
public interface IMonthlyStatusExcelGenerator
{
    byte[] Generate(MonthlyStatusDto report, string schoolName, string? classroomName);
}
