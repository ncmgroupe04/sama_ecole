using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Registration.Queries.GetRegistrationRequestStatus;

/// <summary>
/// Ticket JGK-I02. La projection Select ci-dessous EST la protection contre la fuite de données
/// personnelles : elle ne lit jamais DirectorEmail/DirectorPhone/DirectorPasswordHash/SchoolAddress
/// depuis la base pour cette requête anonyme, plutôt que de les charger puis les omettre du DTO —
/// une différence qui compte si quelqu'un ajoute un jour un log de l'entité complète.
/// </summary>
public class GetRegistrationRequestStatusHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetRegistrationRequestStatusQuery, GetRegistrationRequestStatusResult>
{
    public async Task<GetRegistrationRequestStatusResult> Handle(
        GetRegistrationRequestStatusQuery request, CancellationToken cancellationToken)
    {
        // Normalisé comme le générateur (RegistrationReferenceGenerator) produit ses références : un
        // Directeur qui recopie « reg-abc12345 » depuis son téléphone ne doit pas se heurter à un 404
        // pour une simple différence de casse ou d'espace en bord de champ.
        var reference = request.TrackingReference.Trim().ToUpperInvariant();

        return await dbContext.SchoolRegistrationRequests
            .AsNoTracking()
            .Where(r => r.TrackingReference == reference)
            .Select(r => new GetRegistrationRequestStatusResult(
                r.TrackingReference, r.SchoolName, r.Status, r.CreatedAt, r.RejectionReason))
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("Aucune demande d'inscription ne correspond à cette référence.");
    }
}
