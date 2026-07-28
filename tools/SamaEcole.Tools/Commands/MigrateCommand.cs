using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace SamaEcole.Tools.Commands;

/// <summary>
/// Applique les migrations EF Core en attente, puis rend la main. Conçue pour être lancée en conteneur
/// ÉPHÉMÈRE par le déploiement, avant le démarrage de l'application web.
///
/// Pourquoi ici et pas au démarrage de SamaEcole.Web : les migrations exigent le rôle PROPRIÉTAIRE
/// (sama_ecole), seul habilité à créer/altérer une table et à poser une policy RLS. Or PostgreSQL
/// EXEMPTE le propriétaire d'une table de ses policies RLS — faire tourner le web avec ce rôle
/// désactiverait l'isolation multi-tenant en silence, ce que RlsGuard refuse précisément au démarrage
/// (AGENTS.md règle #2, ticket JGK-A03). Séparer les deux processus permet d'injecter le secret
/// propriétaire dans un conteneur qui vit quelques secondes, jamais dans celui qui sert les requêtes.
///
/// L'ordre du déploiement reste celui de docs/Volume_9_Deployment_Operations.md §85-86 : sauvegarde
/// de la base D'ABORD, migrations ensuite. Cette commande n'assume pas la sauvegarde — elle échoue
/// simplement en code retour non nul, ce qui doit interrompre le déploiement avant que le web ne
/// démarre sur un schéma incohérent.
/// </summary>
public static class MigrateCommand
{
    /// <summary>Code retour d'échec — le déploiement doit s'arrêter là, jamais démarrer le web derrière.</summary>
    private const int Failure = 1;

    private const int Success = 0;

    public static async Task<int> RunAsync(
        string? connectionStringArgument,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        string connectionString;
        try
        {
            connectionString = ResolveConnectionString(connectionStringArgument);
        }
        catch (InvalidOperationException ex)
        {
            await error.WriteLineAsync(ex.Message);
            return Failure;
        }

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString) // Npgsql exclusivement — AGENTS.md règle #1.
            .Options;

        // Aucun tenant : les migrations ne passent par aucun Global Query Filter, exactement comme au
        // design time (DesignTimeDbContextFactory).
        await using var dbContext = new ApplicationDbContext(options, new NoTenantProvider(), NullLogger<ApplicationDbContext>.Instance);

        try
        {
            // Trace d'exploitation : sur quel rôle et quelle base on s'apprête à écrire. Un déploiement
            // qui échoue se diagnostique d'abord là — et un rôle inattendu (l'applicatif au lieu du
            // propriétaire) saute aux yeux avant même l'erreur de droits qui suivrait.
            var identity = await dbContext.Database
                .SqlQuery<string>($"""SELECT current_user || '@' || current_database() AS "Value" """)
                .FirstAsync(cancellationToken);

            await output.WriteLineAsync($"Connecté en tant que {identity}.");

            if (await IsNotTheOwnerRoleAsync(dbContext, cancellationToken))
            {
                await error.WriteLineAsync(
                    "Le rôle connecté ne possède aucune table de la base : ce n'est pas le rôle propriétaire " +
                    "(sama_ecole), et il n'a donc pas les droits DDL nécessaires. Corrigez " +
                    "ConnectionStrings__Migrations — voir AGENTS.md règle #2.");

                return Failure;
            }

            var pending = (await dbContext.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();

            if (pending.Count == 0)
            {
                await output.WriteLineAsync("Aucune migration en attente — la base est à jour.");
                return Success;
            }

            await output.WriteLineAsync($"{pending.Count} migration(s) en attente :");
            foreach (var migration in pending)
            {
                await output.WriteLineAsync($"  - {migration}");
            }

            // EF Core 9 pose un verrou exclusif le temps de l'application : deux instances lancées
            // simultanément par un déploiement progressif ne peuvent pas migrer en parallèle, la
            // seconde attend puis ne trouve plus rien à appliquer.
            await dbContext.Database.MigrateAsync(cancellationToken);

            await output.WriteLineAsync($"{pending.Count} migration(s) appliquée(s).");
            return Success;
        }
        catch (Exception ex)
        {
            // Message brut de PostgreSQL conservé : en exploitation, « permission denied for table X »
            // ou « role ... does not exist » est l'information utile, la masquer ferait perdre du temps.
            await error.WriteLineAsync($"Échec de l'application des migrations : {ex.Message}");
            return Failure;
        }
    }

