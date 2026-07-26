using SamaEcole.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Logging.Abstractions;

namespace SamaEcole.Persistence;

/// <summary>
/// Utilisée uniquement par l'outillage `dotnet ef` (migrations add / database update) — jamais au
/// runtime. Évite de devoir démarrer tout le host ASP.NET (et ses dépendances applicatives) pour
/// générer une migration. Voir https://go.microsoft.com/fwlink/?linkid=851728.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        // Les migrations s'exécutent avec le rôle PROPRIÉTAIRE (sama_ecole), pas avec le rôle
        // applicatif : créer une table, l'ALTER et poser une policy RLS demandent des droits que
        // sama_ecole_app n'a pas — et ne doit pas avoir (ticket JGK-A03).
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Migrations")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Host=localhost;Port=5432;Database=sama_ecole_dev;Username=sama_ecole;Password=changeme";

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString) // Npgsql exclusivement — AGENTS.md règle #1.
            .Options;

        return new ApplicationDbContext(options, new DesignTimeTenantProvider(), NullLogger<ApplicationDbContext>.Instance);
    }

    /// <summary>Aucun tenant au design time : le Global Query Filter n'est jamais évalué par les migrations.</summary>
    private sealed class DesignTimeTenantProvider : ITenantProvider
    {
        public Guid? CurrentSchoolId => null;
    }
}