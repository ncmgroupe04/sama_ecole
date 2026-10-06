using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Internat.Queries.ListInstructors;

/// <summary>
/// GET /api/v1/internat/instructors — les Oustaz de l'école avec l'effectif de leur Halqa. Tous les statuts sont
/// renvoyés (un Oustaz suspendu garde ses élèves tant que la Direction ne les a pas réaffectés) : c'est au client de
/// filtrer, pas à l'API de cacher une fiche encore chargée d'élèves.
/// </summary>
public record ListInstructorsQuery : IRequest<IReadOnlyList<InstructorDto>>;

public class ListInstructorsQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<ListInstructorsQuery, IReadOnlyList<InstructorDto>>
{
    public async Task<IReadOnlyList<InstructorDto>> Handle(ListInstructorsQuery request, CancellationToken cancellationToken) =>
        await InstructorProjection.Dtos(dbContext).ToListAsync(cancellationToken);
}
