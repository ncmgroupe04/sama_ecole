using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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
/// </summary>
public static class RlsGuard
{
    public static async Task EnsureRuntimeRoleCannotBypassRlsAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
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
        var ownedTables = await dbContext.Database
            .SqlQuery<string>(
                $"""
                 SELECT tablename AS "Value"
                 FROM pg_tables
                 WHERE schemaname = 'public'
                   AND tableowner = current_user
                 """)
            .ToListAsync(cancellationToken);

        if (ownedTables.Count > 0)
        {
            throw new InvalidOperationException(
                $"Le rôle de l'application possède des tables ({string.Join(", ", ownedTables)}) : " +
                "PostgreSQL exempte le propriétaire des policies RLS, l'isolation multi-tenant serait " +
                "inopérante (AGENTS.md règle #2). Connectez l'application avec sama_ecole_app, qui n'est " +
                "propriétaire d'aucune table.");
        }
    }
}