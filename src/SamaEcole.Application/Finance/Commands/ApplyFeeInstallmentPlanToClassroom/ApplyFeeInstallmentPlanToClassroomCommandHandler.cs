using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Commands.ApplyFeeInstallmentPlanToClassroom;

public class ApplyFeeInstallmentPlanToClassroomCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    TimeProvider timeProvider)
    : IRequestHandler<ApplyFeeInstallmentPlanToClassroomCommand, ApplyFeeInstallmentPlanToClassroomResult>
{
    public async Task<ApplyFeeInstallmentPlanToClassroomResult> Handle(
        ApplyFeeInstallmentPlanToClassroomCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().DateTime);

        // Seules les inscriptions CONFIRMÉES de l'année scolaire ACTIVE reçoivent un échéancier — une
        // inscription annulée, transférée ou d'une année révolue n'a plus rien à échelonner.
        var enrollments = await (
            from e in dbContext.Enrollments
            join y in dbContext.SchoolYears.AsNoTracking() on e.SchoolYearId equals y.Id
            where e.ClassroomId == request.ClassroomId && e.Status == EnrollmentStatus.Confirmed && y.IsActive
            select e)
            .ToListAsync(cancellationToken);

        if (enrollments.Count == 0)
        {
            return new ApplyFeeInstallmentPlanToClassroomResult(0, 0);
        }

        var appliedCount = 0;
        var skippedCount = 0;

        // Trié par décalage : l'ordre de saisie n'a pas d'importance, mais SequenceNo doit refléter
        // l'ordre chronologique pour que InstallmentScheduleCalculator alloue les paiements à la bonne
        // échéance en premier (même règle que CreateFeeInstallmentPlanCommand).
        var orderedTemplate = request.Template.OrderBy(t => t.OffsetDays).ToList();

        await dbContext.ExecuteInTransactionAsync(async ct =>
        {
            var existingActivePlans = await dbContext.FeeInstallmentPlans
                .Where(p => enrollments.Select(e => e.Id).Contains(p.EnrollmentId)
                    && p.Status == FeeInstallmentPlanStatus.Active)
                .ToListAsync(ct);
            var existingActiveByEnrollment = existingActivePlans.ToDictionary(p => p.EnrollmentId);

            foreach (var enrollment in enrollments)
            {
                if (enrollment.TotalDue <= 0)
                {
                    // Rien à devoir : appliquer un échéancier romprait l'invariant "somme = TotalDue"
                    // avec une seule échéance à 0 FCFA, sans aucun sens pour la famille.
                    skippedCount++;
                    continue;
                }

                if (existingActiveByEnrollment.TryGetValue(enrollment.Id, out var existingPlan))
                {
                    existingPlan.Status = FeeInstallmentPlanStatus.Cancelled;
                }

                var plan = new FeeInstallmentPlan
                {
                    SchoolId = schoolId,
                    EnrollmentId = enrollment.Id,
                    Status = FeeInstallmentPlanStatus.Active,
                    Reason = request.Reason,
                    CreatedFromClassroomTemplate = true
                };
                dbContext.FeeInstallmentPlans.Add(plan);

                // La DERNIÈRE échéance absorbe l'écart d'arrondi : la somme doit égaler EXACTEMENT
                // TotalDue (même invariant que CreateFeeInstallmentPlanCommand), jamais un centime de
                // trop ou de moins à cause d'un pourcentage qui ne tombe pas rond.
                var amounts = new decimal[orderedTemplate.Count];
                var allocated = 0m;
                for (var i = 0; i < orderedTemplate.Count - 1; i++)
                {
                    amounts[i] = Math.Round(enrollment.TotalDue * orderedTemplate[i].Percentage, 0);
                    allocated += amounts[i];
                }
                amounts[^1] = enrollment.TotalDue - allocated;

                for (var i = 0; i < orderedTemplate.Count; i++)
                {
                    dbContext.FeeInstallments.Add(new FeeInstallment
                    {
                        SchoolId = schoolId,
                        FeeInstallmentPlanId = plan.Id,
                        SequenceNo = i + 1,
                        Label = orderedTemplate[i].Label,
                        Amount = amounts[i],
                        DueDate = today.AddDays(orderedTemplate[i].OffsetDays)
                    });
                }

                appliedCount++;
            }

            await dbContext.SaveChangesAsync(ct);
            return 0;
        }, cancellationToken);

        return new ApplyFeeInstallmentPlanToClassroomResult(appliedCount, skippedCount);
    }
}
