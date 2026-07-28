using FluentValidation;

namespace SamaEcole.Application.Grades.Commands.CreateGrade;

/// <summary>
/// Validation de forme uniquement — la borne supérieure de <see cref="CreateGradeCommand.Value"/>
/// dépend du CYCLE de la classe de l'élève (Primaire /10, Collège &amp; Lycée /20), un état en base :
/// ce contrôle vit dans le Handler via <see cref="GradingScaleGuard.ResolveScaleForStudentAsync"/>,
/// comme tous les contrôles métier dépendant de la base (Volume 1 §8.2).
/// </summary>
public class CreateGradeCommandValidator : AbstractValidator<CreateGradeCommand>
{
    public CreateGradeCommandValidator()
    {
        RuleFor(c => c.StudentId).NotEmpty();
        RuleFor(c => c.SubjectId).NotEmpty();
        RuleFor(c => c.TermId).NotEmpty();
        RuleFor(c => c.EvaluationType).IsInEnum().WithMessage("Type d'évaluation invalide.");

        RuleFor(c => c.Value)
            .GreaterThanOrEqualTo(0).WithMessage("Une note ne peut pas être négative.");
    }
}
