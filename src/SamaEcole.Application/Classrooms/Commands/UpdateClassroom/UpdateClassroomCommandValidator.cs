using SamaEcole.Application.Common.Validation;
using FluentValidation;

namespace SamaEcole.Application.Classrooms.Commands.UpdateClassroom;

public class UpdateClassroomCommandValidator : AbstractValidator<UpdateClassroomCommand>
{
    public UpdateClassroomCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(50).NoHtml();

        // Nomenclature LIBRE, comme à la création : aucune liste figée de niveaux.
        RuleFor(x => x.Level).NotEmpty().MaximumLength(50).NoHtml();

        RuleFor(x => x.Capacity)
            .GreaterThan(0).WithMessage("La capacité doit être supérieure à zéro.")
            .LessThanOrEqualTo(200).WithMessage("La capacité annoncée semble irréaliste (maximum 200).");

        this.MustDeclareACoherentAcceleratedPath(x => x.IsAccelerated, x => x.TargetLevel, x => x.Name, x => x.Level);
    }
}
