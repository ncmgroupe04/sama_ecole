using SamaEcole.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace SamaEcole.Persistence;

/// <summary>
/// Utilisée uniquement par l'outillage `dotnet ef` (migrations add / database update) — jamais au
/// runtime. Évite de devoir démarrer tout le host ASP.NET (et ses dépendances applicatives) pour
/// générer une migration. Voir https://go.microsoft.com/fwlink/?linkid=851728.
///
/// RÉSOUT SA CHAÎNE DE CONNEXION COMME L'APPLICATION, et non plus depuis les seules variables
/// d'environnement. Ce n'est pas un confort : la version précédente ne lisait ni appsettings ni
/// user-secrets et retombait EN SILENCE sur un « Host=localhost » codé en dur. Comme les identifiants
/// réels vivent dans les user-secrets (voir docs/Volume_9_Deployment_Operations.md), un
/// `dotnet ef database update` lancé sans variable d'environnement annonçait « Applying migration…
/// Done. » — sur la base LOCALE, pendant que l'application parlait à une tout autre base. La migration
/// semblait passée ; la colonne manquait à l'exécution (« 42703: column … does not exist »), très loin
/// de la commande fautive.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    /// <summary>
    /// UserSecretsId du projet de démarrage (SamaEcole.Web.csproj). Répété ici parce que Persistence ne
    /// référence pas Web — l'inverse serait une inversion de dépendance. À garder synchronisé avec le
    /// .csproj ; c'est un identifiant fixe, il ne change pas en pratique.
    /// </summary>
    private const string StartupUserSecretsId = "sama_ecole-web-dev";

    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var configuration = BuildConfiguration();

        // Les migrations s'exécutent avec le rôle PROPRIÉTAIRE (sama_ecole), pas avec le rôle
        // applicatif : créer une table, l'ALTER et poser une policy RLS demandent des droits que
        // sama_ecole_app n'a pas — et ne doit pas avoir (ticket JGK-A03).
        var connectionString =
            configuration.GetConnectionString("Migrations")
            ?? configuration.GetConnectionString("Default")
            ?? configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Aucune chaîne de connexion pour les migrations. Renseignez « ConnectionStrings:Migrations » "
                + "dans les user-secrets du projet de démarrage, dans appsettings.{Environment}.json, ou "
                + "exportez ConnectionStrings__Migrations. AUCUN repli n'est appliqué : migrer une base "
                + "choisie par défaut plutôt que par vous est précisément le défaut que ce garde-fou empêche.");
        }

        // Annonce la CIBLE (jamais les identifiants) : sans cela, rien à l'écran ne distingue une
        // migration appliquée à la base attendue d'une migration appliquée à une autre.
        Console.WriteLine($"[migrations] cible : {DescribeTarget(connectionString)}");

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString) // Npgsql exclusivement — AGENTS.md règle #1.
            .Options;

        return new ApplicationDbContext(options, new DesignTimeTenantProvider(), NullLogger<ApplicationDbContext>.Instance);
    }

    /// <summary>
    /// Même ordre de priorité que le host ASP.NET : appsettings, puis appsettings.{Environment}, puis
    /// user-secrets, puis variables d'environnement (qui l'emportent — c'est ainsi que la CI et le
    /// service « migrate » de docker-compose.yml injectent la leur).
    ///
    /// `dotnet ef` positionne le répertoire courant sur le PROJET DE DÉMARRAGE (-s), d'où la lecture
    /// des appsettings à cet emplacement.
    /// </summary>
    private static IConfiguration BuildConfiguration()
    {
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";

        return new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .AddUserSecrets(StartupUserSecretsId) // fichier absent = source vide, pas d'erreur
            .AddEnvironmentVariables()
            .Build();
    }

    /// <summary>Hôte et base seulement — jamais l'utilisateur ni le mot de passe : cette ligne est journalisable.</summary>
    private static string DescribeTarget(string connectionString)
    {
        string? host = null;
        string? database = null;

        foreach (var part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = part.IndexOf('=');
            if (separator <= 0) continue;

            var key = part[..separator].Trim();
            var value = part[(separator + 1)..].Trim();

            if (key.Equals("Host", StringComparison.OrdinalIgnoreCase)) host = value;
            else if (key.Equals("Database", StringComparison.OrdinalIgnoreCase)) database = value;
        }

        return $"{host ?? "?"}/{database ?? "?"}";
    }

    /// <summary>Aucun tenant au design time : le Global Query Filter n'est jamais évalué par les migrations.</summary>
    private sealed class DesignTimeTenantProvider : ITenantProvider
    {
        public Guid? CurrentSchoolId => null;
    }
}
