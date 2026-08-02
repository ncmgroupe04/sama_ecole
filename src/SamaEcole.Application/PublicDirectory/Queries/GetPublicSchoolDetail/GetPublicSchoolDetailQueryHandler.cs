using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.PublicDirectory.Queries.GetPublicSchools;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.PublicDirectory.Queries.GetPublicSchoolDetail;

public class GetPublicSchoolDetailQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetPublicSchoolDetailQuery, PublicSchoolDto>
{
    public async Task<PublicSchoolDto> Handle(
        GetPublicSchoolDetailQuery request, CancellationToken cancellationToken)
    {
        // Interroge la VUE, jamais dbContext.Schools : une école sans consentement n'y a aucune ligne,
        // le 404 tombe donc de lui-même. Chercher dans `schools` puis filtrer ici en C# aurait produit
        // le même résultat visible, mais en faisant remonter la ligne complète — NINEA, RCCM et statut
        // compris — dans la mémoire d'un traitement anonyme.
        var row = await dbContext.PublicSchoolDirectory
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == request.SchoolId, cancellationToken)
            ?? throw new KeyNotFoundException($"Établissement {request.SchoolId} introuvable dans l'annuaire public.");

        return GetPublicSchoolsQueryHandler.Map(row);
    }
}
