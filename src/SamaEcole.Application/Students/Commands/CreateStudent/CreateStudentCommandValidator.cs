using SamaEcole.Application.Common.Validation;
using FluentValidation;

namespace SamaEcole.Application.Students.Commands.CreateStudent;

public class CreateStudentCommandValidator : AbstractValidator<CreateStudentCommand>
{
    public CreateStudentCommandValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200).NoHtml();
        // Lieu de naissance OBLIGATOIRE (feature E) : exigence juridique/académique au Sénégal.
        RuleFor(x => x.BirthPlace)
            .NotEmpty().WithMessage("Le lieu de naissance est obligatoire.")
            .MaximumLength(200).NoHtml();
        RuleFor(x => x.Gender).Must(g => g is "M" or "F").WithMessage("Le genre doit être 'M' ou 'F'.");
        RuleFor(x => x.ClassroomId).NotEmpty();
        RuleFor(x => x.BirthDate).LessThan(DateOnly.FromDateTime(DateTime.UtcNow));

        // Même contrat que LogoUrl (UpdateCurrentSchoolCommandValidator) : une adresse http(s), jamais
        // un file:// ou javascript: — la photo n'est jamais téléversée, seulement référencée par URL.
        RuleFor(x => x.PhotoUrl)
            .MaximumLength(500).WithMessage("L'URL de la photo ne peut pas dépasser 500 caractères.")
            .Must(BeAValidHttpUrl).When(x => !string.IsNullOrWhiteSpace(x.PhotoUrl))
            .WithMessage("L'URL de la photo doit être une adresse http(s) valide.");

        RuleFor(x => x.PhotoData).MustBeValidPhotoData();
    }

    private static bool BeAValidHttpUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
