using SamaEcole.Application.Enrollments.Queries.GetExeatCertificate;

namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Rend le Certificat d'Exéat en PDF (A4), sur le même contrat que
/// <see cref="IEnrollmentCertificatePdfGenerator"/> : mise en page en Infrastructure (QuestPDF),
/// donnée en Application, génération déterministe et synchrone. <paramref name="qrCodeImage"/> porte
/// le QR code anti-fraude déjà généré (<c>IQrCodeService</c>), au même titre que le logo côté réseau.
/// </summary>
public interface IExeatCertificatePdfGenerator
{
    byte[] Generate(ExeatCertificateDto certificate, byte[]? logo, byte[] qrCodeImage);
}
