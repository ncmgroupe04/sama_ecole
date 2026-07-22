using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Queries.GetStudentBalance;

/// <summary>
/// GET /finance/students/{studentId}/balance — ticket JGK-F02. Point d'entrée de la caisse : à partir
/// d'un élève trouvé par recherche, retrouve SON INSCRIPTION sur l'année scolaire ACTIVE et le solde à
/// encaisser. Aucune route n'indexait jusqu'ici les inscriptions par élève — tout se faisait par
/// EnrollmentId (reçu). Sans année active ou sans inscription de l'élève sur cette année, il n'y a rien
/// à encaisser : 404, jamais un solde inventé.
///
/// Lecture ouverte à tout rôle de l'école (comme le barème et le reçu) : composer/afficher un solde
/// n'est pas un acte d'encaissement, seul POST /finance/payments l'est (règle #4).
/// </summary>
public record GetStudentBalanceQuery(Guid StudentId) : IRequest<StudentBalanceDto>;

public record StudentBalanceDto(
    Guid EnrollmentId,
    Guid StudentId,
    string Matricule,
    string StudentFullName,
    string ClassroomName,
    string SchoolYearLabel,
    decimal TotalDue,
    decimal AmountPaid,
    decimal RemainingBalance,
    string Status,
    IReadOnlyList<InstallmentDto> Installments,
    IReadOnlyList<StudentBalancePaymentDto> PaymentsHistory);

public record InstallmentDto(
    string Id,
    string Designation,
    decimal Amount,
    decimal AmountPaid,
    decimal RemainingDue,
    DateTimeOffset DueDate,
    string Status);

public record StudentBalancePaymentDto(
    Guid PaymentId,
    string ReceiptNumber,
    decimal Amount,
    string Method,
    DateTimeOffset PaidAt);

public class GetStudentBalanceQueryHandler(IApplicationDbContext dbContext, TimeProvider timeProvider)
    : IRequestHandler<GetStudentBalanceQuery, StudentBalanceDto>
{
    public async Task<StudentBalanceDto> Handle(GetStudentBalanceQuery request, CancellationToken cancellationToken)
    {
        // Le Global Query Filter + la RLS bornent déjà à l'école courante : un élève ou une inscription
        // d'une autre école est structurellement invisible ici, jamais un solde d'un autre tenant.
        var row = await (
            from e in dbContext.Enrollments.AsNoTracking()
            join s in dbContext.Students.AsNoTracking() on e.StudentId equals s.Id
            join c in dbContext.Classrooms.AsNoTracking() on e.ClassroomId equals c.Id
            join y in dbContext.SchoolYears.AsNoTracking() on e.SchoolYearId equals y.Id
            where s.Id == request.StudentId && y.IsActive && e.Status != EnrollmentStatus.Cancelled
            select new
            {
                Enrollment = e,
                Student = s,
                Classroom = c,
                SchoolYear = y
            }
        ).FirstOrDefaultAsync(cancellationToken)
        ?? throw new KeyNotFoundException(
            $"Aucune inscription active pour l'élève {request.StudentId} sur l'année scolaire en cours.");

        var lines = await dbContext.EnrollmentFeeLines.AsNoTracking()
            .Where(l => l.EnrollmentId == row.Enrollment.Id)
            .OrderBy(l => l.IsRecurring)
            .ThenBy(l => l.Designation)
            .ToListAsync(cancellationToken);

        var payments = await dbContext.Payments.AsNoTracking()
            .Where(p => p.EnrollmentId == row.Enrollment.Id && p.Status != PaymentStatus.Cancelled)
            .OrderByDescending(p => p.PaidAt)
            .Select(p => new StudentBalancePaymentDto(
                p.Id,
                p.ReceiptNumber,
                p.Amount,
                p.Method.ToString(),
                p.PaidAt))
            .ToListAsync(cancellationToken);

        var now = timeProvider.GetUtcNow();
        var todayDateOnly = DateOnly.FromDateTime(now.DateTime);
        var remainingPaid = row.Enrollment.AmountPaid;
        var installments = new List<InstallmentDto>();

        foreach (var line in lines)
        {
            if (!line.IsRecurring || line.Months <= 1)
            {
                var instAmount = line.LineTotal;
                var paidForThis = Math.Min(remainingPaid, instAmount);
                remainingPaid -= paidForThis;
                var remainingDue = instAmount - paidForThis;
                string status = remainingDue <= 0 ? "Paid" : (paidForThis > 0 ? "Partial" : (row.SchoolYear.StartDate < todayDateOnly ? "Overdue" : "Pending"));
                var dueDateOffset = new DateTimeOffset(row.SchoolYear.StartDate.ToDateTime(TimeOnly.MinValue), now.Offset);

                installments.Add(new InstallmentDto(
                    $"LINE-{line.Id}",
                    line.Designation,
                    instAmount,
                    paidForThis,
                    remainingDue,
                    dueDateOffset,
                    status));
            }
            else
            {
                for (int m = 1; m <= line.Months; m++)
                {
                    var instAmount = line.UnitAmount;
                    var paidForThis = Math.Min(remainingPaid, instAmount);
                    remainingPaid -= paidForThis;
                    var remainingDue = instAmount - paidForThis;
                    var dueDate = row.SchoolYear.StartDate.AddMonths(m - 1);
                    string status = remainingDue <= 0 ? "Paid" : (paidForThis > 0 ? "Partial" : (dueDate < todayDateOnly ? "Overdue" : "Pending"));
                    var dueDateOffset = new DateTimeOffset(dueDate.ToDateTime(TimeOnly.MinValue), now.Offset);

                    installments.Add(new InstallmentDto(
                        $"MONTH-{line.Id}-{m}",
                        $"{line.Designation} (Mois {m})",
                        instAmount,
                        paidForThis,
                        remainingDue,
                        dueDateOffset,
                        status));
                }
            }
        }

        return new StudentBalanceDto(
            row.Enrollment.Id,
            row.Student.Id,
            row.Student.Matricule,
            row.Student.FullName,
            row.Classroom.Name,
            row.SchoolYear.Label,
            row.Enrollment.TotalDue,
            row.Enrollment.AmountPaid,
            row.Enrollment.TotalDue - row.Enrollment.AmountPaid,
            row.Enrollment.AmountPaid >= row.Enrollment.TotalDue ? "Paid" : "Partial",
            installments,
            payments);
    }
}
