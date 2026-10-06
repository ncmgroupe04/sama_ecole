using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Schools.Commands.ApplyEstablishmentProfile;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Subscriptions.Commands.SelectProfile;

/// <summary>
/// Sort l'école de l'Onboarding : écrit le profil et la tranche sur la souscription ET applique le preset
/// de modules à <c>SchoolSettings</c> — l'un sans l'autre laisserait la sidebar (qui lit encore
/// SchoolSettings) en désaccord avec la souscription. Tout dans UNE sauvegarde, donc atomique.
///
/// L'écriture passe par EF sous le rôle applicatif : la policy RLS autorise l'UPDATE de la ligne de SA
/// propre école. Pas de fonction SECURITY DEFINER ici, contrairement au provisionnement (le Super Admin
/// n'a aucun SchoolId de session, ce qui n'est pas le cas du Directeur).
/// </summary>
public class SelectProfileCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    ILogger<SelectProfileCommandHandler> logger)
    : IRequestHandler<SelectProfileCommand, TenantSubscriptionDto>
{
    public async Task<TenantSubscriptionDto> Handle(SelectProfileCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var subscription = await dbContext.TenantSubscriptions
            .FirstOrDefaultAsync(s => s.SchoolId == schoolId, cancellationToken)
            ?? throw new NotFoundException("Aucune souscription n'est rattachée à votre établissement. Contactez le support.");

        // Le choix ne se rejoue pas : on ne doit pas pouvoir se rabattre sur une tranche inférieure
        // après coup, ni réécrire le profil d'une école déjà en exploitation.
        if (subscription.Status != TenantSubscriptionStatus.PendingOnboarding)
        {
            throw new BusinessRuleException(
                "Le profil et la tranche de votre établissement sont déjà choisis. Contactez le support pour les modifier.",
                "ONBOARDING_ALREADY_COMPLETED");
        }

        var (max, soft) = StudentQuotaDefaults.For(request.Tier);
        var establishmentProfile = ProfileTypeMapping.ToEstablishmentProfile(request.Profile);
        var preset = EstablishmentProfilePresets.For(establishmentProfile);

        subscription.ProfileType = request.Profile;
        subscription.StudentQuotaTier = request.Tier;
        subscription.MaxStudentLimit = max;
        subscription.SoftQuotaLimit = soft;
        subscription.IsPedagogyEnabled = preset.IsPedagogyEnabled;
        subscription.IsFinanceEnabled = preset.IsFinanceEnabled;
        subscription.IsInternatEnabled = preset.IsInternatEnabled;
        subscription.IsCoranModuleEnabled = preset.IsCoranModuleEnabled;
        subscription.Status = TenantSubscriptionStatus.Active;

        // Source de vérité de l'affichage tant que la sidebar n'est pas rebranchée sur la souscription.
        var settings = await dbContext.SchoolSettings.FirstOrDefaultAsync(cancellationToken);

        if (settings is null)
        {
            settings = new SchoolSettings { SchoolId = schoolId };
            dbContext.SchoolSettings.Add(settings);
        }

        settings.ProfileEtablissement = establishmentProfile;
        settings.IsPedagogyEnabled = preset.IsPedagogyEnabled;
        settings.IsFinanceEnabled = preset.IsFinanceEnabled;
        settings.IsInternatEnabled = preset.IsInternatEnabled;
        settings.IsCoranModuleEnabled = preset.IsCoranModuleEnabled;

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Onboarding terminé pour l'école {SchoolId} : profil {Profile}, tranche {Tier}.",
            schoolId, request.Profile, request.Tier);

        return TenantSubscriptionService.ToDto(subscription);
    }
}
