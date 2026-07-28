using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Queries.GetTaxDeclaration;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

public class TaxDeclarationPdfGenerator : ITaxDeclarationPdfGenerator
{
    static TaxDeclarationPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(TaxDeclarationDto declaration, byte[]? logo)
    {
        try
        {
            return new TaxDeclarationDocument(declaration, logo).GeneratePdf();
        }
        catch (Exception) when (logo is not null)
        {
            return new TaxDeclarationDocument(declaration, null).GeneratePdf();
        }
    }
}
