using SamaEcole.Application.Finance.Queries.GetWorkCertificate;

namespace SamaEcole.Application.Common.Interfaces;

/// <summary>Rend l'attestation de travail en PDF (A4) — même contrat que les autres générateurs de documents officiels.</summary>
public interface IWorkCertificatePdfGenerator
{
    byte[] Generate(WorkCertificateDto certificate, byte[]? logo, byte[] qrCodeImage);
}
