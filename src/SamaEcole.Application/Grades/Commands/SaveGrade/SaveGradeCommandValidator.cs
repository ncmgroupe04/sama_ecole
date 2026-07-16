using FluentValidation;

namespace SamaEcole.Application.Grades.Commands.SaveGrade;

/// <summary>
/// Validation de forme uniquement — la borne supérieure de <see cref="SaveGradeCommand.Value"/> dépend
/// du barème de l'école (SchoolSettings.GradingScale, 10 ou 20) : un contrôle métier tenu par le
/// Handler, puisqu'il dépend d'un état en base (Volume 1 §8.2).
/// </summary>
public class SaveGradeCommandValidator : AbstractValidator<SaveGradeCommand>
{
    public SaveGradeCommandValidator()
    {
        RuleFor(c => c.StudentId).NotEmpty();
        RuleFor(c => c.SubjectId).NotEmpty();
        RuleFor(c => c.TermId).NotEmpty();
        RuleFor(c => c.EvaluationType).IsInEnum().WithMessage("Type d'évaluation invalide.");

        RuleFor(c => c.Value)
            .GreaterThanOrEqualTo(0).WithMessage("Une note ne peut pas être négative.");
    }
}
