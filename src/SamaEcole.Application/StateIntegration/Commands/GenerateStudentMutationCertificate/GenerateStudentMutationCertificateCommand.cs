using MediatR;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.StateIntegration.Commands.GenerateStudentMutationCertificate;

/// <summary>
/// POST /api/v1/state-integration/students/{studentId}/mutation-certificate — délivre le certificat
/// de mutation d'un élève et renvoie le PDF (Volume 1 §23.5, ticket JGK-M06).
///
/// COMMANDE, et non Query, alors qu'elle renvoie un document : elle ÉCRIT — un numéro officiel
/// séquentiel, une ligne en base, un code de vérification. Deux appels produisent deux certificats
/// distincts, jamais le même. La ranger en Query aurait laissé croire qu'on peut la rejouer sans
/// conséquence (AGENTS.md règle #7).
/// </summary>
public record GenerateStudentMutationCertificateCommand(
    Guid StudentId,
    Guid SchoolYearId,
    StudentMutationReason Reason,
    string? ReasonDetails = null,
    string? DestinationSchoolName = null,
    string? DestinationCity = null)
    : IRequest<MutationCertificateResult>, IAuditableRequest;

/// <summary>
/// Le PDF, plus les métadonnées dont l'écran a besoin pour confirmer la délivrance sans relire la
/// fiche. <paramref name="WasFinanciallyClear"/> est remonté pour que l'interface puisse alerter
/// l'agent — sans jamais bloquer : retenir un certificat de mutation à un élève débiteur reviendrait
/// à le retenir de force dans l'établissement.
/// </summary>
public record MutationCertificateResult(
    Guid CertificateId,
    string CertificateNumber,
    string FileName,
    byte[] Content,
    bool WasFinanciallyClear);
