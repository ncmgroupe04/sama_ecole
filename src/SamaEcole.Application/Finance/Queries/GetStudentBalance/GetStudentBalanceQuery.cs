using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Common;
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
    IReadOnlyList<StudentBalancePaymentDto> PaymentsHistory,

    /// <summary>
    /// Somme des <see cref="InstallmentDto.RemainingDue"/> des échéances de l'ENGAGEMENT INITIAL
    /// (<see cref="InstallmentDto.IsInitialScope"/>) : ce que le tuteur doit régler EN UNE FOIS à la
    /// caisse, muni de la fiche du secrétariat — frais ponctuels + premier mois de scolarité, jamais
    /// le cumul annuel. Montant proposé par défaut au guichet rapide (pop-up d'encaissement).
    /// </summary>
    decimal DueNowTotal);

public record InstallmentDto(
    string Id,
    string Designation,
    decimal Amount,
    decimal AmountPaid,
    decimal RemainingDue,
    DateTimeOffset DueDate,
    string Status,

    /// <summary>Voir <see cref="InstallmentScheduleCalculator.CalculatedInstallment.IsInitialScope"/> :
    /// échéance de l'engagement initial (frais ponctuel entier, ou premier mois d'un frais récurrent).</summary>
    bool IsInitialScope,

    /// <summary>Catégorie de frais d'origine — sert à ventiler le reçu de caisse quand plusieurs
    /// échéances sont réglées d'un coup. <c>null</c> pour un échéancier personnalisé.</summary>
    Guid? FeeCategoryId);

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

        // Un plan personnalisé ACTIF (Étape 5) prime sur la synthèse mensuelle uniforme — au plus un
        // existe par inscription (index unique partiel, FeeInstallmentPlanConfiguration).
        var customInstallments = await dbContext.FeeInstallmentPlans.AsNoTracking()
            .Where(p => p.EnrollmentId == row.Enrollment.Id && p.Status == FeeInstallmentPlanStatus.Active)
            .SelectMany(p => dbContext.FeeInstallments.Where(i => i.FeeInstallmentPlanId == p.Id))
            .OrderBy(i => i.SequenceNo)
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

        var calculated = InstallmentScheduleCalculator.Calculate(
            row.Enrollment.AmountPaid, todayDateOnly, lines, row.SchoolYear.StartDate, customInstallments);

        var installments = calculated
            .Select(c => new InstallmentDto(
                c.Id,
                c.Label,
                c.Amount,
                c.AmountPaid,
                c.RemainingDue,
                new DateTimeOffset(c.DueDate.ToDateTime(TimeOnly.MinValue), now.Offset),
                c.Status.ToString(),
                c.IsInitialScope,
                c.FeeCategoryId))
            .ToList();

        var dueNowTotal = installments
            .Where(i => i.IsInitialScope)
            .Sum(i => i.RemainingDue);

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
            payments,
            dueNowTotal);
    }
}