    /// <summary>
    /// Garde symétrique de <see cref="Persistence.RlsGuard"/> : celui-ci refuse que le WEB tourne avec le
    /// rôle propriétaire, celle-ci refuse que les MIGRATIONS tournent avec le rôle applicatif.
    ///
    /// Sans elle, une chaîne de connexion mal configurée passe inaperçue : quand la base est déjà à
    /// jour, aucune DDL n'est tentée, aucune erreur de droits ne remonte, et la commande sort en succès.
    /// Le déploiement est vert, la mauvaise configuration reste en place, et l'échec ne survient qu'à la
    /// release SUIVANTE — au pire moment, avec le schéma d'une autre version déjà déployé.
    ///
    /// Sur une base VIERGE, personne ne possède encore de table : on ne peut alors rien conclure, et la
    /// commande poursuit (une migration sans droit CREATE échouera d'elle-même, bruyamment).
    /// </summary>
    private static async Task<bool> IsNotTheOwnerRoleAsync(
        ApplicationDbContext dbContext, CancellationToken cancellationToken)
    {
        var counts = await dbContext.Database
            .SqlQuery<int>(
                $"""
                 SELECT count(*) FILTER (WHERE tableowner = current_user)::int AS "Value"
                 FROM pg_tables
                 WHERE schemaname = 'public'
                 """)
            .ToListAsync(cancellationToken);

        var totalTables = await dbContext.Database
            .SqlQuery<int>(
                $"""SELECT count(*)::int AS "Value" FROM pg_tables WHERE schemaname = 'public' """)
            .FirstAsync(cancellationToken);

        // Base vierge : indécidable, on laisse passer. Sinon, ne posséder AUCUNE table signale le rôle
        // applicatif — le propriétaire, lui, les possède toutes.
        return totalTables > 0 && counts[0] == 0;
    }

    /// <summary>
    /// Chaîne de connexion du rôle PROPRIÉTAIRE : argument explicite, sinon ConnectionStrings__Migrations.
    ///
    /// AUCUN repli sur une valeur par défaut, contrairement à <see cref="DesignTimeDbContextFactory"/> :
    /// celui-ci sert l'outillage local de `dotnet ef`, où retomber sur la base de dev est commode. Ici la
    /// commande est destinée à la PRODUCTION — une variable d'environnement oubliée doit interrompre le
    /// déploiement, jamais faire migrer silencieusement une base locale ou, pire, une base voisine.
    /// ConnectionStrings__Default est volontairement ignorée : c'est le rôle applicatif, qui n'a pas les
    /// droits DDL et ne doit surtout pas les acquérir.
    /// </summary>
    internal static string ResolveConnectionString(string? argument)
    {
        if (!string.IsNullOrWhiteSpace(argument))
        {
            return argument;
        }

        var fromEnvironment = Environment.GetEnvironmentVariable("ConnectionStrings__Migrations");

        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return fromEnvironment;
        }

        throw new InvalidOperationException(
            "Chaîne de connexion des migrations introuvable. Renseignez la variable d'environnement " +
            "ConnectionStrings__Migrations (rôle propriétaire sama_ecole) ou passez --connection <chaîne>. " +
            "Voir .env.example et AGENTS.md règle #2.");
    }

    private sealed class NoTenantProvider : ITenantProvider
    {
        public Guid? CurrentSchoolId => null;
    }
}
