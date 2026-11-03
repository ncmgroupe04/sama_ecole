using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Common.Validation;
using SamaEcole.Application.Finance.Services;
using SamaEcole.Domain.Entities;

namespace SamaEcole.Application.Finance.Commands.RegenerateFichePaie;

/// <summary>
/// POST /finance/payroll/{id}/regenerate — Volume 1 §14.5. Annule la fiche erronée (voir
/// <see cref="FichePaieCancellation"/>) puis en génère une NOUVELLE pour la même période, calculée sur les termes
/// ACTUELS du contrat (corriger d'abord le contrat, via sa modification). Les heures reprennent celles de la fiche
/// annulée, sauf si <see cref="HoursWorked"/> est fourni ; la prime de transport reprend celle du contrat, sauf si
/// <see cref="TransportAllowance"/> est fournie. Tout se fait dans une seule transaction : jamais d'état
/// intermédiaire « annulée sans remplaçante ».
/// </summary>
public record RegenerateFichePaieCommand(
    Guid FichePaieId,
    string Reason,
    uint RowVersion,
    decimal? HoursWorked = null,
    decimal? TransportAllowance = null) : IRequest<RegenerateFichePaieResult>, IAuditableRequest;

public record RegenerateFichePaieResult(Guid CancelledFichePaieId, Guid NewFichePaieId);

public class RegenerateFichePaieCommandValidator : AbstractValidator<RegenerateFichePaieCommand>
{
    public RegenerateFichePaieCommandValidator()
    {
        RuleFor(v => v.FichePaieId).NotEmpty();
        RuleFor(v => v.Reason)
            .NotEmpty().WithMessage("Le motif est obligatoire.")
            .MinimumLength(5).WithMessage("Le motif doit être explicite (5 caractères minimum).")
            .MaximumLength(500)
            .NoHtml();
        RuleFor(v => v.HoursWorked).GreaterThanOrEqualTo(0).When(v => v.HoursWorked.HasValue);
        RuleFor(v => v.TransportAllowance).GreaterThanOrEqualTo(0).When(v => v.TransportAllowance.HasValue);
    }
}

public class RegenerateFichePaieCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    ICurrentUserService currentUser,
    TimeProvider timeProvider,
    IKpiCacheService kpiCache) : IRequestHandler<RegenerateFichePaieCommand, RegenerateFichePaieResult>
{
    public async Task<RegenerateFichePaieResult> Handle(RegenerateFichePaieCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId ?? throw new UnauthorizedAccessException();
        var actorId = currentUser.UserId ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        var old = await dbContext.FichePaies.FirstOrDefaultAsync(f => f.Id == request.FichePaieId, cancellationToken)
            ?? throw new NotFoundException(nameof(FichePaie), request.FichePaieId);

        var contract = await dbContext.EmployeeContracts
            .FirstOrDefaultAsync(c => c.Id == old.EmployeeContractId, cancellationToken)
            ?? throw new NotFoundException(nameof(EmployeeContract), old.EmployeeContractId);

        if (contract.EndDate is not null)
        {
            throw new BusinessRuleException(
                "Ce contrat est clôturé : impossible de régénérer une fiche de paie. Annulez la fiche si elle est erronée.");
        }

        dbContext.SetOriginalConcurrencyToken(old, request.RowVersion);

        var (newId, reversed) = await dbContext.ExecuteInTransactionAsync(async ct =>
        {
            var now = timeProvider.GetUtcNow();

            // 1) Annulation d'abord et sauvegardée seule : l'unicité (contrat, mois, année) ne porte que sur les
            // fiches non annulées, donc la remplaçante ne peut être insérée qu'une fois l'ancienne annulée en base.
            var didReverse = await FichePaieCancellation.ApplyAsync(dbContext, old, request.Reason, actorId, now, ct);
            await dbContext.SaveChangesAsync(ct);

            // 2) Remplaçante, calculée sur les termes actuels du contrat.
            var replacement = PayrollCalculator.CalculateFichePaie(
                schoolId,
                contract.Id,
                old.Month,
                old.Year,
                contract.BaseSalary,
                contract.HourlyRate,
                request.HoursWorked ?? old.HoursWorked,
                request.TransportAllowance ?? contract.TransportAllowance,
                contract.ApplyStatutoryDeductions);

            dbContext.FichePaies.Add(replacement);
            old.ReplacedByFichePaieId = replacement.Id;
            await dbContext.SaveChangesAsync(ct);

            return (replacement.Id, didReverse);
        }, cancellationToken);

        if (reversed) kpiCache.Invalidate(KpiCacheKeys.FinanceDashboard);

        return new RegenerateFichePaieResult(old.Id, newId);
    }
}
