using SamaEcole.Application.StateIntegration;

namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Certificat de mutation en PDF, A4 portrait, une page — la pièce remise au tuteur (Volume 1 §23.5).
///
/// Porte un QR code de vérification produit par <see cref="IQrCodeService"/>. Le QR encode une URL de
/// VÉRIFICATION, jamais l'état civil de l'élève : un QR photographié sur un papier posé sur un bureau
/// est lisible par n'importe qui, et y placer les données rendrait le contrôle d'accès inopérant.
///
/// Implémentation côté Infrastructure (QuestPDF).
/// </summary>
public interface IStudentMutationCertificatePdfGenerator
{
    byte[] Generate(StudentMutationCertificateModel model, byte[]? qrCode);
}
