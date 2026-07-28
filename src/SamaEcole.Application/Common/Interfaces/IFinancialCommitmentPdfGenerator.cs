using SamaEcole.Application.Finance.Queries.GetFinancialCommitment;

namespace SamaEcole.Application.Common.Interfaces;

/// <summary>Rend l'engagement financier en PDF (A4) — même contrat que les autres générateurs de documents officiels.</summary>
public interface IFinancialCommitmentPdfGenerator
{
    byte[] Generate(FinancialCommitmentDto commitment, byte[]? logo, byte[] qrCodeImage);
}
