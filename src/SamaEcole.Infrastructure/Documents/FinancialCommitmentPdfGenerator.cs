using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Queries.GetFinancialCommitment;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

public class FinancialCommitmentPdfGenerator : IFinancialCommitmentPdfGenerator
{
    static FinancialCommitmentPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(FinancialCommitmentDto commitment, byte[]? logo, byte[] qrCodeImage)
    {
        try
        {
            return new FinancialCommitmentDocument(commitment, logo, qrCodeImage).GeneratePdf();
        }
        catch (Exception) when (logo is not null)
        {
            return new FinancialCommitmentDocument(commitment, null, qrCodeImage).GeneratePdf();
        }
    }
}
