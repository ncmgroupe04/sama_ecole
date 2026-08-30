using FluentValidation;

namespace SamaEcole.Application.StateIntegration.Commands.RevokeStudentMutationCertificate;

public class RevokeStudentMutationCertificateCommandValidator
    : AbstractValidator<RevokeStudentMutationCertificateCommand>
{
    public RevokeStudentMutationCertificateCommandValidator()
    {
        RuleFor(c => c.CertificateId).NotEmpty();

        // Motif OBLIGATOIRE : révoquer une pièce officielle sans dire pourquoi la rend inexplicable
        // à l'élève, à la famille et à l'école d'accueil qui la présenteraient.
        RuleFor(c => c.Reason)
            .NotEmpty().WithMessage("Indiquez le motif de la révocation.")
            .MaximumLength(300).WithMessage("Le motif de révocation ne peut dépasser 300 caractères.");
    }
}
