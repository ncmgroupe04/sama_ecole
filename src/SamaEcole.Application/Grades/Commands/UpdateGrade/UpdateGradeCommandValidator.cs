using FluentValidation;

namespace SamaEcole.Application.Grades.Commands.UpdateGrade;

/// <summary>
/// Validation de forme uniquement — comme la création, le plafond de <see cref="UpdateGradeCommand.Value"/>
/// dépend de la matière notée et, à défaut, du cycle de la classe de l'élève (Primaire /10,
/// Collège &amp; Lycée /20). Il est résolu en base par le Handler, via
/// <see cref="GradingScaleGuard.ResolveMaxScoreAsync"/> — exactement comme à la création, sans quoi une
/// note acceptée à la saisie serait refusée à la correction.
/// </summary>
public class UpdateGradeCommandValidator : AbstractValidator<UpdateGradeCommand>
{
    public UpdateGradeCommandValidator()
    {
        RuleFor(c => c.Id).NotEmpty();

        RuleFor(c => c.Value)
            .GreaterThanOrEqualTo(0).WithMessage("Une note ne peut pas être négative.");
    }
}
