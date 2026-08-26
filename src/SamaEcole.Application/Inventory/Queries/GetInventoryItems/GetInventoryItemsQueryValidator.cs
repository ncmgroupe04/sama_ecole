using SamaEcole.Application.Common.Validation;
using FluentValidation;

namespace SamaEcole.Application.Inventory.Queries.GetInventoryItems;

public class GetInventoryItemsQueryValidator : AbstractValidator<GetInventoryItemsQuery>
{
    /// <summary>
    /// Plafond de pageSize : sans lui, `?pageSize=1000000` transforme une liste en déni de service
    /// — la borne n'est pas du confort, c'est la seule chose qui empêche le client de dicter la
    /// taille de la réponse. Même convention que GetStudentsQueryValidator.
    /// </summary>
    public const int MaxPageSize = 100;

    public GetInventoryItemsQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThan(0);
        RuleFor(x => x.PageSize).GreaterThan(0).LessThanOrEqualTo(MaxPageSize);
        RuleFor(x => x.Search).MaximumLength(100).NoHtml();
    }
}
