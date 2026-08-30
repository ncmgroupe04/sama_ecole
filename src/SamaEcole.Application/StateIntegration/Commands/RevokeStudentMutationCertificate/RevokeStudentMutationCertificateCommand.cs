using MediatR;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Application.StateIntegration.Commands.RevokeStudentMutationCertificate;

/// <summary>
/// POST /api/v1/state-integration/certificates/{id}/revoke — révoque un certificat de mutation
/// (Volume 1 §23.5, ticket JGK-M06).
///
/// LA RÉVOCATION EST LE SEUL MOYEN DE CORRIGER une pièce déjà délivrée : un certificat n'est jamais
/// modifié (deux versions du même numéro se contrediraient, la version papier faisant foi contre
/// l'école). Une erreur se corrige donc en révoquant, puis en délivrant un nouveau certificat.
///
/// Effet immédiat : le point de vérification publique (scan du QR) répond « révoqué » à partir de cet
/// instant. La ligne reste en base — aucune suppression physique (règle #6).
///
/// <c>IAuditableRequest</c> : annuler une pièce officielle remise à une famille est une écriture
/// sensible, journalisée au même titre que sa délivrance.
/// </summary>
public record RevokeStudentMutationCertificateCommand(Guid CertificateId, string Reason)
    : IRequest<Unit>, IAuditableRequest;
