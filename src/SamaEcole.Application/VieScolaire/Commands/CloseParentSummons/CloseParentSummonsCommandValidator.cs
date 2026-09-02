using SamaEcole.Domain.Enums;
using FluentValidation;

namespace SamaEcole.Application.VieScolaire.Commands.CloseParentSummons;

public class CloseParentSummonsCommandValidator : AbstractValidator<CloseParentSummonsCommand>
{
    public CloseParentSummonsCommandValidator()
    {
        RuleFor(v => v.SummonsId).NotEmpty();

        RuleFor(v => v.Outcome)
            .IsInEnum().WithMessage("Suite inconnue.")
            // `Scheduled` est l'état de DÉPART, pas une suite : l'accepter ici rendrait la commande
            // silencieusement inopérante — statut inchangé, mais ClosedAt posé, donc un registre qui
            // se prétend clos sans l'être.
            .NotEqual(ParentSummonsStatus.Scheduled)
            .WithMessage("Choisissez une suite : honorée, non honorée, ou reportée.");

        // Le caractère OBLIGATOIRE du compte rendu dépend de la suite choisie : il est porté par le
        // Handler, pas ici, exactement comme DiscrepancyReason sur la clôture de caisse.
        RuleFor(v => v.OutcomeNotes).MaximumLength(2000);
    }
}
