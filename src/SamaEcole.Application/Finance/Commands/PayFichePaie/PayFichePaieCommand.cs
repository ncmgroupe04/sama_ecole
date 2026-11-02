using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Services;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Finance.Commands.PayFichePaie;

/// <summary>
/// POST /finance/payroll/{id}/pay — Volume 1 §14.6. Enregistre le règlement du salaire NET d'une fiche : crée un
/// décaissement de catégorie « Salaires » (trésorerie, comptabilité) au nom de l'employé — y compris pour une
/// personne SANS compte (gardien…) — et marque la fiche payée. Le bulletin devient alors un reçu de paiement à
/// faire signer. Une fiche ne se paie qu'une fois (un second appel est refusé, pas dupliqué) ; la corriger passe par
/// son annulation, qui contre-passe le décaissement.
/// </summary>
public record PayFichePaieCommand(Guid FichePaieId, PaymentMethod PaymentMethod, DateOnly Date, uint RowVersion)
    : IRequest<PayFichePaieResult>, IAuditableRequest;

public record PayFichePaieResult(Guid FichePaieId, Guid DisbursementId, decimal Amount);

public class PayFichePaieCommandValidator : AbstractValidator<PayFichePaieCommand>
{
    public PayFichePaieCommandValidator()
    {
        RuleFor(v => v.FichePaieId).NotEmpty();
        RuleFor(v => v.PaymentMethod).IsInEnum();
        RuleFor(v => v.Date).NotEmpty();
    }
}

public class PayFichePaieCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    ICurrentUserService currentUser,
    TimeProvider timeProvider,
    IKpiCacheService kpiCache) : IRequestHandler<PayFichePaieCommand, PayFichePaieResult>
{
    public async Task<PayFichePaieResult> Handle(PayFichePaieCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId ?? throw new UnauthorizedAccessException();
        var actorId = currentUser.UserId ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        var fiche = await dbContext.FichePaies
            .Include(f => f.EmployeeContract).ThenInclude(c => c.Teacher)
            .Include(f => f.EmployeeContract).ThenInclude(c => c.User)
            .FirstOrDefaultAsync(f => f.Id == request.FichePaieId, cancellationToken)
            ?? throw new NotFoundException(nameof(FichePaie), request.FichePaieId);

        if (fiche.CancelledAt is not null)
        {
            throw new BusinessRuleException("Cette fiche de paie est annulée : elle ne peut pas être payée.");
        }

        if (fiche.PaidAt is not null)
        {
            throw new BusinessRuleException("Cette fiche de paie a déjà été payée.");
        }

        if (fiche.NetSalary <= 0)
        {
            throw new BusinessRuleException("Le net à payer est nul : il n'y a rien à régler sur cette fiche.");
        }

        dbContext.SetOriginalConcurrencyToken(fiche, request.RowVersion);

        var name = EmployeeIdentity.FullName(fiche.EmployeeContract);
        var beneficiary = name.Length > 150 ? name[..150] : name;
        var reason = $"Salaire {fiche.Month:00}/{fiche.Year} — {name}";
        if (reason.Length > 255) reason = reason[..255];

        var disbursement = new Disbursement
        {
            SchoolId = schoolId,
            Reason = reason,
            Category = DisbursementCategory.Salaires,
            Amount = fiche.NetSalary,
            // Une rémunération n'est jamais une livraison taxable (cf. Disbursement.VatRate).
            VatRate = null,
            VatAmount = 0m,
            PaymentMethod = request.PaymentMethod,
            Date = request.Date,
            Beneficiary = beneficiary
        };

        dbContext.Disbursements.Add(disbursement);
        fiche.PaidAt = timeProvider.GetUtcNow();
        fiche.PaidByUserId = actorId;
        fiche.PaidWith = request.PaymentMethod;
        fiche.DisbursementId = disbursement.Id;

        // Un seul SaveChanges : décaissement et fiche payée sont écrits ensemble ou pas du tout.
        await dbContext.SaveChangesAsync(cancellationToken);

        kpiCache.Invalidate(KpiCacheKeys.FinanceDashboard);

        return new PayFichePaieResult(fiche.Id, disbursement.Id, disbursement.Amount);
    }
}
