using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Subscriptions;
using SamaEcole.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SamaEcole.Infrastructure.Subscriptions;

/// <summary>
/// Ticket JGK-B03 — alertes automatiques 30/15/7 jours avant expiration, et passage automatique en
/// lecture seule à expiration (docs/Volume_1_Cahier_des_Charges.md §11). Même patron que
/// DebtorAgingHostedService : tour quotidien, échec sur une école n'empêche jamais les suivantes ni ne
/// tue le service.
///
/// Hors requête HTTP, ce service ne porte AUCUN tenant : toute lecture/écriture cross-écoles passe par
/// des fonctions/vues qui contournent délibérément la RLS (ISubscriptionAdminStore.
/// ExpireOverdueSubscriptionsAsync, IApplicationDbContext.PlatformSubscriptions) — jamais par le rôle
/// propriétaire ni par une requête EF tenant classique, qui ne verrait ici aucune ligne.
/// </summary>
public class SubscriptionLifecycleHostedService(
    IServiceScopeFactory scopeFactory,
    SubscriptionLifecycleSettings settings,
    TimeProvider timeProvider,
    ILogger<SubscriptionLifecycleHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Cycle de vie des abonnements démarré (alertes d'expiration, passage en lecture seule) — toutes les {Interval}.",
            settings.Interval);

        using var timer = new PeriodicTimer(settings.Interval);

        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Tour du cycle de vie des abonnements en échec ; reprise au tour suivant.");
            }
        }
        while (await WaitForNextTickAsync(timer, stoppingToken));

        logger.LogInformation("Cycle de vie des abonnements arrêté.");
    }

    private async Task<bool> WaitForNextTickAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        await ExpireOverdueSubscriptionsAsync(today, stoppingToken);
        await SendExpiryRemindersAsync(today, stoppingToken);
    }

    /// <summary>Bascule les abonnements Actifs expirés en ReadOnly et prévient chaque Directeur concerné.</summary>
    private async Task ExpireOverdueSubscriptionsAsync(DateOnly today, CancellationToken stoppingToken)
    {
        List<Guid> expiredSchoolIds;
        using (var scope = scopeFactory.CreateScope())
        {
            var adminStore = scope.ServiceProvider.GetRequiredService<ISubscriptionAdminStore>();
            expiredSchoolIds = (await adminStore.ExpireOverdueSubscriptionsAsync(today, stoppingToken)).ToList();
        }

        foreach (var schoolId in expiredSchoolIds)
        {
            try
            {
                await NotifySchoolAsync(
                    schoolId,
                    "AutoReadOnly",
                    (schoolName, directorFullName) =>
                        $"""
                         Bonjour {directorFullName},

                         L'abonnement Sama Ecole de l'établissement « {schoolName} » est arrivé à échéance.
                         L'accès est désormais en LECTURE SEULE : vos données restent consultables, mais
                         toute saisie (élèves, notes, paiements…) est bloquée tant que le renouvellement
                         n'est pas confirmé.

                         Merci de régulariser votre abonnement dès que possible depuis votre espace.
                         """,
                    "Abonnement expiré — accès en lecture seule",
                    stoppingToken);

                logger.LogInformation(
                    "Abonnement de l'établissement {SchoolId} basculé en lecture seule (échéance dépassée).", schoolId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Notification de passage en lecture seule en échec pour l'établissement {SchoolId}.", schoolId);
            }
        }
    }

    /// <summary>Alertes 30/15/7 jours avant échéance (Volume 1 §11) pour les abonnements encore Actifs.</summary>
    private async Task SendExpiryRemindersAsync(DateOnly today, CancellationToken stoppingToken)
    {
        foreach (var daysBefore in settings.ReminderDaysBeforeExpiry)
        {
            var targetDate = today.AddDays(daysBefore);

            List<(Guid SchoolId, string SchoolName)> due;
            using (var scope = scopeFactory.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

                // v_platform_subscriptions (security_invoker = false) contourne la RLS — ce service ne
                // porte aucun tenant, une lecture EF classique de `subscriptions` n'y verrait rien.
                due = await dbContext.PlatformSubscriptions
                    .AsNoTracking()
                    .Where(s => s.Status == nameof(SubscriptionStatus.Active) && s.ExpiresAt == targetDate)
                    .Select(s => new ValueTuple<Guid, string>(s.SchoolId, s.SchoolName))
                    .ToListAsync(stoppingToken);
            }

            foreach (var (schoolId, schoolName) in due)
            {
                try
                {
                    await NotifySchoolAsync(
                        schoolId,
                        $"ExpiryReminder:J-{daysBefore}",
                        (name, directorFullName) =>
                            $"""
                             Bonjour {directorFullName},

                             L'abonnement Sama Ecole de l'établissement « {name} » arrive à échéance dans {daysBefore} jour(s).

                             Merci de renouveler votre abonnement depuis votre espace avant cette date, afin
                             de conserver un accès complet à la plateforme.
                             """,
                        $"Abonnement — échéance dans {daysBefore} jour(s)",
                        stoppingToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex, "Rappel d'expiration (J-{DaysBefore}) en échec pour l'établissement {SchoolId}.",
                        daysBefore, schoolId);
                }
            }
        }
    }

    private async Task NotifySchoolAsync(
        Guid schoolId, string auditAction, Func<string, string, string> bodyFactory, string subject,
        CancellationToken stoppingToken)
    {
        using var scope = scopeFactory.CreateScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var authStore = scope.ServiceProvider.GetRequiredService<IAuthStore>();
        var emailSender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
        var auditLogStore = scope.ServiceProvider.GetRequiredService<IAuditLogStore>();

        // `schools` n'est pas sous RLS : lisible normalement même sans tenant (voir DebtorAgingHostedService).
        var school = await dbContext.Schools.AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == schoolId, stoppingToken);

        if (school is null)
        {
            return;
        }

        // Même porte SECURITY DEFINER que « Relancer »/« Infiltrer » (SendSubscriptionReminderCommandHandler) :
        // `users` est sous RLS, ce service ne porte aucun tenant.
        var director = await authStore.FindActiveDirectorForSchoolAsync(schoolId, stoppingToken);

        if (director is null)
        {
            // Aucun Directeur actif : rien à prévenir, mais l'action (ReadOnly/rappel) elle-même a eu
            // lieu — pas une erreur du service.
            return;
        }

        await emailSender.SendAsync(
            new EmailMessage(director.Email, subject, bodyFactory(school.Name, director.FullName)),
            stoppingToken);

        // Attribuée à l'école CIBLE (comme SendSubscriptionReminderCommandHandler) : aucun acteur
        // humain ici, le worker n'a pas d'UserId — utilise l'identité du Directeur notifié, seule
        // partie prenante identifiable de cette écriture automatique.
        await auditLogStore.AppendAsync(
            schoolId, director.Id, "Subscriptions", auditAction,
            success: true, failureReason: null, ipAddress: null, timeProvider.GetUtcNow(), stoppingToken);
    }
}
