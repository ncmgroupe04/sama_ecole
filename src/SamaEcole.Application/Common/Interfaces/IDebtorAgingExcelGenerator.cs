using SamaEcole.Application.Finance.Queries.GetDebtorAgingReport;

namespace SamaEcole.Application.Common.Interfaces;

/// <summary>Export comptable des débiteurs (Étape 5) — même contrat que IRevenueReportExcelGenerator.</summary>
public interface IDebtorAgingExcelGenerator
{
    byte[] Generate(DebtorAgingReportDto report, string schoolName);
}
