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
        // Maths coef 4… »). Puis par DisplayOrder : l'ordre d'une grille d'évaluation est pédagogique,
        // pas alphabétique — « Ressources » précède « Compétences », et rien dans les deux libellés ne
        // le dit. Le nom ne départage plus qu'à égalité de rang, ce qui laisse l'ordre alphabétique
        // d'avant aux écoles qui n'ont jamais touché à la réorganisation (DisplayOrder = 0 partout).
        return await dbContext.Subjects
            .AsNoTracking()
            .OrderBy(s => s.Level).ThenBy(s => s.DisplayOrder).ThenBy(s => s.Name)
            .Select(s => new SubjectDto(
                s.Id, s.Name, s.Level, s.Coefficient, EF.Property<uint>(s, "xmin"),
                s.ParentSubjectId, s.MaxScore, s.DisplayOrder, s.Column1Header, s.Column2Header))
            .ToListAsync(cancellationToken);
    }
}
