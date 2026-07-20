using SamaEcole.Application.Common.Validation;
using FluentValidation;

namespace SamaEcole.Application.Teachers.Commands.CreateTeacher;

public class CreateTeacherCommandValidator : AbstractValidator<CreateTeacherCommand>
{
    public CreateTeacherCommandValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200).NoHtml();

        // EmailAddress() (mode ASP.NET Core) vérifie peu de choses au-delà du « @ » : sans NoHtml,
        // « <script>@x.com » passerait (JGK-F01).
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(255).NoHtml();
        RuleFor(x => x.Phone).MaximumLength(30).NoHtml();
        RuleFor(x => x.BirthDate).LessThan(DateOnly.FromDateTime(DateTime.UtcNow));
        RuleFor(x => x.BirthPlace).MaximumLength(200).NoHtml();

        // Même contrat que LogoUrl / Student.PhotoUrl : une adresse http(s), jamais un file:// ou
        // javascript: — la photo n'est jamais téléversée, seulement référencée par URL.
        RuleFor(x => x.PhotoUrl)
            .MaximumLength(500).WithMessage("L'URL de la photo ne peut pas dépasser 500 caractères.")
            .Must(BeAValidHttpUrl).When(x => !string.IsNullOrWhiteSpace(x.PhotoUrl))
            .WithMessage("L'URL de la photo doit être une adresse http(s) valide.");

        RuleFor(x => x.PhotoData).MustBeValidPhotoData();

        RuleFor(x => x.SubjectIds).NotEmpty()
            .WithMessage("Au moins une matière est requise.");

        RuleFor(x => x.SubjectIds)
            .Must(ids => ids.Distinct().Count() == ids.Count)
            .When(x => x.SubjectIds.Count > 0)
            .WithMessage("Une même matière ne peut être indiquée deux fois.");
    }

    private static bool BeAValidHttpUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
