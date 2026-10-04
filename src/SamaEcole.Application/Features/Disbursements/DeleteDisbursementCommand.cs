using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;

namespace SamaEcole.Application.Features.Disbursements;

/// <summary>
/// DELETE /api/v1/finance/disbursements/{id} — ANNULE un décaissement par une contre-écriture.
///
/// Un décaissement est une écriture comptable (il alimente le solde réel, la trésorerie et la TVA déductible) :
/// il n'est ni supprimé physiquement ni masqué par une suppression logique (conception soft delete 2026-10-01
/// §3.3). La route et le verbe restent ceux de l'écran existant ; l'effet est une NOUVELLE ligne, aux montants
/// opposés, qui référence l'écriture d'origine. Tous les agrégats (somme des montants, TVA) s'annulent donc
/// d'eux-mêmes, et l'historique reste complet.
/// </summary>
//[Authorize(Roles = "SuperAdmin, Directeur, Finance")]
public record DeleteDisbursementCommand(Guid Id) : IRequest;

public class DeleteDisbursementCommandHandler(
    IApplicationDbContext context,
    TimeProvider timeProvider,
    IKpiCacheService kpiCache) : IRequestHandler<DeleteDisbursementCommand>
{
    public const string AlreadyReversedCode = "DISBURSEMENT_ALREADY_REVERSED";
    public const string CannotReverseReversalCode = "DISBURSEMENT_IS_REVERSAL";

    public async Task Handle(DeleteDisbursementCommand request, CancellationToken cancellationToken)
    {
        var original = await context.Disbursements.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(Disbursement), request.Id);

        if (original.ReversalOfId is not null)
        {
            throw new BusinessRuleException(
                "Cette écriture est déjà une annulation : elle ne peut pas être annulée à son tour.",
                CannotReverseReversalCode);
        }

        if (await context.Disbursements.AnyAsync(d => d.ReversalOfId == original.Id, cancellationToken))
        {
            throw new BusinessRuleException("Ce décaissement a déjà été annulé.", AlreadyReversedCode);
        }

        const string prefix = "Annulation — ";
        var reason = prefix + original.Reason;
        if (reason.Length > 255) reason = reason[..255];

        context.Disbursements.Add(new Disbursement
        {
            SchoolId = original.SchoolId,
            Reason = reason,
            Category = original.Category,
            Amount = -original.Amount,
            VatRate = original.VatRate,
            VatAmount = -original.VatAmount,
            PaymentMethod = original.PaymentMethod,
            // La contre-écriture est datée du jour de l'annulation : la période déjà déclarée n'est pas réécrite.
            Date = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime),
            Beneficiary = original.Beneficiary,
            ReversalOfId = original.Id
        });

        // Deux annulations simultanées : l'index unique sur ReversalOfId refuse la seconde (traduit en 409).
        await context.SaveChangesAsync(cancellationToken);

        // CreateDisbursementCommand invalide déjà ce même cache à la création : sans ce miroir,
        // TotalDisbursements/RealBalance du dashboard Finance resteraient faux jusqu'à 7 minutes.
        kpiCache.Invalidate(KpiCacheKeys.FinanceDashboard);
    }
}
