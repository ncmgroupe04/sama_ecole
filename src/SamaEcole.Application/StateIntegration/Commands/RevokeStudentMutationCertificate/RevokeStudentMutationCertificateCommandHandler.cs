using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Application.StateIntegration.Commands.RevokeStudentMutationCertificate;

public class RevokeStudentMutationCertificateCommandHandler(
    IApplicationDbContext dbContext,
    TimeProvider timeProvider)
    : IRequestHandler<RevokeStudentMutationCertificateCommand, Unit>
{
    public async Task<Unit> Handle(
        RevokeStudentMutationCertificateCommand request, CancellationToken cancellationToken)
    {
        // Global Query Filter + RLS : un certificat d'une autre école est structurellement
        // introuvable ici (404, jamais une révocation croisée entre tenants).
        var certificate = await dbContext.StudentMutationCertificates
            .FirstOrDefaultAsync(c => c.Id == request.CertificateId, cancellationToken)
            ?? throw new KeyNotFoundException("Certificat de mutation introuvable dans votre établissement.");

        // Déjà révoqué : on ne réécrit pas le motif ni la date d'une révocation actée — la première
        // fait foi, et une seconde révocation ne veut rien dire.
        if (certificate.RevokedAt is not null)
        {
            throw new BusinessRuleException(
                $"Le certificat {certificate.CertificateNumber} est déjà révoqué "
                + $"(le {certificate.RevokedAt:dd/MM/yyyy}).");
        }

        certificate.RevokedAt = timeProvider.GetUtcNow();
        certificate.RevocationReason = request.Reason.Trim();

        await dbContext.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
