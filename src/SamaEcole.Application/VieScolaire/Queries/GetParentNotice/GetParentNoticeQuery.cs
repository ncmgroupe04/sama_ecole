using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Enrollments;
using SamaEcole.Application.ReportCards;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.VieScolaire.Queries.GetParentNotice;

public record GetParentNoticeQuery(Guid ParentSummonsId) : IRequest<ParentNoticeDto>;

/// <summary>
/// Convocation imprimable d'un parent/tuteur — même formalisme M.E.N. que le certificat de scolarité
/// et le PV de discipline (en-tête République/Ministère/IA/IEF).
/// </summary>
public record ParentNoticeDto(
    Guid ParentSummonsId,
    string NoticeNumber,
    string Matricule,
    string StudentFullName,
    string ClassroomName,
    string? SchoolYearLabel,
    DateTimeOffset ScheduledAt,
    string Reason,
    DateTimeOffset IssuedAt,
    string? GuardianName,
    string? GuardianPhone,
    string SchoolName,
    string? SchoolAddress,
    string? SchoolCity,
    string? InspectionAcademie,
    string? InspectionEducationFormation,
    string HeadingPrefix,
    string? HeadingName,
    string? SchoolLogoUrl);

public class GetParentNoticeQueryHandler(IApplicationDbContext dbContext, TimeProvider timeProvider)
    : IRequestHandler<GetParentNoticeQuery, ParentNoticeDto>
{
    public async Task<ParentNoticeDto> Handle(GetParentNoticeQuery request, CancellationToken cancellationToken)
    {
        var row = await (
            from p in dbContext.ParentSummons.AsNoTracking()
            join s in dbContext.Students.AsNoTracking() on p.StudentId equals s.Id
            join c in dbContext.Classrooms.AsNoTracking() on s.ClassroomId equals c.Id
            join sch in dbContext.Schools.AsNoTracking() on p.SchoolId equals sch.Id
            where p.Id == request.ParentSummonsId
            select new
            {
                p.Id,
                p.ScheduledAt,
                p.Reason,
                s.Matricule,
                StudentFullName = s.FullName,
                s.GuardianName,
                s.GuardianPhone,
                ClassroomName = c.Name,
                c.Cycle,
                SchoolName = sch.Name,
                SchoolAddress = sch.Address,
                sch.InspectionAcademie,
                sch.InspectionEducationFormation,
                sch.NomLycee,
                sch.LogoUrl
            }).FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Convocation introuvable : {request.ParentSummonsId}");

        var activeYearLabel = await dbContext.SchoolYears.AsNoTracking()
            .Where(y => y.IsActive)
            .Select(y => y.Label)
            .FirstOrDefaultAsync(cancellationToken);

        return new ParentNoticeDto(
            row.Id,
            $"CONV-{row.Id.ToString()[..8].ToUpperInvariant()}",
            row.Matricule,
            row.StudentFullName,
            row.ClassroomName,
            activeYearLabel,
            row.ScheduledAt,
            row.Reason,
            timeProvider.GetUtcNow(),
            row.GuardianName,
            row.GuardianPhone,
            row.SchoolName,
            row.SchoolAddress,
            ReceiptCity.FromAddress(row.SchoolAddress),
            row.InspectionAcademie,
            row.InspectionEducationFormation,
            SchoolHeading.PrefixFor(row.Cycle),
            SchoolHeading.StripCyclePrefix(row.NomLycee),
            row.LogoUrl);
    }
}
