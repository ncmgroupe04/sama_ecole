using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Users.Common;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.Infrastructure.Security;
using SamaEcole.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace SamaEcole.Tools.Commands;

/// <summary>
/// Amorce le tout PREMIER compte Super Admin d'un déploiement. Sans elle, aucun chemin n'existe pour
/// en créer un : <c>CreateUserCommandValidator</c> exclut délibérément <see cref="Role.SuperAdmin"/>
/// des rôles assignables via l'API (anti-escalade de privilège), et <c>DbSeeder</c> — seul autre
/// endroit qui sème ce rôle — est réservé à Development avec un mot de passe public codé en dur.
///
/// Même schéma que <see cref="MigrateCommand"/> : rôle PROPRIÉTAIRE (`users` est sous RLS, et les
/// fonctions SECURITY DEFINER du chemin de login — <c>provision_school_director</c> compris — n'ont
/// aucune variante Super Admin), conteneur éphémère, aucune trace du mot de passe en clair dans la
/// sortie de la commande.
///
/// Idempotente par e-mail : si le compte existe déjà, la commande ne fait rien et sort en succès —
/// relancer par erreur ne crée jamais de doublon ni n'écrase un mot de passe déjà en usage.
/// </summary>
public static class SeedSuperAdminCommand
{
    private const int Failure = 1;
    private const int Success = 0;

    public static async Task<int> RunAsync(
        string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken = default)
    {
        var email = ArgValue(args, "--email")?.Trim().ToLowerInvariant();
        var password = ArgValue(args, "--password");
        var fullName = ArgValue(args, "--name") ?? "Super Admin";
        var connectionArgument = ArgValue(args, "--connection");

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            await error.WriteLineAsync(
                "Usage : seed-superadmin --email <adresse> --password <mot de passe> [--name <nom>] "
                + "[--connection <chaîne>]");
            return Failure;
        }

        // Même politique que la création de compte applicative (Volume_7 §2) : un Super Admin n'est
        // pas exempté, contrairement au mot de passe GÉNÉRÉ de JGK-B01 — celui-ci est saisi par un
        // humain et doit satisfaire les mêmes garanties.
        var passwordErrors = PasswordPolicy.Validate(password, fullName, email).ToList();
        if (passwordErrors.Count > 0)
        {
            await error.WriteLineAsync("Mot de passe rejeté : " + string.Join(" ", passwordErrors));
            return Failure;
        }

        string connectionString;
        try
        {
            connectionString = MigrateCommand.ResolveConnectionString(connectionArgument);
        }
        catch (InvalidOperationException ex)
        {
            await error.WriteLineAsync(ex.Message);
            return Failure;
        }

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString) // Npgsql exclusivement — AGENTS.md règle #1.
            .Options;

        // Aucun tenant : User n'est pas une ITenantEntity (SchoolId nullable, Super Admin compris) —
        // même raisonnement que DbSeeder.
        await using var dbContext = new ApplicationDbContext(
            options, new NoTenantProvider(), NullLogger<ApplicationDbContext>.Instance);

        var identity = await dbContext.Database
            .SqlQuery<string>($"""SELECT current_user || '@' || current_database() AS "Value" """)
            .FirstAsync(cancellationToken);

        await output.WriteLineAsync($"Connecté en tant que {identity}.");

        // IgnoreQueryFilters : User n'a pas de filtre global (pas d'ITenantEntity), mais on le pose
        // explicitement par symétrie avec DbSeeder — l'intention (« voir TOUS les comptes ») doit être
        // lisible même si elle est ici un no-op.
        var existing = await dbContext.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

        if (existing is not null)
        {
            await output.WriteLineAsync(
                $"Un compte existe déjà pour {email} (rôle {existing.Role}) — rien à faire.");
            return Success;
        }

        var hasher = new IdentityPasswordHasher();

        dbContext.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            SchoolId = null, // Super Admin : aucun établissement de rattachement (voir User.SchoolId).
            Email = email,
            PasswordHash = hasher.Hash(password),
            FullName = fullName,
            Role = Role.SuperAdmin,
            Status = EntityStatus.Active
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        await output.WriteLineAsync($"Compte Super Admin créé : {email}.");
        return Success;
    }

    private static string? ArgValue(string[] args, string flag)
    {
        var index = Array.IndexOf(args, flag);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private sealed class NoTenantProvider : ITenantProvider
    {
        public Guid? CurrentSchoolId => null;
    }
}
