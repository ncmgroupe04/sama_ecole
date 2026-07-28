using SamaEcole.Application.Finance.Queries.GetDailyClosingReportPdf;

namespace SamaEcole.Application.Common.Interfaces;

public interface IDailyClosingReportPdfGenerator
{
    byte[] Generate(DailyClosingReportDto report, byte[]? schoolLogo);
}
