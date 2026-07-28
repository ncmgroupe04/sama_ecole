using SamaEcole.Application.Finance.Queries.GetTaxDeclaration;

namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Rend la Déclaration Fiscale mensuelle (A4) en PDF — état synthétique des charges sociales
/// (IPRES, CSS, VRS, BRS) et de la TVA (collectée/déductible/nette) de la période. Comme les autres
/// documents officiels, la mise en page est une préoccupation d'infrastructure (QuestPDF) ; la donnée
/// vient de l'Application.
/// </summary>
public interface ITaxDeclarationPdfGenerator
{
    byte[] Generate(TaxDeclarationDto declaration, byte[]? logo);
}
