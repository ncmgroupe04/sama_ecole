using System.Net.Sockets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SamaEcole.Persistence;

/// <summary>
/// Garde-fou de démarrage (ticket JGK-A03).
///
/// La RLS PostgreSQL est SILENCIEUSEMENT inopérante pour un rôle superutilisateur, pour un rôle
/// portant BYPASSRLS, et pour le PROPRIÉTAIRE des tables. Si l'application se connecte avec un tel
/// rôle, toutes les policies sont contournées sans le moindre message : l'isolation multi-tenant
/// tombe, et rien ne le signale. C'est précisément le cas par défaut de docker-compose, où
/// POSTGRES_USER est propriétaire de la base.
///
/// On refuse donc de démarrer plutôt que de servir des données inter-écoles.
///
/// Ce contrôle exige un aller-retour SQL AVANT que le serveur HTTP n'écoute. Sur un hébergement dont
/// l'ordonnanceur attend que le conteneur ouvre son port dans un délai borné (Cloud Run, Kubernetes),
/// une base momentanément injoignable — pooler Supabase qui se réveille, résolution DNS lente au
/// premier démarrage — se traduirait par un échec de déploiement au message trompeur (« failed to
/// start and listen on the port »). D'où la reprise sur erreur ci-dessous : elle laisse la base se
/// rendre joignable, mais ne rend JAMAIS le verdict tolérant — un rôle qui contourne la RLS échoue
/// immédiatement, sans réessai.
/// </summary>
public static class RlsGuard
{
    /// <summary>
    /// Tentatives de CONNEXION (jamais de verdict). Délais 2s + 4s + 8s = 14s d'attente cumulée,
    /// auxquels s'ajoute le délai de connexion de Npgsql à chaque essai : on reste très en deçà du
    /// délai de démarrage par défaut d'un ordonnanceur de conteneurs, tout en absorbant le réveil
    /// d'une base gérée.
    /// </summary>
    private const int MaxAttempts = 4;

    private static readonly TimeSpan FirstRetryDelay = TimeSpan.FromSeconds(2);

    public static async Task EnsureRuntimeRoleCannotBypassRlsAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        var logger = services.GetService<ILoggerFactory>()?.CreateLogger(typeof(RlsGuard).FullName!);

        var (unsafeRoles, ownedTables) = await InspectRuntimeRoleAsync(services, logger, cancellationToken);

        if (unsafeRoles.Count > 0)
        {
            throw new InvalidOperationException(
                $"Le rôle PostgreSQL '{unsafeRoles[0]}' est superutilisateur ou porte BYPASSRLS : il contourne " +
                "les policies RLS et casse l'isolation multi-tenant (AGENTS.md règle #2). Connectez " +
                "l'application avec le rôle applicatif dédié (sama_ecole_app) et réservez le rôle " +
                "propriétaire aux migrations — voir ConnectionStrings:Migrations et .env.example.");
        }

