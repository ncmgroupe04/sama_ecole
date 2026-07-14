using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.Infrastructure.Security;
using SamaEcole.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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

    public static readonly Guid EcoleId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid DirecteurId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

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

        // Le compte de test est semé avec un VRAI hash Identity : le login doit le vérifier
        // réellement, pas court-circuiter le hachage.
        owner.Schools.Add(new School { Id = EcoleId, Name = "École de test" });
        owner.Users.Add(new User
        {
            Id = DirecteurId,
            SchoolId = EcoleId,
            Email = DirecteurEmail,
            PasswordHash = new IdentityPasswordHasher().Hash(DirecteurPassword),
            FullName = "Directeur de test",
            Role = Role.Directeur,
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
                     "ConnectionStrings__Default", "Jwt__Issuer", "Jwt__Audience", "Jwt__SigningKey",
                     "Jwt__AccessTokenMinutes", "Auth__MaxFailedAttempts", "Auth__LockoutMinutes",
                     "Auth__RefreshTokenDays"
                 })
        {
            Environment.SetEnvironmentVariable(key, null);
        }
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
