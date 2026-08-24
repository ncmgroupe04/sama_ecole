using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Persistence.Auth;
using SamaEcole.Persistence.AuditLogs;
using SamaEcole.Persistence.Interceptors;
using SamaEcole.Persistence.Notifications;
using SamaEcole.Persistence.Schools;
using SamaEcole.Persistence.Subscriptions;
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

        // Journal d'audit (ticket JGK-H01) : même contournement RLS, pour les connexions (avant tenant)
        // et les actions Super Admin (aucun SchoolId propre).
        services.AddScoped<IAuditLogStore, AuditLogStore>();

        // Traitement du webhook de paiement (ticket JGK-I06) : acteur anonyme, même contournement RLS
        // que ci-dessus pour lire/écrire subscription_payments et subscriptions.
        services.AddScoped<ISubscriptionPaymentStore, SubscriptionPaymentStore>();

        // Attribution manuelle d'un accès offert par le Super Admin (module Tarification &
        // Promotions) : même contournement RLS que ci-dessus, cette fois pour MODIFIER un abonnement
        // existant plutôt que d'en amorcer un.
        services.AddScoped<ISubscriptionAdminStore, SubscriptionAdminStore>();

        // Purge « Zone de danger » de l'écran Paramètres : le Directeur remet SON école à neuf après
        // une phase d'essai. Vit dans Persistence — elle contourne le Global Query Filter (pour
        // atteindre aussi les lignes en suppression logique) et suit l'ordre des clés étrangères.
        services.AddScoped<IResetSchoolDataService, ResetSchoolDataService>();

        // File des SMS : le worker et le webhook DLR n'ont AUCUN tenant (ni JWT, ni
        // app.current_school_id), donc aucune ligne visible sous RLS. Même contournement étroit que
        // ci-dessus — trois fonctions SECURITY DEFINER, et rien de plus.
        services.AddScoped<ISmsQueueStore, SmsQueueStore>();

        return services;
    }
}