using FluentValidation;

namespace SamaEcole.Application.Grades.Commands.CreateGrade;

/// <summary>
/// Validation de forme uniquement — la borne supérieure de <see cref="CreateGradeCommand.Value"/>
/// dépend de la MATIÈRE visée (Subject.MaxScore : /40, /60, /24… des grilles par compétences) et, à
/// défaut, du cycle de la classe de l'élève (Primaire /10, Collège &amp; Lycée /20). C'est un état en
/// base : ce contrôle vit donc dans le Handler, via
/// <see cref="GradingScaleGuard.ResolveMaxScoreAsync"/>, comme tous les contrôles métier dépendant de
/// la base (Volume 1 §8.2).
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
