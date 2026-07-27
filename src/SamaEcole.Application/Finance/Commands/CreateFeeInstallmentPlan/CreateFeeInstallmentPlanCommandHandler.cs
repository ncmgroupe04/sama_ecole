using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ValidationException = SamaEcole.Application.Common.Exceptions.ValidationException;

namespace SamaEcole.Application.Finance.Commands.CreateFeeInstallmentPlan;

public class CreateFeeInstallmentPlanCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider)
    : IRequestHandler<CreateFeeInstallmentPlanCommand, Guid>
{
    public async Task<Guid> Handle(CreateFeeInstallmentPlanCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        // Le Global Query Filter borne déjà à l'école courante : une inscription d'une autre école est
        // structurellement introuvable ici.
        var enrollment = await dbContext.Enrollments.AsNoTracking()
            .Where(e => e.Id == request.EnrollmentId && e.Status != EnrollmentStatus.Cancelled)
            .Select(e => new { e.Id, e.TotalDue })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Inscription introuvable ou annulée : {request.EnrollmentId}");

        // La somme des échéances doit égaler EXACTEMENT le montant dû : un échéancier qui laisse un
        // reliquat non couvert (ou en réclame davantage) romprait le lien entre le solde affiché
        // (Enrollment.TotalDue - AmountPaid) et le calendrier qui le détaille.
        var sum = request.Installments.Sum(i => i.Amount);
        if (sum != enrollment.TotalDue)
        {
            throw new ValidationException([
                new ValidationFailure(
                    nameof(request.Installments),
                    $"La somme des échéances ({sum:N0} FCFA) doit être égale au montant dû de l'inscription ({enrollment.TotalDue:N0} FCFA).")
            ]);
        }

        return await dbContext.ExecuteInTransactionAsync(async ct =>
        {
            // Un plan négocié en remplace un autre : l'ancien reste consultable (Cancelled), jamais
            // supprimé (règle #6) — l'index unique partiel de FeeInstallmentPlanConfiguration n'accepte
            // qu'un seul plan Active par inscription.
            var existingActivePlan = await dbContext.FeeInstallmentPlans
                .Where(p => p.EnrollmentId == request.EnrollmentId && p.Status == FeeInstallmentPlanStatus.Active)
                .SingleOrDefaultAsync(ct);

            if (existingActivePlan is not null)
            {
                existingActivePlan.Status = FeeInstallmentPlanStatus.Cancelled;
            }

            var plan = new FeeInstallmentPlan
            {
                SchoolId = schoolId,
                EnrollmentId = request.EnrollmentId,
                Status = FeeInstallmentPlanStatus.Active,
                Reason = request.Reason,
                CreatedFromClassroomTemplate = false
            };
            dbContext.FeeInstallmentPlans.Add(plan);

            // Trié par date d'échéance : l'ordre de saisie n'a pas d'importance, mais l'allocation en
            // cascade des paiements (InstallmentScheduleCalculator) doit toujours solder la plus
            // ancienne échéance en premier.
            var ordered = request.Installments.OrderBy(i => i.DueDate).ToList();
            for (var index = 0; index < ordered.Count; index++)
            {
                dbContext.FeeInstallments.Add(new FeeInstallment
                {
                    SchoolId = schoolId,
                    FeeInstallmentPlanId = plan.Id,
                    SequenceNo = index + 1,
                    Label = ordered[index].Label,
                    Amount = ordered[index].Amount,
                    DueDate = ordered[index].DueDate
                });
            }

            await dbContext.SaveChangesAsync(ct);
            return plan.Id;
        }, cancellationToken);
    }
}
