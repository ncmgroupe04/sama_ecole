using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Persistence.Auth;
using SamaEcole.Persistence.Interceptors;
using SamaEcole.Persistence.Schools;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace SamaEcole.Persistence;

public static class DependencyInjection
{
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        // ConnectionStrings:Default = rôle APPLICATIF (sama_ecole_app), soumis à la RLS.
        // Les migrations utilisent ConnectionStrings:Migrations (rôle propriétaire) — voir
        // DesignTimeDbContextFactory. Ne jamais faire tourner l'application avec le rôle
        // propriétaire : PostgreSQL exempte le propriétaire d'une table de ses policies RLS.
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default est manquant. Voir .env.example.");

        services.AddScoped<TenantConnectionInterceptor>();

        services.AddDbContext<ApplicationDbContext>((serviceProvider, options) =>
            options
                .UseNpgsql(connectionString) // Npgsql exclusivement — AGENTS.md règle #1.
                .AddInterceptors(serviceProvider.GetRequiredService<TenantConnectionInterceptor>()));

        services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());

        // Numérotation par école, incrémentée dans la transaction d'enregistrement (ticket JGK-D01,
        // AGENTS.md règle #3). TimeProvider est injecté pour rendre l'année du matricule testable.
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IMatriculeGenerator, MatriculeGenerator>();

        // Chemin d'authentification (ticket JGK-A04) : passe par les fonctions SECURITY DEFINER,
        // car la table users est sous RLS et le login s'exécute sans tenant.
        services.AddScoped<IAuthStore, AuthStore>();

        // Création du premier compte d'une école (ticket JGK-B01) : passe elle aussi par une fonction
        // SECURITY DEFINER, `users` étant sous RLS et le Super Admin sans tenant.
        services.AddScoped<ISchoolProvisioningStore, SchoolProvisioningStore>();

        return services;
    }
}