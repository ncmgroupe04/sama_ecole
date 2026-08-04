namespace SamaEcole.Infrastructure.Caching;

/// <summary>Sous-section « Kpi:Cache » (voir .env.example). Coupé en tests fonctionnels (Kpi__Cache__Enabled=false)
/// pour qu'un dashboard lu deux fois dans le même test class reflète toujours l'état réel, jamais une valeur
/// mise en cache par une assertion précédente.</summary>
public class KpiCacheSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>Durée de rétention, milieu de la fourchette 5–10 min demandée.</summary>
    public int TtlMinutes { get; set; } = 7;
}
