using FluentValidation;

namespace SamaEcole.Application.Schools.Commands.UpdateCurrentSchool;

/// <summary>
/// Le nom est la seule donnée obligatoire (il figure en gros sur le reçu, il ne peut pas être vide).
/// Adresse, téléphone et logo sont facultatifs. Le logo, s'il est fourni, doit être une URL http(s) :
/// le reçu ne pointera jamais un <c>file://</c> ou un <c>javascript:</c>.
/// </summary>
public class UpdateCurrentSchoolCommandValidator : AbstractValidator<UpdateCurrentSchoolCommand>
{
    public UpdateCurrentSchoolCommandValidator()
    {
        RuleFor(c => c.Name)
            .NotEmpty().WithMessage("Le nom de l'établissement est obligatoire.")
            .MaximumLength(200).WithMessage("Le nom ne peut pas dépasser 200 caractères.");

        RuleFor(c => c.Address)
            .MaximumLength(300).WithMessage("L'adresse ne peut pas dépasser 300 caractères.");

        RuleFor(c => c.Phone)
            .MaximumLength(30).WithMessage("Le téléphone ne peut pas dépasser 30 caractères.");

        RuleFor(c => c.LogoUrl)
            .MaximumLength(500).WithMessage("L'URL du logo ne peut pas dépasser 500 caractères.")
            .Must(BeAValidHttpUrl).When(c => !string.IsNullOrWhiteSpace(c.LogoUrl))
            .WithMessage("L'URL du logo doit être une adresse http(s) valide.");
    }

    private static bool BeAValidHttpUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
