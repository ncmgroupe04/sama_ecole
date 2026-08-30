using MediatR;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Application.StateIntegration.Queries.VerifyMutationCertificate;

public class VerifyMutationCertificateQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<VerifyMutationCertificateQuery, MutationCertificateVerificationResult>
{
    public async Task<MutationCertificateVerificationResult> Handle(
        VerifyMutationCertificateQuery request, CancellationToken cancellationToken)
    {
        // Un token vide ou grossièrement mal formé ne vaut pas la peine d'un aller-retour SQL — et
        // surtout, il ne doit pas se distinguer d'un token bien formé mais inexistant : les deux
        // répondent « inconnu », à l'identique.
        var token = request.Token?.Trim() ?? "";
        if (token.Length is < 8 or > 64)
        {
            return MutationCertificateVerificationResult.Unknown;
        }

        var row = await dbContext.VerifyMutationCertificateAsync(token, cancellationToken);

        if (row is null)
        {
            return MutationCertificateVerificationResult.Unknown;
        }

        return new MutationCertificateVerificationResult(
            Status: row.Status,
            CertificateNumber: row.CertificateNumber,
            IssuedOn: row.IssuedOn,
            IssuingSchoolName: row.IssuingSchoolName,
            RevokedAt: row.RevokedAt);
    }
}
