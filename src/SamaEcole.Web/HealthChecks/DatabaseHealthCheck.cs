using SamaEcole.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace SamaEcole.Web.HealthChecks;

/// <summary>
/// Sonde de PRÉPARATION (readiness) : l'application ne peut rien servir d'utile sans PostgreSQL.
/// Utilisée par la vérification post-déploiement et le rolling update
/// (docs/Volume_9_Deployment_Operations.md §2, §4, §9) pour ne router du trafic vers une nouvelle
/// instance qu'une fois sa base joignable.
///
/// Volontairement séparée de la sonde de VIVACITÉ (liveness, "/health/live") : une base
/// momentanément injoignable ne doit pas faire redémarrer en boucle un conteneur par ailleurs sain.
///
/// <c>SELECT 1</c> et rien de plus — aucune requête sur une table tenant (la connexion de la sonde
/// n'a pas de <c>app.current_school_id</c>, la RLS ne laisserait rien passer de toute façon), aucune
/// écriture, coût négligeable.
/// </summary>
public sealed class DatabaseHealthCheck(ApplicationDbContext dbContext) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);

            return canConnect
                ? HealthCheckResult.Healthy("PostgreSQL joignable.")
                : HealthCheckResult.Unhealthy("PostgreSQL injoignable (CanConnect a renvoyé false).");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL injoignable.", ex);
        }
    }
}
