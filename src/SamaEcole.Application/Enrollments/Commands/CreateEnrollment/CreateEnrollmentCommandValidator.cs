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

        RuleFor(x => x.BoardingStatus).IsInEnum();
        RuleFor(x => x.PreviousSchoolName).MaximumLength(150).NoHtml();

        // Un régime Interne sans chambre est une saisie incomplète — RoomId reste libre pour Externe (spec du 18/09 §3.3). Un
        // Demi-pensionnaire ne dort pas à l'internat (spec Pavillon/Lit, N7) : sa chambre est facultative et, si elle est
        // envoyée par l'ancien formulaire, ignorée.
        RuleFor(x => x.RoomId)
            .NotNull().WithMessage("Une chambre est requise pour un régime Interne.")
            .When(x => x.BoardingStatus == BoardingStatus.Interne);

        // Symétrique de la règle ci-dessus : un RoomId sur un régime Externe contournerait la garde du
        // module (le Handler n'entre dans la vérification IsInternatEnabled que si BoardingStatus !=
        // Externe) et fausserait le compte d'occupation d'une chambre (un Externe affecté consommerait
        // un lit dans le calcul de capacité alors qu'il n'est pas interne).
        RuleFor(x => x.RoomId)
            .Null()
            .When(x => x.BoardingStatus == BoardingStatus.Externe)
            .WithMessage("Un élève Externe ne peut pas être affecté à une chambre.");

        // Frais optionnels : forme de la liste seulement (le contrôle « est-ce bien un frais optionnel de cette
        // classe ? » suppose la base et vit dans le Handler). Un doublon ou un Guid vide est une saisie corrompue.
        RuleFor(x => x.OptionalFeeCategoryIds)
            .Must(ids => ids!.All(id => id != Guid.Empty))
            .WithMessage("Une catégorie de frais optionnelle est invalide.")
            .Must(ids => ids!.Distinct().Count() == ids!.Count)
            .WithMessage("Une même catégorie de frais est cochée plusieurs fois.")
            .When(x => x.OptionalFeeCategoryIds is not null);

        When(x => x.Type == EnrollmentType.ReEnrollment, () =>
        {
            RuleFor(x => x.StudentId)
                .NotNull().WithMessage("La réinscription exige de désigner un élève existant.");
        });

        When(x => x.Type == EnrollmentType.NewEnrollment, () =>
        {
            RuleFor(x => x.FullName).NotEmpty().MaximumLength(200).NoHtml();
            RuleFor(x => x.GuardianName).MaximumLength(200).NoHtml();
            RuleFor(x => x.GuardianPhone).MaximumLength(30).NoHtml().MustBeValidSenegalPhone();
            // Lieu de naissance OBLIGATOIRE pour une nouvelle inscription (feature E) : l'élève créé ici
            // suit la même règle que CreateStudentCommand, sans quoi la colonne NOT NULL rejetterait l'insert.
            RuleFor(x => x.BirthPlace)
                .NotEmpty().WithMessage("Le lieu de naissance est obligatoire.")
                .MaximumLength(200).NoHtml();
            RuleFor(x => x.Gender).Must(g => g is "M" or "F").WithMessage("Le genre doit être 'M' ou 'F'.");
            RuleFor(x => x.BirthDate)
                .NotNull().WithMessage("La date de naissance est obligatoire.")
                .Must(d => d < DateOnly.FromDateTime(DateTime.UtcNow))
                .WithMessage("La date de naissance doit être dans le passé.");
        });
    }
}
