using Microsoft.Extensions.Logging;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Queries.GetFinancialCommitment;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

public class FinancialCommitmentPdfGenerator(ILogger<FinancialCommitmentPdfGenerator>? logger = null) : IFinancialCommitmentPdfGenerator
{
    static FinancialCommitmentPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(FinancialCommitmentDto commitment, byte[]? logo, byte[] qrCodeImage) =>
        PdfRenderGuard.Render(
            logger,
            $"engagement financier {commitment.CommitmentNumber} (matricule {commitment.Matricule}, engagement {commitment.FinancialCommitmentId})",
            () => new FinancialCommitmentDocument(commitment, logo, qrCodeImage).GeneratePdf(),
            logo is not null ? () => new FinancialCommitmentDocument(commitment, null, qrCodeImage).GeneratePdf() : null);
}
