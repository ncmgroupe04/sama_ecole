using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Common;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Schools.Queries.GetMatriculeSequences;

public class GetMatriculeSequencesQueryHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    TimeProvider timeProvider)
    : IRequestHandler<GetMatriculeSequencesQuery, MatriculeSequencesDto>
{
    public async Task<MatriculeSequencesDto> Handle(
        GetMatriculeSequencesQuery request,
        CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var year = AcademicYear.ForDate(timeProvider.GetUtcNow());

        var rows = await dbContext.MatriculeSequences
            .AsNoTracking()
            .Where(s => s.SchoolId == schoolId
                        && s.Year == year
                        && (s.Kind == MatriculeKind.Student || s.Kind == MatriculeKind.Teacher))
            .Select(s => new { s.Kind, s.LastValue })
            .ToListAsync(cancellationToken);

        return new MatriculeSequencesDto(
            Build(MatriculeKind.Student),
            Build(MatriculeKind.Teacher));

        MatriculeSequenceInfo Build(MatriculeKind kind)
        {
            // Aucun matricule encore émis cette année : le compteur n'existe pas en base, le
            // prochain sera donc 1.
            var lastValue = rows.FirstOrDefault(r => r.Kind == kind)?.LastValue ?? 0;
            return new MatriculeSequenceInfo(kind.ToString(), year, lastValue, lastValue + 1);
        }
    }
}
