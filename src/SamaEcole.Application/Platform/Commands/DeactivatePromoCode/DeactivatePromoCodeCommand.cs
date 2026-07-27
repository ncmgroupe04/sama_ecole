using SamaEcole.Application.Common.Interfaces;
using MediatR;

namespace SamaEcole.Application.Platform.Commands.DeactivatePromoCode;

/// <summary>
/// POST /admin/promo-codes/{id}/deactivate — réservé au Super Admin. Bascule IsActive à false : pas
/// de suppression physique (AGENTS.md règle #6), un code désactivé reste visible dans l'historique et
/// dans le décompte des écoles qui en ont déjà bénéficié.
/// </summary>
public record DeactivatePromoCodeCommand(Guid Id) : IRequest;

public class DeactivatePromoCodeCommandHandler(IApplicationDbContext dbContext)
    : IRequestHandler<DeactivatePromoCodeCommand>
{
    public async Task Handle(DeactivatePromoCodeCommand request, CancellationToken cancellationToken)
    {
        var promoCode = await dbContext.PromoCodes.FindAsync([request.Id], cancellationToken)
            ?? throw new KeyNotFoundException("Code promo introuvable.");

        promoCode.IsActive = false;

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
