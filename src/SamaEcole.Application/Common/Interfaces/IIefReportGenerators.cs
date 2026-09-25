using SamaEcole.Application.Institutional;

namespace SamaEcole.Application.Common.Interfaces;

/// <summary>Rapport de rentrée IEF en PDF (A4 paysage) — Évolution N°7.</summary>
public interface IIefReportPdfGenerator
{
    byte[] Generate(IefReportDto report);
}

/// <summary>Rapport de rentrée IEF en classeur .xlsx, pour consolidation à l'Inspection — Évolution N°7.</summary>
public interface IIefReportExcelGenerator
{
    byte[] Generate(IefReportDto report);
}
