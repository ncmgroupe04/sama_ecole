using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Persistence;
using SamaEcole.Persistence.Auth;
using SamaEcole.Persistence.Interceptors;
using SamaEcole.Persistence.Schools;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Testcontainers.PostgreSql;

namespace SamaEcole.IntegrationTests.Common;

/// <summary>
/// PostgreSQL réel reproduisant la topologie de rôles de production (ticket JGK-A03) :
///
///   * propriétaire  -> applique les migrations. Exempté de la RLS par PostgreSQL.
///   * sama_ecole_app -> ce que fait l'application. NOSUPERUSER, NOBYPASSRLS, propriétaire d'aucune
///                       table : c'est le SEUL rôle sur lequel les policies mordent réellement.
///
/// Tester avec le rôle propriétaire donnerait des tests verts et une isolation inexistante : la RLS
/// serait contournée sans un bruit. Tout ce qui simule l'application passe donc par le rôle applicatif.
/// </summary>
public sealed class RlsTestDatabase : IAsyncDisposable
{
    public const string AppRole = "sama_ecole_app";
    private const string AppPassword = "app_password_for_tests";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        // Le rôle doit exister AVANT les migrations : c'est la migration EnableRowLevelSecurity qui
        // lui pose ses GRANT. Dans l'autre ordre, il n'aurait aucun droit sur les tables.
        await ExecuteAsOwnerAsync(
            $"""
             CREATE ROLE {AppRole} LOGIN PASSWORD '{AppPassword}'
                 NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;
             """);

        await using var owner = NewOwnerContext();
        await owner.Database.MigrateAsync();
    }

    public ValueTask DisposeAsync() => _postgres.DisposeAsync();

    private string OwnerConnectionString => _postgres.GetConnectionString();

    public string AppConnectionString =>
        new NpgsqlConnectionStringBuilder(OwnerConnectionString)
        {
            Username = AppRole,
            Password = AppPassword
        }.ConnectionString;

    /// <summary>Contexte PROPRIÉTAIRE : migrations et préparation du jeu de données uniquement.</summary>
    public ApplicationDbContext NewOwnerContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(OwnerConnectionString)
            .Options;

        return new ApplicationDbContext(options, new StubTenantProvider(null), NullLogger<ApplicationDbContext>.Instance);
    }

    /// <summary>
    /// Contexte APPLICATIF : rôle bridé + TenantConnectionInterceptor, exactement comme au runtime.
    /// </summary>
    public ApplicationDbContext NewAppContext(Guid? schoolId)
    {
        var tenantProvider = new StubTenantProvider(schoolId);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(AppConnectionString)
            .AddInterceptors(new TenantConnectionInterceptor(tenantProvider))
            .Options;

        return new ApplicationDbContext(options, tenantProvider, NullLogger<ApplicationDbContext>.Instance);
    }

    public MatriculeGenerator NewGenerator(ApplicationDbContext dbContext) =>
        new(dbContext, TimeProvider.System);

    /// <summary>
    /// AuthStore branché sur le rôle APPLICATIF et SANS tenant — exactement la situation du login :
    /// la table users est sous RLS, seules les fonctions SECURITY DEFINER doivent lui donner accès.
    /// </summary>
    public AuthStore NewAuthStore(ApplicationDbContext dbContext, TimeProvider? timeProvider = null) =>
        new(dbContext, timeProvider ?? TimeProvider.System);

    /// <summary>Provisionnement d'école (ticket JGK-B01), branché sur le rôle applicatif.</summary>
    public SchoolProvisioningStore NewProvisioningStore(ApplicationDbContext dbContext) => new(dbContext);

    private async Task ExecuteAsOwnerAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(OwnerConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Ouvre une connexion BRUTE avec le rôle applicatif, en contournant totalement EF Core et son
    /// Global Query Filter. C'est le seul moyen de prouver que l'isolation tient au niveau de la
    /// BASE et non du C# (docs/Volume_8_Test_Strategy.md §5).
    /// </summary>
    public async Task<NpgsqlConnection> OpenRawAppConnectionAsync(Guid? schoolId)
    {
        var connection = new NpgsqlConnection(AppConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT set_config('app.current_school_id', @schoolId, false)";
        command.Parameters.AddWithValue("schoolId", schoolId?.ToString() ?? string.Empty);
        await command.ExecuteNonQueryAsync();

        return connection;
    }

    private sealed class StubTenantProvider(Guid? schoolId) : ITenantProvider
    {
        public Guid? CurrentSchoolId => schoolId;
    }
}