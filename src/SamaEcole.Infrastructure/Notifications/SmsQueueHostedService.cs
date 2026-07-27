using SamaEcole.Application.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SamaEcole.Infrastructure.Notifications;

/// <summary>
/// Fait tourner la file des SMS : un tour de <see cref="ISmsQueueProcessor"/> toutes les
/// <see cref="SmsQueueSettings.PollInterval"/>. Ne contient AUCUNE règle — seulement la boucle, son
/// rythme, et la garantie qu'elle survit à ses propres pannes.
///
/// Une PORTÉE (scope) est ouverte à chaque tour, et non une fois pour toutes : le processeur dépend
/// d'un DbContext, lui-même Scoped. Un DbContext conservé pendant des jours accumulerait un cache de
/// suivi sans fin et garderait une connexion PostgreSQL indéfiniment.
///
/// Interrogation périodique plutôt que LISTEN/NOTIFY : le volume est de quelques SMS par heure et
/// par école, et un délai de quelques secondes sur une alerte d'absence n'a aucune conséquence. Une
/// notification PostgreSQL ajouterait une connexion dédiée et un chemin de reprise à maintenir, pour
/// gagner un temps que personne ne remarquerait.
/// </summary>
public class SmsQueueHostedService(
    IServiceScopeFactory scopeFactory,
    SmsQueueSettings settings,
    ILogger<SmsQueueHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Dépilage de la file SMS démarré (lot de {BatchSize}, toutes les {Interval}).",
            settings.BatchSize, settings.PollInterval);

        using var timer = new PeriodicTimer(settings.PollInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<ISmsQueueProcessor>();

                await processor.ProcessOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // La boucle NE MEURT JAMAIS d'un tour raté : base injoignable, panne réseau, tout est
                // retenté au tour suivant. Une exception qui remonte ici arrêterait définitivement le
                // service hébergé, et plus aucun SMS ne partirait — en silence, l'application
                // continuant par ailleurs de fonctionner normalement.
                logger.LogError(ex, "Tour de dépilage de la file SMS en échec ; reprise au tour suivant.");
            }

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        logger.LogInformation("Dépilage de la file SMS arrêté.");
    }
}
