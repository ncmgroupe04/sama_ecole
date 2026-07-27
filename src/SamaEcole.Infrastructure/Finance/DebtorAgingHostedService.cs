using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Commands.GenerateDebtorReminderBatches;
using SamaEcole.Application.Finance.Common;
using SamaEcole.Infrastructure.Multitenancy;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SamaEcole.Infrastructure.Finance;

/// <summary>
/// Calcule chaque jour, POUR CHAQUE ÉCOLE, les lots de relance de débiteurs (Étape 5). Ne contient
/// AUCUNE règle métier — seulement l'itération des écoles et la garantie qu'un échec sur une école
/// n'empêche pas les suivantes ni ne tue le service (même parti pris que SmsQueueHostedService).
///
/// École par école sous TenantProvider.RunAsSchoolAsync, avec un scope DI et un DbContext NEUFS à
/// CHAQUE itération : hors requête HTTP il n'existe aucun JWT, donc aucun tenant ambient — c'est
/// l'override qui en tient lieu, et il exige une connexion neuve pour que
/// TenantConnectionInterceptor repositionne correctement `app.current_school_id` (voir la doc de
/// RunAsSchoolAsync). Partager une connexion entre deux écoles sous cet override romprait
/// l'isolation multi-tenant (AGENTS.md règle #2) — c'est le risque principal de ce mécanisme, couvert
/// par un test d'isolation dédié (Category=MultiTenant).
/// </summary>
public class DebtorAgingHostedService(
    IServiceScopeFactory scopeFactory,
    DebtorAgingSettings settings,
    ILogger<DebtorAgingHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Calcul quotidien des lots de relance de débiteurs démarré (toutes les {Interval}).",
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
                // Le service ne meurt jamais d'un tour raté : base injoignable, panne réseau, tout est
                // retenté au tour suivant — sans quoi plus aucun brouillon ne serait jamais calculé.
                logger.LogError(ex, "Tour de calcul des lots de relance en échec ; reprise au tour suivant.");
            }
        }
        while (await WaitForNextTickAsync(timer, stoppingToken));

        logger.LogInformation("Calcul quotidien des lots de relance de débiteurs arrêté.");
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
        List<Guid> schoolIds;
        using (var listScope = scopeFactory.CreateScope())
        {
            var listDbContext = listScope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

            // `schools` n'est pas une table tenant (elle DÉFINIT le tenant) : la lire sans SchoolId
            // ambient est normal, même lecture que SendSubscriptionReminderCommandHandler.
            schoolIds = await listDbContext.Schools.AsNoTracking()
                .Select(s => s.Id)
                .ToListAsync(stoppingToken);
        }

        foreach (var schoolId in schoolIds)
        {
            try
            {
                await TenantProvider.RunAsSchoolAsync(schoolId, async () =>
                {
                    using var scope = scopeFactory.CreateScope();
                    var mediator = scope.ServiceProvider.GetRequiredService<ISender>();
                    var created = await mediator.Send(new GenerateDebtorReminderBatchesCommand(), stoppingToken);
                    return created;
                });
            }
            catch (Exception ex)
            {
                // Isolation par école : un établissement dont le calcul échoue ne doit jamais bloquer
                // celui des autres.
                logger.LogError(ex, "Calcul des lots de relance en échec pour l'établissement {SchoolId}.", schoolId);
            }
        }
    }
}
