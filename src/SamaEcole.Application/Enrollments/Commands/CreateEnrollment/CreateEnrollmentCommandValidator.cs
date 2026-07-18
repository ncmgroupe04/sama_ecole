using SamaEcole.Application.Common.Validation;
using SamaEcole.Domain.Enums;
using FluentValidation;

namespace SamaEcole.Application.Enrollments.Commands.CreateEnrollment;

/// <summary>
/// Les règles dépendent du <see cref="CreateEnrollmentCommand.Type"/> : une nouvelle inscription
/// exige l'état civil de l'élève à créer, une réinscription exige au contraire l'identifiant d'un
/// élève déjà connu. Le contrôle d'appartenance à l'école (la classe et l'élève existent DANS ce
/// tenant) reste dans le Handler : il suppose la base, pas seulement la forme de la requête.
/// </summary>
public class CreateEnrollmentCommandValidator : AbstractValidator<CreateEnrollmentCommand>
{
    public CreateEnrollmentCommandValidator()
    {
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.ClassroomId).NotEmpty();

        When(x => x.Type == EnrollmentType.ReEnrollment, () =>
        {
            RuleFor(x => x.StudentId)
                .NotNull().WithMessage("La réinscription exige de désigner un élève existant.");
        });

        When(x => x.Type == EnrollmentType.NewEnrollment, () =>
        {
            RuleFor(x => x.FullName).NotEmpty().MaximumLength(200).NoHtml();
            RuleFor(x => x.Gender).Must(g => g is "M" or "F").WithMessage("Le genre doit être 'M' ou 'F'.");
            RuleFor(x => x.BirthDate)
                .NotNull().WithMessage("La date de naissance est obligatoire.")
                .Must(d => d < DateOnly.FromDateTime(DateTime.UtcNow))
                .WithMessage("La date de naissance doit être dans le passé.");
        });
    }
}
