using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Enrollments;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Queries.GetFinancialCommitment;

public record GetFinancialCommitmentQuery(Guid FinancialCommitmentId) : IRequest<FinancialCommitmentDto>;

/// <summary>
/// Engagement financier imprimable — document RH/Finance interne (comme le reçu) : pas de bandeau
/// M.E.N., établissement en tant que créancier.
/// </summary>
public record FinancialCommitmentDto(
    Guid FinancialCommitmentId,
    string CommitmentNumber,
    string Matricule,
    string StudentFullName,
    string ClassroomName,
    string SchoolYearLabel,
    decimal Amount,
    DateOnly DueDate,
    string Terms,
    DateTimeOffset SignedAt,
    string? GuardianName,
    string? GuardianPhone,
    string SchoolName,
    string? SchoolAddress,
    string? SchoolCity,
    string? SchoolPhone,
    string? SchoolNinea,
    string? SchoolLogoUrl);

public class GetFinancialCommitmentQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetFinancialCommitmentQuery, FinancialCommitmentDto>
{
    public async Task<FinancialCommitmentDto> Handle(GetFinancialCommitmentQuery request, CancellationToken cancellationToken)
    {
        var row = await (
            from fc in dbContext.FinancialCommitments.AsNoTracking()
            join e in dbContext.Enrollments.AsNoTracking() on fc.EnrollmentId equals e.Id
            join s in dbContext.Students.AsNoTracking() on e.StudentId equals s.Id
            join c in dbContext.Classrooms.AsNoTracking() on e.ClassroomId equals c.Id
            join y in dbContext.SchoolYears.AsNoTracking() on e.SchoolYearId equals y.Id
            join sch in dbContext.Schools.AsNoTracking() on fc.SchoolId equals sch.Id
            where fc.Id == request.FinancialCommitmentId
            select new
            {
                fc.Id,
                fc.Amount,
                fc.DueDate,
                fc.Terms,
                fc.CreatedAt,
                s.Matricule,
                StudentFullName = s.FullName,
                s.GuardianName,
                s.GuardianPhone,
                ClassroomName = c.Name,
                SchoolYearLabel = y.Label,
                School = sch
            }).FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Engagement financier introuvable : {request.FinancialCommitmentId}");

        return new FinancialCommitmentDto(
            row.Id,
            $"ENG-{row.Id.ToString()[..8].ToUpperInvariant()}",
            row.Matricule,
            row.StudentFullName,
            row.ClassroomName,
            row.SchoolYearLabel,
            row.Amount,
            row.DueDate,
            row.Terms,
            row.CreatedAt,
            row.GuardianName,
            row.GuardianPhone,
            row.School.Name,
            row.School.Address,
            ReceiptCity.FromAddress(row.School.Address),
            row.School.Phone,
            row.School.Ninea,
            row.School.LogoUrl);
    }
}
