using Jangalekat.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Jangalekat.Persistence;

public static class DependencyInjection
{
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default est manquant. Voir .env.example.");

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(connectionString)); // Npgsql exclusivement — AGENTS.md règle #1, ne jamais ajouter un autre provider.

        services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());

        // TODO (ticket JGK-D01) : enregistrer ici IMatriculeGenerator une fois son
        // implémentation PostgreSQL (séquence par école) écrite.

        return services;
    }
}
