using SamaEcole.Application.Finance.Queries.GetPayslip;

namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Rend le Bulletin de Paie (A4) en PDF — détail des cotisations sociales sénégalaises (IPRES
/// employé/employeur, CSS, VRS/CFCE, BRS) et du net à payer. Comme les autres documents officiels, la
/// mise en page est une préoccupation d'infrastructure (QuestPDF) ; la donnée vient de l'Application.
/// </summary>
public interface IPayslipPdfGenerator
{
    byte[] Generate(PayslipDto payslip, byte[]? logo);
}
