using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Internat.Queries.GetStudentHizbProgress;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Internat.Queries.GetStudentHizbReport;

/// <summary>
/// Données du bulletin coranique d'un élève : son identité, son Oustaz, et la grille des 60 Hizb avec le résumé. La
/// grille et la PORTÉE (un Oustaz ne tire que le bulletin de SES élèves, 403 sinon ; élève d'une autre école : 404)
/// viennent de <see cref="GetStudentHizbProgressQuery"/> : une seule règle d'accès pour l'écran et pour le document.
///
/// Aucune appréciation n'est inventée : le suivi ne porte qu'une note de 1 à 5 par Hizb. Le bulletin l'affiche telle
/// quelle, ainsi que sa moyenne, et laisse un espace pour l'appréciation manuscrite de l'Oustaz.
/// </summary>
public record GetStudentHizbReportQuery(Guid StudentId) : IRequest<HizbReportDto>;

/// <param name="AverageRating">Moyenne des notes saisies (1 à 5), ou <c>null</c> si aucun Hizb n'a été noté.</param>
/// <param name="LastEvaluatedAt">Date de la dernière évaluation, ou <c>null</c> si l'élève n'a jamais été évalué.</param>
public record HizbReportDto(
    string SchoolName,
    string? SchoolAddress,
    string? SchoolLogoUrl,
    Guid StudentId,
    string Matricule,
    string FullName,
    string? FullNameAr,
    DateOnly BirthDate,
    string BirthPlace,
    string ClassroomName,
    string? SchoolYearLabel,
    string? InstructorName,
    string? InstructorNameAr,
    DateTimeOffset GeneratedAt,
    HizbSummaryDto Summary,
    decimal? AverageRating,
    DateTimeOffset? LastEvaluatedAt,
    IReadOnlyList<HizbCellDto> Hizbs);

public class GetStudentHizbReportQueryHandler(ISender mediator, IApplicationDbContext dbContext, TimeProvider timeProvider)
    : IRequestHandler<GetStudentHizbReportQuery, HizbReportDto>
{
    public async Task<HizbReportDto> Handle(GetStudentHizbReportQuery request, CancellationToken cancellationToken)
    {
        // 404 (élève hors école) et 403 (Oustaz hors de sa Halqa) sont levés ici, avant toute autre lecture.
        var progress = await mediator.Send(new GetStudentHizbProgressQuery(request.StudentId), cancellationToken);

        var student = await dbContext.Students
            .AsNoTracking()
            .Where(s => s.Id == request.StudentId)
            .Select(s => new { s.Matricule, s.FullName, s.FullNameAr, s.BirthDate, s.BirthPlace, s.ClassroomId, s.SchoolId })
            .FirstAsync(cancellationToken);

        var classroomName = await dbContext.Classrooms.AsNoTracking()
            .Where(c => c.Id == student.ClassroomId)
            .Select(c => c.Name)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

        var school = await dbContext.Schools.AsNoTracking()
            .Where(s => s.Id == student.SchoolId)
            .Select(s => new { s.Name, s.Address, s.LogoUrl })
            .FirstAsync(cancellationToken);

        var yearLabel = await dbContext.SchoolYears.AsNoTracking()
            .Where(y => y.IsActive)
            .Select(y => y.Label)
            .FirstOrDefaultAsync(cancellationToken);

        var instructor = progress.InstructorId is { } instructorId
            ? await dbContext.Instructors.AsNoTracking()
                .Where(i => i.Id == instructorId)
                .Select(i => new { i.FullName, i.FullNameAr })
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        var rated = progress.Hizbs.Where(c => c.Rating is not null).Select(c => c.Rating!.Value).ToList();
        decimal? averageRating = rated.Count == 0
            ? null
            : Math.Round((decimal)rated.Average(), 1, MidpointRounding.AwayFromZero);

        var lastEvaluatedAt = progress.Hizbs.Where(c => c.LastEvaluatedAt is not null).Max(c => c.LastEvaluatedAt);

        return new HizbReportDto(
            school.Name, school.Address, school.LogoUrl,
            request.StudentId, student.Matricule, student.FullName, student.FullNameAr,
            student.BirthDate, student.BirthPlace, classroomName, yearLabel,
            instructor?.FullName, instructor?.FullNameAr,
            timeProvider.GetUtcNow(), progress.Summary, averageRating, lastEvaluatedAt, progress.Hizbs);
    }
}
