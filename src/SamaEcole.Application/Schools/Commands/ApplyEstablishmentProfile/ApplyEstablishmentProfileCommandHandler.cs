using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Subscriptions.Changes;
using SamaEcole.Application.Subscriptions;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace SamaEcole.Application.Schools.Commands.ApplyEstablishmentProfile;

/// <summary>
/// Onboarding — enregistre le profil choisi par le Directeur ET applique le PRESET de modules associé
/// (voir EstablishmentProfilePresets) en une seule transaction : un profil sans son preset laisserait
/// l'établissement dans un état incohérent (ex. Daara/Internat choisi mais Internat resté désactivé).
///
/// VERROU D'ABONNEMENT : une fois la souscription active, le profil n'est plus modifiable librement. Changer de profil est
/// INTERCEPTÉ avant toute écriture (SubscriptionChangeRequiredException, 409) et devient une demande que le Super Admin
/// valide. Redemander le profil actuel ne fait rien : réappliquer son preset effacerait les modules souscrits en plus.
/// </summary>
public class ApplyEstablishmentProfileCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    SubscriptionChangeService subscriptionChanges,
    ILogger<ApplyEstablishmentProfileCommandHandler> logger)
    : IRequestHandler<ApplyEstablishmentProfileCommand, SchoolSettingsDto>
{
    public async Task<SchoolSettingsDto> Handle(
        ApplyEstablishmentProfileCommand request,
        CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        // Valeur validée en amont : à ce stade, elle correspond forcément à un membre de l'enum.
        var profile = Enum.Parse<ProfileEtablissement>(request.Profile, ignoreCase: true);
        var preset = EstablishmentProfilePresets.For(profile);

        var settings = await dbContext.SchoolSettings.FirstOrDefaultAsync(cancellationToken);

        // Verrou : un changement de profil devient une demande. Même profil (ou école sans souscription à protéger) : on continue.
        var evaluation = await subscriptionChanges.EvaluateAsync(
            new SubscriptionChangeSet(Profile: ProfileTypeMapping.FromEstablishmentProfile(profile)), cancellationToken);

        if (evaluation is { Plan.IsNoOp: false })
        {
            throw new SamaEcole.Application.Common.Exceptions.SubscriptionChangeRequiredException(evaluation.Preview);
        }

        // Profil déjà celui de la souscription : rien à réappliquer (le preset effacerait les modules souscrits en plus).
        if (evaluation is not null && settings is not null)
        {
            return SchoolSettingsDtoMapper.From(settings);
        }

        if (settings is null)
        {
            settings = new SchoolSettings { SchoolId = schoolId };
            dbContext.SchoolSettings.Add(settings);
        }

        settings.ProfileEtablissement = profile;
        settings.IsPedagogyEnabled = preset.IsPedagogyEnabled;
        settings.IsFinanceEnabled = preset.IsFinanceEnabled;
        settings.IsInternatEnabled = preset.IsInternatEnabled;
        settings.IsCoranModuleEnabled = preset.IsCoranModuleEnabled;

        // Garde la souscription commerciale alignée : sans cela, un changement de profil APRÈS l'Onboarding
        // (route toujours exposée) laisserait TenantSubscription.ProfileType sur l'ancien choix pendant que
        // la sidebar, qui lit ces réglages, afficherait le nouveau. La tranche et le statut ne bougent pas —
        // seul le Super Admin les modifie. Pas de souscription (école antérieure) : rien à aligner.
        var subscription = await dbContext.TenantSubscriptions.FirstOrDefaultAsync(cancellationToken);

        if (subscription is not null)
        {
            subscription.ProfileType = ProfileTypeMapping.FromEstablishmentProfile(profile);
            subscription.IsPedagogyEnabled = preset.IsPedagogyEnabled;
            subscription.IsFinanceEnabled = preset.IsFinanceEnabled;
            subscription.IsInternatEnabled = preset.IsInternatEnabled;
            subscription.IsCoranModuleEnabled = preset.IsCoranModuleEnabled;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Profil d'établissement {Profile} appliqué à l'école {SchoolId}.", profile, schoolId);

        return SchoolSettingsDtoMapper.From(settings);
    }
}
