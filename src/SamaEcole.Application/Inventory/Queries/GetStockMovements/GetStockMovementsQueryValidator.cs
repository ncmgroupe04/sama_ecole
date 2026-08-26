using FluentValidation;

namespace SamaEcole.Application.Inventory.Queries.GetStockMovements;

public class GetStockMovementsQueryValidator : AbstractValidator<GetStockMovementsQuery>
{
    /// <summary>Même convention que GetStudentsQueryValidator : sans plafond, pageSize dicte la taille de la réponse.</summary>
    public const int MaxPageSize = 100;

    public GetStockMovementsQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThan(0);
        RuleFor(x => x.PageSize).GreaterThan(0).LessThanOrEqualTo(MaxPageSize);
        RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From).When(x => x.From.HasValue && x.To.HasValue)
            .WithMessage("La date de fin ne peut pas précéder la date de début.");
    }
}
