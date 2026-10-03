using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Commands.UpdateFeeCategory;

public class UpdateFeeCategoryCommandHandler(IApplicationDbContext dbContext)
    : IRequestHandler<UpdateFeeCategoryCommand, Unit>
{
    public async Task<Unit> Handle(UpdateFeeCategoryCommand request, CancellationToken cancellationToken)
    {
        // Le Global Query Filter + la RLS bornent la recherche à l'école courante : viser la catégorie
        // d'une autre école renvoie 404, jamais une modification silencieuse.
        var category = await dbContext.FeeCategories
            .FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Catégorie de frais {request.Id} introuvable.");

        // Même règle qu'à la création (CreateFeeCategoryCommandValidator), revérifiée ici parce que le
        // validateur ne voit pas l'état persisté : IsBoardingFee n'est pas dans la commande.
        if (request.IsOptional && category.IsBoardingFee)
        {
            throw new ValidationException([
                new ValidationFailure(
                    nameof(request.IsOptional),
                    "Une catégorie de pension ne peut pas être optionnelle : son inclusion se choisit avec le régime d'hébergement.")
            ]);
        }

        category.IsOptional = request.IsOptional;

        await dbContext.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
