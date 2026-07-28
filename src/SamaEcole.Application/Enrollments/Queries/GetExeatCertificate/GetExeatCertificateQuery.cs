using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Enrollments;
using SamaEcole.Application.ReportCards;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Enrollments.Queries.GetExeatCertificate;

public record GetExeatCertificateQuery(Guid EnrollmentId) : IRequest<ExeatCertificateDto>;

/// <summary>
/// Données du Certificat d'Exéat (A4, normes M.E.N. Sénégal) — atteste qu'une scolarité s'est arrêtée
/// (abandon ou transfert, <see cref="ChangeEnrollmentStatusCommand"/>) et l'état du compte de
/// l'élève à cette date. N'existe que pour une inscription DÉJÀ passée à <c>DroppedOut</c> ou
/// <c>Transferred</c> — une inscription encore active n'a pas d'exéat (404, jamais un document
/// inventé pour une scolarité en cours).
/// </summary>
public record ExeatCertificateDto(
    Guid EnrollmentId,
    string CertificateNumber,
    string Matricule,
    string StudentFullName,
    DateOnly StudentBirthDate,
    string StudentBirthPlace,
    string StudentGender,
    string ClassroomName,
    string SchoolYearLabel,
    string Motive,
    DateTimeOffset LeftAt,
    decimal TotalDue,
    decimal AmountPaid,
    string SchoolName,
    string? SchoolAddress,
    string? SchoolCity,
    string? InspectionAcademie,
    string? InspectionEducationFormation,
    string HeadingPrefix,
    string? HeadingName,
    string? SchoolLogoUrl);

public class GetExeatCertificateQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetExeatCertificateQuery, ExeatCertificateDto>
{
    public async Task<ExeatCertificateDto> Handle(GetExeatCertificateQuery request, CancellationToken cancellationToken)
    {
        var row = await (
            from e in dbContext.Enrollments.AsNoTracking()
            join s in dbContext.Students.AsNoTracking() on e.StudentId equals s.Id
            join c in dbContext.Classrooms.AsNoTracking() on e.ClassroomId equals c.Id
            join y in dbContext.SchoolYears.AsNoTracking() on e.SchoolYearId equals y.Id
            join sch in dbContext.Schools.AsNoTracking() on e.SchoolId equals sch.Id
            where e.Id == request.EnrollmentId
                && (e.Status == EnrollmentStatus.DroppedOut || e.Status == EnrollmentStatus.Transferred)
            select new
            {
                e.Id,
                e.Status,
                e.UpdatedAt,
                e.EnrolledAt,
                e.TotalDue,
                e.AmountPaid,
                s.Matricule,
                StudentFullName = s.FullName,
                s.BirthDate,
                s.BirthPlace,
                s.Gender,
                ClassroomName = c.Name,
                c.Cycle,
                SchoolYearLabel = y.Label,
                SchoolName = sch.Name,
                SchoolAddress = sch.Address,
                sch.InspectionAcademie,
                sch.InspectionEducationFormation,
                sch.NomLycee,
                sch.LogoUrl
            }).FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException(
                $"Aucun exéat disponible : inscription introuvable ou scolarité toujours active pour {request.EnrollmentId}");

        return new ExeatCertificateDto(
            row.Id,
            $"EX-{row.Id.ToString()[..8].ToUpperInvariant()}",
            row.Matricule,
            row.StudentFullName,
            row.BirthDate,
            row.BirthPlace,
            row.Gender.ToString(),
            row.ClassroomName,
            row.SchoolYearLabel,
            row.Status == EnrollmentStatus.Transferred ? "Transfert vers un autre établissement" : "Abandon en cours d'année scolaire",
            row.UpdatedAt ?? row.EnrolledAt,
            row.TotalDue,
            row.AmountPaid,
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
