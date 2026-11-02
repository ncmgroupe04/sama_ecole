using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Common.Validation;
using SamaEcole.Application.Finance.Services;
using SamaEcole.Domain.Entities;

namespace SamaEcole.Application.Finance.Commands.CancelFichePaie;

/// <summary>
/// POST /finance/payroll/{id}/cancel — Volume 1 §14.5. Annule une fiche de paie erronée SANS la supprimer : elle
/// reste consultable, marquée annulée, avec auteur, date et motif. Si elle était payée, le décaissement
/// « Salaires » est contre-passé. Pour la remplacer par une fiche corrigée, voir
/// <c>RegenerateFichePaieCommand</c>.
/// </summary>
public record CancelFichePaieCommand(Guid FichePaieId, string Reason, uint RowVersion)
    : IRequest<CancelFichePaieResult>, IAuditableRequest;

public record CancelFichePaieResult(Guid FichePaieId, bool PaymentReversed);

public class CancelFichePaieCommandValidator : AbstractValidator<CancelFichePaieCommand>
{
    public CancelFichePaieCommandValidator()
    {
        RuleFor(v => v.FichePaieId).NotEmpty();
        RuleFor(v => v.Reason)
            .NotEmpty().WithMessage("Le motif est obligatoire.")
            .MinimumLength(5).WithMessage("Le motif doit être explicite (5 caractères minimum).")
            .MaximumLength(500)
            .NoHtml();
    }
}

public class CancelFichePaieCommandHandler(
    IApplicationDbContext dbContext,
    ICurrentUserService currentUser,
    TimeProvider timeProvider,
    IKpiCacheService kpiCache) : IRequestHandler<CancelFichePaieCommand, CancelFichePaieResult>
{
    public async Task<CancelFichePaieResult> Handle(CancelFichePaieCommand request, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        // Filtre de tenant + RLS : viser la fiche d'une autre école renvoie 404, jamais une modification.
        var fiche = await dbContext.FichePaies.FirstOrDefaultAsync(f => f.Id == request.FichePaieId, cancellationToken)
            ?? throw new NotFoundException(nameof(FichePaie), request.FichePaieId);

        // Verrou optimiste (AGENTS.md règle #5) : la version lue par le client devient l'origine attendue de xmin.
        dbContext.SetOriginalConcurrencyToken(fiche, request.RowVersion);

        var reversed = await FichePaieCancellation.ApplyAsync(
            dbContext, fiche, request.Reason, actorId, timeProvider.GetUtcNow(), cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        if (reversed) kpiCache.Invalidate(KpiCacheKeys.FinanceDashboard);

        return new CancelFichePaieResult(fiche.Id, reversed);
    }
}
