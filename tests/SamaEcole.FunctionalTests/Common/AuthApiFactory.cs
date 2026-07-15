using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.Infrastructure.Security;
using SamaEcole.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace SamaEcole.FunctionalTests.Common;

/// <summary>
/// Démarre l'API réelle contre un PostgreSQL réel (ticket JGK-A04).
///
/// L'application se connecte avec le RÔLE APPLICATIF (NOSUPERUSER/NOBYPASSRLS) : c'est la seule
/// configuration où RlsGuard accepte de démarrer, et la seule où les policies RLS mordent vraiment.
/// Les migrations, elles, tournent avec le propriétaire.
/// </summary>
public class AuthApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string AppRole = "sama_ecole_app";
    private const string AppPassword = "app_password_for_tests";

    public const string SigningKey = "cle_de_test_suffisamment_longue_pour_hmac_sha256_0123456789";
    public const string Issuer = "https://api.sama-ecole.sn";
    public const string Audience = "sama-ecole-clients";

    public const string DirecteurEmail = "directeur@sama-ecole.sn";
    public const string DirecteurPassword = "Motdepasse!Solide2026";

    public const string SecretaireEmail = "secretaire@sama-ecole.sn";
    public const string SecretairePassword = "AutreMotdepasse!2026";

    public const string FinanceEmail = "finance@sama-ecole.sn";
    public const string FinancePassword = "FinanceMotdepasse!2026";

    // Super Admin : SchoolId NULL, il n'appartient à aucun établissement (ticket JGK-B01).
    public const string SuperAdminEmail = "superadmin@sama-ecole.sn";
    public const string SuperAdminPassword = "SuperMotdepasse!2026";

    public static readonly Guid EcoleId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid DirecteurId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    public static readonly Guid SecretaireId = Guid.Parse("cccccccc-0000-0000-0000-000000000003");
    public static readonly Guid SuperAdminId = Guid.Parse("dddddddd-0000-0000-0000-000000000004");
    public static readonly Guid FinanceId = Guid.Parse("eeeeeeee-0000-0000-0000-000000000005");

    /// <summary>Capture les e-mails sortants : c'est le seul canal par lequel passe le mot de passe initial.</summary>
    public FakeEmailSender Emails { get; } = new();

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private string OwnerConnectionString => _postgres.GetConnectionString();

    private string AppConnectionString =>
        new NpgsqlConnectionStringBuilder(OwnerConnectionString)
        {
            Username = AppRole,
            Password = AppPassword
        }.ConnectionString;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        // AVANT toute création du host (elle est déclenchée par le premier CreateClient()/Services).
        ApplyEnvironment();

        // Le rôle doit exister AVANT les migrations : ce sont elles qui lui posent ses GRANT,
        // y compris EXECUTE sur les fonctions d'authentification.
        await ExecuteAsOwnerAsync($"""
            CREATE ROLE {AppRole} LOGIN PASSWORD '{AppPassword}'
                NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;
            """);

        await using var owner = NewOwnerContext();
        await owner.Database.MigrateAsync();

        // Les comptes de test sont semés avec un VRAI hash Identity : le login doit le vérifier
        // réellement, pas court-circuiter le hachage.
        var hasher = new IdentityPasswordHasher();

        owner.Schools.Add(new School { Id = EcoleId, Name = "École de test" });

        owner.Users.AddRange(
            new User
            {
                Id = DirecteurId,
                SchoolId = EcoleId,
                Email = DirecteurEmail,
                PasswordHash = hasher.Hash(DirecteurPassword),
                FullName = "Directeur de test",
                Role = Role.Directeur,
                Status = EntityStatus.Active
            },
            // Cible des suspensions (ticket JGK-A05) : un Directeur ne peut pas modifier son PROPRE
            // statut, il faut donc un second compte dans la même école.
            new User
            {
                Id = SecretaireId,
                SchoolId = EcoleId,
                Email = SecretaireEmail,
                PasswordHash = hasher.Hash(SecretairePassword),
                FullName = "Secrétaire de test",
                Role = Role.Secretariat,
                Status = EntityStatus.Active
            },
            // Rôle Finance : encaisse, mais ne compose JAMAIS un montant dû à l'inscription (règle #4).
            // Sert au test d'autorisation négatif obligatoire de JGK-E01 (POST /enrollments → 403).
            new User
            {
                Id = FinanceId,
                SchoolId = EcoleId,
                Email = FinanceEmail,
                PasswordHash = hasher.Hash(FinancePassword),
                FullName = "Comptable de test",
                Role = Role.Finance,
                Status = EntityStatus.Active
            },
            // Aucun SchoolId : sa session n'a pas de tenant, la RLS lui ferme donc toutes les tables
            // d'école. C'est précisément pourquoi la création d'un Directeur exige une fonction
            // SECURITY DEFINER (ticket JGK-B01).
            new User
            {
                Id = SuperAdminId,
                SchoolId = null,
                Email = SuperAdminEmail,
                PasswordHash = hasher.Hash(SuperAdminPassword),
                FullName = "Super Admin de test",
                Role = Role.SuperAdmin,
                Status = EntityStatus.Active
            });

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
        ClearEnvironment();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);

        // ConfigureTestServices s'exécute APRÈS les enregistrements de Program.cs : le remplacement de
        // service fonctionne, lui (contrairement à ConfigureAppConfiguration — voir plus bas).
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Emails);
        });
    }

    /// <summary>
    /// La configuration passe par des VARIABLES D'ENVIRONNEMENT, et non par ConfigureAppConfiguration.
    ///
    /// Piège du minimal hosting : Program.cs lit builder.Configuration pendant l'enregistrement des
    /// services (AddPersistence(builder.Configuration), AddJwtBearer(...)), alors que les sources
    /// ajoutées par WebApplicationFactory.ConfigureAppConfiguration ne sont appliquées qu'au Build().
    /// Elles arrivent donc TROP TARD : l'API de test se connectait à la base de développement réelle
    /// au lieu du conteneur, et cherchait le compte de test dans la mauvaise base.
    ///
    /// WebApplication.CreateBuilder lit AddEnvironmentVariables() d'entrée de jeu : ces valeurs-là
    /// sont visibles dès la première ligne de Program.cs.
    ///
    /// Contrepartie : les variables d'environnement sont globales au processus. D'où la
    /// désactivation du parallélisme dans AssemblyInfo.cs, sans quoi deux fabriques concurrentes se
    /// voleraient leur chaîne de connexion.
    /// </summary>
    private void ApplyEnvironment()
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", AppConnectionString);

        // NEUTRALISE la chaîne « Migrations ». L'API de test tourne en environnement Development et
        // charge donc appsettings.Development.json, où cette chaîne pointe vers la base de
        // développement RÉELLE du poste (localhost:5432). Or Program.cs s'en sert pour semer les
        // comptes de démonstration au démarrage : sans cette ligne, chaque exécution des tests
        // fonctionnels écrivait dans la vraie base du développeur au lieu de son conteneur — un
        // effet de bord invisible tant que les deux schémas coïncidaient.
        //
        // Vidée, elle fait sauter le seeder (Program.cs ignore une chaîne vide) : ces tests posent
        // eux-mêmes le jeu de données dont ils ont besoin, et rien d'autre.
        Environment.SetEnvironmentVariable("ConnectionStrings__Migrations", string.Empty);
        Environment.SetEnvironmentVariable("Jwt__Issuer", Issuer);
        Environment.SetEnvironmentVariable("Jwt__Audience", Audience);
        Environment.SetEnvironmentVariable("Jwt__SigningKey", SigningKey);
        Environment.SetEnvironmentVariable("Jwt__AccessTokenMinutes", "15");
        Environment.SetEnvironmentVariable("Auth__MaxFailedAttempts", "5");
        Environment.SetEnvironmentVariable("Auth__LockoutMinutes", "15");
        Environment.SetEnvironmentVariable("Auth__RefreshTokenDays", "14");
    }

    private static void ClearEnvironment()
    {
        foreach (var key in new[]
                 {
                     "ConnectionStrings__Default", "ConnectionStrings__Migrations",
                     "Jwt__Issuer", "Jwt__Audience", "Jwt__SigningKey",
                     "Jwt__AccessTokenMinutes", "Auth__MaxFailedAttempts", "Auth__LockoutMinutes",
                     "Auth__RefreshTokenDays"
                 })
        {
            Environment.SetEnvironmentVariable(key, null);
        }
    }

    /// <summary>
    /// Remet les comptes de test à neuf entre deux tests (ticket JGK-A05). Les tests de statut
    /// bloquent et débloquent les mêmes comptes : sans cela, le premier qui bloque la secrétaire
    /// ferait échouer tous les suivants, et l'ordre d'exécution deviendrait significatif.
    ///
    /// Exécuté par le PROPRIÉTAIRE : le rôle applicatif n'a volontairement pas le droit de purger
    /// user_status_history (journal append-only).
    /// </summary>
    public async Task ResetTestUsersAsync()
    {
        await using var owner = NewOwnerContext();

        await owner.Database.ExecuteSqlRawAsync("DELETE FROM user_status_history;");
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM refresh_tokens;");

        // Écoles et comptes créés PAR les tests (ticket JGK-B01) : sans cette purge, une école créée
        // dans un test resterait provisionnée et fausserait le suivant. Les utilisateurs d'abord :
        // ils référencent les écoles.
        await owner.Database.ExecuteSqlRawAsync(
            $"""
             DELETE FROM users
             WHERE "Id" NOT IN ('{DirecteurId}', '{SecretaireId}', '{SuperAdminId}', '{FinanceId}');
             """);

        // Inscriptions (ticket JGK-E01), AVANT les tables qu'elles référencent en Restrict (élèves,
        // classes, années, catégories de frais). Les lignes de frais d'abord : elles pointent l'inscription.
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM enrollment_fee_lines;");
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM enrollments;");

        // Avant les écoles : school_settings les référence en Restrict, et un PUT de test aurait
        // laissé une ligne de réglages sur l'école semée (ticket JGK-B02).
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM school_settings;");

        // Idem pour les années scolaires (ticket JGK-C01) — et il ne s'agit pas seulement des écoles
        // créées par les tests : une école n'a droit qu'à UNE année active. Sans cette purge, la
        // première année créée par un test resterait active et le suivant, croyant créer sa première
        // année, obtiendrait une année inactive. L'ordre d'exécution deviendrait significatif.
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM school_years;");

        // Matières (ticket JGK-C03) : un test qui crée « Maths / Primaire » ferait échouer en 409 le
        // suivant qui croit créer la même matière à neuf.
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM subjects;");

        // Frais (ticket JGK-F01), dans l'ordre des dépendances : l'historique référence le barème, le
        // barème référence classes et catégories. Le journal est append-only pour le rôle applicatif,
        // mais CE contexte est le propriétaire des tables — il peut le purger.
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM fee_change_history;");
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM class_fees;");
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM fee_categories;");

        // Élèves et classes : class_fees et students les référencent, donc APRÈS eux. Sans cette
        // purge, une classe laissée par un test fausserait le décompte « appliquer à toutes les
        // classes » (Option 1 de JGK-F01) du test suivant.
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM students;");
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM classrooms;");

        await owner.Database.ExecuteSqlRawAsync(
            $"""DELETE FROM schools WHERE "Id" <> '{EcoleId}';""");

        await owner.Database.ExecuteSqlRawAsync(
            """UPDATE users SET "Status" = 'Active', "AccessFailedCount" = 0, "LockoutEndAt" = NULL;""");

        Emails.Clear();
    }

    /// <summary>Forge un token signé par la MÊME clé que l'API, mais déjà expiré.</summary>
    public string CreateExpiredAccessToken()
    {
        var generator = new JwtTokenGenerator(
            Options.Create(new JwtOptions
            {
                Issuer = Issuer,
                Audience = Audience,
                SigningKey = SigningKey,
                AccessTokenMinutes = -5 // expiré depuis 5 minutes, bien au-delà du ClockSkew de 30 s
            }),
            TimeProvider.System);

        return generator.Generate(DirecteurId, EcoleId, Role.Directeur).Value;
    }

    private ApplicationDbContext NewOwnerContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(OwnerConnectionString)
            .Options;

        return new ApplicationDbContext(options, new NoTenantProvider());
    }

    private async Task ExecuteAsOwnerAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(OwnerConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private sealed class NoTenantProvider : ITenantProvider
    {
        public Guid? CurrentSchoolId => null;
    }
}
