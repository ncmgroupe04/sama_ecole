using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Enrollments;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Queries.GetDuesNotice;

public record GetDuesNoticeQuery(Guid EnrollmentId) : IRequest<DuesNoticeDto>;

/// <summary>
/// Sommation pour impayés — lettre formelle de mise en demeure adressée au tuteur d'un élève dont au
/// moins une échéance est en retard (règle #4 : ce document ne modifie rien, il constate le solde déjà
/// posé par le Secrétariat à l'inscription). Refuse d'émettre une sommation pour une inscription à
/// jour : ce n'est pas un document que la Finance peut produire "au cas où", il constate un état réel.
/// </summary>
public record DuesNoticeDto(
    Guid EnrollmentId,
    string NoticeNumber,
    string Matricule,
    string StudentFullName,
    string ClassroomName,
    string SchoolYearLabel,
    string? GuardianName,
    string? GuardianPhone,
    decimal TotalDue,
    decimal AmountPaid,
    decimal RemainingBalance,
    IReadOnlyList<DuesNoticeInstallmentDto> OverdueInstallments,
    DateTimeOffset IssuedAt,
    string SchoolName,
    string? SchoolAddress,
    string? SchoolCity,
    string? SchoolPhone,
    string? SchoolNinea,
    string? SchoolLogoUrl);

public record DuesNoticeInstallmentDto(string Designation, decimal Amount, DateOnly DueDate);

public class GetDuesNoticeQueryHandler(IApplicationDbContext dbContext, TimeProvider timeProvider)
    : IRequestHandler<GetDuesNoticeQuery, DuesNoticeDto>
{
    public async Task<DuesNoticeDto> Handle(GetDuesNoticeQuery request, CancellationToken cancellationToken)
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
                Enrollment = e,
                Student = s,
                ClassroomName = c.Name,
                SchoolYearLabel = y.Label,
                SchoolYearStart = y.StartDate,
                School = sch
            }).FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Inscription introuvable ou annulée : {request.EnrollmentId}");

        var lines = await dbContext.EnrollmentFeeLines.AsNoTracking()
            .Where(l => l.EnrollmentId == row.Enrollment.Id)
            .OrderBy(l => l.IsRecurring)
            .ThenBy(l => l.Designation)
            .ToListAsync(cancellationToken);

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().DateTime);
        var remainingPaid = row.Enrollment.AmountPaid;
        var overdue = new List<DuesNoticeInstallmentDto>();

        foreach (var line in lines)
        {
            if (!line.IsRecurring || line.Months <= 1)
            {
                AccountFor(line.Designation, line.LineTotal, row.SchoolYearStart);
            }
            else
            {
                for (int m = 1; m <= line.Months; m++)
                {
                    AccountFor($"{line.Designation} (Mois {m})", line.UnitAmount, row.SchoolYearStart.AddMonths(m - 1));
                }
            }
        }

        void AccountFor(string designation, decimal amount, DateOnly dueDate)
        {
            var paidForThis = Math.Min(remainingPaid, amount);
            remainingPaid -= paidForThis;
            var remainingDue = amount - paidForThis;
            if (remainingDue > 0 && dueDate < today)
            {
                overdue.Add(new DuesNoticeInstallmentDto(designation, remainingDue, dueDate));
            }
        }

        if (overdue.Count == 0)
        {
            throw new BusinessRuleException(
                "Aucune échéance en retard sur cette inscription : impossible d'émettre une sommation pour un compte à jour.");
        }

        return new DuesNoticeDto(
            row.Enrollment.Id,
            $"SOM-{row.Enrollment.Id.ToString()[..8].ToUpperInvariant()}",
            row.Student.Matricule,
            row.Student.FullName,
            row.ClassroomName,
            row.SchoolYearLabel,
            row.Student.GuardianName,
            row.Student.GuardianPhone,
            row.Enrollment.TotalDue,
            row.Enrollment.AmountPaid,
            row.Enrollment.TotalDue - row.Enrollment.AmountPaid,
            overdue,
            timeProvider.GetUtcNow(),
            row.School.Name,
            row.School.Address,
            ReceiptCity.FromAddress(row.School.Address),
            row.School.Phone,
            row.School.Ninea,
            row.School.LogoUrl);
    }
}
