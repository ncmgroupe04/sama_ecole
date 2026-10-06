using FluentValidation;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Subscriptions.Commands.UpdateSubscriptionTier;

public class UpdateSubscriptionTierCommandValidator : AbstractValidator<UpdateSubscriptionTierCommand>
{
    public UpdateSubscriptionTierCommandValidator()
    {
        RuleFor(c => c.SchoolId).NotEmpty();

        RuleFor(c => c)
            .Must(c => c.Tier is not null || c.Status is not null)
            .WithName("Request")
            .WithMessage("Indiquez la tranche et/ou le statut à modifier.");

        // Valeurs nullables : null (« inchangé ») passe, toute valeur fournie est contrôlée.
        RuleFor(c => c.Tier)
            .IsInEnum().WithMessage("Tranche d'effectif invalide.");

        RuleFor(c => c.Status)
            .IsInEnum().WithMessage("Statut invalide.")
            .NotEqual(TenantSubscriptionStatus.PendingOnboarding)
            .WithMessage("Le statut PendingOnboarding est l'état de naissance d'une école : il ne s'assigne pas.");

        // Plafond sur mesure : obligatoire avec la tranche sur mesure, interdit avec toute autre — jamais
        // ignoré en silence (un plafond saisi puis perdu serait pire qu'une erreur).
        RuleFor(c => c.CustomMaxStudentLimit)
            .NotNull().WithMessage("Le plafond d'élèves est obligatoire pour la tranche sur mesure.")
            .GreaterThanOrEqualTo(1).WithMessage("Le plafond doit être d'au moins 1 élève.")
            .When(c => c.Tier == StudentQuotaTier.Tier4_Custom);

        RuleFor(c => c.CustomMaxStudentLimit)
            .Null().WithMessage("Le plafond personnalisé n'est accepté qu'avec la tranche sur mesure.")
            .When(c => c.Tier != StudentQuotaTier.Tier4_Custom);
    }
}
