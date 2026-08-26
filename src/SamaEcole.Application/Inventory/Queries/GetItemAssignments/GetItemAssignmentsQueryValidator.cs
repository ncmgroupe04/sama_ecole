using FluentValidation;

namespace SamaEcole.Application.Inventory.Queries.GetItemAssignments;

public class GetItemAssignmentsQueryValidator : AbstractValidator<GetItemAssignmentsQuery>
{
    /// <summary>Même convention que GetStudentsQueryValidator : sans plafond, pageSize dicte la taille de la réponse.</summary>
    public const int MaxPageSize = 100;

    public GetItemAssignmentsQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThan(0);
        RuleFor(x => x.PageSize).GreaterThan(0).LessThanOrEqualTo(MaxPageSize);
    }
}
