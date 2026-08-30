using SamaEcole.Application.Common.Validation;
using FluentValidation;

namespace SamaEcole.Application.Teachers.Commands.UpdateTeacher;

public class UpdateTeacherCommandValidator : AbstractValidator<UpdateTeacherCommand>
{
    public UpdateTeacherCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200).NoHtml();

        // EmailAddress() vérifie peu au-delà du « @ » : sans NoHtml, « <script>@x.com » passerait.
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(255).NoHtml();
        RuleFor(x => x.Phone).MaximumLength(30).NoHtml().MustBeValidSenegalPhone();
        RuleFor(x => x.BirthDate).LessThan(DateOnly.FromDateTime(DateTime.UtcNow));
        RuleFor(x => x.BirthPlace).MaximumLength(200).NoHtml();
        RuleFor(x => x.Address).MaximumLength(300).NoHtml();

        RuleFor(x => x.PhotoUrl)
            .MaximumLength(500).WithMessage("L'URL de la photo ne peut pas dépasser 500 caractères.")
            .Must(BeAValidHttpUrl).When(x => !string.IsNullOrWhiteSpace(x.PhotoUrl))
            .WithMessage("L'URL de la photo doit être une adresse http(s) valide.");

        RuleFor(x => x.SubjectIds).NotEmpty()
            .WithMessage("Au moins une matière est requise.");

        RuleFor(x => x.SubjectIds)
            .Must(ids => ids.Distinct().Count() == ids.Count)
            .When(x => x.SubjectIds.Count > 0)
            .WithMessage("Une même matière ne peut être indiquée deux fois.");

        // Champs STATEDUC (JGK-M05) — mêmes règles qu'à la création.
        RuleFor(x => x.Gender).MaximumLength(1).NoHtml();
        RuleFor(x => x.AcademicQualification).IsInEnum();
        RuleFor(x => x.ProfessionalQualification).IsInEnum();
        RuleFor(x => x.CivilServiceStatus).IsInEnum();
        RuleFor(x => x.CivilServiceMatricule).MaximumLength(30).NoHtml();
        RuleFor(x => x.FirstAppointmentDate)
            .LessThanOrEqualTo(DateOnly.FromDateTime(DateTime.UtcNow))
            .When(x => x.FirstAppointmentDate.HasValue)
            .WithMessage("La date de première prise de service ne peut pas être dans le futur.");
    }

    private static bool BeAValidHttpUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
