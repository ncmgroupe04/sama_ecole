using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Enrollments;
using SamaEcole.Application.ReportCards;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Discipline.Queries.GetDisciplinaryPv;

public record GetDisciplinaryPvQuery(Guid DisciplineRecordId) : IRequest<DisciplinaryPvDto>;

/// <summary>
/// Procès-verbal imprimable d'une sanction disciplinaire (<see cref="DisciplineRecord"/>) — même
/// formalisme M.E.N. que le certificat de scolarité (en-tête République/Ministère/IA/IEF), remis à
/// l'élève et à son tuteur en double, comme le prévoit le cahier des charges Documents.
/// </summary>
public record DisciplinaryPvDto(
    Guid DisciplineRecordId,
    string PvNumber,
    string Matricule,
    string StudentFullName,
    DateOnly StudentBirthDate,
    string ClassroomName,
    string? SchoolYearLabel,
    DateTime Date,
    string SanctionType,
    string Reason,
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

public class GetDisciplinaryPvQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetDisciplinaryPvQuery, DisciplinaryPvDto>
{
    private static readonly Dictionary<DisciplineType, string> SanctionLabels = new()
    {
        [DisciplineType.Avertissement] = "Avertissement",
        [DisciplineType.Blame] = "Blâme",
        [DisciplineType.Retenue] = "Retenue",
        [DisciplineType.Exclusion] = "Exclusion temporaire"
    };

    public async Task<DisciplinaryPvDto> Handle(GetDisciplinaryPvQuery request, CancellationToken cancellationToken)
    {
        var row = await (
            from d in dbContext.DisciplineRecords.AsNoTracking()
            join s in dbContext.Students.AsNoTracking() on d.StudentId equals s.Id
            join c in dbContext.Classrooms.AsNoTracking() on s.ClassroomId equals c.Id
            join sch in dbContext.Schools.AsNoTracking() on d.SchoolId equals sch.Id
            where d.Id == request.DisciplineRecordId
            select new
            {
                d.Id,
                d.Date,
                d.Type,
                d.Reason,
                s.Matricule,
                StudentFullName = s.FullName,
                s.BirthDate,
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
            ?? throw new KeyNotFoundException($"Sanction disciplinaire introuvable : {request.DisciplineRecordId}");

        var activeYearLabel = await dbContext.SchoolYears.AsNoTracking()
            .Where(y => y.IsActive)
            .Select(y => y.Label)
            .FirstOrDefaultAsync(cancellationToken);

        return new DisciplinaryPvDto(
            row.Id,
            $"PV-{row.Id.ToString()[..8].ToUpperInvariant()}",
            row.Matricule,
            row.StudentFullName,
            row.BirthDate,
            row.ClassroomName,
            activeYearLabel,
            row.Date,
            SanctionLabels[row.Type],
            row.Reason,
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
