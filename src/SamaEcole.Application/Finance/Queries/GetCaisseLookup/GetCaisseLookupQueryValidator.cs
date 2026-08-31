using SamaEcole.Application.Common.Validation;
using FluentValidation;

namespace SamaEcole.Application.Finance.Queries.GetCaisseLookup;

/// <summary>
/// Validation de FORME : le terme de recherche (identifiant ou matricule) doit être présent et borné.
/// La résolution effective — GUID ? matricule ? élève introuvable ? — vit dans le Handler, elle
/// suppose la base.
/// </summary>
public class GetCaisseLookupQueryValidator : AbstractValidator<GetCaisseLookupQuery>
{
    public GetCaisseLookupQueryValidator()
    {
        RuleFor(x => x.Query)
            .NotEmpty().WithMessage("Indiquez l'identifiant ou le matricule de l'élève.")
            .MaximumLength(100)
            .NoHtml();
    }
}
