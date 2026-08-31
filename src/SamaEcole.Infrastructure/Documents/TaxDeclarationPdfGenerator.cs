using Microsoft.Extensions.Logging;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Queries.GetTaxDeclaration;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

public class TaxDeclarationPdfGenerator(ILogger<TaxDeclarationPdfGenerator>? logger = null) : ITaxDeclarationPdfGenerator
{
    static TaxDeclarationPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(TaxDeclarationDto declaration, byte[]? logo) =>
        PdfRenderGuard.Render(
            logger,
            $"déclaration fiscale {declaration.DeclarationNumber} ({declaration.Month:00}/{declaration.Year}, id {declaration.Id})",
            () => new TaxDeclarationDocument(declaration, logo).GeneratePdf(),
            logo is not null ? () => new TaxDeclarationDocument(declaration, null).GeneratePdf() : null);
}
