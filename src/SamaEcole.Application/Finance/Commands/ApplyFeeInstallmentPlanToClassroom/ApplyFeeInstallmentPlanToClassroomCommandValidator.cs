using SamaEcole.Application.Common.Validation;
using FluentValidation;

namespace SamaEcole.Application.Finance.Commands.ApplyFeeInstallmentPlanToClassroom;

public class ApplyFeeInstallmentPlanToClassroomCommandValidator
    : AbstractValidator<ApplyFeeInstallmentPlanToClassroomCommand>
{
    public ApplyFeeInstallmentPlanToClassroomCommandValidator()
    {
        RuleFor(v => v.ClassroomId).NotEmpty();
        RuleFor(v => v.Reason).MaximumLength(500).NoHtml();

        RuleFor(v => v.Template)
            .NotEmpty().WithMessage("Le modèle d'échéancier doit comporter au moins une échéance.")
            .Must(t => t.Count <= 24).WithMessage("Un modèle d'échéancier ne peut pas dépasser 24 échéances.")
            .Must(t => t.Sum(l => l.Percentage) == 1m)
            .WithMessage("La somme des pourcentages du modèle doit être exactement 100 %.");

        RuleForEach(v => v.Template).ChildRules(line =>
        {
            line.RuleFor(l => l.Label).NotEmpty().MaximumLength(120).NoHtml();
            line.RuleFor(l => l.Percentage).GreaterThan(0).LessThanOrEqualTo(1);
            line.RuleFor(l => l.OffsetDays).GreaterThanOrEqualTo(0);
        });
    }
}
