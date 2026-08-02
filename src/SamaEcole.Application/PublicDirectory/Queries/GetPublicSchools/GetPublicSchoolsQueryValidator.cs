using SamaEcole.Application.Common.Validation;
using SamaEcole.Domain.Enums;
using FluentValidation;

namespace SamaEcole.Application.PublicDirectory.Queries.GetPublicSchools;

public class GetPublicSchoolsQueryValidator : AbstractValidator<GetPublicSchoolsQuery>
{
    /// <summary>
    /// Plafond de pageSize. Même raisonnement que GetStudentsQueryValidator, mais ENCORE plus strict
    /// (48 contre 100) : cet endpoint est anonyme, donc ouvert à n'importe qui sur Internet. Sans
    /// borne, « ?pageSize=1000000 » transformerait l'annuaire en levier d'épuisement du serveur.
    /// </summary>
    public const int MaxPageSize = 48;

    public GetPublicSchoolsQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThan(0);
        RuleFor(x => x.PageSize).GreaterThan(0).LessThanOrEqualTo(MaxPageSize);

        RuleFor(x => x.Search).MaximumLength(100).NoHtml();
        RuleFor(x => x.City).MaximumLength(120).NoHtml();
        RuleFor(x => x.Region).MaximumLength(120).NoHtml();

        // Restreint aux valeurs réelles de l'énumération : un cycle libre serait comparé à un tableau
        // de textes en base sans jamais correspondre, renvoyant silencieusement une page vide — un
        // 422 explicite vaut mieux qu'un annuaire qui paraît vide à cause d'une faute de frappe.
        RuleFor(x => x.Cycle)
            .Must(cycle => cycle is null || Enum.TryParse<CycleType>(cycle, ignoreCase: true, out _))
            .WithMessage($"Cycle inconnu. Valeurs acceptées : {string.Join(", ", Enum.GetNames<CycleType>())}.");
    }
}
