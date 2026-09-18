using SamaEcole.Application.Common.Validation;
using FluentValidation;

namespace SamaEcole.Application.Subjects.Commands.CreateSubject;

public class CreateSubjectCommandValidator : AbstractValidator<CreateSubjectCommand>
{
    public CreateSubjectCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(80).NoHtml();

        // Module Coran/Franco-Arabe : optionnel, texte libre (aucune traduction automatique).
        RuleFor(x => x.NameAr).MaximumLength(80).NoHtml();

        // Niveau LIBRE, comme pour les classes (openapi.yaml) : primaire, collège et lycée ne
        // découpent pas leur scolarité de la même façon. Imposer une énumération exclurait des écoles.
        RuleFor(x => x.Level).NotEmpty().MaximumLength(50).NoHtml();

        RuleFor(x => x.Coefficient)
            .GreaterThan(0).WithMessage("Le coefficient doit être supérieur à zéro.")
            .LessThanOrEqualTo(20).WithMessage("Le coefficient annoncé semble irréaliste (maximum 20).");

        // Structure d'évaluation (grilles APC). Forme uniquement : l'existence du domaine et la
        // profondeur de la hiérarchie dépendent de la base, donc du Handler (SubjectHierarchyGuard).
        RuleFor(x => x.MaxScore)
            .Must(SubjectStructureRules.IsValidMaxScore).WithMessage(SubjectStructureRules.MaxScoreMessage);

        RuleFor(x => x.DisplayOrder)
            .InclusiveBetween(0, 999).WithMessage("L'ordre d'affichage doit être compris entre 0 et 999.");

        RuleFor(x => x.Column1Header).MaximumLength(40).NoHtml();
        RuleFor(x => x.Column2Header).MaximumLength(40).NoHtml();
    }
}
