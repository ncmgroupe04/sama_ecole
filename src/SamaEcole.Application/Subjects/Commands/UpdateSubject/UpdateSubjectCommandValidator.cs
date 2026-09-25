using SamaEcole.Application.Common.Validation;
using FluentValidation;

namespace SamaEcole.Application.Subjects.Commands.UpdateSubject;

public class UpdateSubjectCommandValidator : AbstractValidator<UpdateSubjectCommand>
{
    public UpdateSubjectCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(80).NoHtml();

        // Module Coran/Franco-Arabe : optionnel, texte libre (aucune traduction automatique).
        RuleFor(x => x.NameAr).MaximumLength(80).NoHtml();

        // Niveau LIBRE, comme à la création : aucune liste figée de niveaux.
        RuleFor(x => x.Level).NotEmpty().MaximumLength(50).NoHtml();

        RuleFor(x => x.Coefficient)
            .GreaterThan(0).WithMessage("Le coefficient doit être supérieur à zéro.")
            .LessThanOrEqualTo(20).WithMessage("Le coefficient annoncé semble irréaliste (maximum 20).");

        // Mêmes bornes qu'à la création, à la lettre (SubjectStructureRules) : une grille enregistrée
        // doit rester modifiable.
        RuleFor(x => x.MaxScore)
            .Must(SubjectStructureRules.IsValidMaxScore).WithMessage(SubjectStructureRules.MaxScoreMessage);

        RuleFor(x => x.DisplayOrder)
            .InclusiveBetween(0, 999).WithMessage("L'ordre d'affichage doit être compris entre 0 et 999.");

        RuleFor(x => x.Column1Header).MaximumLength(40).NoHtml();
        RuleFor(x => x.Column2Header).MaximumLength(40).NoHtml();

        // Matières optionnelles : mêmes règles qu'à la création, à la lettre. Le refus d'un domaine qui porte
        // déjà des activités dépend de la base, donc de UpdateSubjectCommandHandler.
        RuleFor(x => x.OptionGroup).MaximumLength(50).NoHtml();

        RuleFor(x => x.OptionGroup)
            .Must((command, group) => command.IsOptional || string.IsNullOrWhiteSpace(group))
            .WithMessage("Un groupe d'options n'a de sens que pour une matière optionnelle.");

        RuleFor(x => x.IsOptional)
            .Must((command, isOptional) => !isOptional || command.ParentSubjectId is null)
            .WithMessage("Une activité d'un domaine d'évaluation ne peut pas être une matière optionnelle.");
    }
}
