using FluentValidation;

namespace SamaEcole.Application.Classrooms.Commands.CreateClassroom;

public class CreateClassroomCommandValidator : AbstractValidator<CreateClassroomCommand>
{
    public CreateClassroomCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(50);

        // Nomenclature LIBRE : aucune liste figée de niveaux (openapi.yaml). Une école primaire, un
        // collège et un lycée nomment leurs cycles différemment — imposer une énumération ici
        // rendrait le produit inutilisable pour une partie des établissements.
        RuleFor(x => x.Level).NotEmpty().MaximumLength(50);

        RuleFor(x => x.Capacity)
            .GreaterThan(0).WithMessage("La capacité doit être supérieure à zéro.")
            .LessThanOrEqualTo(200).WithMessage("La capacité annoncée semble irréaliste (maximum 200).");
    }
}
