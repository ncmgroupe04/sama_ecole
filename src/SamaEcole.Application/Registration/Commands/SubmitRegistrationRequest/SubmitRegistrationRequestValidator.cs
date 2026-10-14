using SamaEcole.Application.Common.Validation;
using SamaEcole.Application.Registration;
using SamaEcole.Application.Users.Common;
using SamaEcole.Domain.Enums;
using FluentValidation;

namespace SamaEcole.Application.Registration.Commands.SubmitRegistrationRequest;

/// <summary>
/// Ticket JGK-I01. Les longueurs maximales reflètent les contraintes de colonne (docs/Volume_3_DDS.md §5.7) :
/// mieux vaut un 422 explicite sur le bon champ qu'une DbUpdateException remontée en 500.
///
/// La politique de mot de passe (docs/Volume_7_Security.md §2) est la MÊME PasswordPolicy partagée que la
/// création/réinitialisation de compte (JGK-A05) : le mot de passe est CHOISI par le Directeur ici
/// (contrairement à JGK-B01 où il est généré), c'est donc un des rares endroits où un humain le saisit.
/// </summary>
public class SubmitRegistrationRequestValidator : AbstractValidator<SubmitRegistrationRequestCommand>
{
    public SubmitRegistrationRequestValidator()
    {
        RuleFor(x => x.DirectorFullName).NotEmpty().MaximumLength(200).NoHtml();
        RuleFor(x => x.DirectorEmail).NotEmpty().EmailAddress().MaximumLength(200).NoHtml();
        RuleFor(x => x.DirectorPhone).NotEmpty().MaximumLength(30).NoHtml().MustBeValidSenegalPhone();

        RuleFor(x => x.DirectorPassword).Custom((password, context) =>
        {
            foreach (var error in PasswordPolicy.Validate(
                password ?? string.Empty, context.InstanceToValidate.DirectorFullName, context.InstanceToValidate.SchoolName))
            {
                context.AddFailure(error);
            }
        });

        RuleFor(x => x.SchoolName).NotEmpty().MaximumLength(200).NoHtml();
        RuleFor(x => x.SchoolAddress).MaximumLength(300).NoHtml();
        RuleFor(x => x.City).MaximumLength(100).NoHtml();
        RuleFor(x => x.Region).MaximumLength(100).NoHtml();

        // Un effectif renseigné doit être plausible ; NULL reste autorisé (champ facultatif).
        RuleFor(x => x.EstimatedStudentCount)
            .GreaterThan(0).LessThanOrEqualTo(100_000)
            .When(x => x.EstimatedStudentCount.HasValue);

        // Deux offres coexistent. OFFRE PAR TRANCHE D'EFFECTIF (privé) : dès qu'un profil ou une tranche est donné. Offre
        // HISTORIQUE (cycles × taille) : sans profil ni tranche — demandes publiques, anciens clients du formulaire.
        When(IsTieredOffer, ValidateTieredOffer);
        When(x => !IsTieredOffer(x), ValidateLegacyOffer);
    }

    private static bool IsTieredOffer(SubmitRegistrationRequestCommand x) => x.Profile.HasValue || x.Tier.HasValue;

    private void ValidateLegacyOffer()
    {
        // Profil tarifaire : un choix EXPLICITE de chaque champ (les propriétés sont nullables), puis la cohérence de la
        // combinaison avec la grille — public : un seul cycle, sans palier ; privé : palier de taille obligatoire.
        RuleFor(x => x.Ownership).NotNull().WithMessage("Précisez si votre établissement est public ou privé.")
            .IsInEnum();
        RuleFor(x => x.CycleProfile).NotNull().WithMessage("Précisez les cycles gérés par votre établissement.")
            .IsInEnum();
        RuleFor(x => x.SizeTier).IsInEnum().When(x => x.SizeTier.HasValue);

        RuleFor(x => x.SizeTier)
            .NotNull().WithMessage("Précisez la taille de votre établissement.")
            .When(x => x.Ownership == SchoolOwnership.Private);

        RuleFor(x => x.CycleProfile)
            .Must(profile => profile is SchoolCycleProfile.Primaire or SchoolCycleProfile.College or SchoolCycleProfile.Lycee)
            .WithMessage("Un établissement public gère un seul cycle : école élémentaire, collège (CEM) ou lycée.")
            .When(x => x.Ownership == SchoolOwnership.Public && x.CycleProfile.HasValue);
    }

    /// <summary>
    /// Offre par tranche d'effectif. L'ordre des règles est celui de ce que le demandeur doit corriger : le type
    /// d'établissement, le profil et la tranche d'abord, puis les cycles, puis la cohérence effectif ↔ tranche. Ces
    /// contrôles sont REJOUÉS à l'approbation (RegistrationOffer.Check) : la demande a pu attendre, et le validateur
    /// ne protège que la soumission.
    /// </summary>
    private void ValidateTieredOffer()
    {
        RuleFor(x => x.Ownership)
            .Must(o => o == SchoolOwnership.Private)
            .WithMessage("L'offre par tranche d'effectif s'adresse aux établissements privés ; les établissements publics sont facturés par élève, sur devis.");

        RuleFor(x => x.Profile).NotNull().WithMessage("Précisez le profil d'enseignement de votre établissement.")
            .IsInEnum();
        RuleFor(x => x.Tier).NotNull().WithMessage("Choisissez la tranche d'effectif de votre établissement.")
            .IsInEnum();

        // Le champ libre est la VÉRIFICATION de la tranche cochée : sans lui, rien ne borne la tranche.
        RuleFor(x => x.EstimatedStudentCount)
            .NotNull().WithMessage("Indiquez l'effectif exact de votre établissement.");

        // Cycles : cochés parmi ceux que le profil autorise, sans doublon ; au moins un pour un profil académique.
        RuleFor(x => x.Cycles)
            .Must(cycles => cycles is { Count: > 0 })
            .WithMessage("Cochez au moins un cycle géré par votre établissement.")
            .When(x => x.Profile is { } profile && Enum.IsDefined(profile) && RegistrationOffer.RequiresCycles(profile));

        RuleFor(x => x.Cycles)
            .Must(cycles => cycles!.All(Enum.IsDefined) && cycles!.Distinct().Count() == cycles!.Count)
            .WithMessage("Les cycles cochés sont invalides.")
            .When(x => x.Cycles is { Count: > 0 });

        RuleFor(x => x.Cycles)
            .Must((x, cycles) => cycles!.All(RegistrationOffer.AllowedCycles(x.Profile!.Value).Contains))
            .WithMessage("Un établissement élémentaire ne gère ni le collège ni le lycée : choisissez le profil « Enseignement général » pour ces cycles.")
            .When(x => x.Profile is { } profile && Enum.IsDefined(profile) && x.Cycles is { Count: > 0 } && x.Cycles.All(Enum.IsDefined));

        // Anti-fraude : tranche cochée ≥ tranche minimale de l'effectif déclaré ; au-delà de 400 élèves, sur devis.
        RuleFor(x => x.Tier)
            .Custom((tier, context) =>
            {
                var command = context.InstanceToValidate;

                if (tier is not { } chosen || !Enum.IsDefined(chosen) || command.EstimatedStudentCount is not { } declared || declared <= 0)
                {
                    return;
                }

                if (RegistrationOffer.Check(declared, chosen) is { } problem)
                {
                    context.AddFailure(nameof(command.Tier), RegistrationOffer.Describe(problem, declared));
                }
            });
    }
}
