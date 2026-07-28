using SamaEcole.Application.Common.Validation;
using FluentValidation;

namespace SamaEcole.Application.Registration.Commands.RejectRegistrationRequest;

/// <summary>
/// Ticket JGK-I03 — « Si la demande est rejetée, le motif doit être obligatoire » : c'est la seule
/// explication que recevra le Directeur (suivi public JGK-I02). Un rejet sans motif laisserait un
/// candidat sans aucune piste pour corriger et retenter.
/// </summary>
public class RejectRegistrationRequestValidator : AbstractValidator<RejectRegistrationRequestCommand>
{
    public RejectRegistrationRequestValidator()
    {
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Le motif de rejet est obligatoire.")
            .MaximumLength(1000)
            .NoHtml();
    }
}
