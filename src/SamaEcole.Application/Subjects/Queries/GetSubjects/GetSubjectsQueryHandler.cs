using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Subjects.Queries.GetSubjects;

public class GetSubjectsQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetSubjectsQuery, IReadOnlyList<SubjectDto>>
{
    public async Task<IReadOnlyList<SubjectDto>> Handle(
        GetSubjectsQuery request, CancellationToken cancellationToken)
    {
        // Groupé par niveau à l'affichage : c'est ainsi qu'un enseignant lit son barème (« au primaire,
        // Maths coef 4… »). AsNoTracking : lecture pure, aucun suivi de modifications à payer.
        return await dbContext.Subjects
            .AsNoTracking()
            .OrderBy(s => s.Level).ThenBy(s => s.Name)
            .Select(s => new SubjectDto(s.Id, s.Name, s.Level, s.Coefficient))
            .ToListAsync(cancellationToken);
    }
}
