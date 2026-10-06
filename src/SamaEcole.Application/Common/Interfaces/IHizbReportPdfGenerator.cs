using SamaEcole.Application.Internat.Queries.GetStudentHizbReport;

namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Rend le bulletin coranique d'un élève (suivi de mémorisation Hizb par Hizb) en PDF A4, sur le même contrat que
/// <see cref="IExeatCertificatePdfGenerator"/> : mise en page en Infrastructure (QuestPDF, police arabe embarquée),
/// donnée en Application, génération déterministe et synchrone. <paramref name="logo"/> est <c>null</c> quand
/// l'établissement n'en a pas (ou qu'il est injoignable) : le bulletin se génère alors sans logo.
/// </summary>
public interface IHizbReportPdfGenerator
{
    byte[] Generate(HizbReportDto report, byte[]? logo);
}
