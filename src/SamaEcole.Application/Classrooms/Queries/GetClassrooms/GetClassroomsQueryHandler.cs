using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Classrooms.Queries.GetClassrooms;

public class GetClassroomsQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetClassroomsQuery, IReadOnlyList<ClassroomDto>>
{
    public async Task<IReadOnlyList<ClassroomDto>> Handle(
        GetClassroomsQuery request, CancellationToken cancellationToken)
    {
        // AsNoTracking : lecture pure, aucun suivi de modifications à payer.
        // Le décompte d'élèves est une sous-requête corrélée exécutée par PostgreSQL — pas un
        // chargement des élèves en mémoire suivi d'un Count() côté C#, qui ramènerait toute l'école
        // pour n'en garder qu'un entier. Les deux requêtes voient le même tenant : la RLS et le
        // Global Query Filter s'appliquent aussi à la sous-requête.
        return await dbContext.Classrooms
            .AsNoTracking()
            .OrderBy(c => c.Level).ThenBy(c => c.Name)
            .Select(c => new ClassroomDto(
                c.Id,
                c.Name,
                c.Level,
                c.Capacity,
                dbContext.Students.Count(s => s.ClassroomId == c.Id),
                c.Cycle,
                EF.Property<uint>(c, "xmin")))
            .ToListAsync(cancellationToken);
    }
}
