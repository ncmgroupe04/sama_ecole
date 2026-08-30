using FluentValidation;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.StateIntegration.Commands.GenerateStudentMutationCertificate;

public class GenerateStudentMutationCertificateCommandValidator
    : AbstractValidator<GenerateStudentMutationCertificateCommand>
{
    public GenerateStudentMutationCertificateCommandValidator()
    {
        RuleFor(c => c.StudentId).NotEmpty();
        RuleFor(c => c.SchoolYearId).NotEmpty();

        RuleFor(c => c.Reason)
            .IsInEnum()
            .WithMessage("Motif de mutation inconnu.");

        // « Autre » sans précision produit une pièce officielle qui ne dit rien du motif — l'école
        // d'accueil et l'IEF la refusent. Le motif détaillé devient donc obligatoire dans ce seul cas.
        RuleFor(c => c.ReasonDetails)
            .NotEmpty()
            .When(c => c.Reason == StudentMutationReason.Autre)
            .WithMessage("Précisez le motif lorsque vous choisissez « Autre ».");

        RuleFor(c => c.ReasonDetails)
            .MaximumLength(300)
            .WithMessage("Le motif détaillé ne peut dépasser 300 caractères.");

        RuleFor(c => c.DestinationSchoolName)
            .MaximumLength(150)
            .WithMessage("Le nom de l'établissement de destination ne peut dépasser 150 caractères.");

        RuleFor(c => c.DestinationCity)
            .MaximumLength(100)
            .WithMessage("La ville de destination ne peut dépasser 100 caractères.");
    }
}
