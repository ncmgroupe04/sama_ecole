using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Enrollments.Queries.GetEnrollmentReceipt;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Enrollments.Queries.GetEnrollmentCertificate;

public record GetEnrollmentCertificateQuery(Guid EnrollmentId) : IRequest<EnrollmentCertificateDto>;

public record EnrollmentCertificateDto(
    Guid EnrollmentId,
    string CertificateNumber,
    string Matricule,
    string StudentFullName,
    DateOnly StudentBirthDate,
    string StudentBirthPlace,
    string StudentGender,
    string? GuardianName,
    string? GuardianPhone,
    string ClassroomName,
    string ClassroomLevel,
    string SchoolYearLabel,
    string Type,
    DateTimeOffset EnrolledAt,
    string SchoolName,
    string? SchoolAddress,
    string? SchoolPhone,
    string? SchoolEmail,
    string? SchoolCity,
    string? SchoolNinea,
    string? SchoolRegistreCommerce,
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
                s.GuardianName,
                s.GuardianPhone,
                ClassroomName = c.Name,
                ClassroomLevel = c.Level,
                SchoolYearLabel = y.Label,
                e.Type,
                e.EnrolledAt,
                SchoolName = sch.Name,
                SchoolAddress = sch.Address,
                SchoolPhone = sch.Phone,
                SchoolEmail = sch.Email,
                sch.Ninea,
                sch.RegistreCommerce,
                sch.LogoUrl
            }).FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Inscription introuvable ou annulée : {request.EnrollmentId}");

        return new EnrollmentCertificateDto(
            row.Id,
            $"CERT-{row.Id.ToString()[..8].ToUpperInvariant()}",
            row.Matricule,
            row.StudentFullName,
            row.BirthDate,
            row.BirthPlace,
            row.Gender.ToString(),
            row.GuardianName,
            row.GuardianPhone,
            row.ClassroomName,
            row.ClassroomLevel,
            row.SchoolYearLabel,
            row.Type.ToString(),
            row.EnrolledAt,
            row.SchoolName,
            row.SchoolAddress,
            row.SchoolPhone,
            row.SchoolEmail,
            ReceiptCity.FromAddress(row.SchoolAddress),
            row.Ninea,
            row.RegistreCommerce,
            row.LogoUrl);
    }
}