        // Le propriétaire d'une table est lui aussi exempté de RLS (sauf FORCE ROW LEVEL SECURITY).
        // Vérifié sur TOUTES les tables du schéma, pas une liste blanche codée en dur : sama_ecole_app
        // ne doit posséder AUCUNE table (AGENTS.md règle #2), donc une seule table possédée — tenant
        // ou non — signale déjà une connexion faite avec le mauvais rôle. Une liste explicite aurait
        // silencieusement raté toute nouvelle table tenant ajoutée sans être répercutée ici (c'est
        // précisément ce qui s'est produit : la liste d'origine ne couvrait que 7 des ~24 tables
        // tenant existantes).
        if (ownedTables.Count > 0)
        {
            throw new InvalidOperationException(
                $"Le rôle de l'application possède des tables ({string.Join(", ", ownedTables)}) : " +
                "PostgreSQL exempte le propriétaire des policies RLS, l'isolation multi-tenant serait " +
                "inopérante (AGENTS.md règle #2). Connectez l'application avec sama_ecole_app, qui n'est " +
                "propriétaire d'aucune table.");
        }
    }

    /// <summary>
    /// Interroge le catalogue PostgreSQL, en réessayant tant que l'échec vient de la CONNEXION. Le
    /// verdict lui-même est rendu par l'appelant : rien ici ne peut transformer un rôle dangereux en
    /// démarrage réussi.
    /// </summary>
    private static async Task<(List<string> UnsafeRoles, List<string> OwnedTables)> InspectRuntimeRoleAsync(
        IServiceProvider services,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        var delay = FirstRetryDelay;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var scope = services.CreateAsyncScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var unsafeRoles = await dbContext.Database
                    .SqlQuery<string>(
                        $"""
                         SELECT r.rolname AS "Value"
                         FROM pg_roles r
                         WHERE r.rolname = current_user
                           AND (r.rolsuper OR r.rolbypassrls)
                         """)
                    .ToListAsync(cancellationToken);

                var ownedTables = await dbContext.Database
                    .SqlQuery<string>(
                        $"""
                         SELECT tablename AS "Value"
                         FROM pg_tables
                         WHERE schemaname = 'public'
                           AND tableowner = current_user
                         """)
                    .ToListAsync(cancellationToken);

                return (unsafeRoles, ownedTables);
            }
            catch (OperationCanceledException)
            {
                // Arrêt demandé (l'hôte s'éteint pendant le démarrage) : ce n'est ni un verdict ni une
                // base injoignable, rien à réessayer ni à requalifier.
                throw;
            }
            catch (Exception ex) when (attempt < MaxAttempts && !cancellationToken.IsCancellationRequested)
            {
                logger?.LogWarning(
                    ex,
                    "Vérification RLS de démarrage : PostgreSQL injoignable (tentative {Attempt}/{MaxAttempts}). "
                    + "Nouvel essai dans {Delay}s.",
                    attempt, MaxAttempts, delay.TotalSeconds);

                await Task.Delay(delay, cancellationToken);
                delay += delay;
            }
            catch (Exception ex)
            {
                // Dernière tentative : on convertit en message d'exploitation. Sans cela, l'ordonnanceur
                // ne rapporte que « le conteneur n'a pas écouté sur le port », ce qui envoie chercher un
                // problème de binding là où il n'y en a pas.
                //
                // Indice supplémentaire quand l'échec vient d'un SocketException (résolution DNS ou
                // absence de route) : sur Cloud Run, c'est la signature d'une instance Cloud SQL non
                // rattachée au service (--add-cloudsql-instances absent) plutôt que d'un mot de passe ou
                // d'un nom de base incorrect — constaté en exploitation (révision sama-ecole-web-00008-bjd,
                // 04/09/2026) : ConnectionStrings__Default pointait un nom d'hôte que Cloud Run ne pouvait
                // pas résoudre, alors que l'instance Cloud SQL n'était rattachée à aucun VPC connector ni
                // via le proxy géré. Le socket Unix `/cloudsql/<connection-name>` (Cloud SQL Auth Proxy,
                // activé par ce même indicateur) contourne le problème sans jamais passer par le DNS.
                var socketHint = ContainsSocketException(ex)
                    ? " La cause immédiate est une erreur réseau de bas niveau (résolution DNS ou route " +
                      "absente) : sur Cloud Run avec Cloud SQL, vérifiez que l'instance est rattachée au " +
                      "service (--add-cloudsql-instances=<PROJET>:<REGION>:<INSTANCE>) et que " +
                      "ConnectionStrings__Default utilise le socket Unix correspondant " +
                      "(Host=/cloudsql/<connection-name>), plutôt qu'un nom d'hôte TCP."
                    : string.Empty;

                throw new InvalidOperationException(
                    $"PostgreSQL est resté injoignable après {MaxAttempts} tentatives : impossible de vérifier "
                    + "que le rôle applicatif est bien soumis à la Row-Level Security (AGENTS.md règle #2), donc "
                    + "l'application refuse de démarrer. Vérifiez ConnectionStrings__Default (hôte, port, "
                    + "SslMode) et que la base accepte les connexions depuis cet hébergement — voir .env.example."
                    + socketHint,
                    ex);
            }
        }
    }

    /// <summary>
    /// Parcourt la chaîne d'exceptions internes à la recherche d'un <see cref="SocketException"/> —
    /// signe d'un échec réseau de bas niveau (DNS, route absente, connexion refusée) plutôt que d'un
    /// rejet applicatif de PostgreSQL (identifiants invalides, base inexistante), qui remonterait en
    /// <see cref="Npgsql.PostgresException"/> et n'a pas besoin de cet indice.
    /// </summary>
    private static bool ContainsSocketException(Exception? ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is SocketException)
            {
                return true;
            }
        }

        return false;
    }
}
