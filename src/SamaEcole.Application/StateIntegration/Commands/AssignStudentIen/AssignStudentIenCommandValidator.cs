using FluentValidation;

namespace SamaEcole.Application.StateIntegration.Commands.AssignStudentIen;

public class AssignStudentIenCommandValidator : AbstractValidator<AssignStudentIenCommand>
{
    public AssignStudentIenCommandValidator()
    {
        RuleFor(c => c.StudentId).NotEmpty();

        // Bornes de LONGUEUR seulement. La forme exacte est contrôlée par IIenGeneratorService dans le
        // Handler — un seul endroit connaît la règle, et ce n'est pas ici : le validateur ne doit pas
        // porter une seconde définition du format qui divergerait le jour où le format national réel
        // sera connu.
        //
        // La chaîne VIDE est refusée explicitement : null signifie « génère-moi un provisoire »,
        // "" ne signifie rien. Les confondre ferait générer un numéro de secours à un utilisateur qui
        // a simplement validé un champ resté vide.
        When(c => c.IenNumber is not null, () =>
        {
            RuleFor(c => c.IenNumber!)
                .NotEmpty()
                .WithMessage("Saisissez un IEN, ou laissez le champ absent pour générer un numéro provisoire.")
                .MaximumLength(24)
                .WithMessage("Un IEN ne dépasse pas 24 caractères.");
        });
    }
}
