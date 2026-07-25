using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Enrollments.Queries.GetEnrollmentReceipt;
using SamaEcole.Application.ReportCards;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Enrollments.Queries.GetEnrollmentCertificate;

public record GetEnrollmentCertificateQuery(Guid EnrollmentId) : IRequest<EnrollmentCertificateDto>;

/// <summary>
/// Données du Certificat de Scolarité (A4, normes M.E.N. Sénégal) — même route API et même bouton
/// front que l'ancienne attestation A5 : ce n'est pas un document distinct, la mise en page a été
/// remplacée pour se conformer au formalisme administratif sénégalais (en-tête République/Ministère,
/// IA/IEF, format A4 portrait), sur le modèle déjà validé du bulletin de notes (<see cref="SchoolHeading"/>).
/// </summary>
public record EnrollmentCertificateDto(
    Guid EnrollmentId,
    string CertificateNumber,
    string Matricule,
    string StudentFullName,
    DateOnly StudentBirthDate,
    string StudentBirthPlace,
    string StudentGender,
    string ClassroomName,
    string SchoolYearLabel,
    DateTimeOffset EnrolledAt,
    string SchoolName,
    string? SchoolAddress,
    string? SchoolCity,
    string? InspectionAcademie,
    string? InspectionEducationFormation,
    string HeadingPrefix,
    string? HeadingName,
    string? SchoolLogoUrl);

public class GetEnrollmentCertificateQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetEnrollmentCertificateQuery, EnrollmentCertificateDto>
{
    public async Task<EnrollmentCertificateDto> Handle(GetEnrollmentCertificateQuery request, CancellationToken cancellationToken)
    {
        var row = await (
            from e in dbContext.Enrollments.AsNoTracking()
            join s in dbContext.Students.AsNoTracking() on e.StudentId equals s.Id
            join c in dbContext.Classrooms.AsNoTracking() on e.ClassroomId equals c.Id
            join y in dbContext.SchoolYears.AsNoTracking() on e.SchoolYearId equals y.Id
            join sch in dbContext.Schools.AsNoTracking() on e.SchoolId equals sch.Id
            where e.Id == request.EnrollmentId && e.Status != EnrollmentStatus.Cancelled
            select new
            {
                e.Id,
                s.Matricule,
                StudentFullName = s.FullName,
                s.BirthDate,
                s.BirthPlace,
                s.Gender,
                ClassroomName = c.Name,
                c.Cycle,
                SchoolYearLabel = y.Label,
                e.EnrolledAt,
                SchoolName = sch.Name,
                SchoolAddress = sch.Address,
                sch.InspectionAcademie,
                sch.InspectionEducationFormation,
                sch.NomLycee,
                sch.LogoUrl
            }).FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Inscription introuvable ou annulée : {request.EnrollmentId}");

        return new EnrollmentCertificateDto(
            row.Id,
            $"CS-{row.Id.ToString()[..8].ToUpperInvariant()}",
            row.Matricule,
            row.StudentFullName,
            row.BirthDate,
            row.BirthPlace,
            row.Gender.ToString(),
            row.ClassroomName,
            row.SchoolYearLabel,
            row.EnrolledAt,
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
