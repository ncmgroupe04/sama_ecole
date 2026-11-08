# Annexe B — Module Comptabilité / Finance : tests

> Complément de [`MODULE_COMPTABILITE.md`](MODULE_COMPTABILITE.md). Code **intégral** des tests du module, repris ligne pour ligne du dépôt `ncmgroupe04/sama_ecole`.
> Les tests sont la meilleure spécification exécutable des invariants (concurrence, idempotence, registre daté, isolation multi-tenant). **À reprendre en priorité** lors d'une réimplémentation : les tests d'intégration (`Category=MultiTenant`) et ceux de concurrence.

## Sommaire
* B.1 Infrastructure de test (fixtures partagées)
* B.2 Tests unitaires (validateurs, calculateurs, politiques, PDF)
* B.3 Tests d'intégration (PostgreSQL réel, RLS, xmin, idempotence)
* B.4 Tests fonctionnels (API HTTP de bout en bout)
* B.5 Tests JavaScript (Node)

**Stratégie :**
* **Unitaires** : logique pure (`PayrollCalculator`, `VatCalculator`, `InstallmentScheduleCalculator`, `PaymentCancellationPolicy`, validateurs) et génération des PDF (le fichier est produit et contient les mentions obligatoires).
* **Intégration** : base PostgreSQL réelle avec le **rôle applicatif non propriétaire** (`RlsTestDatabase`) afin que la RLS soit réellement exercée ; scénarios de concurrence à deux contextes ; vérification que les tables append-only refusent `UPDATE/DELETE`.
* **Fonctionnels** : `WebApplicationFactory` (`AuthApiFactory`) avec JWT, faux service de paiement et faux e-mail ; routes, codes HTTP, rôles, délégations.
* **JS** : tests Node (`node --test`) des fonctions pures de l'UI (paiement rapide, retenues de paie, frais/dépenses).

## B.1 Infrastructure de test

#### `tests/SamaEcole.IntegrationTests/Common/RlsTestDatabase.cs`

````csharp
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Persistence;
using SamaEcole.Persistence.Auth;
using SamaEcole.Persistence.Interceptors;
using SamaEcole.Persistence.Schools;
using SamaEcole.Persistence.Subscriptions;
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

    public string OwnerConnectionString => _postgres.GetConnectionString();

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
    /// Souscription ACTIVE à plafond illimité — celle que la migration AddTenantSubscriptions donne aux écoles
    /// existantes. À ajouter au jeu de données de tout test qui crée des élèves par les Handlers : sans
    /// souscription, le quota refuse toute création (fail-closed, voir StudentQuotaGuard).
    /// </summary>
    public static SamaEcole.Domain.Entities.TenantSubscription UnlimitedSubscription(Guid schoolId) => new()
    {
        SchoolId = schoolId,
        ProfileType = SamaEcole.Domain.Enums.ProfileType.EnseignementGeneral,
        StudentQuotaTier = SamaEcole.Domain.Enums.StudentQuotaTier.Tier4_Custom,
        MaxStudentLimit = int.MaxValue,
        SoftQuotaLimit = int.MaxValue,
        Status = SamaEcole.Domain.Enums.TenantSubscriptionStatus.Active,
        IsPedagogyEnabled = true,
        IsFinanceEnabled = true
    };

    /// <summary>Garde de quota branchée sur le contexte (donc le rôle) fourni, pour construire les Handlers à la main.</summary>
    public SamaEcole.Application.Subscriptions.StudentQuotaGuard NewQuotaGuard(ApplicationDbContext dbContext, Guid schoolId) =>
        new(
            new SamaEcole.Application.Subscriptions.TenantSubscriptionService(dbContext, new StubTenantProvider(schoolId)),
            new SamaEcole.Application.Subscriptions.Changes.OverQuotaTracker(dbContext, new StubTenantProvider(schoolId), TimeProvider.System));

    /// <summary>Verrou d'abonnement branché sur le contexte (donc le rôle) fourni, pour construire à la main les Handlers qu'il garde.</summary>
    public SamaEcole.Application.Subscriptions.Changes.SubscriptionChangeService NewChangeService(ApplicationDbContext dbContext, Guid schoolId) =>
        new(dbContext, new StubTenantProvider(schoolId), new SamaEcole.Application.Subscriptions.Pricing.SubscriptionPricingEngine(dbContext));

    /// <summary>
    /// AuthStore branché sur le rôle APPLICATIF et SANS tenant — exactement la situation du login :
    /// la table users est sous RLS, seules les fonctions SECURITY DEFINER doivent lui donner accès.
    /// </summary>
    public AuthStore NewAuthStore(ApplicationDbContext dbContext, TimeProvider? timeProvider = null) =>
        new(dbContext, timeProvider ?? TimeProvider.System);

    /// <summary>Provisionnement d'école (ticket JGK-B01), branché sur le rôle applicatif.</summary>
    public SchoolProvisioningStore NewProvisioningStore(ApplicationDbContext dbContext) => new(dbContext);

    /// <summary>
    /// Purge « Zone de danger » (réinitialisation des données d'essai), branchée sur le rôle
    /// APPLICATIF : c'est le seul moyen de prouver que la RLS borde la suppression. Avec le rôle
    /// propriétaire, un DELETE mal filtré viderait les autres écoles sans qu'aucun test ne bronche.
    /// </summary>
    public ResetSchoolDataService NewResetSchoolDataService(ApplicationDbContext dbContext) =>
        new(dbContext, NullLogger<ResetSchoolDataService>.Instance);

    /// <summary>
    /// Suppression d'une année scolaire en mode test (DELETE /school-years/{id}), branchée sur le rôle
    /// APPLICATIF pour la même raison que la purge ci-dessus : c'est le seul moyen de prouver que
    /// l'isolation tient au niveau de la base.
    /// </summary>
    public SchoolYearPurgeService NewSchoolYearPurgeService(ApplicationDbContext dbContext) =>
        new(dbContext, NullLogger<SchoolYearPurgeService>.Instance);

    /// <summary>Attribution d'accès offert et expiration d'abonnement (JGK-B03), rôle applicatif.</summary>
    public SubscriptionAdminStore NewSubscriptionAdminStore(ApplicationDbContext dbContext) => new(dbContext);

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
````


#### `tests/SamaEcole.IntegrationTests/Common/TestCurrentUser.cs`

````csharp
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;

namespace SamaEcole.IntegrationTests.Common;

/// <summary>Compte courant simulé pour les Handlers qui lisent <see cref="ICurrentUserService"/>.</summary>
public sealed class TestCurrentUser(Guid? userId = null, Role? role = null) : ICurrentUserService
{
    public Guid? UserId => userId;
    public Role? Role => role;
    public string? IpAddress => null;
}
````


#### `tests/SamaEcole.FunctionalTests/Common/AuthApiFactory.cs`

````csharp
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
using Microsoft.Extensions.Logging.Abstractions;
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
    /// <summary>
    /// Coupé par défaut pour TOUTE la suite (voir ApplyEnvironment) : un dashboard relu deux fois dans
    /// la même classe de tests doit refléter l'état réel, pas une valeur mise en cache par une
    /// assertion précédente. Un seul test a besoin du cache réellement ACTIF — celui qui prouve qu'un
    /// Super Admin (sans SchoolId) reçoit 200 et non 403 quand MemoryKpiCacheService est sur le chemin
    /// qui appelle vraiment BuildKey. Une PROPRIÉTÉ plutôt qu'un second constructeur : xUnit exige
    /// qu'IClassFixture&lt;AuthApiFactory&gt; n'expose qu'un SEUL constructeur public ("may only define
    /// a single public constructor") — le test dédié construit la fabrique lui-même (hors du mécanisme
    /// de fixture d'xUnit) et pose cette propriété AVANT d'appeler InitializeAsync(), seul moment où
    /// ApplyEnvironment() la lit.
    /// </summary>
    public bool KpiCacheEnabled { get; set; }

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

    public const string EnseignantEmail = "enseignant@sama-ecole.sn";
    public const string EnseignantPassword = "EnseignantMotdepasse!2026";

    // Super Admin : SchoolId NULL, il n'appartient à aucun établissement (ticket JGK-B01).
    public const string SuperAdminEmail = "superadmin@sama-ecole.sn";
    public const string SuperAdminPassword = "SuperMotdepasse!2026";

    public static readonly Guid EcoleId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid DirecteurId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    public static readonly Guid SecretaireId = Guid.Parse("cccccccc-0000-0000-0000-000000000003");
    public static readonly Guid SuperAdminId = Guid.Parse("dddddddd-0000-0000-0000-000000000004");
    public static readonly Guid FinanceId = Guid.Parse("eeeeeeee-0000-0000-0000-000000000005");
    public static readonly Guid EnseignantId = Guid.Parse("ffffffff-0000-0000-0000-000000000006");

    /// <summary>Capture les e-mails sortants : c'est le seul canal par lequel passe le mot de passe initial.</summary>
    public FakeEmailSender Emails { get; } = new();

    /// <summary>Remplace PayDunyaPaymentService (ticket JGK-I05) : aucune clé réelle dans les tests.</summary>
    public FakePaymentService Payments { get; } = new();

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

        // Souscription ACTIVE à plafond illimité — ce que la migration AddTenantSubscriptions donne aux
        // écoles existantes. Sans elle, le quota (fail-closed) refuserait toute création d'élève.
        owner.TenantSubscriptions.Add(new TenantSubscription
        {
            SchoolId = EcoleId,
            ProfileType = ProfileType.EnseignementGeneral,
            StudentQuotaTier = StudentQuotaTier.Tier4_Custom,
            MaxStudentLimit = int.MaxValue,
            SoftQuotaLimit = int.MaxValue,
            Status = TenantSubscriptionStatus.Active,
            // TOUS les modules sont souscrits : les tests de modules (Internat, Coran…) les activent par les réglages, ce
            // que le verrou d'abonnement n'autorise que pour un module compris dans les droits. Les tests du verrou lui-même
            // retirent un droit avec ExecuteOwnerSqlAsync.
            IsPedagogyEnabled = true,
            IsFinanceEnabled = true,
            IsInternatEnabled = true,
            IsCoranModuleEnabled = true
        });

        owner.Users.AddRange(SeedUsers(hasher));

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>
    /// Les 5 comptes de test, dans leur forme canonique — semés une fois par <see
    /// cref="InitializeAsync"/>, et RÉINSÉRÉS par <see cref="ResetTestUsersAsync"/> quand un test les a
    /// fait disparaître (voir son commentaire : depuis le 19/09/2026, <c>reset-data</c> supprime
    /// PHYSIQUEMENT les comptes du personnel — <c>DELETE FROM users WHERE "Id" = ANY(v_staff)</c>,
    /// migration <c>DecoupleProductionLockFromLiveMode</c>). Extrait pour que les deux méthodes
    /// décrivent le MÊME jeu de comptes, jamais deux copies qui divergent.
    /// </summary>
    private static IReadOnlyList<User> SeedUsers(IdentityPasswordHasher hasher) =>
    [
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
        // Rôle Enseignant (ticket JGK-G01) : saisit les notes, mais ne fixe ni les frais ni les
        // inscriptions.
        new User
        {
            Id = EnseignantId,
            SchoolId = EcoleId,
            Email = EnseignantEmail,
            PasswordHash = hasher.Hash(EnseignantPassword),
            FullName = "Enseignant de test",
            Role = Role.Enseignant,
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
        }
    ];

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

            services.RemoveAll<IPaymentService>();
            services.AddSingleton<IPaymentService>(Payments);
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
        Environment.SetEnvironmentVariable("Auth__RefreshTokenDays", "14");

        // La limite par défaut (5 requêtes / 5 min, appsettings.json) protège le formulaire public
        // d'inscription (JGK-I01) en production, mais TestServer ne renseigne aucune IP source
        // (Connection.RemoteIpAddress est null en transport in-memory) : toutes les requêtes de TOUS
        // les tests de la classe partagent donc la même partition « unknown ». Sans ce relèvement,
        // une poignée de tests suffirait à déclencher un 429 et à faire échouer les suivants.
        Environment.SetEnvironmentVariable("RateLimiting__Registration__PermitLimit", "1000");
        Environment.SetEnvironmentVariable("RateLimiting__Registration__WindowMinutes", "5");

        // Même raisonnement que la limite d'inscription ci-dessus : TestServer ne renseigne aucune IP
        // source, donc TOUTES les connexions de TOUS les tests d'une classe partagent la même
        // partition « unknown ». La limite de production (10 / 5 min, appsettings.json) protège contre
        // le credential stuffing — elle n'a rien à prouver ici, où chaque test se reconnecte
        // volontairement à chaque fois par clarté et isolation plutôt que de partager un jeton.
        Environment.SetEnvironmentVariable("RateLimiting__Login__PermitLimit", "1000");
        Environment.SetEnvironmentVariable("RateLimiting__Login__WindowMinutes", "5");

        // Même raison encore : la limite de production est ici volontairement BASSE (5 / 15 min — une
        // réinitialisation est un geste rare), donc la partition « unknown » partagée la ferait sauter
        // dès le sixième appel de la suite. Le comportement de refus lui-même n'a pas à être prouvé
        // ici : c'est de la configuration ASP.NET, pas du code applicatif.
        Environment.SetEnvironmentVariable("RateLimiting__PasswordReset__PermitLimit", "1000");
        Environment.SetEnvironmentVariable("RateLimiting__PasswordReset__WindowMinutes", "5");

        // Même raison : TestServer ne renseigne aucune IP source, tous les webhooks d'une classe
        // partagent la partition « unknown ». La limite de production (120 / min) n'a rien à prouver
        // ici — un test qui rejoue un webhook plusieurs fois (idempotence) la ferait sauter.
        Environment.SetEnvironmentVariable("RateLimiting__Webhooks__PermitLimit", "1000");
        Environment.SetEnvironmentVariable("RateLimiting__Webhooks__WindowMinutes", "5");

        // COUPE le dépilage de la file SMS. Sans cela, SmsQueueHostedService tournerait en tâche de
        // fond pendant toute la suite et ferait passer un message de Pending à Sent entre l'action
        // d'un test et son assertion : l'issue dépendrait du moment où le minuteur se déclenche.
        // Le worker est testé pour lui-même, en pilotant un tour explicitement (SmsQueueTests).
        Environment.SetEnvironmentVariable("Sms__Queue__Enabled", "false");

        // Même raison : COUPE le calcul quotidien des lots de relance de débiteurs
        // (DebtorAgingHostedService, Étape 5). Le worker est testé pour lui-même en invoquant
        // GenerateDebtorReminderBatchesCommand explicitement.
        Environment.SetEnvironmentVariable("Finance__DebtorAging__Enabled", "false");

        // Même raison encore : COUPE les alertes d'expiration et le passage automatique en lecture
        // seule (SubscriptionLifecycleHostedService, ticket JGK-B03). Le worker est testé pour
        // lui-même via ISubscriptionAdminStore.ExpireOverdueSubscriptionsAsync.
        Environment.SetEnvironmentVariable("Subscriptions__Lifecycle__Enabled", "false");

        // COUPE le cache KPI des dashboards par défaut (MemoryKpiCacheService). Sans cela, un dashboard
        // lu deux fois dans la même classe de tests refléterait la valeur mise en cache par une
        // assertion précédente plutôt que l'état réel après une mutation. _kpiCacheEnabled n'est mis à
        // true que par la fabrique dédiée qui prouve le comportement du Super Admin CACHE ACTIF.
        Environment.SetEnvironmentVariable("Kpi__Cache__Enabled", KpiCacheEnabled ? "true" : "false");

        // Destinataire de l'alerte « nouvelle demande d'inscription » (Super Admin) — les tests
        // Registration vérifient qu'elle part, et que le demandeur, lui, ne reçoit rien à la soumission.
        Environment.SetEnvironmentVariable("Registration__AdminNotificationEmail", SuperAdminAlertEmail);
    }

    public const string SuperAdminAlertEmail = "alertes-superadmin@unikol.test";

    private static void ClearEnvironment()
    {
        foreach (var key in new[]
                 {
                     "ConnectionStrings__Default", "ConnectionStrings__Migrations",
                     "Jwt__Issuer", "Jwt__Audience", "Jwt__SigningKey",
                     "Jwt__AccessTokenMinutes", "Auth__MaxFailedAttempts",
                     "Auth__RefreshTokenDays", "RateLimiting__Registration__PermitLimit",
                     "RateLimiting__Registration__WindowMinutes", "RateLimiting__Login__PermitLimit",
                     "RateLimiting__Login__WindowMinutes", "RateLimiting__PasswordReset__PermitLimit",
                     "RateLimiting__PasswordReset__WindowMinutes",
                     "RateLimiting__Webhooks__PermitLimit", "RateLimiting__Webhooks__WindowMinutes",
                     "Sms__Queue__Enabled",
                     "Finance__DebtorAging__Enabled", "Subscriptions__Lifecycle__Enabled",
                     "Kpi__Cache__Enabled", "Registration__AdminNotificationEmail"
                 })
        {
            Environment.SetEnvironmentVariable(key, null);
        }
    }

    /// <summary>
    /// Remet les comptes de test à neuf entre deux tests (ticket JGK-A05, puis la réinitialisation de
    /// mot de passe, puis d'e-mail). Les tests de statut bloquent et débloquent les mêmes comptes, et
    /// ceux de mot de passe/e-mail les RÉÉCRIVENT en dur : sans remise à zéro ici aussi, le premier test
    /// qui change le mot de passe (ou l'e-mail) de la secrétaire ferait échouer tous les suivants qui se
    /// connectent avec SecretairePassword/SecretaireEmail — l'ordre d'exécution deviendrait significatif.
    ///
    /// Exécuté par le PROPRIÉTAIRE : le rôle applicatif n'a volontairement pas le droit de purger
    /// user_status_history (journal append-only).
    ///
    /// RÉINSÈRE aussi un compte disparu, pas seulement « à neuf » : depuis le 19/09/2026,
    /// reset-data supprime PHYSIQUEMENT les comptes du personnel (Secrétariat/Finance/Enseignant,
    /// migration DecoupleProductionLockFromLiveMode). Un simple UPDATE ne touche alors plus aucune
    /// ligne — constaté sur SchoolModeEndpointsTests, dont plusieurs [Fact] appellent reset-data et
    /// faisaient donc échouer en 401 « e-mail inconnu » tout test suivant de la même classe qui se
    /// connectait en Secrétariat.
    /// </summary>
    public async Task ResetTestUsersAsync()
    {
        await using var owner = NewOwnerContext();

        await owner.Database.ExecuteSqlRawAsync("DELETE FROM user_status_history;");
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM audit_logs;");
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM refresh_tokens;");

        // Demandes d'inscription self-service (ticket JGK-I01) : table PLATEFORME, sans SchoolId à
        // rattacher — sans cette purge, une demande laissée par un test fausserait le décompte du
        // suivant (ex. « lister les demandes en attente » du futur JGK-I03).
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM school_registration_requests;");

        var hasher = new IdentityPasswordHasher();

        // IgnoreQueryFilters : défense en profondeur, User n'a de toute façon aucun filtre de tenant
        // (voir son commentaire de classe) — juste pour ne jamais dépendre d'un futur filtre global.
        var existingIds = (await owner.Users.IgnoreQueryFilters().Select(u => u.Id).ToListAsync())
            .ToHashSet();

        foreach (var seedUser in SeedUsers(hasher))
        {
            if (existingIds.Contains(seedUser.Id))
            {
                await owner.Database.ExecuteSqlInterpolatedAsync(
                    $"""UPDATE users SET "PasswordHash" = {seedUser.PasswordHash}, "Email" = {seedUser.Email} WHERE "Id" = {seedUser.Id};""");
            }
            else
            {
                // reset-data (depuis le 19/09/2026, migration DecoupleProductionLockFromLiveMode)
                // supprime PHYSIQUEMENT les comptes du personnel : un test de la classe qui l'appelle
                // fait donc disparaître ce compte pour de vrai. Sans cette branche, tout test suivant
                // qui s'y connecte échoue en 401 « e-mail inconnu » — jamais rejoué par un simple
                // UPDATE, qui ne touche aucune ligne quand la clé n'existe plus.
                owner.Users.Add(seedUser);
            }
        }

        await owner.SaveChangesAsync(CancellationToken.None);

        // Paie (Volume 1 §14) : fiche_paies et employee_contract_histories référencent employee_contracts
        // en Restrict, donc AVANT elle — et employee_contracts référence teachers/users en Restrict,
        // donc AVANT la purge des utilisateurs juste en dessous. Sans cette purge, un contrat créé sur
        // le compte Secrétaire partagé (JGK-A05) bloquerait le test suivant sur l'index unique
        // TeacherId/UserId (un seul contrat ACTIF par personne).
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM fiche_paies;");
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM employee_contract_histories;");
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM employee_contracts;");

        // Internat/Daara : le suivi par Hizb référence les élèves et les Oustaz référencent un compte, donc AVANT la purge
        // des comptes et des élèves plus bas. students.InstructorId est détaché d'abord (FK vers instructors). Sans cette
        // purge, un test qui crée une Halqa ferait échouer le nettoyage du suivant en 23503.
        // Journal des évaluations Hizb : FK vers students ET instructors, donc supprimé avant eux (le rôle propriétaire peut DELETE).
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM student_hizb_evaluations;");
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM student_hizb_statuses;");
        await owner.Database.ExecuteSqlRawAsync("""UPDATE students SET "InstructorId" = NULL WHERE "InstructorId" IS NOT NULL;""");
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM instructors;");

        // Internat « Pavillon/Lit » : les séjours référencent les inscriptions, les élèves et les lits (FK Restrict), et les
        // pavillons peuvent référencer un compte Surveillant — donc AVANT la purge des inscriptions, des élèves et des
        // comptes plus bas. Enfants avant parents. Sans elle, un test qui affecte un élève ferait échouer le nettoyage du
        // suivant en 23503.
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM boarding_attendances;");
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM attendance_sessions;");
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM boarding_leaves;");
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM boarding_enrollments;");
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM beds;");
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM dormitory_rooms;");
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM dormitories;");

        // Écoles et comptes créés PAR les tests (ticket JGK-B01) : sans cette purge, une école créée
        // dans un test resterait provisionnée et fausserait le suivant. Les utilisateurs d'abord :
        // ils référencent les écoles.
        await owner.Database.ExecuteSqlAsync(
            $"""
             DELETE FROM users
             WHERE "Id" NOT IN ({DirecteurId}, {SecretaireId}, {SuperAdminId}, {FinanceId}, {EnseignantId});
             """);

        // Encaissements (ticket JGK-F02) : ils référencent l'inscription en Restrict, donc AVANT elle.
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM payments;");

        // Sessions de caisse (module Caisse) : payments.CashierSessionId les référence, donc APRÈS la
        // purge des encaissements. Sans cette ligne, la session ouverte par le compte Finance/Directeur
        // dans un test resterait Open pour le suivant — qui se ferait refuser l'ouverture d'une nouvelle
        // session (« déjà une session ouverte », 422) sur ce même compte partagé entre tous les tests.
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM cashier_sessions;");

        // Échéanciers personnalisés et lots de relance (Étape 5) : ils référencent inscriptions ET
        // classes en Restrict, donc AVANT elles — les tables enfants (items/échéances) d'abord.
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM debtor_reminder_batch_items;");
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM fee_installments;");
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM debtor_reminder_batches;");
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM fee_installment_plans;");

        // Inscriptions (ticket JGK-E01), AVANT les tables qu'elles référencent en Restrict (élèves,
        // classes, années, catégories de frais). Les lignes de frais d'abord : elles pointent l'inscription.
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM enrollment_fee_lines;");
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM enrollments;");

        // Avant les écoles : school_settings les référence en Restrict, et un PUT de test aurait
        // laissé une ligne de réglages sur l'école semée (ticket JGK-B02).
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM school_settings;");

        // Observations du conseil (bulletins, JGK-G03) : référencent élèves ET trimestres en Restrict,
        // donc AVANT l'un ou l'autre — sinon un bulletin annoté par un test laisse une ligne qui bloque
        // la purge des trimestres du test suivant (FK_report_card_remarks_terms_SchoolId_TermId).
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM report_card_remarks;");

        // Notes (ticket JGK-G01), AVANT les tables qu'elles référencent en Restrict (élèves, matières,
        // trimestres) — et les trimestres eux-mêmes AVANT les années scolaires qu'ils référencent.
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM grades;");
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM terms;");

        // Mentions (ticket JGK-G02) : une mention personnalisée créée par un test ferait recevoir au
        // suivant SA liste au lieu des 5 valeurs par défaut (GetMentionsQueryHandler ne renvoie les
        // défauts que si la table est VIDE) — l'ordre d'exécution deviendrait significatif.
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM mentions;");

        // Présences (ticket JGK-D06) : student_attendances référence attendance_sheets ET students ;
        // attendance_sheets référence classrooms, subjects et school_years. Donc les lignes élève
        // d'abord, la fiche ensuite, le tout AVANT les tables référencées plus bas.
        // Saisies du cahier de roulement : FK Restrict vers students, classrooms et school_years.
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM roll_book_entries;");
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM student_attendances;");
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM attendance_sheets;");

        // Attributions enseignant (ticket JGK-D04) : référence teachers, classrooms, subjects ET
        // school_years en Restrict, donc AVANT chacune d'entre elles.
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM teacher_assignments;");

        // Appel par créneau (Évolution N°5) : attendance_sheets (déjà purgée plus haut) et LateArrivals portent une
        // FK Restrict vers ScheduleSlots, donc les billets d'abord, puis les créneaux — AVANT les enseignants, classes
        // et matières qu'ils référencent. student_attendances (porteuse du lien vers un billet) est déjà partie.
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM \"LateArrivals\";");
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM \"ScheduleSlots\";");

        // Surcharges de coefficient (Évolution N°4) : FK Restrict vers school_years, subjects ET classrooms,
        // donc AVANT les trois. Sans cette purge, une surcharge laissée par un test bloquerait celui d'après
        // en 23503 dès la suppression des années scolaires ci-dessous.
        // Programme des classes et options des élèves (Évolution N°6) : FK Restrict vers students,
        // school_years, subjects ET classrooms — les choix d'options d'abord (ils référencent le programme).
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM student_subject_enrollments;");
        // Dispenses de matières : FK Restrict vers students, subjects ET school_years, donc AVANT les trois.
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM student_subject_exemptions;");
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM class_subjects;");
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM subject_coefficient_overrides;");

        // Idem pour les années scolaires (ticket JGK-C01) — et il ne s'agit pas seulement des écoles
        // créées par les tests : une école n'a droit qu'à UNE année active. Sans cette purge, la
        // première année créée par un test resterait active et le suivant, croyant créer sa première
        // année, obtiendrait une année inactive. L'ordre d'exécution deviendrait significatif.
        // Les fermetures du calendrier (FK Restrict vers school_years) partent avant les années.
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM school_closures;");
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM school_years;");

        // Enseignants (ticket JGK-D03) : teacher_subjects référence teachers ET subjects en Restrict,
        // donc avant l'un ou l'autre. Sans cette purge, un enseignant laissé par un test fausserait
        // le décompte de la liste du test suivant.
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM teacher_subjects;");
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM teachers;");

        // Programmes (Évolution N°7) : syllabus_units référence subjects en Restrict ; les liens séance → chapitre
        // d'abord (ils référencent syllabus_units).
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM class_journal_entry_units;");
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM syllabus_units;");
        // Volumes horaires de l'école (Évolution N°7) : FK Restrict vers subjects.
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM weekly_hour_norms;");

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

        // Paiements d'abonnement (ticket JGK-I05) : référencent subscriptions (Restrict), donc AVANT eux.
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM subscription_payments;");

        // Abonnements créés PAR l'approbation d'une demande (ticket JGK-I03) : ils référencent schools
        // (Restrict), donc AVANT la suppression des écoles. Aucun n'est semé, on peut tout purger.
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM subscriptions;");

        // Souscriptions commerciales (Onboarding & Pricing SaaS) : même raison — elles référencent schools
        // (Restrict). Celle de l'école semée est remise à « Active, illimitée » : des tests la basculent en
        // PendingOnboarding, Suspended, ou lui posent un quota, et le suivant doit la retrouver neuve.
        // Demandes de changement d'abonnement : elles référencent schools (Restrict) — purgées avec le reste.
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM subscription_change_requests;");

        // Séquences de matricules : un test qui crée des élèves dans une école NEUVE en laisse une, qui référence schools (Restrict).
        // Celle de l'école semée est conservée (ses numéros n'ont pas à repartir de zéro).
        await owner.Database.ExecuteSqlAsync(
            $"""DELETE FROM matricule_sequences WHERE "SchoolId" <> {EcoleId};""");
        await owner.Database.ExecuteSqlAsync(
            $"""DELETE FROM tenant_subscriptions WHERE "SchoolId" <> {EcoleId};""");
        await owner.Database.ExecuteSqlAsync(
            $"""
             UPDATE tenant_subscriptions
                SET "Status" = 'Active', "ProfileType" = 'EnseignementGeneral', "StudentQuotaTier" = 'Tier4_Custom',
                    "MaxStudentLimit" = 2147483647, "SoftQuotaLimit" = 2147483647,
                    "IsPedagogyEnabled" = true, "IsFinanceEnabled" = true, "IsInternatEnabled" = true, "IsCoranModuleEnabled" = true,
                    "HasBoarding" = false, "OverQuotaSince" = NULL
              WHERE "SchoolId" = {EcoleId};
             """);

        await owner.Database.ExecuteSqlAsync(
            $"""DELETE FROM schools WHERE "Id" <> {EcoleId};""");

        // Remet la fiche de l'école semée à neuf : un PUT /schools/current de test la renomme, et sans
        // cette remise à zéro le test suivant hériterait du nom/adresse modifiés (ordre significatif).
        await owner.Database.ExecuteSqlAsync(
            $"""
             UPDATE schools
             SET "Name" = 'École de test', "Address" = NULL, "Phone" = NULL, "LogoUrl" = NULL
             WHERE "Id" = {EcoleId};
             """);

        await owner.Database.ExecuteSqlRawAsync(
            """UPDATE users SET "Status" = 'Active', "AccessFailedCount" = 0, "LockoutEndAt" = NULL;""");

        Emails.Clear();
        Payments.Clear();
    }

    /// <summary>
    /// Crée un utilisateur supplémentaire DIRECTEMENT en base (ticket JGK-I04) : contourne l'API à
    /// dessein — un établissement AwaitingPayment ne peut précisément PAS créer de compte via POST
    /// /users (bloqué par SubscriptionAwaitingPaymentMiddleware), c'est ce que ce ticket vérifie. Sert
    /// à prouver que le blocage s'applique à N'IMPORTE QUEL rôle de l'école, pas seulement au Directeur.
    /// </summary>
    public async Task<Guid> CreateAdditionalUserAsync(Guid schoolId, string email, string password, Role role)
    {
        await using var owner = NewOwnerContext();
        var hasher = new IdentityPasswordHasher();

        var user = new User
        {
            SchoolId = schoolId,
            Email = email,
            PasswordHash = hasher.Hash(password),
            FullName = "Utilisateur de test JGK-I04",
            Role = role,
            Status = EntityStatus.Active
        };

        owner.Users.Add(user);
        await owner.SaveChangesAsync(CancellationToken.None);

        return user.Id;
    }

    /// <summary>Exécute du SQL brut sous le rôle PROPRIÉTAIRE — simule un état de schéma (colonne absente, base
    /// non migrée) qu'aucune API ne sait produire.</summary>
    public async Task ExecuteOwnerSqlAsync(string sql)
    {
        await using var owner = NewOwnerContext();
        await owner.Database.ExecuteSqlRawAsync(sql);
    }

    /// <summary>
    /// Simule la fin de l'Onboarding d'une école créée par un test (Super Admin ou approbation) : sa
    /// souscription commerciale, née en PendingOnboarding, devient Active à plafond illimité. À appeler
    /// avant d'exercer une route métier de cette école — sinon OnboardingRoutingMiddleware la refuse en 403.
    /// </summary>
    public Task CompleteOnboardingAsync(Guid schoolId) =>
        ExecuteOwnerSqlAsync(
            $"""
             UPDATE tenant_subscriptions
                SET "Status" = 'Active', "MaxStudentLimit" = 2147483647, "SoftQuotaLimit" = 2147483647,
                    "IsPedagogyEnabled" = true, "IsFinanceEnabled" = true
              WHERE "SchoolId" = '{schoolId}'
             """);

    /// <summary>
    /// Modifie directement le statut d'un abonnement (ticket JGK-I04) : simule une confirmation de
    /// paiement SANS passer par le webhook (utile pour tester I04 indépendamment de I06). Pour tester le
    /// traitement du webhook lui-même, voir ProcessPaymentWebhookEndpointsTests (JGK-I06).
    /// </summary>
    public async Task SetSubscriptionStatusAsync(Guid schoolId, SubscriptionStatus status)
    {
        await using var owner = NewOwnerContext();

        await owner.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE subscriptions SET "Status" = {status.ToString()} WHERE "SchoolId" = {schoolId};""");
    }

    /// <summary>
    /// Fixe l'échéance d'un abonnement sans toucher à son statut : simule une échéance dépassée alors que le
    /// contrôle quotidien n'a pas encore basculé l'abonnement en ReadOnly (le worker est désactivé en test).
    /// </summary>
    public async Task SetSubscriptionExpiryAsync(Guid schoolId, DateOnly? expiresAt)
    {
        await using var owner = NewOwnerContext();

        await owner.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE subscriptions SET "ExpiresAt" = {expiresAt} WHERE "SchoolId" = {schoolId};""");
    }

    /// <summary>
    /// Lit une ligne SubscriptionPayment directement en base (ticket JGK-I05) : vérifie ce que l'API ne
    /// peut pas encore exposer elle-même (aucun GET par identifiant, JGK-I07 pas encore livré) — que la
    /// transaction de paiement est bien liée à l'abonnement, avec le bon montant et le bon statut.
    /// </summary>
    public async Task<SubscriptionPayment?> GetSubscriptionPaymentAsync(Guid paymentId)
    {
        await using var owner = NewOwnerContext();

        // IgnoreQueryFilters : SubscriptionPayment implémente ITenantEntity, et NewOwnerContext() utilise
        // NoTenantProvider (CurrentSchoolId => null) — sans ceci, le Global Query Filter deviendrait
        // "SchoolId == null" et ne trouverait donc JAMAIS aucune ligne réelle.
        return await owner.SubscriptionPayments.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == paymentId);
    }

    /// <summary>
    /// Lit l'abonnement d'une école directement en base (ticket JGK-I06) : vérifie l'activation
    /// (Status, ExpiresAt) déclenchée par le traitement du webhook. IgnoreQueryFilters, même
    /// raisonnement que GetSubscriptionPaymentAsync : Subscription implémente ITenantEntity et
    /// NewOwnerContext() utilise NoTenantProvider (CurrentSchoolId => null) — sans ceci, le Global
    /// Query Filter deviendrait « SchoolId == null » et ne trouverait JAMAIS aucune ligne réelle. La
    /// policy RLS, elle, exempte nativement le rôle PROPRIÉTAIRE utilisé ici.
    /// </summary>
    public async Task<Subscription?> GetSubscriptionAsync(Guid schoolId)
    {
        await using var owner = NewOwnerContext();

        return await owner.Subscriptions.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(s => s.SchoolId == schoolId);
    }

    /// <summary>
    /// Lit une classe directement en base, IgnoreQueryFilters compris (même raisonnement que
    /// GetSubscriptionPaymentAsync) — seul moyen de vérifier IsDeleted/DeletedAt/DeletedBy après un
    /// DELETE /classrooms/{id} : le Global Query Filter masquerait sinon la ligne archivée, et la RLS
    /// isolerait la requête sur un tenant que NoTenantProvider ne fournit pas.
    /// </summary>
    public async Task<Classroom?> GetClassroomAsync(Guid id)
    {
        await using var owner = NewOwnerContext();

        return await owner.Classrooms.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == id);
    }

    /// <summary>Même raisonnement que GetClassroomAsync, pour vérifier le soft delete de DELETE /buildings/{id}.</summary>
    public async Task<Building?> GetBuildingAsync(Guid id)
    {
        await using var owner = NewOwnerContext();

        return await owner.Buildings.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(b => b.Id == id);
    }

    /// <summary>Même raisonnement que GetClassroomAsync, pour vérifier le soft delete de DELETE /students/{id}.</summary>
    public async Task<Student?> GetStudentAsync(Guid id)
    {
        await using var owner = NewOwnerContext();

        return await owner.Students.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == id);
    }

    /// <summary>Même raisonnement que GetClassroomAsync, pour vérifier le soft delete de DELETE /subjects/{id}.</summary>
    public async Task<Subject?> GetSubjectAsync(Guid id)
    {
        await using var owner = NewOwnerContext();

        return await owner.Subjects.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == id);
    }

    /// <summary>Même raisonnement que GetClassroomAsync, pour vérifier le soft delete de DELETE /teachers/{id}.</summary>
    public async Task<Teacher?> GetTeacherAsync(Guid id)
    {
        await using var owner = NewOwnerContext();

        return await owner.Teachers.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(t => t.Id == id);
    }

    /// <summary>
    /// Même raisonnement que GetClassroomAsync, pour vérifier après DELETE /enrollments/{id} (annulation)
    /// que Status passe bien à Cancelled SANS que IsDeleted ne devienne true — CancelEnrollmentCommand
    /// documente explicitement ce choix (l'historique scolaire doit rester visible, jamais masqué par
    /// le Global Query Filter).
    /// </summary>
    public async Task<Enrollment?> GetEnrollmentAsync(Guid id)
    {
        await using var owner = NewOwnerContext();

        return await owner.Enrollments.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(e => e.Id == id);
    }

    /// <summary>
    /// Force un UPDATE PostgreSQL sur une inscription SANS toucher un champ métier visible (aucun
    /// changement de Status, aucun Payment créé) — PostgreSQL attribue malgré tout un nouveau xmin à
    /// toute ligne réécrite (MVCC), même quand aucune valeur ne change réellement.
    ///
    /// Nécessaire UNIQUEMENT pour tester le conflit RowVersion (règle #5) de CancelEnrollmentCommand :
    /// contrairement à Classroom/Subject/Teacher/Student (qui ont chacun leur propre UpdateCommand
    /// anodin pour faire tourner xmin avant un test de conflit), la seule autre écriture connue sur
    /// Enrollment est RecordPayment — qui ferait échouer le Cancel testé pour la MAUVAISE raison (« un
    /// paiement existe déjà », business rule 409) plutôt que pour un jeton simplement périmé.
    /// </summary>
    public async Task TouchEnrollmentRowVersionAsync(Guid enrollmentId)
    {
        await using var owner = NewOwnerContext();

        var enrollment = await owner.Enrollments.IgnoreQueryFilters()
            .SingleAsync(e => e.Id == enrollmentId);

        owner.Entry(enrollment).State = EntityState.Modified;
        await owner.SaveChangesAsync(CancellationToken.None);
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

        return generator.Generate(DirecteurId, "Directeur de test", EcoleId, Role.Directeur).Value;
    }

    /// <summary>
    /// Sème un jeu de données via le rôle PROPRIÉTAIRE (exempté de RLS), pour les scénarios où passer
    /// par l'API serait disproportionné (ticket JGK-R01 : le tableau de bord agrège élèves, inscriptions,
    /// enseignants, présences et abonnement — reconstituer tout cela par appels HTTP alourdirait le test
    /// sans rien prouver de plus). La LECTURE testée, elle, passe bien par l'API sous RLS.
    /// </summary>
    public async Task SeedAsOwnerAsync(Func<ApplicationDbContext, Task> seed)
    {
        await using var owner = NewOwnerContext();
        await seed(owner);
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    private ApplicationDbContext NewOwnerContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(OwnerConnectionString)
            .Options;

        return new ApplicationDbContext(options, new NoTenantProvider(), NullLogger<ApplicationDbContext>.Instance);
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
````


#### `tests/SamaEcole.FunctionalTests/Common/FakeEmailSender.cs`

````csharp
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.FunctionalTests.Common;

/// <summary>
/// Capture les e-mails au lieu de les envoyer (ticket JGK-B01).
///
/// Permet de vérifier ce qu'on ne peut vérifier nulle part ailleurs : le mot de passe initial du
/// Directeur ne sort QUE par cet e-mail — jamais par la réponse HTTP. Le test s'en sert ensuite pour
/// se connecter réellement avec le compte fraîchement créé, ce qui prouve toute la chaîne.
/// </summary>
public class FakeEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _sent = new();

    public IReadOnlyCollection<EmailMessage> Sent => _sent.ToArray();

    public void Clear() => _sent.Clear();

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        _sent.Enqueue(message);
        return Task.CompletedTask;
    }

    public EmailMessage? LastTo(string email) =>
        _sent.LastOrDefault(m => m.To.Equals(email, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Extrait le jeton du lien de réinitialisation. Comme le mot de passe initial, le jeton en clair
    /// ne sort QUE par cet e-mail : la base n'en stocke que le SHA-256, et la réponse HTTP ne le
    /// contient pas. Sans cette extraction, le parcours self-service ne serait testable qu'à moitié.
    /// </summary>
    public static string ExtractResetToken(EmailMessage message)
    {
        var match = Regex.Match(message.Body, @"reinitialiser-mot-de-passe\?token=(?<token>\S+)");

        if (!match.Success)
        {
            throw new InvalidOperationException(
                $"Aucun lien de réinitialisation trouvé dans l'e-mail :\n{message.Body}");
        }

        return match.Groups["token"].Value;
    }

    /// <summary>Extrait le mot de passe provisoire du corps de l'e-mail.</summary>
    public static string ExtractPassword(EmailMessage message)
    {
        var match = Regex.Match(message.Body, @"Mot de passe\s*:\s*(?<password>\S+)");

        if (!match.Success)
        {
            throw new InvalidOperationException(
                $"Aucun mot de passe trouvé dans l'e-mail :\n{message.Body}");
        }

        return match.Groups["password"].Value;
    }
}
````


#### `tests/SamaEcole.FunctionalTests/Common/FakePaymentService.cs`

````csharp
using System.Collections.Concurrent;
using System.Text.Json;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.FunctionalTests.Common;

/// <summary>
/// Remplace PayDunyaPaymentService dans les tests fonctionnels (tickets JGK-I05/I06) : aucune clé
/// PayDunya réelle n'existe pour cette suite, et un test fonctionnel n'a pas à dépendre d'un agrégateur
/// externe. Capture les requêtes reçues pour vérifier ce que le Handler lui a transmis (montant calculé
/// serveur, corrélation) sans jamais toucher le réseau.
///
/// Le FORMAT exact d'un webhook PayDunya réel est vérifié séparément par PayDunyaPaymentServiceTests
/// (tests unitaires, contre un transport HTTP simulé) — ce faux service, lui, accepte un JSON SIMPLIFIÉ
/// ({ "internalPaymentId": "...", "signatureValid": true|false }) pour que chaque test exprime le
/// scénario qu'il veut exercer directement dans le corps qu'il poste, sans état partagé mutable entre
/// scénarios (donc sans dépendance à l'ordre d'exécution des tests).
/// </summary>
public class FakePaymentService : IPaymentService
{
    private readonly ConcurrentQueue<PaymentInitiationRequest> _received = new();

    public string ProviderName => "FakeProvider";

    /// <summary>Si renseigné, le prochain appel lève une PaymentProviderException avec ce message (simule un refus PayDunya).</summary>
    public string? FailureMessage { get; set; }

    public IReadOnlyCollection<PaymentInitiationRequest> Received => _received.ToArray();

    public Task<PaymentInitiationResult> InitiatePaymentAsync(
        PaymentInitiationRequest request, CancellationToken cancellationToken)
    {
        _received.Enqueue(request);

        if (FailureMessage is not null)
        {
            throw new PaymentProviderException(FailureMessage);
        }

        var reference = $"FAKE-{Guid.NewGuid():N}";
        return Task.FromResult(new PaymentInitiationResult(reference, $"https://fake-checkout.example/{reference}"));
    }

    public WebhookParseResult ParseWebhook(string rawBody, string? contentType)
    {
        try
        {
            using var document = JsonDocument.Parse(rawBody);
            var root = document.RootElement;

            var signatureValid = !root.TryGetProperty("signatureValid", out var signatureProperty)
                || signatureProperty.GetBoolean();

            Guid? internalPaymentId = root.TryGetProperty("internalPaymentId", out var idProperty)
                && Guid.TryParse(idProperty.GetString(), out var parsed)
                ? parsed
                : null;

            return new WebhookParseResult(signatureValid, internalPaymentId, rawBody);
        }
        catch (JsonException)
        {
            return new WebhookParseResult(false, null, rawBody);
        }
    }

    /// <summary>Contrôle la réponse du PROCHAIN appel à ConfirmPaymentAsync (simule l'appel IPN, ticket JGK-I06).</summary>
    public bool NextConfirmationIsPaid { get; set; } = true;

    public decimal NextConfirmationAmount { get; set; }

    public Task<PaymentConfirmationResult> ConfirmPaymentAsync(string providerTransactionRef, CancellationToken cancellationToken)
        => Task.FromResult(new PaymentConfirmationResult(
            NextConfirmationIsPaid, NextConfirmationAmount, NextConfirmationIsPaid ? "completed" : "failed"));

    public void Clear()
    {
        _received.Clear();
        FailureMessage = null;
        NextConfirmationIsPaid = true;
        NextConfirmationAmount = 0;
    }
}
````


## B.2 Tests unitaires

#### `tests/SamaEcole.UnitTests/Finance/ApplyFeeInstallmentPlanToClassroomCommandValidatorTests.cs`

````csharp
using FluentAssertions;
using SamaEcole.Application.Finance.Commands.ApplyFeeInstallmentPlanToClassroom;
using Xunit;

namespace SamaEcole.UnitTests.Finance;

public class ApplyFeeInstallmentPlanToClassroomCommandValidatorTests
{
    private readonly ApplyFeeInstallmentPlanToClassroomCommandValidator _validator = new();

    private static ApplyFeeInstallmentPlanToClassroomCommand Valid() => new(
        Guid.NewGuid(),
        "Facilité de fin d'année",
        [
            new InstallmentTemplateLine("Versement 1", 0.4m, 0),
            new InstallmentTemplateLine("Versement 2", 0.3m, 30),
            new InstallmentTemplateLine("Versement 3", 0.3m, 60)
        ]);

    [Fact]
    public void A_Well_Formed_Template_Passes()
    {
        _validator.Validate(Valid()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void An_Empty_Classroom_Is_Rejected()
    {
        var result = _validator.Validate(Valid() with { ClassroomId = Guid.Empty });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(ApplyFeeInstallmentPlanToClassroomCommand.ClassroomId));
    }

    [Fact]
    public void Percentages_That_Do_Not_Sum_To_One_Hundred_Percent_Are_Rejected()
    {
        var result = _validator.Validate(Valid() with
        {
            Template = [new InstallmentTemplateLine("Versement unique", 0.9m, 0)]
        });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(ApplyFeeInstallmentPlanToClassroomCommand.Template));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void A_Percentage_Outside_Zero_Exclusive_To_One_Inclusive_Is_Rejected(decimal percentage)
    {
        var result = _validator.Validate(Valid() with
        {
            Template = [new InstallmentTemplateLine("Versement unique", percentage, 0)]
        });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void A_Negative_Offset_Is_Rejected()
    {
        var result = _validator.Validate(Valid() with
        {
            Template = [new InstallmentTemplateLine("Versement unique", 1m, -1)]
        });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void An_Empty_Template_Is_Rejected()
    {
        var result = _validator.Validate(Valid() with { Template = [] });

        result.IsValid.Should().BeFalse();
    }
}
````


#### `tests/SamaEcole.UnitTests/Finance/CancelPaymentCommandValidatorTests.cs`

````csharp
using FluentAssertions;
using SamaEcole.Application.Finance.Commands.CancelPayment;
using Xunit;

namespace SamaEcole.UnitTests.Finance;

public class CancelPaymentCommandValidatorTests
{
    private readonly CancelPaymentCommandValidator _validator = new();

    [Fact]
    public void A_Payment_Id_And_A_Reason_Are_Valid()
    {
        _validator.Validate(new CancelPaymentCommand(Guid.NewGuid(), "Saisi sur le mauvais élève"))
            .IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ok")]
    public void A_Missing_Or_Too_Short_Reason_Is_Refused(string reason)
    {
        _validator.Validate(new CancelPaymentCommand(Guid.NewGuid(), reason))
            .Errors.Should().Contain(e => e.PropertyName == nameof(CancelPaymentCommand.Reason));
    }

    [Fact]
    public void A_Reason_Longer_Than_500_Characters_Is_Refused()
    {
        _validator.Validate(new CancelPaymentCommand(Guid.NewGuid(), new string('x', 501)))
            .Errors.Should().Contain(e => e.PropertyName == nameof(CancelPaymentCommand.Reason));
    }

    [Fact]
    public void An_Empty_Payment_Id_Is_Refused()
    {
        _validator.Validate(new CancelPaymentCommand(Guid.Empty, "Erreur de saisie"))
            .Errors.Should().Contain(e => e.PropertyName == nameof(CancelPaymentCommand.PaymentId));
    }
}
````


#### `tests/SamaEcole.UnitTests/Finance/CancelledPaymentPdfTests.cs`

````csharp
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using SamaEcole.Application.Finance;
using SamaEcole.Application.Finance.Queries.GetDailyCashRegisterPdf;
using SamaEcole.Application.Finance.Queries.GetDailyClosingReportPdf;
using SamaEcole.Domain.Enums;
using SamaEcole.Infrastructure.Documents;
using Xunit;

namespace SamaEcole.UnitTests.Finance;

/// <summary>
/// Les trois pièces qui portent la trace d'un paiement annulé (spec 2026-10-17, §2.4) se rendent sans exception :
/// reçu annulé (bandeau + filigrane), journal de clôture (versement signalé « annulé le … » + annulations en négatif)
/// et journal de caisse par date (annulation en négatif). QuestPDF ne lève qu'au rendu : ces tests le déclenchent.
/// </summary>
public class CancelledPaymentPdfTests
{
    private static void ShouldBeAValidPdf(byte[] pdf)
    {
        pdf.Should().NotBeNullOrEmpty();
        Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
        pdf.Length.Should().BeGreaterThan(1000);
    }

    [Fact]
    public void A_Cancelled_Receipt_Is_Rendered_With_Its_Notice()
    {
        var receipt = new PaymentReceiptDto(
            "REC-2026-0007", "École Primaire Les Baobabs", "123 Rue de l'École", "contact@baobabs.sn", "+221 77 123 45 67",
            "Dakar", "123456789", "SN-DKR-2025-B-1234", "https://exemple.sn/logo.png", "ELEV-2026-0008", "Awa Fall", "CE1",
            "2026-2027", "Cash", 30_000m, 160_000m, 30_000m, 130_000m, new DateTimeOffset(2026, 10, 12, 9, 0, 0, TimeSpan.Zero),
            [], null, false,
            CancellationNotice: "REÇU ANNULÉ — " + PaymentCancellationOutcomeLabels.CancelledNote(
                new DateTimeOffset(2026, 10, 13, 9, 0, 0, TimeSpan.Zero), "Mauvais élève", PaymentCancellationOutcome.KeptBySchool));

        var pdf = new PaymentReceiptPdfGenerator(Mock.Of<ILogger<PaymentReceiptPdfGenerator>>()).Generate(receipt, logo: null);

        ShouldBeAValidPdf(pdf);
    }

    [Fact]
    public void A_Closing_Report_With_A_Flagged_Payment_And_A_Reversal_Is_Rendered()
    {
        var report = new DailyClosingReportDto(
            SchoolName: "Complexe Scolaire Touba Darou Karim", SchoolAddress: "Touba, Sénégal", SchoolLogoUrl: string.Empty,
            Date: new DateTime(2026, 10, 13), SessionId: "SES-2026-1013-01A0", CashierName: "Cheikh Mbacké Nguirane",
            TimeRange: "08:00 - 16:00", OpeningBalance: 100_000m, TotalCollected: -30_000m, TotalCashInRegister: 120_000m,
            MethodBreakdowns: [new PaymentMethodBreakdownDto(PaymentMethod.Cash, -30_000m)],
            CategoryBreakdowns: [new FeeCategoryBreakdownDto("Scolarité", -30_000m)],
            Transactions:
            [
                new TransactionRowDto("10:00", "REC-2026-0009", "Mamadou Sow", "Tuition", "Cash", 20_000m),
                new TransactionRowDto("11:00", "REC-2026-0010", "Awa Fall", "Tuition", "Cash", 5_000m,
                    CancelledNote: "Annulé le 14/10/2026 — Doublon (simple correction, l'argent reste à l'école)")
            ],
            ActualCashAmount: 120_000m, DiscrepancyAmount: 0m, DiscrepancyReason: null,
            Reversals:
            [
                new ReversalRowDto("13/10 09:00", "REC-2026-0007", "Awa Fall", "Cash", -50_000m,
                    "Annulation du reçu encaissé le 12/10/2026 — Mauvais élève (argent réutilisé pour l'inscription de l'enfant)")
            ],
            TotalReversed: 50_000m);

        var pdf = new DailyClosingReportPdfGenerator(Mock.Of<ILogger<DailyClosingReportPdfGenerator>>()).Generate(report, schoolLogo: null);

        ShouldBeAValidPdf(pdf);
    }

    [Fact]
    public void A_Daily_Cash_Register_With_A_Reversal_Is_Rendered()
    {
        var data = new DailyCashRegisterDto(
            "Complexe Scolaire Touba Darou Karim", "Touba, Sénégal", "contact@touba.sn", "+221 77 123 45 67", null,
            new DateOnly(2026, 10, 13), -30_000m,
            [new DailyCashRegisterMethodTotalDto("Cash", -30_000m)],
            [
                new DailyCashRegisterPaymentDto(new DateTimeOffset(2026, 10, 13, 10, 0, 0, TimeSpan.Zero), "Mamadou Sow", "ELEV-2026-0009", "REC-2026-0009", "Cash", 20_000m),
                new DailyCashRegisterPaymentDto(new DateTimeOffset(2026, 10, 13, 9, 0, 0, TimeSpan.Zero), "Awa Fall", "ELEV-2026-0008", "REC-2026-0007", "Cash", -50_000m,
                    IsReversal: true, Note: "Annulation du reçu encaissé le 12/10/2026 — Mauvais élève (remboursé au parent)")
            ]);

        var pdf = new DailyCashRegisterPdfGenerator().Generate(data, schoolLogo: null);

        ShouldBeAValidPdf(pdf);
    }
}
````


#### `tests/SamaEcole.UnitTests/Finance/CloseCashierSessionCommandValidatorTests.cs`

````csharp
using FluentAssertions;
using SamaEcole.Application.Finance.Commands.CloseCashierSession;
using Xunit;

namespace SamaEcole.UnitTests.Finance;

public class CloseCashierSessionCommandValidatorTests
{
    private readonly CloseCashierSessionCommandValidator _validator = new();

    [Fact]
    public void A_Well_Formed_Command_Is_Accepted()
    {
        _validator.Validate(new CloseCashierSessionCommand(Guid.NewGuid(), 25_000m)).IsValid.Should().BeTrue();
    }

    [Fact]
    public void An_Empty_SessionId_Is_Rejected()
    {
        var result = _validator.Validate(new CloseCashierSessionCommand(Guid.Empty, 25_000m));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CloseCashierSessionCommand.SessionId));
    }

    [Fact]
    public void A_Negative_ActualCashAmount_Is_Rejected()
    {
        var result = _validator.Validate(new CloseCashierSessionCommand(Guid.NewGuid(), -1m));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CloseCashierSessionCommand.ActualCashAmount));
    }

    [Fact]
    public void A_Zero_ActualCashAmount_Is_Accepted()
    {
        // Une caisse vidée à zéro reste un montant compté valide (JGK-F09) — ce n'est jamais rejeté
        // pour cette seule raison, seul un montant négatif n'a pas de sens physique.
        _validator.Validate(new CloseCashierSessionCommand(Guid.NewGuid(), 0m)).IsValid.Should().BeTrue();
    }

    [Fact]
    public void A_DiscrepancyReason_Over_500_Characters_Is_Rejected()
    {
        var result = _validator.Validate(
            new CloseCashierSessionCommand(Guid.NewGuid(), 25_000m, new string('x', 501)));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CloseCashierSessionCommand.DiscrepancyReason));
    }
}
````


#### `tests/SamaEcole.UnitTests/Finance/CloseEmployeeContractCommandValidatorTests.cs`

````csharp
using FluentAssertions;
using SamaEcole.Application.Finance.Commands.CloseEmployeeContract;
using Xunit;

namespace SamaEcole.UnitTests.Finance;

/// <summary>Volume 1 §14.1 — clôture d'un contrat : date de fin et motif obligatoires.</summary>
public class CloseEmployeeContractCommandValidatorTests
{
    private readonly CloseEmployeeContractCommandValidator _validator = new();

    private static CloseEmployeeContractCommand Command(
        DateOnly? endDate = null, string reason = "Fin de contrat — départ de l'établissement.") =>
        new(Guid.NewGuid(), endDate ?? new DateOnly(2026, 3, 31), reason, RowVersion: 1);

    [Fact]
    public void A_Valid_Command_Should_Be_Accepted()
    {
        _validator.Validate(Command()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void An_Empty_EndDate_Should_Be_Rejected()
    {
        var command = new CloseEmployeeContractCommand(
            Guid.NewGuid(), default, "Fin de contrat — départ de l'établissement.", 1);

        _validator.Validate(command).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ok")]
    public void An_Empty_Or_Vague_Reason_Should_Be_Rejected(string reason)
    {
        var result = _validator.Validate(Command(reason: reason));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CloseEmployeeContractCommand.Reason));
    }

    [Fact]
    public void A_Reason_Containing_Html_Should_Be_Rejected()
    {
        _validator.Validate(Command(reason: "<script>alert(1)</script> départ")).IsValid.Should().BeFalse();
    }

    [Fact]
    public void An_Empty_ContractId_Should_Be_Rejected()
    {
        var command = new CloseEmployeeContractCommand(
            Guid.Empty, new DateOnly(2026, 3, 31), "Fin de contrat — départ de l'établissement.", 1);

        _validator.Validate(command).IsValid.Should().BeFalse();
    }
}
````


#### `tests/SamaEcole.UnitTests/Finance/CreateDisbursementCommandValidatorTests.cs`

````csharp
using FluentAssertions;
using SamaEcole.Application.Features.Disbursements;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.UnitTests.Finance;

public class CreateDisbursementCommandValidatorTests
{
    private readonly CreateDisbursementCommandValidator _validator = new();

    private static CreateDisbursementCommand Valid() => new(
        Reason: "Fournitures de bureau",
        Category: DisbursementCategory.Fournitures,
        Amount: 25_000m,
        PaymentMethod: PaymentMethod.Cash,
        Date: new DateOnly(2026, 3, 10),
        Beneficiary: "Papeterie du Marché",
        ReceiptUrl: null);

    [Fact]
    public void A_Well_Formed_Disbursement_Passes()
    {
        _validator.Validate(Valid()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void A_Null_Vat_Rate_Is_Accepted_As_Not_Subject_To_Vat()
    {
        _validator.Validate(Valid() with { VatRate = null }).IsValid.Should().BeTrue("ex. Salaires n'est jamais assujettie");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.18)]
    [InlineData(1)]
    public void A_Vat_Rate_Within_Zero_And_One_Is_Accepted(decimal vatRate)
    {
        _validator.Validate(Valid() with { VatRate = vatRate }).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    [InlineData(18)]
    public void A_Vat_Rate_Outside_Zero_And_One_Is_Rejected(decimal vatRate)
    {
        var result = _validator.Validate(Valid() with { VatRate = vatRate });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateDisbursementCommand.VatRate));
    }

    [Fact]
    public void An_Empty_Reason_Is_Rejected()
    {
        _validator.Validate(Valid() with { Reason = "" }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void A_Non_Positive_Amount_Is_Rejected()
    {
        _validator.Validate(Valid() with { Amount = 0m }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void An_Empty_Beneficiary_Is_Rejected()
    {
        _validator.Validate(Valid() with { Beneficiary = "" }).IsValid.Should().BeFalse();
    }
}
````


#### `tests/SamaEcole.UnitTests/Finance/CreateEmployeeContractCommandValidatorTests.cs`

````csharp
using FluentAssertions;
using SamaEcole.Application.Finance.Commands.CreateEmployeeContract;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.UnitTests.Finance;

public class CreateEmployeeContractCommandValidatorTests
{
    private readonly CreateEmployeeContractCommandValidator _validator = new();

    private static CreateEmployeeContractCommand ValidTeacherContract() => new(
        TeacherId: Guid.NewGuid(), UserId: null, Type: ContractType.Permanent,
        BaseSalary: 250_000m, HourlyRate: 0m, TransportAllowance: 15_000m);

    private static CreateEmployeeContractCommand ValidUserContract() => new(
        TeacherId: null, UserId: Guid.NewGuid(), Type: ContractType.Vacataire,
        BaseSalary: 0m, HourlyRate: 5_000m, TransportAllowance: 0m);

    [Fact]
    public void Valid_Teacher_Contract_Should_Pass()
    {
        _validator.Validate(ValidTeacherContract()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Valid_User_Contract_Should_Pass()
    {
        _validator.Validate(ValidUserContract()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Neither_Teacher_Nor_User_Should_Fail()
    {
        var command = ValidTeacherContract() with { TeacherId = null };

        _validator.Validate(command).IsValid.Should().BeFalse("le contrat doit être lié à quelqu'un");
    }

    [Fact]
    public void Both_Teacher_And_User_Should_Fail()
    {
        var command = ValidTeacherContract() with { UserId = Guid.NewGuid() };

        _validator.Validate(command).IsValid.Should().BeFalse("un contrat ne peut pas être ambigu entre deux personnes");
    }

    [Fact]
    public void Permanent_Contract_Without_Base_Salary_Should_Fail()
    {
        // Sans cette règle, PayrollCalculator produirait silencieusement une fiche de paie à 0.
        var command = ValidTeacherContract() with { BaseSalary = 0m };

        _validator.Validate(command).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Vacataire_Contract_Without_Hourly_Rate_Should_Fail()
    {
        var command = ValidUserContract() with { HourlyRate = 0m };

        _validator.Validate(command).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(-1)]
    public void Negative_Transport_Allowance_Should_Fail(decimal transportAllowance)
    {
        var command = ValidTeacherContract() with { TransportAllowance = transportAllowance };

        _validator.Validate(command).IsValid.Should().BeFalse();
    }
}
````


#### `tests/SamaEcole.UnitTests/Finance/CreateFeeInstallmentPlanCommandValidatorTests.cs`

````csharp
using FluentAssertions;
using SamaEcole.Application.Finance.Commands.CreateFeeInstallmentPlan;
using Xunit;

namespace SamaEcole.UnitTests.Finance;

public class CreateFeeInstallmentPlanCommandValidatorTests
{
    private readonly CreateFeeInstallmentPlanCommandValidator _validator = new();

    private static CreateFeeInstallmentPlanCommand Valid() => new(
        Guid.NewGuid(),
        "Accord du 12/07",
        [
            new CreateFeeInstallmentLine("Versement 1", 40_000m, new DateOnly(2026, 10, 1)),
            new CreateFeeInstallmentLine("Versement 2", 60_000m, new DateOnly(2026, 11, 1))
        ]);

    [Fact]
    public void A_Well_Formed_Plan_Passes()
    {
        _validator.Validate(Valid()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void An_Empty_Enrollment_Is_Rejected()
    {
        var result = _validator.Validate(Valid() with { EnrollmentId = Guid.Empty });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateFeeInstallmentPlanCommand.EnrollmentId));
    }

    [Fact]
    public void An_Empty_Installment_List_Is_Rejected()
    {
        var result = _validator.Validate(Valid() with { Installments = [] });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateFeeInstallmentPlanCommand.Installments));
    }

    [Fact]
    public void More_Than_Twenty_Four_Installments_Is_Rejected()
    {
        var installments = Enumerable.Range(1, 25)
            .Select(i => new CreateFeeInstallmentLine($"Versement {i}", 1_000m, new DateOnly(2026, 10, 1).AddMonths(i)))
            .ToList();

        var result = _validator.Validate(Valid() with { Installments = installments });

        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_Non_Positive_Installment_Amount_Is_Rejected(int amount)
    {
        var result = _validator.Validate(Valid() with
        {
            Installments = [new CreateFeeInstallmentLine("Versement 1", amount, new DateOnly(2026, 10, 1))]
        });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void An_Empty_Installment_Label_Is_Rejected()
    {
        var result = _validator.Validate(Valid() with
        {
            Installments = [new CreateFeeInstallmentLine("", 40_000m, new DateOnly(2026, 10, 1))]
        });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void An_Html_Reason_Is_Rejected()
    {
        var result = _validator.Validate(Valid() with { Reason = "<script>alert(1)</script>" });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateFeeInstallmentPlanCommand.Reason));
    }

    [Fact]
    public void A_Null_Reason_Is_Accepted()
    {
        _validator.Validate(Valid() with { Reason = null }).IsValid.Should().BeTrue();
    }
}
````


#### `tests/SamaEcole.UnitTests/Finance/DailyClosingReportPdfGeneratorTests.cs`

````csharp
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using SamaEcole.Application.Finance.Queries.GetDailyClosingReportPdf;
using SamaEcole.Domain.Enums;
using SamaEcole.Infrastructure.Documents;
using Xunit;

namespace SamaEcole.UnitTests.Finance;

/// <summary>
/// Non-régression du moteur PDF (QuestPDF) du rapport de clôture de caisse (JGK-F09). Ce générateur
/// n'avait AUCUN test : il a longtemps échoué en silence faute de licence QuestPDF, puis renvoyait le
/// document brut sans filet. On couvre ici : PDF valide et non trivial, robustesse à un logo illisible
/// (repli sans logo, comme les autres pièces officielles), et session sans mouvement (0 transaction).
/// </summary>
public class DailyClosingReportPdfGeneratorTests
{
    private static readonly byte[] PdfMagic = "%PDF-"u8.ToArray();

    private static DailyClosingReportPdfGenerator NewGenerator() =>
        new(Mock.Of<ILogger<DailyClosingReportPdfGenerator>>());

    private static DailyClosingReportDto Report(
        IReadOnlyList<TransactionRowDto>? transactions = null,
        decimal? actualCashAmount = 0m) => new(
        SchoolName: "Complexe Scolaire Touba Darou Karim",
        SchoolAddress: "Touba, Sénégal",
        SchoolLogoUrl: string.Empty,
        Date: new DateTime(2026, 8, 31),
        SessionId: "SES-2026-0831-01A0",
        CashierName: "Cheikh Mbacké Nguirane",
        TimeRange: "08:00 - 16:00",
        OpeningBalance: 25_000m,
        TotalCollected: 80_000m,
        TotalCashInRegister: 105_000m,
        MethodBreakdowns: [new PaymentMethodBreakdownDto(PaymentMethod.Cash, 80_000m)],
        CategoryBreakdowns: [new FeeCategoryBreakdownDto("Scolarité", 80_000m)],
        Transactions: (transactions ??
        [
            new TransactionRowDto("09:15", "REC-2025-0007", "Awa Fall", "Scolarité", "Cash", 50_000m),
            new TransactionRowDto("11:40", "REC-2025-0008", "Mamadou Sow", "Scolarité", "Cash", 30_000m)
        ]).ToList(),
        ActualCashAmount: actualCashAmount,
        DiscrepancyAmount: actualCashAmount is null ? null : actualCashAmount - 105_000m,
        DiscrepancyReason: null);

    private static void ShouldBeAValidPdf(byte[] pdf)
    {
        pdf.Should().NotBeNullOrEmpty();
        Encoding.ASCII.GetString(pdf, 0, PdfMagic.Length).Should().Be("%PDF-", "l'en-tête magique d'un PDF");
    }

    [Fact]
    public void Generate_Produces_A_Valid_Non_Trivial_Pdf()
    {
        var pdf = NewGenerator().Generate(Report(), schoolLogo: null);

        ShouldBeAValidPdf(pdf);
        pdf.Length.Should().BeGreaterThan(1000, "un rapport de clôture complet n'est pas un fichier vide");
    }

    [Fact]
    public void Generate_Is_Robust_To_A_Session_Without_Any_Transaction()
    {
        var pdf = NewGenerator().Generate(Report(transactions: []), schoolLogo: null);

        ShouldBeAValidPdf(pdf);
    }

    [Fact]
    public void Generate_Is_Robust_To_A_Report_Still_Open_Without_Physical_Count()
    {
        var pdf = NewGenerator().Generate(Report(actualCashAmount: null), schoolLogo: null);

        ShouldBeAValidPdf(pdf);
    }

    [Fact]
    public void Generate_Falls_Back_To_A_Logoless_Report_When_Logo_Bytes_Are_Unreadable()
    {
        // Le filet ajouté avec les autres pièces officielles : des octets pathologiques en guise de
        // logo ne doivent jamais empêcher l'émission du rapport de clôture.
        var unreadable = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        var act = () => NewGenerator().Generate(Report(), schoolLogo: unreadable);

        act.Should().NotThrow();
        ShouldBeAValidPdf(NewGenerator().Generate(Report(), schoolLogo: unreadable));
    }
}
````


#### `tests/SamaEcole.UnitTests/Finance/DuesNoticePdfGeneratorTests.cs`

````csharp
using System.Text;
using FluentAssertions;
using SamaEcole.Application.Finance.Queries.GetDuesNotice;
using SamaEcole.Infrastructure.Documents;
using Xunit;

namespace SamaEcole.UnitTests.Finance;

/// <summary>Non-régression du moteur PDF (QuestPDF) de la sommation pour impayés.</summary>
public class DuesNoticePdfGeneratorTests
{
    private static readonly byte[] PdfMagic = "%PDF-"u8.ToArray();

    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private static DuesNoticeDto Notice(
        string? guardianName = "Moussa Fall",
        IReadOnlyList<DuesNoticeInstallmentDto>? installments = null) => new(
        EnrollmentId: Guid.NewGuid(),
        NoticeNumber: "SOM-1A2B3C4D",
        Matricule: "ELEV-2025-0008",
        StudentFullName: "Awa Fall",
        ClassroomName: "CE1",
        SchoolYearLabel: "2025-2026",
        GuardianName: guardianName,
        GuardianPhone: "+221771234567",
        TotalDue: 150_000,
        AmountPaid: 50_000,
        RemainingBalance: 100_000,
        OverdueInstallments: installments ?? new List<DuesNoticeInstallmentDto>
        {
            new("Frais de scolarité (Mois 1)", 50_000, new DateOnly(2025, 10, 1)),
            new("Frais de scolarité (Mois 2)", 50_000, new DateOnly(2025, 11, 1))
        },
        IssuedAt: new DateTimeOffset(2026, 1, 10, 9, 0, 0, TimeSpan.Zero),
        SchoolName: "École Primaire Les Baobabs",
        SchoolAddress: "Rue 12, Médina, Dakar",
        SchoolCity: "Dakar",
        SchoolPhone: "+221338001122",
        SchoolNinea: "123456789",
        SchoolLogoUrl: "https://exemple.sn/logo.png");

    private static void ShouldBeAValidPdf(byte[] pdf)
    {
        pdf.Should().NotBeNullOrEmpty();
        Encoding.ASCII.GetString(pdf, 0, PdfMagic.Length).Should().Be("%PDF-", "l'en-tête magique d'un PDF");
    }

    [Fact]
    public void Generate_Produces_A_Valid_Non_Trivial_Pdf()
    {
        var pdf = new DuesNoticePdfGenerator().Generate(Notice(), logo: null, qrCodeImage: TinyPng);

        ShouldBeAValidPdf(pdf);
        pdf.Length.Should().BeGreaterThan(500, "une sommation complète n'est pas un fichier vide");
    }

    [Fact]
    public void Generate_Is_Robust_When_No_Guardian_Is_Registered()
    {
        var pdf = new DuesNoticePdfGenerator().Generate(Notice(guardianName: null), logo: null, qrCodeImage: TinyPng);

        ShouldBeAValidPdf(pdf);
    }

    [Fact]
    public void Generate_Handles_A_Single_Overdue_Installment()
    {
        var pdf = new DuesNoticePdfGenerator().Generate(
            Notice(installments: new List<DuesNoticeInstallmentDto> { new("Frais d'inscription", 25_000, new DateOnly(2025, 9, 1)) }),
            logo: null, qrCodeImage: TinyPng);

        ShouldBeAValidPdf(pdf);
    }

    [Fact]
    public void Generate_Falls_Back_To_A_Logoless_Notice_When_Logo_Bytes_Are_Unreadable()
    {
        var unreadable = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        var act = () => new DuesNoticePdfGenerator().Generate(Notice(), logo: unreadable, qrCodeImage: TinyPng);

        act.Should().NotThrow();
    }
}
````


#### `tests/SamaEcole.UnitTests/Finance/FeeValidatorsTests.cs`

````csharp
using FluentAssertions;
using SamaEcole.Application.Finance.Commands.ApplyStandardFee;
using SamaEcole.Application.Finance.Commands.CreateFeeCategory;
using SamaEcole.Application.Finance.Commands.DeleteClassFee;
using SamaEcole.Application.Finance.Commands.DeleteFeeCategory;
using SamaEcole.Application.Finance.Commands.UpdateClassFee;
using Xunit;

namespace SamaEcole.UnitTests.Finance;

public class FeeValidatorsTests
{
    private readonly CreateFeeCategoryCommandValidator _categoryValidator = new();
    private readonly ApplyStandardFeeCommandValidator _applyValidator = new();
    private readonly UpdateClassFeeCommandValidator _updateValidator = new();
    private readonly DeleteClassFeeCommandValidator _deleteFeeValidator = new();
    private readonly DeleteFeeCategoryCommandValidator _deleteCategoryValidator = new();

    [Fact]
    public void Valid_Category_Should_Pass()
    {
        var command = new CreateFeeCategoryCommand { Name = "Mensualité", IsRecurring = true };

        _categoryValidator.Validate(command).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Empty_Category_Name_Should_Fail()
    {
        var command = new CreateFeeCategoryCommand { Name = "", IsRecurring = false };

        _categoryValidator.Validate(command).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Category_Name_With_Html_Should_Fail()
    {
        // JGK-F01 — branchement de la règle NoHtml (payloads exhaustifs : SafeTextValidationTests).
        var command = new CreateFeeCategoryCommand { Name = "<i>Frais</i>", IsRecurring = false };

        _categoryValidator.Validate(command).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Frais_D_Inscription_Should_Pass()
    {
        // Le nom de catégorie LE PLUS COURANT du produit contient la sous-chaîne « script » : si la
        // règle anti-injection bloquait le mot au lieu des caractères « < » / « > », ce test tomberait.
        var command = new CreateFeeCategoryCommand { Name = "Frais d'inscription", IsRecurring = false };

        _categoryValidator.Validate(command).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Apply_Standard_With_A_Zero_Amount_Should_Pass()
    {
        // Zéro est légitime : un frais peut être offert (inscription gratuite, par exemple).
        var command = new ApplyStandardFeeCommand { FeeCategoryId = Guid.NewGuid(), Amount = 0 };

        _applyValidator.Validate(command).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Apply_Standard_With_A_Negative_Amount_Should_Fail()
    {
        var command = new ApplyStandardFeeCommand { FeeCategoryId = Guid.NewGuid(), Amount = -1 };

        _applyValidator.Validate(command).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Apply_Standard_With_An_Absurd_Amount_Should_Fail()
    {
        // Garde-fou contre le zéro de trop, qui multiplierait le montant sur toutes les classes.
        var command = new ApplyStandardFeeCommand { FeeCategoryId = Guid.NewGuid(), Amount = 999_000_000 };

        _applyValidator.Validate(command).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Apply_Standard_Without_A_Category_Should_Fail()
    {
        var command = new ApplyStandardFeeCommand { FeeCategoryId = Guid.Empty, Amount = 15000 };

        _applyValidator.Validate(command).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Apply_Standard_Without_A_Level_Should_Pass()
    {
        // Périmètre absent = toutes les classes : le champ reste facultatif.
        var command = new ApplyStandardFeeCommand { FeeCategoryId = Guid.NewGuid(), Amount = 15000, Level = null };

        _applyValidator.Validate(command).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Apply_Standard_On_A_Free_Form_Level_Should_Pass()
    {
        // Nomenclature LIBRE : aucune liste figée de niveaux, ici comme à la création d'une classe.
        var command = new ApplyStandardFeeCommand { FeeCategoryId = Guid.NewGuid(), Amount = 15000, Level = "Cycle moyen" };

        _applyValidator.Validate(command).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Apply_Standard_On_A_Level_Containing_Html_Should_Fail()
    {
        var command = new ApplyStandardFeeCommand
        {
            FeeCategoryId = Guid.NewGuid(),
            Amount = 15000,
            Level = "<script>alert(1)</script>"
        };

        _applyValidator.Validate(command).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Update_With_A_Valid_Amount_Should_Pass()
    {
        var command = new UpdateClassFeeCommand(Guid.NewGuid(), 15000, RowVersion: 42);

        _updateValidator.Validate(command).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Update_With_A_Negative_Amount_Should_Fail()
    {
        var command = new UpdateClassFeeCommand(Guid.NewGuid(), -5000, RowVersion: 42);

        _updateValidator.Validate(command).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Delete_Fee_With_A_Valid_Id_Should_Pass()
    {
        var command = new DeleteClassFeeCommand(Guid.NewGuid(), RowVersion: 42);

        _deleteFeeValidator.Validate(command).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Delete_Fee_Without_An_Id_Should_Fail()
    {
        var command = new DeleteClassFeeCommand(Guid.Empty, RowVersion: 42);

        _deleteFeeValidator.Validate(command).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Delete_Category_With_A_Valid_Id_Should_Pass()
    {
        var command = new DeleteFeeCategoryCommand(Guid.NewGuid());

        _deleteCategoryValidator.Validate(command).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Delete_Category_Without_An_Id_Should_Fail()
    {
        var command = new DeleteFeeCategoryCommand(Guid.Empty);

        _deleteCategoryValidator.Validate(command).IsValid.Should().BeFalse();
    }
}
````


#### `tests/SamaEcole.UnitTests/Finance/FinancialCommitmentPdfGeneratorTests.cs`

````csharp
using System.Text;
using FluentAssertions;
using SamaEcole.Application.Finance.Queries.GetFinancialCommitment;
using SamaEcole.Infrastructure.Documents;
using Xunit;

namespace SamaEcole.UnitTests.Finance;

/// <summary>Non-régression du moteur PDF (QuestPDF) de l'engagement financier.</summary>
public class FinancialCommitmentPdfGeneratorTests
{
    private static readonly byte[] PdfMagic = "%PDF-"u8.ToArray();

    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private static FinancialCommitmentDto Commitment(
        string? guardianName = "Moussa Fall",
        string? city = "Dakar") => new(
        FinancialCommitmentId: Guid.NewGuid(),
        CommitmentNumber: "ENG-1A2B3C4D",
        Matricule: "ELEV-2025-0008",
        StudentFullName: "Awa Fall",
        ClassroomName: "CE1",
        SchoolYearLabel: "2025-2026",
        Amount: 100_000,
        DueDate: new DateOnly(2026, 6, 30),
        Terms: "Règlement en 3 versements mensuels de 33 333 FCFA à compter d'avril 2026.",
        SignedAt: new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero),
        GuardianName: guardianName,
        GuardianPhone: "+221771234567",
        SchoolName: "École Primaire Les Baobabs",
        SchoolAddress: city is null ? null : $"Rue 12, Médina, {city}",
        SchoolCity: city,
        SchoolPhone: "+221338001122",
        SchoolNinea: "123456789",
        SchoolLogoUrl: "https://exemple.sn/logo.png");

    private static void ShouldBeAValidPdf(byte[] pdf)
    {
        pdf.Should().NotBeNullOrEmpty();
        Encoding.ASCII.GetString(pdf, 0, PdfMagic.Length).Should().Be("%PDF-", "l'en-tête magique d'un PDF");
    }

    [Fact]
    public void Generate_Produces_A_Valid_Non_Trivial_Pdf()
    {
        var pdf = new FinancialCommitmentPdfGenerator().Generate(Commitment(), logo: null, qrCodeImage: TinyPng);

        ShouldBeAValidPdf(pdf);
        pdf.Length.Should().BeGreaterThan(500, "un engagement complet n'est pas un fichier vide");
    }

    [Fact]
    public void Generate_Is_Robust_When_No_Guardian_Is_Registered()
    {
        var pdf = new FinancialCommitmentPdfGenerator().Generate(Commitment(guardianName: null), logo: null, qrCodeImage: TinyPng);

        ShouldBeAValidPdf(pdf);
    }

    [Fact]
    public void Generate_Falls_Back_To_A_Logoless_Commitment_When_Logo_Bytes_Are_Unreadable()
    {
        var unreadable = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        var act = () => new FinancialCommitmentPdfGenerator().Generate(Commitment(), logo: unreadable, qrCodeImage: TinyPng);

        act.Should().NotThrow();
    }
}
````


#### `tests/SamaEcole.UnitTests/Finance/HourRecordSheetPdfGeneratorTests.cs`

````csharp
using System.Text;
using FluentAssertions;
using SamaEcole.Application.Finance.Queries.GetHourRecordSheet;
using SamaEcole.Infrastructure.Documents;
using Xunit;

namespace SamaEcole.UnitTests.Finance;

/// <summary>Non-régression du moteur PDF (QuestPDF) de la fiche de suivi des heures (Vacataire).</summary>
public class HourRecordSheetPdfGeneratorTests
{
    private static readonly byte[] PdfMagic = "%PDF-"u8.ToArray();

    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private static HourRecordSheetDto Sheet(IReadOnlyList<HourRecordLineDto>? lines = null)
    {
        var resolvedLines = lines ?? new List<HourRecordLineDto>
        {
            new(new DateOnly(2026, 3, 2), 3, "Mathématiques 6e"),
            new(new DateOnly(2026, 3, 9), 4, null)
        };

        return new HourRecordSheetDto(
            EmployeeContractId: Guid.NewGuid(),
            SheetNumber: "FH-1A2B3C4D-202603",
            EmployeeFullName: "Fatou Ndiaye",
            Month: 3,
            Year: 2026,
            HourlyRate: 5_000,
            Lines: resolvedLines,
            TotalHours: resolvedLines.Sum(l => l.Hours),
            SchoolName: "École Primaire Les Baobabs",
            SchoolAddress: "Rue 12, Médina, Dakar",
            SchoolCity: "Dakar",
            SchoolPhone: "+221338001122",
            SchoolNinea: "123456789",
            SchoolLogoUrl: "https://exemple.sn/logo.png");
    }

    private static void ShouldBeAValidPdf(byte[] pdf)
    {
        pdf.Should().NotBeNullOrEmpty();
        Encoding.ASCII.GetString(pdf, 0, PdfMagic.Length).Should().Be("%PDF-", "l'en-tête magique d'un PDF");
    }

    [Fact]
    public void Generate_Produces_A_Valid_Non_Trivial_Pdf()
    {
        var pdf = new HourRecordSheetPdfGenerator().Generate(Sheet(), logo: null);

        ShouldBeAValidPdf(pdf);
        pdf.Length.Should().BeGreaterThan(500, "une fiche complète n'est pas un fichier vide");
    }

    [Fact]
    public void Generate_Is_Robust_When_No_Hours_Are_Recorded_This_Month()
    {
        var pdf = new HourRecordSheetPdfGenerator().Generate(Sheet(lines: new List<HourRecordLineDto>()), logo: null);

        ShouldBeAValidPdf(pdf);
    }

    [Fact]
    public void Generate_Falls_Back_To_A_Logoless_Sheet_When_Logo_Bytes_Are_Unreadable()
    {
        var unreadable = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        var act = () => new HourRecordSheetPdfGenerator().Generate(Sheet(), logo: unreadable);

        act.Should().NotThrow();
    }
}
````


#### `tests/SamaEcole.UnitTests/Finance/InstallmentScheduleCalculatorTests.cs`

````csharp
using FluentAssertions;
using SamaEcole.Application.Finance.Common;
using SamaEcole.Domain.Entities;
using Xunit;

namespace SamaEcole.UnitTests.Finance;

/// <summary>
/// Étape 5 — même algorithme d'allocation en cascade que GetStudentBalanceQuery/GetDuesNoticeQuery
/// utilisaient avant leur extraction ici : ces tests verrouillent la non-régression du comportement
/// dérivé (aucun échéancier personnalisé) ET couvrent le nouveau chemin (plan personnalisé).
/// </summary>
public class InstallmentScheduleCalculatorTests
{
    private static readonly DateOnly SchoolYearStart = new(2026, 10, 1);

    private static EnrollmentFeeLine RecurringLine(decimal unitAmount, int months) => new()
    {
        SchoolId = Guid.NewGuid(),
        EnrollmentId = Guid.NewGuid(),
        FeeCategoryId = Guid.NewGuid(),
        Designation = "Mensualité",
        IsRecurring = true,
        UnitAmount = unitAmount,
        Months = months,
        LineTotal = unitAmount * months
    };

    private static EnrollmentFeeLine OneOffLine(decimal amount) => new()
    {
        SchoolId = Guid.NewGuid(),
        EnrollmentId = Guid.NewGuid(),
        FeeCategoryId = Guid.NewGuid(),
        Designation = "Inscription",
        IsRecurring = false,
        UnitAmount = amount,
        Months = 1,
        LineTotal = amount
    };

    [Fact]
    public void Derived_Schedule_Marks_Fully_Paid_Installments_As_Paid()
    {
        var lines = new List<EnrollmentFeeLine> { RecurringLine(10_000m, 3) };

        var result = InstallmentScheduleCalculator.Calculate(
            totalAmountPaid: 30_000m, today: SchoolYearStart, lines, SchoolYearStart, customInstallments: null);

        result.Should().HaveCount(3);
        result.Should().OnlyContain(i => i.Status == InstallmentScheduleCalculator.InstallmentState.Paid);
    }

    [Fact]
    public void Derived_Schedule_Allocates_Payment_To_Oldest_Installment_First()
    {
        var lines = new List<EnrollmentFeeLine> { RecurringLine(10_000m, 3) };

        // Un seul versement de 15 000 : la 1ère mensualité (mois 1) doit être Paid, la 2e Partial,
        // la 3e Pending (échue plus tard) — jamais une répartition égale entre les trois.
        var result = InstallmentScheduleCalculator.Calculate(
            totalAmountPaid: 15_000m, today: SchoolYearStart, lines, SchoolYearStart, customInstallments: null);

        result[0].Status.Should().Be(InstallmentScheduleCalculator.InstallmentState.Paid);
        result[0].RemainingDue.Should().Be(0m);
        result[1].Status.Should().Be(InstallmentScheduleCalculator.InstallmentState.Partial);
        result[1].RemainingDue.Should().Be(5_000m);
        result[2].Status.Should().Be(InstallmentScheduleCalculator.InstallmentState.Pending);
        result[2].RemainingDue.Should().Be(10_000m);
    }

    [Fact]
    public void Derived_Schedule_Marks_Unpaid_Past_Due_Installment_As_Overdue()
    {
        var lines = new List<EnrollmentFeeLine> { RecurringLine(10_000m, 2) };
        var farInTheFuture = SchoolYearStart.AddMonths(6);

        var result = InstallmentScheduleCalculator.Calculate(
            totalAmountPaid: 0m, today: farInTheFuture, lines, SchoolYearStart, customInstallments: null);

        result.Should().OnlyContain(i => i.Status == InstallmentScheduleCalculator.InstallmentState.Overdue);
    }

    [Fact]
    public void One_Off_Line_Produces_A_Single_Installment_Never_Multiplied_By_Months()
    {
        var lines = new List<EnrollmentFeeLine> { OneOffLine(5_000m) };

        var result = InstallmentScheduleCalculator.Calculate(
            totalAmountPaid: 0m, today: SchoolYearStart, lines, SchoolYearStart, customInstallments: null);

        result.Should().ContainSingle();
        result[0].Amount.Should().Be(5_000m);
        result[0].DueDate.Should().Be(SchoolYearStart);
    }

    [Fact]
    public void Custom_Plan_Takes_Priority_Over_Derived_Lines_When_Present()
    {
        // Des lignes dérivées existent (comme sur toute inscription), mais un plan personnalisé est
        // actif : lui seul doit produire les échéances retournées.
        var lines = new List<EnrollmentFeeLine> { RecurringLine(10_000m, 9) };
        var planId = Guid.NewGuid();
        var customInstallments = new List<FeeInstallment>
        {
            new() { SchoolId = Guid.NewGuid(), FeeInstallmentPlanId = planId, SequenceNo = 1, Label = "Versement 1", Amount = 40_000m, DueDate = SchoolYearStart },
            new() { SchoolId = Guid.NewGuid(), FeeInstallmentPlanId = planId, SequenceNo = 2, Label = "Versement 2", Amount = 50_000m, DueDate = SchoolYearStart.AddMonths(1) }
        };

        var result = InstallmentScheduleCalculator.Calculate(
            totalAmountPaid: 40_000m, today: SchoolYearStart, lines, SchoolYearStart, customInstallments);

        result.Should().HaveCount(2);
        result[0].Label.Should().Be("Versement 1");
        result[0].Status.Should().Be(InstallmentScheduleCalculator.InstallmentState.Paid);
        result[1].Label.Should().Be("Versement 2");
        result[1].RemainingDue.Should().Be(50_000m);
    }

    [Fact]
    public void Derived_Schedule_Marks_OneOff_Lines_And_Only_The_First_Month_As_InitialScope()
    {
        // Engagement initial = frais ponctuels EN ENTIER + le PREMIER mois de scolarité seulement.
        var lines = new List<EnrollmentFeeLine> { OneOffLine(35_000m), RecurringLine(22_000m, 9) };

        var result = InstallmentScheduleCalculator.Calculate(
            totalAmountPaid: 0m, today: SchoolYearStart, lines, SchoolYearStart, customInstallments: null);

        result.Should().HaveCount(10);
        result.Where(i => i.IsInitialScope).Select(i => i.Label)
            .Should().BeEquivalentTo(["Inscription", "Mensualité (Mois 1)"]);
        // Les mois 2..9 n'en font pas partie : on n'encaisse pas d'avance une mensualité non échue.
        result.Where(i => !i.IsInitialScope).Should().OnlyContain(i => i.Label.StartsWith("Mensualité (Mois "));
    }

    [Fact]
    public void DueNow_Is_Only_The_Initial_Scope_Before_Any_Month_Is_Reached()
    {
        var lines = new List<EnrollmentFeeLine> { OneOffLine(35_000m), RecurringLine(22_000m, 9) };

        var result = InstallmentScheduleCalculator.Calculate(
            totalAmountPaid: 0m, today: SchoolYearStart.AddDays(-10), lines, SchoolYearStart, customInstallments: null);

        result.Where(i => i.IsDueNow).Select(i => i.Label)
            .Should().BeEquivalentTo(["Inscription", "Mensualité (Mois 1)"]);
    }

    [Fact]
    public void DueNow_Includes_Every_Month_Whose_Due_Date_Is_Reached_But_Never_Future_Months()
    {
        // 15/12/2026 : échéances des 01/10 (mois 1), 01/11 (mois 2) et 01/12 (mois 3) atteintes ; 01/01 non.
        var lines = new List<EnrollmentFeeLine> { OneOffLine(35_000m), RecurringLine(22_000m, 9) };

        var result = InstallmentScheduleCalculator.Calculate(
            totalAmountPaid: 0m, today: new DateOnly(2026, 12, 15), lines, SchoolYearStart, customInstallments: null);

        result.Where(i => i.IsDueNow).Select(i => i.Label).Should().BeEquivalentTo(
            ["Inscription", "Mensualité (Mois 1)", "Mensualité (Mois 2)", "Mensualité (Mois 3)"]);
        result.Where(i => i.IsDueNow).Sum(i => i.RemainingDue).Should().Be(35_000m + 3 * 22_000m);
    }

    [Fact]
    public void DueNow_Reports_The_Overdue_Months_Once_The_Initial_Commitment_Is_Paid()
    {
        // Le cas signalé en production : inscription + 1er mois réglés (57 000), mais les mois 2 et 3,
        // échus, ne l'étaient pas — la caisse affichait « frais du jour réglés ».
        var lines = new List<EnrollmentFeeLine> { OneOffLine(35_000m), RecurringLine(22_000m, 9) };

        var result = InstallmentScheduleCalculator.Calculate(
            totalAmountPaid: 57_000m, today: new DateOnly(2026, 12, 15), lines, SchoolYearStart, customInstallments: null);

        result.Where(i => i.IsDueNow && i.RemainingDue > 0).Select(i => i.Label)
            .Should().BeEquivalentTo(["Mensualité (Mois 2)", "Mensualité (Mois 3)"]);
        result.Where(i => i.IsDueNow).Sum(i => i.RemainingDue).Should().Be(2 * 22_000m);
    }

    [Fact]
    public void InitialScope_RemainingDue_Sum_Is_OneOff_Plus_A_Single_Month()
    {
        var lines = new List<EnrollmentFeeLine>
        {
            OneOffLine(35_000m),                 // inscription
            RecurringLine(22_000m, 9)            // scolarité : 9 × 22 000
        };

        var result = InstallmentScheduleCalculator.Calculate(
            totalAmountPaid: 0m, today: SchoolYearStart, lines, SchoolYearStart, customInstallments: null);

        result.Where(i => i.IsInitialScope).Sum(i => i.RemainingDue).Should().Be(57_000m); // 35 000 + 22 000
    }

    [Fact]
    public void Derived_Schedule_Carries_The_Fee_Category_Of_Its_Source_Line()
    {
        var oneOff = OneOffLine(35_000m);
        var recurring = RecurringLine(22_000m, 3);

        var result = InstallmentScheduleCalculator.Calculate(
            totalAmountPaid: 0m, today: SchoolYearStart, [oneOff, recurring], SchoolYearStart, customInstallments: null);

        result.Single(i => i.Label == "Inscription").FeeCategoryId.Should().Be(oneOff.FeeCategoryId);
        result.Where(i => i.Label.StartsWith("Mensualité"))
            .Should().OnlyContain(i => i.FeeCategoryId == recurring.FeeCategoryId);
    }

    [Fact]
    public void Custom_Plan_Marks_Only_First_Installment_As_InitialScope_With_No_Fee_Category()
    {
        var planId = Guid.NewGuid();
        var customInstallments = new List<FeeInstallment>
        {
            new() { SchoolId = Guid.NewGuid(), FeeInstallmentPlanId = planId, SequenceNo = 1, Label = "Versement 1", Amount = 40_000m, DueDate = SchoolYearStart },
            new() { SchoolId = Guid.NewGuid(), FeeInstallmentPlanId = planId, SequenceNo = 2, Label = "Versement 2", Amount = 50_000m, DueDate = SchoolYearStart.AddMonths(1) }
        };

        var result = InstallmentScheduleCalculator.Calculate(
            totalAmountPaid: 0m, today: SchoolYearStart, [], SchoolYearStart, customInstallments);

        result.Where(i => i.IsInitialScope).Select(i => i.Label).Should().BeEquivalentTo(["Versement 1"]);
        result.Should().OnlyContain(i => i.FeeCategoryId == null);
    }

    [Fact]
    public void Custom_Plan_Installments_Are_Allocated_In_SequenceNo_Order_Regardless_Of_Input_Order()
    {
        var planId = Guid.NewGuid();
        // Volontairement fourni hors ordre : SequenceNo doit primer, pas l'ordre de la liste.
        var customInstallments = new List<FeeInstallment>
        {
            new() { SchoolId = Guid.NewGuid(), FeeInstallmentPlanId = planId, SequenceNo = 2, Label = "Second", Amount = 30_000m, DueDate = SchoolYearStart.AddMonths(1) },
            new() { SchoolId = Guid.NewGuid(), FeeInstallmentPlanId = planId, SequenceNo = 1, Label = "First", Amount = 20_000m, DueDate = SchoolYearStart }
        };

        var result = InstallmentScheduleCalculator.Calculate(
            totalAmountPaid: 20_000m, today: SchoolYearStart, [], SchoolYearStart, customInstallments);

        result[0].Label.Should().Be("First");
        result[0].Status.Should().Be(InstallmentScheduleCalculator.InstallmentState.Paid);
        result[1].Label.Should().Be("Second");
        result[1].Status.Should().Be(InstallmentScheduleCalculator.InstallmentState.Pending);
    }
}
````


#### `tests/SamaEcole.UnitTests/Finance/OpenCashierSessionCommandValidatorTests.cs`

````csharp
using FluentAssertions;
using SamaEcole.Application.Finance.Commands.OpenCashierSession;
using Xunit;

namespace SamaEcole.UnitTests.Finance;

public class OpenCashierSessionCommandValidatorTests
{
    private readonly OpenCashierSessionCommandValidator _validator = new();

    [Fact]
    public void A_Zero_Or_Positive_Opening_Balance_Is_Accepted()
    {
        _validator.Validate(new OpenCashierSessionCommand(0m)).IsValid.Should().BeTrue();
        _validator.Validate(new OpenCashierSessionCommand(50_000m)).IsValid.Should().BeTrue();
    }

    [Fact]
    public void A_Negative_Opening_Balance_Is_Rejected()
    {
        var result = _validator.Validate(new OpenCashierSessionCommand(-1m));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(OpenCashierSessionCommand.OpeningBalance));
    }
}
````


#### `tests/SamaEcole.UnitTests/Finance/OptionalFeeCategoryTests.cs`

````csharp
using FluentAssertions;
using SamaEcole.Application.Finance.Commands.CreateFeeCategory;
using SamaEcole.Application.Finance.Commands.UpdateFeeCategory;
using SamaEcole.Domain.Entities;
using Xunit;

namespace SamaEcole.UnitTests.Finance;

/// <summary>
/// Frais optionnels, Tâche 1 — le drapeau <see cref="FeeCategory.IsOptional"/> : un frais que la famille
/// choisit à l'inscription (uniforme, tenue de sport) plutôt que d'un frais dû par tous.
///
/// Le défaut OBLIGATOIRE est la protection des frais essentiels (inscription, mensualité) : la liste des
/// catégories est libre et aucun nom n'est codé en dur, donc seule une catégorie explicitement marquée
/// optionnelle par le Directeur peut un jour être décochée à l'inscription. Une catégorie existante, ou
/// créée par un ancien client qui ignore le drapeau, reste due par tous.
/// </summary>
public class OptionalFeeCategoryTests
{
    private readonly CreateFeeCategoryCommandValidator _createValidator = new();
    private readonly UpdateFeeCategoryCommandValidator _updateValidator = new();

    [Fact]
    public void A_Fee_Category_Is_Mandatory_By_Default()
    {
        var category = new FeeCategory { Name = "Inscription" };

        category.IsOptional.Should().BeFalse();
    }

    [Fact]
    public void Create_Command_Is_Mandatory_By_Default()
    {
        // Un ancien client qui n'envoie pas le champ ne doit jamais créer un frais décochable.
        var command = new CreateFeeCategoryCommand { Name = "Mensualité", IsRecurring = true };

        command.IsOptional.Should().BeFalse();
    }

    [Fact]
    public void An_Optional_One_Off_Category_Should_Pass()
    {
        var command = new CreateFeeCategoryCommand { Name = "Tenue de sport", IsOptional = true };

        _createValidator.Validate(command).IsValid.Should().BeTrue();
    }

    [Fact]
    public void An_Optional_Recurring_Category_Should_Pass()
    {
        // Une cantine ou un transport mensuels sont à la fois récurrents et facultatifs.
        var command = new CreateFeeCategoryCommand { Name = "Cantine", IsRecurring = true, IsOptional = true };

        _createValidator.Validate(command).IsValid.Should().BeTrue();
    }

    [Fact]
    public void An_Optional_Boarding_Category_Should_Fail()
    {
        // La pension a déjà son propre choix explicite (IncludeBoardingFee + régime d'hébergement). Lui
        // ajouter une seconde case créerait deux mécanismes concurrents pour la même décision.
        var command = new CreateFeeCategoryCommand { Name = "Pension", IsRecurring = true, IsBoardingFee = true, IsOptional = true };

        var result = _createValidator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateFeeCategoryCommand.IsOptional));
    }

    [Fact]
    public void Update_With_A_Category_Id_Should_Pass()
    {
        var command = new UpdateFeeCategoryCommand(Guid.NewGuid(), IsOptional: true);

        _updateValidator.Validate(command).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Update_With_An_Empty_Category_Id_Should_Fail()
    {
        var command = new UpdateFeeCategoryCommand(Guid.Empty, IsOptional: true);

        _updateValidator.Validate(command).IsValid.Should().BeFalse();
    }
}
````


#### `tests/SamaEcole.UnitTests/Finance/PaymentCancellationPolicyTests.cs`

````csharp
using FluentAssertions;
using SamaEcole.Application.Finance;
using Xunit;

namespace SamaEcole.UnitTests.Finance;

public class PaymentCancellationPolicyTests
{
    [Fact]
    public void The_Grace_Period_Is_48_Hours()
    {
        PaymentCancellationPolicy.GracePeriod.Should().Be(TimeSpan.FromHours(48));
    }

    [Fact]
    public void The_Deadline_Is_48_Hours_After_The_Payment()
    {
        var paidAt = new DateTimeOffset(2026, 10, 12, 9, 30, 0, TimeSpan.Zero); // lundi 09:30

        PaymentCancellationPolicy.DeadlineFor(paidAt)
            .Should().Be(new DateTimeOffset(2026, 10, 14, 9, 30, 0, TimeSpan.Zero)); // mercredi 09:30
    }
}
````


#### `tests/SamaEcole.UnitTests/Finance/PaymentReceiptLineLabelTests.cs`

````csharp
using FluentAssertions;
using SamaEcole.Application.Finance;
using Xunit;

namespace SamaEcole.UnitTests.Finance;

/// <summary>
/// Refonte des pièces de caisse (2026-08) — la colonne « Période / Note » du reçu ventilé.
///
/// La règle tient en une cascade : le libellé propre à la ligne, à défaut la période du versement
/// entier, à défaut RIEN. Le dernier maillon est le plus important : une cellule vide est honnête, un
/// tiret ou une période devinée ne l'est pas. Même convention que les coordonnées d'établissement
/// absentes de l'en-tête (docs/design-references/README.md §1.1).
///
/// Ces tests portent sur la logique d'Application, sans instancier QuestPDF (AGENTS.md règle #8) :
/// le générateur PDF ne fait que mettre en page ce que le DTO a déjà résolu.
/// </summary>
public class PaymentReceiptLineLabelTests
{
    private static PaymentReceiptDto Receipt(
        IReadOnlyList<PaymentReceiptLineDto> lines,
        string? referencePeriod = null,
        decimal amount = 30_000m) => new(
        ReceiptNumber: "REC-2026-0007",
        SchoolName: "École Primaire Les Baobabs",
        SchoolAddress: null,
        SchoolEmail: null,
        SchoolPhone: null,
        SchoolCity: null,
        SchoolNinea: null,
        SchoolRegistreCommerce: null,
        SchoolLogoUrl: null,
        Matricule: "ELEV-2026-0008",
        StudentFullName: "Awa Fall",
        ClassroomName: "CE1",
        SchoolYearLabel: "2026-2027",
        Method: "Cash",
        Amount: amount,
        TotalDue: 160_000m,
        AlreadyPaid: amount,
        RemainingBalance: 160_000m - amount,
        PaidAt: new DateTimeOffset(2026, 9, 5, 9, 0, 0, TimeSpan.Zero),
        Lines: lines,
        ReferencePeriod: referencePeriod);

    [Fact]
    public void A_Line_Label_Wins_Over_The_Payment_Period()
    {
        var line = new PaymentReceiptLineDto("Tenue scolaire", "2 jeux", 5_000m);
        var receipt = Receipt([line], referencePeriod: "Septembre 2026");

        receipt.ResolveLineLabel(line).Should().Be("2 jeux");
    }

    [Fact]
    public void A_Line_Without_Label_Falls_Back_To_The_Payment_Period()
    {
        var line = new PaymentReceiptLineDto("Mensualité scolarité", null, 15_000m);
        var receipt = Receipt([line], referencePeriod: "Septembre 2026");

        receipt.ResolveLineLabel(line).Should().Be("Septembre 2026");
    }

    /// <summary>
    /// Le cas qui compte : sans libellé NI période, la cellule reste vide. Jamais « — », jamais le mois
    /// courant deviné — un reçu est une pièce comptable, il n'invente pas la période qu'il atteste.
    /// </summary>
    [Fact]
    public void A_Line_With_Nothing_To_Show_Resolves_To_Null()
    {
        var line = new PaymentReceiptLineDto("Cantine", null, 5_000m);
        var receipt = Receipt([line], referencePeriod: null);

        receipt.ResolveLineLabel(line).Should().BeNull();
    }

    /// <summary>
    /// Une saisie blanche vaut une absence de saisie, des deux côtés de la cascade — sans quoi un
    /// espace tapé au guichet suffirait à supprimer le repli et à vider la colonne sans raison.
    /// </summary>
    [Theory]
    [InlineData("", "Septembre 2026", "Septembre 2026")]
    [InlineData("   ", "Septembre 2026", "Septembre 2026")]
    [InlineData(null, "   ", null)]
    [InlineData("  ", "", null)]
    public void Blank_Is_Treated_As_Absent_On_Both_Levels(string? label, string? period, string? expected)
    {
        var line = new PaymentReceiptLineDto("Transport", label, 6_000m);
        var receipt = Receipt([line], referencePeriod: period);

        receipt.ResolveLineLabel(line).Should().Be(expected);
    }

    [Fact]
    public void A_Breakdown_Summing_To_The_Amount_Is_Balanced()
    {
        var receipt = Receipt(
            [
                new("Frais d'inscription", "Unique", 10_000m),
                new("Mensualité scolarité", null, 15_000m),
                new("Tenue scolaire", "2 jeux", 5_000m)
            ],
            amount: 30_000m);

        receipt.LinesTotal.Should().Be(30_000m);
        receipt.HasBalancedLines.Should().BeTrue();
    }

    /// <summary>
    /// Ventilation partielle : la caisse n'a imputé qu'une partie du versement. Le reçu ne doit PAS
    /// imprimer ce détail — un tableau dont le total contredit la somme encaissée est un faux.
    /// </summary>
    [Fact]
    public void A_Breakdown_That_Does_Not_Sum_To_The_Amount_Is_Rejected()
    {
        var receipt = Receipt([new("Cantine", null, 5_000m)], amount: 30_000m);

        receipt.HasBalancedLines.Should().BeFalse();
    }

    /// <summary>Aucune ventilation saisie : comportement historique du reçu, ligne unique.</summary>
    [Fact]
    public void An_Empty_Breakdown_Is_Not_Balanced()
    {
        var receipt = Receipt([], amount: 30_000m);

        receipt.LinesTotal.Should().Be(0m);
        receipt.HasBalancedLines.Should().BeFalse();
    }
}
````


#### `tests/SamaEcole.UnitTests/Finance/PaymentReceiptPdfGeneratorTests.cs`

````csharp
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using SamaEcole.Application.Finance;
using SamaEcole.Infrastructure.Documents;
using Xunit;

namespace SamaEcole.UnitTests.Finance;

/// <summary>
/// Ticket JGK-F02 — le moteur PDF (QuestPDF) produit-il un reçu de paiement valide, sans exception, pour
/// toutes les formes de reçu ? La fidélité visuelle à la référence se vérifie à l'œil ; ces tests couvrent
/// la NON-RÉGRESSION : en-tête PDF correct, robustesse au logo présent puis illisible, et aux coordonnées
/// d'établissement absentes.
/// </summary>
public class PaymentReceiptPdfGeneratorTests
{
    private static readonly byte[] PdfMagic = "%PDF-"u8.ToArray();

    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private static PaymentReceiptDto Receipt(
        string? phone = "+221 77 123 45 67",
        string? city = "Dakar",
        bool isAcceleratedClass = false,
        string classroomName = "CE1",
        IReadOnlyList<PaymentReceiptLineDto>? lines = null,
        string? referencePeriod = null,
        decimal amount = 30_000m) => new(
        ReceiptNumber: "REC-2025-0007",
        SchoolName: "École Primaire Les Baobabs",
        SchoolAddress: "123 Rue de l'École",
        SchoolEmail: "contact@baobabs.sn",
        SchoolPhone: phone,
        SchoolCity: city,
        SchoolNinea: "123456789",
        SchoolRegistreCommerce: "SN-DKR-2025-B-1234",
        SchoolLogoUrl: "https://exemple.sn/logo.png",
        Matricule: "ELEV-2025-0008",
        StudentFullName: "Awa Fall",
        ClassroomName: classroomName,
        SchoolYearLabel: "2025-2026",
        Method: "MobileMoney",
        Amount: amount,
        TotalDue: 160_000m,
        AlreadyPaid: amount,
        RemainingBalance: 160_000m - amount,
        PaidAt: new DateTimeOffset(2026, 10, 5, 9, 0, 0, TimeSpan.Zero),
        Lines: lines ?? [],
        ReferencePeriod: referencePeriod,
        IsAcceleratedClass: isAcceleratedClass);

    /// <summary>Ventilation qui BALANCE : la somme des lignes fait exactement les 30 000 encaissés.</summary>
    private static IReadOnlyList<PaymentReceiptLineDto> BalancedLines() =>
    [
        new("Frais d'inscription", "Unique", 10_000m),
        new("Mensualité scolarité", null, 15_000m),   // repli sur ReferencePeriod
        new("Tenue scolaire", "2 jeux", 5_000m)
    ];

    private static void ShouldBeAValidPdf(byte[] pdf)
    {
        pdf.Should().NotBeNullOrEmpty();
        Encoding.ASCII.GetString(pdf, 0, PdfMagic.Length).Should().Be("%PDF-", "l'en-tête magique d'un PDF");
    }

    [Fact]
    public void Generate_Produces_A_Valid_Non_Trivial_Pdf()
    {
        var pdf = new PaymentReceiptPdfGenerator(Mock.Of<ILogger<PaymentReceiptPdfGenerator>>()).Generate(Receipt(), logo: null);

        ShouldBeAValidPdf(pdf);
        pdf.Length.Should().BeGreaterThan(1000, "un reçu complet n'est pas un fichier vide");
    }

    [Fact]
    public void Generate_Is_Robust_To_Missing_Phone_And_City()
    {
        var pdf = new PaymentReceiptPdfGenerator(Mock.Of<ILogger<PaymentReceiptPdfGenerator>>()).Generate(Receipt(phone: null, city: null), logo: null);

        ShouldBeAValidPdf(pdf);
    }

    /// <summary>
    /// Reçu de caisse d'un élève de classe passerelle : même mention que sur le reçu d'inscription qu'il
    /// complète. Couverture de NON-RÉGRESSION du rendu ; la composition du libellé elle-même est couverte
    /// par ClassroomPromotionTests (aucun extracteur de texte PDF n'est référencé dans ce projet).
    /// </summary>
    [Fact]
    public void Generate_Is_Robust_To_An_Accelerated_Bridge_Class()
    {
        var pdf = new PaymentReceiptPdfGenerator(Mock.Of<ILogger<PaymentReceiptPdfGenerator>>())
            .Generate(Receipt(isAcceleratedClass: true, classroomName: "CI-CP"), logo: null);

        ShouldBeAValidPdf(pdf);
        pdf.Length.Should().BeGreaterThan(1000);
    }

    /// <summary>
    /// Reçu VENTILÉ (refonte 2026-08) : une ligne par poste imputé, dont une sans libellé propre qui
    /// doit retomber sur la période du versement. Couverture de non-régression du rendu ; la résolution
    /// du libellé elle-même est couverte par PaymentReceiptLineLabelTests.
    /// </summary>
    [Fact]
    public void Generate_Renders_A_Ventilated_Receipt()
    {
        var pdf = new PaymentReceiptPdfGenerator(Mock.Of<ILogger<PaymentReceiptPdfGenerator>>())
            .Generate(Receipt(lines: BalancedLines(), referencePeriod: "Septembre 2026"), logo: null);

        ShouldBeAValidPdf(pdf);
        pdf.Length.Should().BeGreaterThan(1000);
    }

    /// <summary>
    /// Ventilation qui NE BALANCE PAS : le document doit se replier sur sa ligne unique plutôt que
    /// d'imprimer un détail dont le total contredit le montant encaissé. Un reçu qui ne balance pas
    /// est comptablement invalide — mieux vaut moins de détail qu'un faux.
    /// </summary>
    [Fact]
    public void Generate_Is_Robust_To_An_Unbalanced_Breakdown()
    {
        IReadOnlyList<PaymentReceiptLineDto> unbalanced = [new("Cantine", null, 1_000m)];

        var pdf = new PaymentReceiptPdfGenerator(Mock.Of<ILogger<PaymentReceiptPdfGenerator>>())
            .Generate(Receipt(lines: unbalanced), logo: null);

        ShouldBeAValidPdf(pdf);
    }

    /// <summary>
    /// Depuis la ventilation, le tableau du reçu a une longueur VARIABLE — il lui faut donc la même
    /// garde que l'attestation (voir ReceiptPdfGeneratorTests) : un versement imputé sur 8 postes doit
    /// rester sur une seule page A5 paysage. Le nombre de pages se lit dans l'objet racine
    /// `/Type /Pages /Count N`, aucune bibliothèque d'extraction PDF n'étant référencée ici.
    /// </summary>
    [Fact]
    public void Generate_Stays_On_A_Single_Page_With_Many_Ventilated_Lines()
    {
        var many = Enumerable.Range(1, 8)
            .Select(i => new PaymentReceiptLineDto($"Poste réglé n° {i}", i % 2 == 0 ? null : $"Note {i}", 1_000m * i))
            .ToList();

        // 1+2+…+8 = 36 milliers : le total doit correspondre, sinon le document se replie sur sa
        // ligne unique et le test ne prouverait plus rien sur la forme ventilée.
        var pdf = new PaymentReceiptPdfGenerator(Mock.Of<ILogger<PaymentReceiptPdfGenerator>>())
            .Generate(Receipt(amount: 36_000m, lines: many, referencePeriod: "Septembre 2026"), logo: null);

        ShouldBeAValidPdf(pdf);
        Encoding.ASCII.GetString(pdf).Should().Contain("/Count 1",
            "un versement imputé sur beaucoup de postes doit rester sur une seule page A5 paysage");
    }

    [Fact]
    public void Generate_Embeds_A_Provided_Logo_Without_Error()
    {
        var pdf = new PaymentReceiptPdfGenerator(Mock.Of<ILogger<PaymentReceiptPdfGenerator>>()).Generate(Receipt(), logo: TinyPng);

        ShouldBeAValidPdf(pdf);
    }

    [Fact]
    public void Generate_Falls_Back_To_A_Logoless_Receipt_When_Logo_Bytes_Are_Unreadable()
    {
        // Le filet de sécurité : des octets pathologiques ne doivent jamais empêcher l'émission du reçu.
        var unreadable = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        var act = () => new PaymentReceiptPdfGenerator(Mock.Of<ILogger<PaymentReceiptPdfGenerator>>()).Generate(Receipt(), logo: unreadable);

        act.Should().NotThrow();
        ShouldBeAValidPdf(new PaymentReceiptPdfGenerator(Mock.Of<ILogger<PaymentReceiptPdfGenerator>>()).Generate(Receipt(), logo: unreadable));
    }
}
````


#### `tests/SamaEcole.UnitTests/Finance/PayrollCalculatorTests.cs`

````csharp
using FluentAssertions;
using SamaEcole.Application.Finance.Services;
using Xunit;

namespace SamaEcole.UnitTests.Finance;

/// <summary>
/// Module Comptabilité & Fiscalité (JGK) — le calcul des cotisations sociales sénégalaises (IPRES, CSS,
/// VRS/CFCE, BRS) est le cœur du bulletin de paie : une erreur ici se répercute sur ce que l'école doit
/// à ses caisses sociales ET sur ce qu'elle verse à l'employé. AGENTS.md interdit de livrer du code
/// Finance sans test associé — ces cas couvrent le calcul horaire vs mensuel, le plafonnement IPRES/CSS,
/// et la base imposable du BRS.
/// </summary>
public class PayrollCalculatorTests
{
    [Fact]
    public void Permanent_Contract_Uses_Base_Salary_Not_Hours()
    {
        // Un contrat Permanent (HourlyRate = 0) doit ignorer HoursWorked et retenir BaseSalary.
        var fiche = PayrollCalculator.CalculateFichePaie(
            schoolId: Guid.NewGuid(), contractId: Guid.NewGuid(), month: 3, year: 2026,
            baseSalary: 300_000m, hourlyRate: 0m, hoursWorked: 999m, transportAllowance: 0m);

        fiche.GrossSalary.Should().Be(300_000m, "un contrat Permanent se base sur le salaire fixe, pas sur des heures");
    }

    [Fact]
    public void Vacataire_Contract_Uses_Hours_Times_Rate_Not_Base_Salary()
    {
        // Un contrat Vacataire (HourlyRate > 0) doit ignorer BaseSalary et calculer Heures × Taux.
        var fiche = PayrollCalculator.CalculateFichePaie(
            schoolId: Guid.NewGuid(), contractId: Guid.NewGuid(), month: 3, year: 2026,
            baseSalary: 999_999m, hourlyRate: 5_000m, hoursWorked: 40m, transportAllowance: 0m);

        fiche.GrossSalary.Should().Be(200_000m, "40h × 5 000 FCFA, BaseSalary ne doit pas entrer en jeu");
    }

    [Fact]
    public void Ipres_Contribution_Is_Capped_At_The_Monthly_Ceiling()
    {
        // Brut très supérieur au plafond IPRES (360 000) : la base de calcul doit être plafonnée, pas le brut réel.
        var fiche = PayrollCalculator.CalculateFichePaie(
            schoolId: Guid.NewGuid(), contractId: Guid.NewGuid(), month: 3, year: 2026,
            baseSalary: 1_000_000m, hourlyRate: 0m, hoursWorked: 0m, transportAllowance: 0m);

        fiche.IpresEmployee.Should().Be(360_000m * PayrollCalculator.IpresEmployeeRate,
            "la base IPRES est plafonnée à 360 000 FCFA, jamais le brut réel au-delà");
        fiche.IpresEmployer.Should().Be(360_000m * PayrollCalculator.IpresEmployerRate);
    }

    [Fact]
    public void Css_Contribution_Is_Capped_At_Its_Own_Lower_Ceiling()
    {
        // CSS a un plafond distinct (63 000), plus bas que celui d'IPRES — les deux bases ne doivent
        // jamais être confondues.
        var fiche = PayrollCalculator.CalculateFichePaie(
            schoolId: Guid.NewGuid(), contractId: Guid.NewGuid(), month: 3, year: 2026,
            baseSalary: 200_000m, hourlyRate: 0m, hoursWorked: 0m, transportAllowance: 0m);

        fiche.CssEmployer.Should().Be(63_000m * PayrollCalculator.CssEmployerRate,
            "200 000 dépasse le plafond CSS (63 000) : la base doit être plafonnée");
    }

    [Fact]
    public void Below_Ceiling_Salary_Is_Not_Capped()
    {
        // Sous les deux plafonds : la base de calcul est le brut réel, sans troncature.
        var fiche = PayrollCalculator.CalculateFichePaie(
            schoolId: Guid.NewGuid(), contractId: Guid.NewGuid(), month: 3, year: 2026,
            baseSalary: 50_000m, hourlyRate: 0m, hoursWorked: 0m, transportAllowance: 0m);

        fiche.IpresEmployee.Should().Be(50_000m * PayrollCalculator.IpresEmployeeRate);
        fiche.CssEmployer.Should().Be(50_000m * PayrollCalculator.CssEmployerRate);
    }

    [Fact]
    public void Brs_Is_Computed_On_Gross_Minus_Employee_Ipres_Not_On_Gross_Alone()
    {
        var fiche = PayrollCalculator.CalculateFichePaie(
            schoolId: Guid.NewGuid(), contractId: Guid.NewGuid(), month: 3, year: 2026,
            baseSalary: 100_000m, hourlyRate: 0m, hoursWorked: 0m, transportAllowance: 0m);

        var expectedTaxableBase = 100_000m - fiche.IpresEmployee;
        fiche.Brs.Should().Be(expectedTaxableBase * PayrollCalculator.BrsRate,
            "le BRS se calcule sur le brut APRÈS déduction de l'IPRES salarié, pas sur le brut seul");
    }

    [Fact]
    public void Net_Salary_Adds_Transport_And_Subtracts_Only_Employee_Side_Deductions()
    {
        // Le net à payer n'intègre QUE les retenues salariales (IPRES employé, BRS) — jamais les parts
        // patronales (IPRES employeur, CSS, VRS), qui restent à la charge de l'établissement.
        var fiche = PayrollCalculator.CalculateFichePaie(
            schoolId: Guid.NewGuid(), contractId: Guid.NewGuid(), month: 3, year: 2026,
            baseSalary: 100_000m, hourlyRate: 0m, hoursWorked: 0m, transportAllowance: 15_000m);

        var expectedNet = fiche.GrossSalary + 15_000m - fiche.IpresEmployee - fiche.Brs;
        fiche.NetSalary.Should().Be(expectedNet);
        fiche.NetSalary.Should().BeLessThan(fiche.GrossSalary + 15_000m, "des retenues salariales existent forcément sur un salaire positif");
    }

    [Fact]
    public void Zero_Gross_Salary_Never_Produces_A_Negative_Brs()
    {
        // Contrat Vacataire sans heures saisies (0h) : brut nul, aucune cotisation ne doit devenir négative.
        var fiche = PayrollCalculator.CalculateFichePaie(
            schoolId: Guid.NewGuid(), contractId: Guid.NewGuid(), month: 3, year: 2026,
            baseSalary: 0m, hourlyRate: 5_000m, hoursWorked: 0m, transportAllowance: 0m);

        fiche.GrossSalary.Should().Be(0m);
        fiche.Brs.Should().Be(0m, "une base imposable nulle ou négative ne doit jamais produire une retenue négative");
        fiche.NetSalary.Should().Be(0m);
    }
}
````


#### `tests/SamaEcole.UnitTests/Finance/PayrollWithoutDeductionsCalculatorTests.cs`

````csharp
using FluentAssertions;
using SamaEcole.Application.Finance.Services;
using Xunit;

namespace SamaEcole.UnitTests.Finance;

/// <summary>
/// Paie « sans retenues » (structure qui ne cotise pas, ex. un Daara) : la personne reçoit exactement son salaire brut plus
/// sa prime de transport, sans IPRES, CSS, VRS/CFCE ni BRS. Le calcul historique (avec retenues) reste le comportement par
/// défaut et ne doit pas bouger : les deux régimes coexistent dans une même école. Finance : test obligatoire (AGENTS.md).
/// </summary>
public class PayrollWithoutDeductionsCalculatorTests
{
    private static Domain.Entities.FichePaie Calculate(
        decimal baseSalary, decimal hourlyRate = 0m, decimal hours = 0m, decimal transport = 0m, bool? applyDeductions = null)
        => applyDeductions is { } apply
            ? PayrollCalculator.CalculateFichePaie(
                Guid.NewGuid(), Guid.NewGuid(), 3, 2026, baseSalary, hourlyRate, hours, transport, apply)
            : PayrollCalculator.CalculateFichePaie(
                Guid.NewGuid(), Guid.NewGuid(), 3, 2026, baseSalary, hourlyRate, hours, transport);

    [Fact]
    public void Without_Deductions_The_Net_Is_Exactly_The_Gross_Salary()
    {
        // Le cas de l'Oustaz payé 40 000 FCFA : il reçoit 40 000 FCFA, rien n'est retenu.
        var fiche = Calculate(baseSalary: 40_000m, applyDeductions: false);

        fiche.GrossSalary.Should().Be(40_000m);
        fiche.NetSalary.Should().Be(40_000m);
        fiche.DeductionsApplied.Should().BeFalse();
    }

    [Fact]
    public void Without_Deductions_Every_Contribution_And_Withholding_Is_Zero()
    {
        var fiche = Calculate(baseSalary: 300_000m, applyDeductions: false);

        fiche.IpresEmployee.Should().Be(0m);
        fiche.IpresEmployer.Should().Be(0m, "aucune charge patronale non plus : la structure ne cotise pas");
        fiche.CssEmployer.Should().Be(0m);
        fiche.Vrs.Should().Be(0m);
        fiche.Brs.Should().Be(0m);
    }

    [Fact]
    public void Without_Deductions_The_Transport_Allowance_Is_Still_Added_To_The_Net()
    {
        var fiche = Calculate(baseSalary: 40_000m, transport: 5_000m, applyDeductions: false);

        fiche.NetSalary.Should().Be(45_000m);
    }

    [Fact]
    public void Without_Deductions_An_Hourly_Contract_Is_Paid_Hours_Times_Rate_Untouched()
    {
        var fiche = Calculate(baseSalary: 0m, hourlyRate: 2_500m, hours: 16m, applyDeductions: false);

        fiche.GrossSalary.Should().Be(40_000m);
        fiche.NetSalary.Should().Be(40_000m);
    }

    [Fact]
    public void Without_Deductions_A_Salary_Above_The_Ipres_Ceiling_Is_Still_Paid_In_Full()
    {
        // Au-dessus du plafond IPRES (360 000), le régime avec retenues plafonne la base ; sans retenues, rien à plafonner.
        var fiche = Calculate(baseSalary: 500_000m, applyDeductions: false);

        fiche.NetSalary.Should().Be(500_000m);
    }

    [Fact]
    public void Without_Deductions_The_Fiche_Keeps_Its_Identity_Fields()
    {
        var school = Guid.NewGuid();
        var contract = Guid.NewGuid();

        var fiche = PayrollCalculator.CalculateFichePaie(school, contract, 11, 2026, 40_000m, 0m, 0m, 0m, applyDeductions: false);

        fiche.SchoolId.Should().Be(school);
        fiche.EmployeeContractId.Should().Be(contract);
        fiche.Month.Should().Be(11);
        fiche.Year.Should().Be(2026);
        fiche.Id.Should().NotBeEmpty();
    }

    [Fact]
    public void By_Default_The_Standard_Deductions_Are_Still_Applied()
    {
        // Non-régression : sans argument, le calcul historique est inchangé (300 000 : IPRES 5,6 % = 16 800, BRS 5 % de 283 200 = 14 160).
        var fiche = Calculate(baseSalary: 300_000m);

        fiche.DeductionsApplied.Should().BeTrue();
        fiche.IpresEmployee.Should().Be(16_800m);
        fiche.Brs.Should().Be(14_160m);
        fiche.NetSalary.Should().Be(300_000m - 16_800m - 14_160m);
    }

    [Fact]
    public void Asking_Explicitly_For_Deductions_Gives_The_Same_Result_As_The_Default()
    {
        var byDefault = Calculate(baseSalary: 250_000m, transport: 10_000m);
        var explicitTrue = Calculate(baseSalary: 250_000m, transport: 10_000m, applyDeductions: true);

        explicitTrue.NetSalary.Should().Be(byDefault.NetSalary);
        explicitTrue.IpresEmployee.Should().Be(byDefault.IpresEmployee);
        explicitTrue.Brs.Should().Be(byDefault.Brs);
        explicitTrue.Vrs.Should().Be(byDefault.Vrs);
    }
}
````


#### `tests/SamaEcole.UnitTests/Finance/PayslipPdfGeneratorTests.cs`

````csharp
using System.Text;
using FluentAssertions;
using SamaEcole.Application.Finance.Queries.GetPayslip;
using SamaEcole.Infrastructure.Documents;
using Xunit;

namespace SamaEcole.UnitTests.Finance;

/// <summary>
/// Le moteur PDF (QuestPDF) du Bulletin de Paie produit-il un document valide, sans lever d'exception,
/// y compris aux cas limites (contrat Vacataire sans heures, NINEA absent, logo présent ou illisible) ?
/// </summary>
public class PayslipPdfGeneratorTests
{
    private static readonly byte[] PdfMagic = "%PDF-"u8.ToArray();

    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private static PayslipDto Payslip(
        string contractType = "Permanent",
        decimal hoursWorked = 0m,
        string? ninea = "SN-DKR-2020-B-1234",
        string? city = "Dakar") => new(
        FichePaieId: Guid.NewGuid(),
        PayslipNumber: "BP-1A2B3C4D",
        EmployeeFullName: "Fatou Ndiaye",
        EmployeeRole: "Enseignant",
        ContractType: contractType,
        Month: 3,
        Year: 2026,
        HoursWorked: hoursWorked,
        GrossSalary: 300_000m,
        TransportAllowance: 15_000m,
        IpresEmployee: 16_800m,
        IpresEmployer: 25_200m,
        CssEmployer: 4_410m,
        Vrs: 9_000m,
        Brs: 14_160m,
        NetSalary: 284_040m,
        SchoolName: "École Primaire Les Baobabs",
        SchoolAddress: city is null ? null : $"Rue 12, Médina, {city}",
        SchoolCity: city,
        SchoolNinea: ninea,
        SchoolLogoUrl: "https://exemple.sn/logo.png");

    private static void ShouldBeAValidPdf(byte[] pdf)
    {
        pdf.Should().NotBeNullOrEmpty();
        Encoding.ASCII.GetString(pdf, 0, PdfMagic.Length).Should().Be("%PDF-", "l'en-tête magique d'un PDF");
    }

    [Fact]
    public void Generate_Produces_A_Valid_Non_Trivial_Pdf()
    {
        var pdf = new PayslipPdfGenerator().Generate(Payslip(), logo: null);

        ShouldBeAValidPdf(pdf);
        pdf.Length.Should().BeGreaterThan(500, "un bulletin complet n'est pas un fichier vide");
    }

    [Fact]
    public void Generate_Produces_A_Valid_Pdf_For_A_Payslip_Without_Deductions()
    {
        // Paie « sans retenues » (ex. Daara) : aucune ligne de cotisation, une mention explicite à la place. Le net égale le brut.
        var payslip = Payslip() with
        {
            GrossSalary = 40_000m, TransportAllowance = 0m,
            IpresEmployee = 0m, IpresEmployer = 0m, CssEmployer = 0m, Vrs = 0m, Brs = 0m,
            NetSalary = 40_000m, DeductionsApplied = false
        };

        var pdf = new PayslipPdfGenerator().Generate(payslip, logo: null);

        ShouldBeAValidPdf(pdf);
        pdf.Length.Should().BeGreaterThan(500);
    }

    [Fact]
    public void Generate_Is_Robust_To_A_Vacataire_Contract_Without_Hours()
    {
        var pdf = new PayslipPdfGenerator().Generate(Payslip(contractType: "Vacataire", hoursWorked: 0m), logo: null);

        ShouldBeAValidPdf(pdf);
    }

    [Fact]
    public void Generate_Is_Robust_To_Missing_Ninea_And_City()
    {
        var pdf = new PayslipPdfGenerator().Generate(Payslip(ninea: null, city: null), logo: null);

        ShouldBeAValidPdf(pdf);
    }

    [Fact]
    public void Generate_Embeds_A_Provided_Logo_Without_Error()
    {
        var pdf = new PayslipPdfGenerator().Generate(Payslip(), logo: TinyPng);

        ShouldBeAValidPdf(pdf);
    }

    [Fact]
    public void Generate_Falls_Back_To_A_Logoless_Payslip_When_Logo_Bytes_Are_Unreadable()
    {
        var unreadable = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        var act = () => new PayslipPdfGenerator().Generate(Payslip(), logo: unreadable);

        act.Should().NotThrow();
        ShouldBeAValidPdf(new PayslipPdfGenerator().Generate(Payslip(), logo: unreadable));
    }
}
````


#### `tests/SamaEcole.UnitTests/Finance/RecordPaymentCommandValidatorTests.cs`

````csharp
using FluentAssertions;
using SamaEcole.Application.Finance.Commands.RecordPayment;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.UnitTests.Finance;

/// <summary>
/// Ticket JGK-F02 — validation de FORME du versement (le contrôle de solde vit dans le Handler, il
/// dépend de l'état en base). On verrouille : montant strictement positif, inscription renseignée,
/// moyen de paiement valide.
/// </summary>
public class RecordPaymentCommandValidatorTests
{
    private readonly RecordPaymentCommandValidator _validator = new();

    private static RecordPaymentCommand Valid() =>
        new(Guid.NewGuid(), 15_000m, PaymentMethod.Cash);

    [Fact]
    public void A_Well_Formed_Payment_Passes()
    {
        _validator.Validate(Valid()).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-15000)]
    public void A_Non_Positive_Amount_Is_Rejected(int amount)
    {
        var result = _validator.Validate(Valid() with { Amount = amount });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(RecordPaymentCommand.Amount));
    }

    [Fact]
    public void An_Empty_Enrollment_Is_Rejected()
    {
        var result = _validator.Validate(Valid() with { EnrollmentId = Guid.Empty });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(RecordPaymentCommand.EnrollmentId));
    }

    [Fact]
    public void An_Unknown_Payment_Method_Is_Rejected()
    {
        var result = _validator.Validate(Valid() with { Method = (PaymentMethod)999 });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(RecordPaymentCommand.Method));
    }

    [Theory]
    [InlineData(PaymentMethod.Cash)]
    [InlineData(PaymentMethod.Cheque)]
    [InlineData(PaymentMethod.Transfer)]
    [InlineData(PaymentMethod.MobileMoney)]
    public void Every_Supported_Method_Is_Accepted(PaymentMethod method)
    {
        _validator.Validate(Valid() with { Method = method }).IsValid.Should().BeTrue();
    }

    [Fact]
    public void A_Null_Vat_Rate_Is_Accepted_As_Not_Subject_To_Vat()
    {
        _validator.Validate(Valid() with { VatRate = null }).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.18)]
    [InlineData(1)]
    public void A_Vat_Rate_Within_Zero_And_One_Is_Accepted(decimal vatRate)
    {
        _validator.Validate(Valid() with { VatRate = vatRate }).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    [InlineData(18)] // Piège plausible : saisir "18" en pensant "18 %" au lieu de la fraction 0.18.
    public void A_Vat_Rate_Outside_Zero_And_One_Is_Rejected(decimal vatRate)
    {
        var result = _validator.Validate(Valid() with { VatRate = vatRate });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(RecordPaymentCommand.VatRate));
    }
}
````


#### `tests/SamaEcole.UnitTests/Finance/TaxDeclarationPdfGeneratorTests.cs`

````csharp
using System.Text;
using FluentAssertions;
using SamaEcole.Application.Finance.Queries.GetTaxDeclaration;
using SamaEcole.Infrastructure.Documents;
using Xunit;

namespace SamaEcole.UnitTests.Finance;

/// <summary>
/// Le moteur PDF (QuestPDF) de la Déclaration Fiscale produit-il un document valide, sans lever
/// d'exception, y compris aux cas limites (NINEA absent, logo présent ou illisible) ?
/// </summary>
public class TaxDeclarationPdfGeneratorTests
{
    private static readonly byte[] PdfMagic = "%PDF-"u8.ToArray();

    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private static TaxDeclarationDto Declaration(string? ninea = "SN-DKR-2020-B-1234", string? address = "Rue 12, Médina, Dakar") => new(
        Id: Guid.NewGuid(),
        DeclarationNumber: "DECL-1A2B3C4D-202603",
        Month: 3,
        Year: 2026,
        TotalIpres: 25_200m,
        TotalCss: 4_410m,
        TotalVrs: 9_000m,
        TotalBrs: 14_160m,
        TvaCollected: 180_000m,
        TvaDeductible: 60_000m,
        NetTva: 120_000m,
        TotalDueToState: 143_160m,
        CreatedAt: DateTimeOffset.UtcNow,
        SchoolName: "École Primaire Les Baobabs",
        SchoolAddress: address,
        SchoolCity: "Dakar",
        SchoolNinea: ninea,
        SchoolLogoUrl: "https://exemple.sn/logo.png");

    private static void ShouldBeAValidPdf(byte[] pdf)
    {
        pdf.Should().NotBeNullOrEmpty();
        Encoding.ASCII.GetString(pdf, 0, PdfMagic.Length).Should().Be("%PDF-", "l'en-tête magique d'un PDF");
    }

    [Fact]
    public void Generate_Produces_A_Valid_Non_Trivial_Pdf()
    {
        var pdf = new TaxDeclarationPdfGenerator().Generate(Declaration(), logo: null);

        ShouldBeAValidPdf(pdf);
        pdf.Length.Should().BeGreaterThan(500, "une déclaration complète n'est pas un fichier vide");
    }

    [Fact]
    public void Generate_Is_Robust_To_Missing_Ninea_And_Address()
    {
        var pdf = new TaxDeclarationPdfGenerator().Generate(Declaration(ninea: null, address: null), logo: null);

        ShouldBeAValidPdf(pdf);
    }

    [Fact]
    public void Generate_Embeds_A_Provided_Logo_Without_Error()
    {
        var pdf = new TaxDeclarationPdfGenerator().Generate(Declaration(), logo: TinyPng);

        ShouldBeAValidPdf(pdf);
    }

    [Fact]
    public void Generate_Falls_Back_To_A_Logoless_Declaration_When_Logo_Bytes_Are_Unreadable()
    {
        var unreadable = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        var act = () => new TaxDeclarationPdfGenerator().Generate(Declaration(), logo: unreadable);

        act.Should().NotThrow();
        ShouldBeAValidPdf(new TaxDeclarationPdfGenerator().Generate(Declaration(), logo: unreadable));
    }
}
````


#### `tests/SamaEcole.UnitTests/Finance/UpdateEmployeeContractCommandValidatorTests.cs`

````csharp
using FluentAssertions;
using SamaEcole.Application.Finance.Commands.UpdateEmployeeContract;
using Xunit;

namespace SamaEcole.UnitTests.Finance;

/// <summary>Volume 1 §14.1 — modification d'un contrat actif : montants non négatifs, motif obligatoire et explicite.</summary>
public class UpdateEmployeeContractCommandValidatorTests
{
    private readonly UpdateEmployeeContractCommandValidator _validator = new();

    private static UpdateEmployeeContractCommand Command(
        decimal baseSalary = 250_000m, decimal hourlyRate = 0m, decimal transportAllowance = 15_000m,
        string reason = "Augmentation annuelle actée en conseil.") =>
        new(Guid.NewGuid(), baseSalary, hourlyRate, transportAllowance, reason, RowVersion: 1);

    [Fact]
    public void A_Valid_Command_Should_Be_Accepted()
    {
        _validator.Validate(Command()).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(-1)]
    public void A_Negative_BaseSalary_Should_Be_Rejected(decimal value)
    {
        _validator.Validate(Command(baseSalary: value)).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(-1)]
    public void A_Negative_HourlyRate_Should_Be_Rejected(decimal value)
    {
        _validator.Validate(Command(hourlyRate: value)).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ok")]
    public void An_Empty_Or_Vague_Reason_Should_Be_Rejected(string reason)
    {
        var result = _validator.Validate(Command(reason: reason));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(UpdateEmployeeContractCommand.Reason));
    }

    [Fact]
    public void A_Reason_Containing_Html_Should_Be_Rejected()
    {
        _validator.Validate(Command(reason: "<script>alert(1)</script> augmentation")).IsValid.Should().BeFalse();
    }

    [Fact]
    public void An_Empty_ContractId_Should_Be_Rejected()
    {
        var command = new UpdateEmployeeContractCommand(
            Guid.Empty, 250_000m, 0m, 15_000m, "Augmentation annuelle actée en conseil.", 1);

        _validator.Validate(command).IsValid.Should().BeFalse();
    }
}
````


#### `tests/SamaEcole.UnitTests/Finance/VatCalculatorTests.cs`

````csharp
using FluentAssertions;
using SamaEcole.Application.Finance.Services;
using Xunit;

namespace SamaEcole.UnitTests.Finance;

/// <summary>
/// La TVA se calcule "en dedans" : Amount (Payment/Disbursement) est un montant TTC réel — de l'argent
/// qui a changé de main —, jamais un HT auquel il faudrait ajouter la taxe par-dessus. AGENTS.md interdit
/// de livrer du code Finance sans test associé.
/// </summary>
public class VatCalculatorTests
{
    [Fact]
    public void Null_Rate_Means_Not_Subject_To_Vat_And_Returns_Zero()
    {
        VatCalculator.ComputeVatAmount(100_000m, null).Should().Be(0m,
            "la scolarité (Category par défaut) est typiquement exonérée — null, jamais un taux inventé");
    }

    [Fact]
    public void Zero_Rate_Also_Returns_Zero()
    {
        VatCalculator.ComputeVatAmount(100_000m, 0m).Should().Be(0m);
    }

    [Fact]
    public void Eighteen_Percent_Extracts_The_Correct_Vat_Portion_From_A_Ttc_Amount()
    {
        // 118 TTC à 18% -> 18 exactement (100 HT + 18 TVA = 118 TTC) : cas rond, vérifie le sens du calcul.
        VatCalculator.ComputeVatAmount(118m, 0.18m).Should().Be(18m);
    }

    [Fact]
    public void The_Vat_Amount_Is_Always_Strictly_Less_Than_The_Ttc_Total()
    {
        // Un montant TTC ne peut jamais être ENTIÈREMENT de la TVA — même à un taux élevé, la part HT
        // reste strictement positive.
        var vat = VatCalculator.ComputeVatAmount(100_000m, 0.18m);

        vat.Should().BeLessThan(100_000m);
        vat.Should().BeGreaterThan(0m);
    }

    [Fact]
    public void Result_Is_Rounded_To_Two_Decimals_Away_From_Zero()
    {
        // 100 000 * 0.18 / 1.18 = 15254.237... -> arrondi à 15254.24 (AwayFromZero, même convention que
        // ReportCardDocument.FormatGrade).
        VatCalculator.ComputeVatAmount(100_000m, 0.18m).Should().Be(15_254.24m);
    }

    [Theory]
    [InlineData(-0.01)]
    public void A_Negative_Rate_Is_Treated_As_Not_Applicable(decimal negativeRate)
    {
        // Garde défensive : la validation applicative (RuleFor InclusiveBetween(0,1)) empêche déjà un
        // taux négatif d'atteindre ce calculateur, mais celui-ci reste un filet de sécurité pur.
        VatCalculator.ComputeVatAmount(100_000m, negativeRate).Should().Be(0m);
    }

    [Fact]
    public void A_Zero_Ttc_Amount_Produces_Zero_Vat_Even_With_A_Valid_Rate()
    {
        VatCalculator.ComputeVatAmount(0m, 0.18m).Should().Be(0m);
    }
}
````


#### `tests/SamaEcole.UnitTests/Finance/WorkCertificatePdfGeneratorTests.cs`

````csharp
using System.Text;
using FluentAssertions;
using SamaEcole.Application.Finance.Queries.GetWorkCertificate;
using SamaEcole.Infrastructure.Documents;
using Xunit;

namespace SamaEcole.UnitTests.Finance;

/// <summary>Non-régression du moteur PDF (QuestPDF) de l'attestation de travail.</summary>
public class WorkCertificatePdfGeneratorTests
{
    private static readonly byte[] PdfMagic = "%PDF-"u8.ToArray();

    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private static WorkCertificateDto Certificate(
        string? city = "Dakar",
        string contractType = "Permanent") => new(
        ContractId: Guid.NewGuid(),
        CertificateNumber: "AT-1A2B3C4D",
        EmployeeFullName: "Fatou Ndiaye",
        EmployeeRole: "Enseignant",
        ContractType: contractType,
        SinceDate: new DateTimeOffset(2023, 9, 1, 8, 0, 0, TimeSpan.Zero),
        IssuedAt: new DateTimeOffset(2026, 1, 10, 9, 0, 0, TimeSpan.Zero),
        SchoolName: "École Primaire Les Baobabs",
        SchoolAddress: city is null ? null : $"Rue 12, Médina, {city}",
        SchoolCity: city,
        SchoolPhone: "+221338001122",
        SchoolNinea: "123456789",
        SchoolLogoUrl: "https://exemple.sn/logo.png");

    private static void ShouldBeAValidPdf(byte[] pdf)
    {
        pdf.Should().NotBeNullOrEmpty();
        Encoding.ASCII.GetString(pdf, 0, PdfMagic.Length).Should().Be("%PDF-", "l'en-tête magique d'un PDF");
    }

    [Fact]
    public void Generate_Produces_A_Valid_Non_Trivial_Pdf()
    {
        var pdf = new WorkCertificatePdfGenerator().Generate(Certificate(), logo: null, qrCodeImage: TinyPng);

        ShouldBeAValidPdf(pdf);
        pdf.Length.Should().BeGreaterThan(500, "une attestation complète n'est pas un fichier vide");
    }

    [Fact]
    public void Generate_Is_Robust_To_Missing_City()
    {
        var pdf = new WorkCertificatePdfGenerator().Generate(Certificate(city: null), logo: null, qrCodeImage: TinyPng);

        ShouldBeAValidPdf(pdf);
    }

    [Fact]
    public void Generate_Handles_Vacataire_Contract_Type()
    {
        var pdf = new WorkCertificatePdfGenerator().Generate(Certificate(contractType: "Vacataire"), logo: null, qrCodeImage: TinyPng);

        ShouldBeAValidPdf(pdf);
    }

    [Fact]
    public void Generate_Falls_Back_To_A_Logoless_Certificate_When_Logo_Bytes_Are_Unreadable()
    {
        var unreadable = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        var act = () => new WorkCertificatePdfGenerator().Generate(Certificate(), logo: unreadable, qrCodeImage: TinyPng);

        act.Should().NotThrow();
    }
}
````


#### `tests/SamaEcole.UnitTests/Enrollments/OptionalFeeSelectionTests.cs`

````csharp
using FluentAssertions;
using SamaEcole.Application.Enrollments;
using SamaEcole.Application.Enrollments.Commands.CreateEnrollment;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.UnitTests.Enrollments;

/// <summary>
/// Frais optionnels, Tâche 2 — la règle qui décide quels frais entrent dans le dû annuel d'une inscription.
///
/// Trois invariants portés ici, parce qu'ils protègent la facture :
///   1. un frais OBLIGATOIRE est toujours facturé, quoi que le client envoie (une requête forgée ne peut
///      pas décocher l'inscription ou la mensualité) ;
///   2. un frais optionnel n'est facturé que s'il est choisi — liste vide = aucun ;
///   3. l'absence de liste (client antérieur à la fonctionnalité) garde le comportement historique :
///      tous les frais de la classe sont facturés, la facture d'un ancien client ne change pas.
/// </summary>
public class OptionalFeeSelectionTests
{
    private static readonly Guid Uniforme = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid TenueSport = Guid.Parse("00000000-0000-0000-0000-0000000000a2");
    private static readonly Guid Mensualite = Guid.Parse("00000000-0000-0000-0000-0000000000b1");

    // ------------------------------------------------------------------ IsBilled

    [Fact]
    public void A_Mandatory_Fee_Is_Billed_Without_A_Selection()
    {
        OptionalFeeSelection.IsBilled(isOptional: false, Mensualite, null).Should().BeTrue();
    }

    [Fact]
    public void A_Mandatory_Fee_Is_Billed_With_An_Empty_Selection()
    {
        OptionalFeeSelection.IsBilled(isOptional: false, Mensualite, []).Should().BeTrue();
    }

    [Fact]
    public void A_Mandatory_Fee_Cannot_Be_Unchecked_By_A_Forged_List()
    {
        // Un client malveillant n'envoie que la tenue de sport : la mensualité reste due.
        OptionalFeeSelection.IsBilled(isOptional: false, Mensualite, [TenueSport]).Should().BeTrue();
    }

    [Fact]
    public void An_Optional_Fee_Is_Billed_When_Chosen()
    {
        OptionalFeeSelection.IsBilled(isOptional: true, Uniforme, [Uniforme]).Should().BeTrue();
    }

    [Fact]
    public void An_Optional_Fee_Is_Not_Billed_When_Not_Chosen()
    {
        OptionalFeeSelection.IsBilled(isOptional: true, Uniforme, [TenueSport]).Should().BeFalse();
    }

    [Fact]
    public void An_Empty_Selection_Bills_No_Optional_Fee()
    {
        OptionalFeeSelection.IsBilled(isOptional: true, Uniforme, []).Should().BeFalse();
    }

    [Fact]
    public void Without_A_Selection_An_Optional_Fee_Keeps_The_Historical_Behaviour_And_Is_Billed()
    {
        OptionalFeeSelection.IsBilled(isOptional: true, Uniforme, null).Should().BeTrue();
    }

    // ------------------------------------------------------------------ FindInvalid

    [Fact]
    public void Without_A_Selection_Nothing_Is_Invalid()
    {
        OptionalFeeSelection.FindInvalid(null, new HashSet<Guid> { Uniforme }).Should().BeEmpty();
    }

    [Fact]
    public void Choosing_Optional_Fees_Of_The_Class_Is_Valid()
    {
        OptionalFeeSelection.FindInvalid([Uniforme, TenueSport], new HashSet<Guid> { Uniforme, TenueSport })
            .Should().BeEmpty();
    }

    [Fact]
    public void Choosing_A_Mandatory_Fee_Is_Invalid_Rather_Than_Silently_Ignored()
    {
        // La mensualité n'est pas dans les frais optionnels de la classe : la lister est une erreur de
        // saisie ou une tentative de contournement — on la signale (422), on ne l'avale pas.
        OptionalFeeSelection.FindInvalid([Mensualite], new HashSet<Guid> { Uniforme })
            .Should().Equal(Mensualite);
    }

    [Fact]
    public void Choosing_An_Unknown_Category_Is_Invalid()
    {
        var inconnue = Guid.NewGuid();

        OptionalFeeSelection.FindInvalid([Uniforme, inconnue], new HashSet<Guid> { Uniforme })
            .Should().Equal(inconnue);
    }

    // ------------------------------------------------------------------ Validator

    private readonly CreateEnrollmentCommandValidator _validator = new();

    private static CreateEnrollmentCommand ReEnrollment(IReadOnlyList<Guid>? optionalFeeCategoryIds) => new()
    {
        Type = EnrollmentType.ReEnrollment,
        ClassroomId = Guid.NewGuid(),
        StudentId = Guid.NewGuid(),
        OptionalFeeCategoryIds = optionalFeeCategoryIds
    };

    [Fact]
    public void The_Selection_Is_Absent_By_Default()
    {
        // Un ancien client n'envoie pas le champ : null, jamais une liste vide qui décocherait tout.
        new CreateEnrollmentCommand { Type = EnrollmentType.ReEnrollment, ClassroomId = Guid.NewGuid() }
            .OptionalFeeCategoryIds.Should().BeNull();
    }

    [Fact]
    public void A_Missing_Selection_Should_Pass()
    {
        _validator.Validate(ReEnrollment(null)).IsValid.Should().BeTrue();
    }

    [Fact]
    public void An_Empty_Selection_Should_Pass()
    {
        // « La famille ne prend aucun frais optionnel » est un choix légitime.
        _validator.Validate(ReEnrollment([])).IsValid.Should().BeTrue();
    }

    [Fact]
    public void A_Selection_Of_Distinct_Categories_Should_Pass()
    {
        _validator.Validate(ReEnrollment([Uniforme, TenueSport])).IsValid.Should().BeTrue();
    }

    [Fact]
    public void A_Selection_With_Duplicates_Should_Fail()
    {
        var result = _validator.Validate(ReEnrollment([Uniforme, Uniforme]));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateEnrollmentCommand.OptionalFeeCategoryIds));
    }

    [Fact]
    public void A_Selection_With_An_Empty_Guid_Should_Fail()
    {
        var result = _validator.Validate(ReEnrollment([Guid.Empty]));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateEnrollmentCommand.OptionalFeeCategoryIds));
    }
}
````


#### `tests/SamaEcole.UnitTests/Enrollments/ReceiptCityTests.cs`

````csharp
using FluentAssertions;
using SamaEcole.Application.Enrollments;
using Xunit;

namespace SamaEcole.UnitTests.Enrollments;

/// <summary>
/// Ticket JGK-E02 — la ligne « Fait à [ville] » du reçu. La ville est dérivée de l'adresse libre de
/// l'école (dernier segment, convention sénégalaise) ; sans adresse exploitable, on ne montre RIEN
/// plutôt qu'une valeur inventée, et le reçu laisse la mention à compléter à la main.
/// </summary>
public class ReceiptCityTests
{
    [Theory]
    [InlineData("Rue 12, Médina, Dakar", "Dakar")]
    [InlineData("Avenue Bourguiba, Dakar", "Dakar")]
    [InlineData("Touba", "Touba")]
    [InlineData("  Quartier Escale ,  Ziguinchor  ", "Ziguinchor")]
    public void FromAddress_Returns_The_Last_Segment_As_The_City(string address, string expected)
    {
        ReceiptCity.FromAddress(address).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Rue 12,")]
    public void FromAddress_Returns_Null_When_No_Usable_City(string? address)
    {
        ReceiptCity.FromAddress(address).Should().BeNull();
    }
}
````


#### `tests/SamaEcole.UnitTests/Enrollments/ReceiptPdfGeneratorTests.cs`

````csharp
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using SamaEcole.Application.Enrollments;
using SamaEcole.Domain.Enums;
using SamaEcole.Infrastructure.Documents;
using Xunit;

namespace SamaEcole.UnitTests.Enrollments;

/// <summary>
/// Ticket JGK-E02 — le moteur PDF (QuestPDF) produit-il un document valide, sans lever d'exception,
/// pour toutes les formes de reçu ? La fidélité visuelle à la référence se vérifie à l'œil (critère du
/// ticket) ; ces tests couvrent la NON-RÉGRESSION de la génération : en-tête PDF correct, taille non
/// triviale, et robustesse aux cas limites (aucun frais, téléphone/ville absents, logo présent ou illisible).
/// </summary>
public class ReceiptPdfGeneratorTests
{
    private static readonly byte[] PdfMagic = "%PDF-"u8.ToArray();

    // PNG 1×1 transparent, valide — de quoi exercer l'incrustation réelle du logo sans dépendre du réseau.
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private static EnrollmentReceiptDto Receipt(
        IReadOnlyList<EnrollmentFeeLineDto>? lines = null,
        string? phone = "+221 77 123 45 67",
        string? city = "Dakar",
        IReadOnlyList<CollectedFeeLineDto>? collected = null,
        string? legalMentions = "SN-DKR-2020-B-1234",
        bool isAcceleratedClass = false,
        string classroomName = "CE1",
        string receiptNumber = "REC-2025-0002",
        string status = nameof(EnrollmentStatus.Confirmed)) => new(
        EnrollmentId: Guid.NewGuid(),
        ReceiptNumber: receiptNumber,
        SchoolName: "École Primaire Les Baobabs",
        SchoolAddress: city is null ? null : $"Rue 12, Médina, {city}",
        SchoolPhone: phone,
        SchoolEmail: legalMentions is null ? null : "contact@baobabs.sn",
        SchoolNinea: legalMentions,
        SchoolRegistreCommerce: legalMentions,
        SchoolCity: city,
        SchoolLogoUrl: "https://exemple.sn/logo.png",
        Matricule: "ELEV-2025-0008",
        StudentFullName: "Awa Fall",
        ClassroomName: classroomName,
        ClassroomLevel: "Primaire",
        SchoolYearLabel: "2025-2026",
        GuardianName: legalMentions is null ? null : "Ndèye Fall",
        GuardianPhone: legalMentions is null ? null : "77 000 00 00",
        Type: nameof(EnrollmentType.NewEnrollment),
        Status: status,
        EnrolledAt: new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero),
        Lines: lines ?? new List<EnrollmentFeeLineDto>
        {
            new("Droits d'inscription administrative", IsRecurring: false, UnitAmount: 25_000m, Months: 1, LineTotal: 25_000m),
            new("Mensualité", IsRecurring: true, UnitAmount: 15_000m, Months: 9, LineTotal: 135_000m)
        },
        TotalDue: 160_000m,
        CollectedLines: collected ?? new List<CollectedFeeLineDto>
        {
            new("Droits d'inscription administrative", IsRecurring: false, Months: 1, Amount: 25_000m),
            new("Mensualité", IsRecurring: true, Months: 1, Amount: 15_000m)
        },
        TotalCollected: collected?.Sum(c => c.Amount) ?? 40_000m,
        PaymentMethod: nameof(PaymentMethod.Cash),
        IsAcceleratedClass: isAcceleratedClass);

    private static void ShouldBeAValidPdf(byte[] pdf)
    {
        pdf.Should().NotBeNullOrEmpty();
        Encoding.ASCII.GetString(pdf, 0, PdfMagic.Length).Should().Be("%PDF-", "l'en-tête magique d'un PDF");
    }

    [Fact]
    public void Generate_Produces_A_Valid_Non_Trivial_Pdf()
    {
        var pdf = new ReceiptPdfGenerator(Mock.Of<ILogger<ReceiptPdfGenerator>>()).Generate(Receipt(), logo: null);

        ShouldBeAValidPdf(pdf);
        pdf.Length.Should().BeGreaterThan(1000, "un reçu complet n'est pas un fichier vide");
    }

    [Fact]
    public void Generate_Is_Robust_To_A_Class_Without_Any_Fees()
    {
        var pdf = new ReceiptPdfGenerator(Mock.Of<ILogger<ReceiptPdfGenerator>>()).Generate(
            Receipt(lines: new List<EnrollmentFeeLineDto>()), logo: null);

        ShouldBeAValidPdf(pdf);
    }

    [Fact]
    public void Generate_Is_Robust_To_Missing_Phone_And_City()
    {
        var pdf = new ReceiptPdfGenerator(Mock.Of<ILogger<ReceiptPdfGenerator>>()).Generate(
            Receipt(phone: null, city: null), logo: null);

        ShouldBeAValidPdf(pdf);
    }

    [Fact]
    public void Generate_Is_Robust_To_Missing_Legal_Mentions_And_Guardian()
    {
        // Une école qui n'a pas encore saisi NINEA/RCCM ni le tuteur doit quand même pouvoir imprimer :
        // la mention absente ne s'imprime pas, elle ne fait pas tomber le reçu.
        var pdf = new ReceiptPdfGenerator(Mock.Of<ILogger<ReceiptPdfGenerator>>()).Generate(Receipt(legalMentions: null), logo: null);

        ShouldBeAValidPdf(pdf);
    }

    [Fact]
    public void Generate_Is_Robust_To_An_Enrollment_Without_Any_Collection()
    {
        // Dossier ouvert sans versement : le reçu s'imprime avec un total encaissé de 0.
        var pdf = new ReceiptPdfGenerator(Mock.Of<ILogger<ReceiptPdfGenerator>>()).Generate(
            Receipt(collected: new List<CollectedFeeLineDto>()), logo: null);

        ShouldBeAValidPdf(pdf);
    }

    [Fact]
    public void Generate_Embeds_A_Provided_Logo_Without_Error()
    {
        var pdf = new ReceiptPdfGenerator(Mock.Of<ILogger<ReceiptPdfGenerator>>()).Generate(Receipt(), logo: TinyPng);

        // On ne peut pas « voir » l'image en test unitaire, mais on couvre la branche d'incrustation :
        // un logo valide doit produire un PDF valide, sans exception.
        ShouldBeAValidPdf(pdf);
    }

    /// <summary>
    /// Classe PASSERELLE / ACCÉLÉRÉE : la ligne « Classe d'affectation » porte la mention du dispositif,
    /// parce que le reçu est la seule pièce que le tuteur conserve — elle doit dire que l'année réglée
    /// couvre deux niveaux.
    ///
    /// Ce que ce test couvre exactement : la NON-RÉGRESSION de la génération pour une classe passerelle
    /// (le libellé composé ne fait pas tomber le rendu, quelle que soit sa longueur). Le CONTENU de la
    /// ligne se vérifie sur ClassroomPromotion.DisplayName — voir ClassroomPromotionTests — que le
    /// document appelle : aucune bibliothèque d'extraction de texte PDF n'est référencée ici, et en
    /// ajouter une pour relire un libellé déjà couvert ne se justifie pas.
    /// </summary>
    [Fact]
    public void Generate_Is_Robust_To_An_Accelerated_Bridge_Class()
    {
        var pdf = new ReceiptPdfGenerator(Mock.Of<ILogger<ReceiptPdfGenerator>>()).Generate(
            Receipt(isAcceleratedClass: true, classroomName: "CI-CP"), logo: null);

        ShouldBeAValidPdf(pdf);
        pdf.Length.Should().BeGreaterThan(1000);
    }

    [Fact]
    public void Generate_Falls_Back_To_A_Logoless_Receipt_When_Logo_Bytes_Are_Unreadable()
    {
        // Le filet de sécurité : des octets pathologiques (ni PNG ni JPEG décodable) ne doivent JAMAIS
        // empêcher l'émission du reçu officiel — le générateur régénère alors sans le logo.
        var unreadable = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        var act = () => new ReceiptPdfGenerator(Mock.Of<ILogger<ReceiptPdfGenerator>>()).Generate(Receipt(), logo: unreadable);

        act.Should().NotThrow();
        ShouldBeAValidPdf(new ReceiptPdfGenerator(Mock.Of<ILogger<ReceiptPdfGenerator>>()).Generate(Receipt(), logo: unreadable));
    }

    /// <summary>
    /// Ticket de compactage A5 : un dossier chargé (8 lignes de frais + tuteur renseigné) ne doit JAMAIS
    /// déborder sur une deuxième page — la mise en page (padding/police) est calibrée pour ça. On ne
    /// référence aucune bibliothèque d'extraction PDF (même raison que <see cref="Generate_Is_Robust_To_An_Accelerated_Bridge_Class"/>) :
    /// le nombre de pages se lit directement dans l'objet racine `/Type /Pages /Count N` du PDF généré,
    /// une structure stable pour les documents à page unique de QuestPDF/SkiaSharp.
    /// </summary>
    [Fact]
    public void Generate_Stays_On_A_Single_Page_With_Many_Fee_Lines()
    {
        var manyLines = Enumerable.Range(1, 8)
            .Select(i => new EnrollmentFeeLineDto($"Frais {i}", IsRecurring: i % 2 == 0, UnitAmount: 1000m * i, Months: 9, LineTotal: 9000m * i))
            .ToList();

        var pdf = new ReceiptPdfGenerator(Mock.Of<ILogger<ReceiptPdfGenerator>>()).Generate(Receipt(lines: manyLines), logo: null);

        ShouldBeAValidPdf(pdf);
        Encoding.ASCII.GetString(pdf).Should().Contain("/Count 1",
            "un dossier avec beaucoup de frais doit rester sur une seule page A5 paysage, jamais déborder sur une deuxième");
    }
}
````


#### `tests/SamaEcole.UnitTests/Enrollments/CreateEnrollmentCommandValidatorTests.cs`

````csharp
using FluentAssertions;
using SamaEcole.Application.Enrollments.Commands.CreateEnrollment;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.UnitTests.Enrollments;

/// <summary>
/// Ticket JGK-E01 — les règles de validation dépendent du type de mouvement : une nouvelle
/// inscription exige l'état civil de l'élève, une réinscription exige un élève existant.
/// </summary>
public class CreateEnrollmentCommandValidatorTests
{
    private readonly CreateEnrollmentCommandValidator _validator = new();

    [Fact]
    public void A_Valid_New_Enrollment_Should_Pass()
    {
        var command = new CreateEnrollmentCommand
        {
            Type = EnrollmentType.NewEnrollment,
            ClassroomId = Guid.NewGuid(),
            FullName = "Awa Ndiaye",
            BirthDate = new DateOnly(2015, 3, 12),
            BirthPlace = "Dakar",
            Gender = "F"
        };

        _validator.Validate(command).IsValid.Should().BeTrue();
    }

    [Fact]
    public void A_New_Enrollment_Without_A_BirthPlace_Should_Fail()
    {
        // Feature E — lieu de naissance obligatoire pour une nouvelle inscription (l'élève créé ici
        // suit la même règle que CreateStudentCommand).
        var command = new CreateEnrollmentCommand
        {
            Type = EnrollmentType.NewEnrollment,
            ClassroomId = Guid.NewGuid(),
            FullName = "Awa Ndiaye",
            BirthDate = new DateOnly(2015, 3, 12),
            Gender = "F"
        };

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(command.BirthPlace));
    }

    [Fact]
    public void A_Valid_Re_Enrollment_Should_Pass()
    {
        var command = new CreateEnrollmentCommand
        {
            Type = EnrollmentType.ReEnrollment,
            ClassroomId = Guid.NewGuid(),
            StudentId = Guid.NewGuid()
        };

        _validator.Validate(command).IsValid.Should().BeTrue();
    }

    [Fact]
    public void A_New_Enrollment_Without_A_Name_Should_Fail()
    {
        var command = new CreateEnrollmentCommand
        {
            Type = EnrollmentType.NewEnrollment,
            ClassroomId = Guid.NewGuid(),
            BirthDate = new DateOnly(2015, 3, 12),
            Gender = "F"
        };

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(command.FullName));
    }

    [Theory]
    [InlineData("X")]
    [InlineData("")]
    public void A_New_Enrollment_With_An_Invalid_Gender_Should_Fail(string gender)
    {
        var command = new CreateEnrollmentCommand
        {
            Type = EnrollmentType.NewEnrollment,
            ClassroomId = Guid.NewGuid(),
            FullName = "Awa Ndiaye",
            BirthDate = new DateOnly(2015, 3, 12),
            Gender = gender
        };

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(command.Gender));
    }

    [Fact]
    public void A_New_Enrollment_With_A_Future_Birth_Date_Should_Fail()
    {
        var command = new CreateEnrollmentCommand
        {
            Type = EnrollmentType.NewEnrollment,
            ClassroomId = Guid.NewGuid(),
            FullName = "Awa Ndiaye",
            BirthDate = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)),
            Gender = "F"
        };

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(command.BirthDate));
    }

    [Fact]
    public void A_Re_Enrollment_Without_A_Student_Should_Fail()
    {
        var command = new CreateEnrollmentCommand
        {
            Type = EnrollmentType.ReEnrollment,
            ClassroomId = Guid.NewGuid()
        };

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(command.StudentId));
    }

    [Fact]
    public void An_Externe_Enrollment_With_A_RoomId_Should_Fail()
    {
        // Module Internat — symétrique de la règle "RoomId requis si non-Externe" : un RoomId sur un
        // régime Externe contournerait la garde IsInternatEnabled et fausserait le compte d'occupation
        // d'une chambre (voir CreateEnrollmentCommandValidator).
        var command = new CreateEnrollmentCommand
        {
            Type = EnrollmentType.NewEnrollment,
            ClassroomId = Guid.NewGuid(),
            FullName = "Awa Ndiaye",
            BirthDate = new DateOnly(2015, 3, 12),
            BirthPlace = "Dakar",
            Gender = "F",
            BoardingStatus = BoardingStatus.Externe,
            RoomId = Guid.NewGuid()
        };

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(command.RoomId));
    }

    [Fact]
    public void An_Interne_Enrollment_Without_A_RoomId_Should_Fail()
    {
        var command = new CreateEnrollmentCommand
        {
            Type = EnrollmentType.NewEnrollment, ClassroomId = Guid.NewGuid(), FullName = "Awa Ndiaye",
            BirthDate = new DateOnly(2015, 3, 12), BirthPlace = "Dakar", Gender = "F",
            BoardingStatus = BoardingStatus.Interne, RoomId = null
        };

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(command.RoomId));
    }

    [Fact]
    public void A_Half_Boarder_Enrollment_Does_Not_Need_A_RoomId()
    {
        // Spec Pavillon/Lit (N7) : un demi-pensionnaire ne dort pas à l'internat, donc n'a ni lit ni chambre.
        var command = new CreateEnrollmentCommand
        {
            Type = EnrollmentType.NewEnrollment, ClassroomId = Guid.NewGuid(), FullName = "Awa Ndiaye",
            BirthDate = new DateOnly(2015, 3, 12), BirthPlace = "Dakar", Gender = "F",
            BoardingStatus = BoardingStatus.DemiPensionnaire, RoomId = null
        };

        _validator.Validate(command).IsValid.Should().BeTrue();
    }

    [Fact]
    public void An_Enrollment_Without_A_Classroom_Should_Fail()
    {
        var command = new CreateEnrollmentCommand
        {
            Type = EnrollmentType.NewEnrollment,
            ClassroomId = Guid.Empty,
            FullName = "Awa Ndiaye",
            BirthDate = new DateOnly(2015, 3, 12),
            Gender = "F"
        };

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(command.ClassroomId));
    }
}
````


#### `tests/SamaEcole.UnitTests/Common/TenantCacheKeyFactoryTests.cs`

````csharp
using FluentAssertions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Infrastructure.Multitenancy;
using NSubstitute;
using Xunit;

namespace SamaEcole.UnitTests.Common;

/// <summary>
/// Audit BOLA/IDOR (recommandation 2/3 : anticiper le cache Redis). Aucun cache applicatif n'est
/// encore consommé en code, mais la garantie « toute clé de cache porte le SchoolId » (Volume_3_DDS.md
/// §2.5) doit être vraie dès la première utilisation — donc testée dès maintenant.
/// </summary>
public class TenantCacheKeyFactoryTests
{
    [Fact]
    public void BuildKey_Should_Prefix_The_Key_With_The_Current_Tenant()
    {
        var schoolId = Guid.NewGuid();
        var tenantProvider = Substitute.For<ITenantProvider>();
        tenantProvider.CurrentSchoolId.Returns(schoolId);
        var factory = new TenantCacheKeyFactory(tenantProvider);

        var key = factory.BuildKey("grading-scale");

        key.Should().Be($"school:{schoolId}:grading-scale");
    }

    [Fact]
    public void BuildKey_Should_Never_Return_The_Same_Key_For_Two_Different_Schools()
    {
        var tenantProviderA = Substitute.For<ITenantProvider>();
        tenantProviderA.CurrentSchoolId.Returns(Guid.NewGuid());
        var tenantProviderB = Substitute.For<ITenantProvider>();
        tenantProviderB.CurrentSchoolId.Returns(Guid.NewGuid());

        var keyA = new TenantCacheKeyFactory(tenantProviderA).BuildKey("grading-scale");
        var keyB = new TenantCacheKeyFactory(tenantProviderB).BuildKey("grading-scale");

        keyA.Should().NotBe(keyB, "deux écoles ne doivent jamais pouvoir se lire l'une l'autre via une clé de cache partagée");
    }

    [Fact]
    public void BuildKey_Should_Fail_Closed_When_No_Tenant_Is_Resolved()
    {
        var tenantProvider = Substitute.For<ITenantProvider>();
        tenantProvider.CurrentSchoolId.Returns((Guid?)null);
        var factory = new TenantCacheKeyFactory(tenantProvider);

        var act = () => factory.BuildKey("grading-scale");

        act.Should().Throw<UnauthorizedAccessException>("une clé de cache 'globale' accidentelle serait pire que l'absence de cache");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildKey_Should_Reject_A_Blank_Logical_Key(string? blankKey)
    {
        var tenantProvider = Substitute.For<ITenantProvider>();
        tenantProvider.CurrentSchoolId.Returns(Guid.NewGuid());
        var factory = new TenantCacheKeyFactory(tenantProvider);

        var act = () => factory.BuildKey(blankKey!);

        act.Should().Throw<ArgumentException>();
    }
}
````


## B.3 Tests d'intégration

#### `tests/SamaEcole.IntegrationTests/Finance/CancelPaymentTests.cs`

````csharp
using FluentAssertions;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Commands.CancelPayment;
using SamaEcole.Application.Finance.Commands.RecordPayment;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace SamaEcole.IntegrationTests.Finance;

/// <summary>
/// Annulation d'un paiement saisi par erreur (spec 2026-10-17-deletion-dependency-guard-design.md §2.4).
/// Prérequis de la suppression d'un élève : un paiement annulé ne bloque plus, mais il n'est JAMAIS effacé
/// (AGENTS.md règle #6) et son numéro de reçu reste consommé (règle #3, aucun trou).
/// </summary>
[Trait("Category", "MultiTenant")]
public class CancelPaymentTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("11111111-6666-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("22222222-6666-1111-1111-111111111111");
    private static readonly Guid Classe = Guid.Parse("aaaaaaaa-6666-0000-0000-00000000000a");
    private static readonly Guid Annee = Guid.Parse("bbbbbbbb-6666-0000-0000-00000000000b");
    private static readonly Guid Eleve = Guid.Parse("eeeeeeee-6666-0000-0000-00000000000e");
    private static readonly Guid Inscription = Guid.Parse("11111111-6666-0000-0000-0000000000f1");
    private static readonly Guid Caissier = Guid.Parse("dddddddd-6666-0000-0000-00000000000d");
    private static readonly Guid Directeur = Guid.Parse("dddddddd-6666-0000-0000-0000000000d2");
    private static readonly Guid SessionCaisse = Guid.Parse("cccccccc-6666-0000-0000-00000000000c");

    private const decimal TotalDue = 100_000m;

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();

        owner.Schools.Add(new School { Id = EcoleA, Name = "École A" });
        owner.Schools.Add(new School { Id = EcoleB, Name = "École B" });
        owner.Classrooms.Add(new Classroom { Id = Classe, SchoolId = EcoleA, Name = "CM2", Level = "Primaire", Capacity = 40 });
        owner.SchoolYears.Add(new SchoolYear
        {
            Id = Annee, SchoolId = EcoleA, Label = "2026-2027",
            StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true
        });
        owner.Students.Add(new Student
        {
            Id = Eleve, SchoolId = EcoleA, Matricule = "ELEV-2026-0001",
            FullName = "Awa Fall", BirthDate = new DateOnly(2015, 5, 20), BirthPlace = "Dakar", Gender = "F", ClassroomId = Classe
        });
        owner.Enrollments.Add(new Enrollment
        {
            Id = Inscription, SchoolId = EcoleA, StudentId = Eleve, SchoolYearId = Annee, ClassroomId = Classe,
            Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed,
            TotalDue = TotalDue, AmountPaid = 0m, ReceiptNumber = "REC-2026-0001",
            EnrolledAt = DateTimeOffset.UtcNow
        });
        owner.Users.Add(new User
        {
            Id = Caissier, SchoolId = EcoleA, Email = "caissier.a@ecole-a.sn", PasswordHash = "hash",
            FullName = "Caissier A", Role = Role.Finance
        });
        owner.Users.Add(new User
        {
            Id = Directeur, SchoolId = EcoleA, Email = "directeur.a@ecole-a.sn", PasswordHash = "hash",
            FullName = "Directeur A", Role = Role.Directeur
        });
        owner.CashierSessions.Add(new CashierSession
        {
            Id = SessionCaisse, SchoolId = EcoleA, CashierId = Caissier,
            Status = CashierSessionStatus.Open, OpenedAt = DateTimeOffset.UtcNow, OpeningBalance = 0m
        });

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private async Task<RecordPaymentResult> RecordAsync(decimal amount)
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new RecordPaymentCommandHandler(
            ctx, new FixedTenant(EcoleA), new FixedUser(Caissier),
            new SamaEcole.Persistence.MatriculeGenerator((SamaEcole.Persistence.ApplicationDbContext)ctx, TimeProvider.System),
            new NoSms(), TimeProvider.System, new NoKpiCache());

        return await handler.Handle(
            new RecordPaymentCommand(Inscription, amount, PaymentMethod.Cash), CancellationToken.None);
    }

    private static CancelPaymentCommandHandler NewHandler(IApplicationDbContext ctx, Guid actor, Role role = Role.Directeur) =>
        new(ctx, new FixedUser(actor, role), TimeProvider.System, new NoKpiCache());

    /// <summary>Recule la date d'encaissement : le versement sort (ou non) du délai de grâce de 48 h.</summary>
    private async Task BackdateAsync(Guid paymentId, TimeSpan age)
    {
        await using var owner = _db.NewOwnerContext();
        var payment = await owner.Payments.IgnoreQueryFilters().SingleAsync(p => p.Id == paymentId);
        payment.PaidAt = DateTimeOffset.UtcNow - age;
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    // ----------------------------------------------------------------------------- Contre-passation (hors délai de 48 h)

    [Fact]
    public async Task Past_The_Grace_Period_An_Ordinary_Cancellation_Is_Refused_And_Points_To_The_Reversal()
    {
        var payment = await RecordAsync(30_000m);
        await BackdateAsync(payment.PaymentId, TimeSpan.FromHours(72));

        await using var ctx = _db.NewAppContext(EcoleA);
        var act = async () => await NewHandler(ctx, Directeur).Handle(
            new CancelPaymentCommand(payment.PaymentId, "Erreur de saisie"), CancellationToken.None);

        var error = (await act.Should().ThrowAsync<BusinessRuleException>()).Which;
        error.Code.Should().Be("PAYMENT_CANCELLATION_WINDOW_EXPIRED");
        error.Message.Should().Contain("contre-passation");
    }

    [Fact]
    public async Task Past_The_Grace_Period_The_Director_Can_Pass_A_Reversal_Which_Keeps_The_Row_And_Gives_The_Amount_Back()
    {
        var payment = await RecordAsync(30_000m);
        await BackdateAsync(payment.PaymentId, TimeSpan.FromHours(72));

        await using (var ctx = _db.NewAppContext(EcoleA))
        {
            var result = await NewHandler(ctx, Directeur).Handle(
                new CancelPaymentCommand(payment.PaymentId, "Doublon constaté à l'audit", Reversal: true), CancellationToken.None);

            result.IsReversal.Should().BeTrue();
            result.AmountPaid.Should().Be(0m);
        }

        await using var check = _db.NewAppContext(EcoleA);
        var stored = await check.Payments.SingleAsync(p => p.Id == payment.PaymentId);
        stored.Status.Should().Be(PaymentStatus.Cancelled, "la ligne et son reçu restent, marqués annulés — jamais effacés");
        stored.CancelledAt.Should().NotBeNull("la date de l'écriture inverse est celle d'aujourd'hui");
        stored.CancelledAt!.Value.Should().BeAfter(stored.PaidAt.AddHours(48));
        stored.CancellationReason.Should().Be("Doublon constaté à l'audit");
    }

    [Fact]
    public async Task Past_The_Grace_Period_The_Finance_Role_Cannot_Pass_A_Reversal()
    {
        var payment = await RecordAsync(30_000m);
        await BackdateAsync(payment.PaymentId, TimeSpan.FromHours(72));

        await using var ctx = _db.NewAppContext(EcoleA);
        var act = async () => await NewHandler(ctx, Caissier, Role.Finance).Handle(
            new CancelPaymentCommand(payment.PaymentId, "Tentative", Reversal: true), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();

        await using var check = _db.NewAppContext(EcoleA);
        (await check.Payments.SingleAsync(p => p.Id == payment.PaymentId)).Status.Should().NotBe(PaymentStatus.Cancelled);
    }

    [Fact]
    public async Task Within_The_Grace_Period_The_Reversal_Flag_Changes_Nothing_And_Is_Not_Reported_As_A_Reversal()
    {
        var payment = await RecordAsync(30_000m);

        await using var ctx = _db.NewAppContext(EcoleA);
        var result = await NewHandler(ctx, Caissier, Role.Finance).Handle(
            new CancelPaymentCommand(payment.PaymentId, "Mauvais montant", Reversal: true), CancellationToken.None);

        result.IsReversal.Should().BeFalse("dans le délai de grâce c'est une annulation ordinaire, ouverte à la Finance");
    }

    [Fact]
    public async Task Cancelling_A_Payment_Keeps_The_Row_Marks_It_Cancelled_And_Gives_The_Amount_Back_To_The_Balance()
    {
        var payment = await RecordAsync(30_000m);

        await using var ctx = _db.NewAppContext(EcoleA);
        var result = await NewHandler(ctx, Directeur).Handle(
            new CancelPaymentCommand(payment.PaymentId, "Saisi sur le mauvais élève"), CancellationToken.None);

        result.ReceiptNumber.Should().Be(payment.ReceiptNumber, "le numéro de reçu reste consommé, aucun trou dans le carnet");
        result.AmountPaid.Should().Be(0m);
        result.RemainingBalance.Should().Be(TotalDue);

        await using var check = _db.NewAppContext(EcoleA);
        var stored = await check.Payments.AsNoTracking().SingleAsync(p => p.Id == payment.PaymentId);
        stored.Status.Should().Be(PaymentStatus.Cancelled);
        stored.CancellationReason.Should().Be("Saisi sur le mauvais élève");
        stored.CancelledByUserId.Should().Be(Directeur);
        stored.CancelledAt.Should().NotBeNull();
        stored.Amount.Should().Be(30_000m, "le montant d'origine n'est jamais réécrit");
        stored.ReceiptNumber.Should().Be(payment.ReceiptNumber);

        (await check.Enrollments.Where(e => e.Id == Inscription).Select(e => e.AmountPaid).FirstAsync())
            .Should().Be(0m);
    }

    [Fact]
    public async Task The_Destination_Of_The_Money_And_The_Cancellers_Open_Session_Are_Recorded()
    {
        var payment = await RecordAsync(30_000m);

        // Le caissier (qui a une session ouverte) annule : son annulation est comptée dans SA session.
        await using var ctx = _db.NewAppContext(EcoleA);
        await NewHandler(ctx, Caissier).Handle(
            new CancelPaymentCommand(payment.PaymentId, "Parent venu reprendre son argent", PaymentCancellationOutcome.RefundedToParent),
            CancellationToken.None);

        await using var check = _db.NewAppContext(EcoleA);
        var stored = await check.Payments.AsNoTracking().SingleAsync(p => p.Id == payment.PaymentId);
        stored.CancellationOutcome.Should().Be(PaymentCancellationOutcome.RefundedToParent);
        stored.CancellationSessionId.Should().Be(SessionCaisse);
    }

    [Fact]
    public async Task The_Default_Destination_Is_A_Simple_Correction_And_Without_An_Open_Session_None_Is_Recorded()
    {
        var payment = await RecordAsync(30_000m);

        // Le Directeur n'a pas de session de caisse ouverte : l'annulation figure dans les états par date seulement.
        await using var ctx = _db.NewAppContext(EcoleA);
        await NewHandler(ctx, Directeur).Handle(
            new CancelPaymentCommand(payment.PaymentId, "Erreur de saisie"), CancellationToken.None);

        await using var check = _db.NewAppContext(EcoleA);
        var stored = await check.Payments.AsNoTracking().SingleAsync(p => p.Id == payment.PaymentId);
        stored.CancellationOutcome.Should().Be(PaymentCancellationOutcome.KeptBySchool);
        stored.CancellationSessionId.Should().BeNull();
    }

    [Fact]
    public async Task Only_The_Cancelled_Payment_Is_Removed_From_The_Cumulative_Amount()
    {
        var first = await RecordAsync(30_000m);
        await RecordAsync(20_000m);

        await using var ctx = _db.NewAppContext(EcoleA);
        var result = await NewHandler(ctx, Directeur).Handle(
            new CancelPaymentCommand(first.PaymentId, "Doublon"), CancellationToken.None);

        result.AmountPaid.Should().Be(20_000m);
        result.RemainingBalance.Should().Be(80_000m);
    }

    [Fact]
    public async Task The_Amount_Can_Be_Collected_Again_After_A_Cancellation()
    {
        var payment = await RecordAsync(100_000m);

        await using (var ctx = _db.NewAppContext(EcoleA))
        {
            await NewHandler(ctx, Directeur).Handle(
                new CancelPaymentCommand(payment.PaymentId, "Erreur de montant"), CancellationToken.None);
        }

        var act = async () => await RecordAsync(100_000m);

        await act.Should().NotThrowAsync("l'inscription n'est plus soldée une fois le paiement annulé");
    }

    [Fact]
    public async Task Cancelling_Twice_Is_Refused()
    {
        var payment = await RecordAsync(30_000m);

        await using (var ctx = _db.NewAppContext(EcoleA))
        {
            await NewHandler(ctx, Directeur).Handle(
                new CancelPaymentCommand(payment.PaymentId, "Erreur"), CancellationToken.None);
        }

        await using var again = _db.NewAppContext(EcoleA);
        var act = async () => await NewHandler(again, Directeur).Handle(
            new CancelPaymentCommand(payment.PaymentId, "Encore"), CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleException>();

        await using var check = _db.NewAppContext(EcoleA);
        (await check.Enrollments.Where(e => e.Id == Inscription).Select(e => e.AmountPaid).FirstAsync())
            .Should().Be(0m, "un second appel ne doit pas retirer le montant une deuxième fois");
    }

    // Le contexte propriétaire n'a pas de tenant : sans IgnoreQueryFilters, le filtre global (SchoolId)
    // exclurait la ligne et la mise à jour toucherait zéro ligne sans un bruit.
    private async Task CloseCashierSessionAsync()
    {
        await using var owner = _db.NewOwnerContext();
        var updated = await owner.CashierSessions.IgnoreQueryFilters().Where(s => s.Id == SessionCaisse)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, CashierSessionStatus.Closed));
        updated.Should().Be(1, "la session de caisse doit réellement être clôturée avant l'appel");
    }

    private async Task MovePaidAtBackAsync(Guid paymentId, TimeSpan age)
    {
        await using var owner = _db.NewOwnerContext();
        var updated = await owner.Payments.IgnoreQueryFilters().Where(p => p.Id == paymentId)
            .ExecuteUpdateAsync(p => p.SetProperty(x => x.PaidAt, DateTimeOffset.UtcNow - age));
        updated.Should().Be(1);
    }

    [Fact]
    public async Task A_Payment_Of_A_Closed_Cashier_Session_Can_Still_Be_Cancelled_Within_The_Grace_Period()
    {
        var payment = await RecordAsync(30_000m);
        await CloseCashierSessionAsync();
        // Erreur remarquée le lendemain : 30 h après l'encaissement, caisse déjà clôturée.
        await MovePaidAtBackAsync(payment.PaymentId, TimeSpan.FromHours(30));

        await using var ctx = _db.NewAppContext(EcoleA);
        var result = await NewHandler(ctx, Directeur).Handle(
            new CancelPaymentCommand(payment.PaymentId, "Découvert le lendemain"), CancellationToken.None);

        result.AmountPaid.Should().Be(0m);

        await using var check = _db.NewAppContext(EcoleA);
        (await check.Payments.AsNoTracking().SingleAsync(p => p.Id == payment.PaymentId)).Status
            .Should().Be(PaymentStatus.Cancelled);
    }

    [Fact]
    public async Task A_Payment_Older_Than_The_Grace_Period_Can_No_Longer_Be_Cancelled()
    {
        var payment = await RecordAsync(30_000m);
        await MovePaidAtBackAsync(payment.PaymentId, TimeSpan.FromHours(49));

        await using var ctx = _db.NewAppContext(EcoleA);
        var act = async () => await NewHandler(ctx, Directeur).Handle(
            new CancelPaymentCommand(payment.PaymentId, "Trop tard"), CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("*délai*");

        await using var check = _db.NewAppContext(EcoleA);
        (await check.Payments.AsNoTracking().SingleAsync(p => p.Id == payment.PaymentId)).Status
            .Should().NotBe(PaymentStatus.Cancelled);
        (await check.Enrollments.Where(e => e.Id == Inscription).Select(e => e.AmountPaid).FirstAsync())
            .Should().Be(30_000m, "un refus ne doit rien retirer du solde");
    }

    [Fact]
    public async Task A_Payment_Of_Another_School_Is_Not_Found()
    {
        var payment = await RecordAsync(30_000m);

        await using var ctx = _db.NewAppContext(EcoleB);
        var act = async () => await NewHandler(ctx, Guid.NewGuid()).Handle(
            new CancelPaymentCommand(payment.PaymentId, "Tentative inter-école"), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    private sealed class FixedTenant(Guid schoolId) : ITenantProvider
    {
        public Guid? CurrentSchoolId => schoolId;
    }

    private sealed class FixedUser(Guid userId, Role role = SamaEcole.Domain.Enums.Role.Directeur) : ICurrentUserService
    {
        public Guid? UserId => userId;
        public Role? Role => role;
        public string? IpAddress => null;
    }

    private sealed class NoSms : ISmsDispatcher
    {
        public Task<SmsDispatchOutcome> DispatchAsync(SmsDispatchRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(SmsDispatchOutcome.Skipped("Désactivé dans ce test."));
    }

    private sealed class NoKpiCache : IKpiCacheService
    {
        public Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken) =>
            factory(cancellationToken);

        public void Invalidate(string key) { }
    }
}
````


#### `tests/SamaEcole.IntegrationTests/Finance/CancellationReportsTests.cs`

````csharp
using FluentAssertions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Commands.CloseCashierSession;
using SamaEcole.Application.Finance.Commands.GenerateTaxDeclaration;
using SamaEcole.Application.Finance.Queries.GetDailyClosingReportPdf;
using SamaEcole.Application.Finance.Queries.GetTreasuryDashboard;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Finance;

/// <summary>
/// Remèdes « caisse clôturée » (spec 2026-10-17, §2.4) : un versement encaissé le lundi, annulé le mardi.
///   • le journal de LUNDI garde le versement, signalé « annulé le … » — les chiffres signés ne bougent pas ;
///   • la session de MARDI porte une écriture négative ; seuls les espèces d'un REMBOURSEMENT quittent le tiroir ;
///   • la trésorerie et la TVA suivent le même registre daté.
/// </summary>
[Trait("Category", "MultiTenant")]
public class CancellationReportsTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("11111111-8888-1111-1111-111111111111");
    private static readonly Guid Classe = Guid.Parse("aaaaaaaa-8888-0000-0000-00000000000a");
    private static readonly Guid Annee = Guid.Parse("bbbbbbbb-8888-0000-0000-00000000000b");
    private static readonly Guid Eleve = Guid.Parse("eeeeeeee-8888-0000-0000-00000000000e");
    private static readonly Guid Inscription = Guid.Parse("11111111-8888-0000-0000-0000000000f1");
    private static readonly Guid Caissier = Guid.Parse("dddddddd-8888-0000-0000-00000000000d");
    private static readonly Guid SessionLundi = Guid.Parse("cccccccc-8888-0000-0000-0000000000c1");
    private static readonly Guid SessionMardi = Guid.Parse("cccccccc-8888-0000-0000-0000000000c2");

    // Octobre 2026 : lundi 12 (encaissement), mardi 13 (annulation).
    private static readonly DateTimeOffset Lundi = new(2026, 10, 12, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Mardi = Lundi.AddDays(1);

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();
        owner.Schools.Add(new School { Id = Ecole, Name = "École A" });
        owner.Classrooms.Add(new Classroom { Id = Classe, SchoolId = Ecole, Name = "CM2", Level = "Primaire", Capacity = 40 });
        owner.SchoolYears.Add(new SchoolYear
        {
            Id = Annee, SchoolId = Ecole, Label = "2026-2027",
            StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true
        });
        owner.Students.Add(new Student
        {
            Id = Eleve, SchoolId = Ecole, Matricule = "ELEV-2026-0001", FullName = "Awa Fall",
            BirthDate = new DateOnly(2015, 5, 20), BirthPlace = "Dakar", Gender = "F", ClassroomId = Classe
        });
        owner.Enrollments.Add(new Enrollment
        {
            Id = Inscription, SchoolId = Ecole, StudentId = Eleve, SchoolYearId = Annee, ClassroomId = Classe,
            Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed,
            TotalDue = 500_000m, AmountPaid = 0m, ReceiptNumber = "REC-2026-0001", EnrolledAt = Lundi
        });
        owner.Users.Add(new User
        {
            Id = Caissier, SchoolId = Ecole, Email = "c@ecole-a.sn", PasswordHash = "h", FullName = "Caissier", Role = Role.Finance
        });
        owner.CashierSessions.Add(new CashierSession
        {
            Id = SessionLundi, SchoolId = Ecole, CashierId = Caissier, Status = CashierSessionStatus.Closed,
            OpenedAt = Lundi.AddHours(8), ClosedAt = Lundi.AddHours(18), OpeningBalance = 0m
        });
        // Mardi : fonds de caisse de 100 000, session ouverte — c'est là que l'on annule.
        owner.CashierSessions.Add(new CashierSession
        {
            Id = SessionMardi, SchoolId = Ecole, CashierId = Caissier, Status = CashierSessionStatus.Open,
            OpenedAt = Mardi.AddHours(8), OpeningBalance = 100_000m
        });
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    /// <summary>Lundi : 50 000 en espèces (TVA 9 000), annulé mardi dans la session de mardi.</summary>
    private async Task SeedMondayPaymentCancelledOnTuesdayAsync(PaymentCancellationOutcome outcome)
    {
        await using var owner = _db.NewOwnerContext();
        owner.Payments.Add(new Payment
        {
            SchoolId = Ecole, EnrollmentId = Inscription, CashierSessionId = SessionLundi, Amount = 50_000m,
            VatAmount = 9_000m, Method = PaymentMethod.Cash, Status = PaymentStatus.Cancelled, BalanceAfter = 0m,
            ReceiptNumber = "REC-LUNDI", ReceivedByUserId = Caissier, PaidAt = Lundi.AddHours(10),
            CancelledAt = Mardi.AddHours(9), CancelledByUserId = Caissier, CancellationReason = "Mauvais élève",
            CancellationOutcome = outcome, CancellationSessionId = SessionMardi
        });
        // Mardi : un vrai encaissement de 20 000 en espèces.
        owner.Payments.Add(new Payment
        {
            SchoolId = Ecole, EnrollmentId = Inscription, CashierSessionId = SessionMardi, Amount = 20_000m,
            Method = PaymentMethod.Cash, Status = PaymentStatus.Partial, BalanceAfter = 0m,
            ReceiptNumber = "REC-MARDI", ReceivedByUserId = Caissier, PaidAt = Mardi.AddHours(10)
        });
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    private async Task<CloseCashierSessionResult> CloseTuesdayAsync(decimal actualCash)
    {
        await using var ctx = _db.NewAppContext(Ecole);
        var handler = new CloseCashierSessionCommandHandler(ctx, new FixedTenant(Ecole), TimeProvider.System);
        return await handler.Handle(new CloseCashierSessionCommand(SessionMardi, actualCash), CancellationToken.None);
    }

    [Fact]
    public async Task A_Simple_Correction_Lowers_The_Net_Total_But_Not_The_Expected_Cash()
    {
        await SeedMondayPaymentCancelledOnTuesdayAsync(PaymentCancellationOutcome.KeptBySchool);

        // Fonds 100 000 + encaissement de mardi 20 000 : le tiroir contient 120 000. L'annulation d'une erreur de
        // saisie de lundi n'a déplacé aucune espèce aujourd'hui.
        var result = await CloseTuesdayAsync(actualCash: 120_000m);

        result.TotalCollected.Should().Be(20_000m - 50_000m, "le total encaissé est net de l'annulation (écriture négative)");
        result.ExpectedCashAmount.Should().Be(120_000m, "aucune espèce ne quitte le tiroir pour une simple correction");
        result.DiscrepancyAmount.Should().Be(0m);
    }

    [Fact]
    public async Task A_Refund_To_The_Parent_Takes_The_Cash_Out_Of_The_Drawer()
    {
        await SeedMondayPaymentCancelledOnTuesdayAsync(PaymentCancellationOutcome.RefundedToParent);

        // 100 000 (fonds) + 20 000 (mardi) − 50 000 (remboursé au parent) = 70 000 attendus.
        var result = await CloseTuesdayAsync(actualCash: 70_000m);

        result.ExpectedCashAmount.Should().Be(70_000m);
        result.DiscrepancyAmount.Should().Be(0m);
    }

    [Fact]
    public async Task The_Monday_Closing_Report_Still_Shows_The_Payment_Flagged_As_Cancelled()
    {
        await SeedMondayPaymentCancelledOnTuesdayAsync(PaymentCancellationOutcome.KeptBySchool);

        var report = await BuildReportAsync(SessionLundi);

        report.TotalCollected.Should().Be(50_000m, "les chiffres signés lundi ne bougent pas");
        var row = report.Transactions.Should().ContainSingle().Subject;
        row.ReceiptNumber.Should().Be("REC-LUNDI");
        row.CancelledNote.Should().Contain("Annulé le 13/10/2026").And.Contain("Mauvais élève");
        report.Reversals.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task The_Tuesday_Closing_Report_Lists_The_Cancellation_As_A_Negative_Line()
    {
        await SeedMondayPaymentCancelledOnTuesdayAsync(PaymentCancellationOutcome.ReusedForEnrollment);

        var report = await BuildReportAsync(SessionMardi);

        report.TotalCollected.Should().Be(-30_000m);
        report.TotalReversed.Should().Be(50_000m);
        var reversal = report.Reversals.Should().ContainSingle().Subject;
        reversal.ReceiptNumber.Should().Be("REC-LUNDI");
        reversal.Amount.Should().Be(-50_000m);
        reversal.Note.Should().Contain("12/10/2026").And.Contain("réutilisé pour l'inscription");
        report.Transactions.Should().ContainSingle(t => t.ReceiptNumber == "REC-MARDI");
    }

    [Fact]
    public async Task The_Treasury_Of_October_Is_Net_And_Lists_The_Cancellation()
    {
        await SeedMondayPaymentCancelledOnTuesdayAsync(PaymentCancellationOutcome.KeptBySchool);

        await using var ctx = _db.NewAppContext(Ecole);
        var handler = new GetTreasuryDashboardQueryHandler(ctx);

        var october = await handler.Handle(
            new GetTreasuryDashboardQuery(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31)), CancellationToken.None);
        october.TotalCollected.Should().Be(20_000m, "50 000 encaissés puis annulés dans le même mois : il ne reste que mardi");

        // Lundi seul : le versement annulé le lendemain y reste compté, la trésorerie de lundi ne bouge pas.
        var monday = await handler.Handle(
            new GetTreasuryDashboardQuery(new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 12)), CancellationToken.None);
        monday.TotalCollected.Should().Be(50_000m);

        var tuesday = await handler.Handle(
            new GetTreasuryDashboardQuery(new DateOnly(2026, 10, 13), new DateOnly(2026, 10, 13)), CancellationToken.None);
        tuesday.TotalCollected.Should().Be(20_000m - 50_000m);
        tuesday.RecentTransactions.Should().Contain(t => t.Kind == "Annulation" && t.Amount == -50_000m);
    }

    [Fact]
    public async Task The_Vat_Collected_Takes_The_Cancellation_Back_In_The_Month_It_Is_Made()
    {
        // Annulé le mois SUIVANT : la TVA d'octobre (déjà déposée) ne change pas, novembre régularise en négatif.
        await using (var owner = _db.NewOwnerContext())
        {
            owner.Payments.Add(new Payment
            {
                SchoolId = Ecole, EnrollmentId = Inscription, CashierSessionId = SessionLundi, Amount = 50_000m,
                VatAmount = 9_000m, Method = PaymentMethod.Cash, Status = PaymentStatus.Cancelled, BalanceAfter = 0m,
                ReceiptNumber = "REC-OCT", ReceivedByUserId = Caissier, PaidAt = Lundi.AddHours(10),
                CancelledAt = new DateTimeOffset(2026, 11, 3, 9, 0, 0, TimeSpan.Zero), CancelledByUserId = Caissier,
                CancellationReason = "Erreur", CancellationOutcome = PaymentCancellationOutcome.KeptBySchool
            });
            await owner.SaveChangesAsync(CancellationToken.None);
        }

        decimal VatOf(Guid declarationId, IReadOnlyList<TaxeDeclaration> all) => all.Single(d => d.Id == declarationId).TvaCollected;

        await using var ctx = _db.NewAppContext(Ecole);
        var handler = new GenerateTaxDeclarationCommandHandler(ctx, new FixedTenant(Ecole));

        var october = await handler.Handle(new GenerateTaxDeclarationCommand(10, 2026), CancellationToken.None);
        var november = await handler.Handle(new GenerateTaxDeclarationCommand(11, 2026), CancellationToken.None);

        var declarations = ctx.TaxeDeclarations.ToList();
        VatOf(october, declarations).Should().Be(9_000m, "la déclaration d'octobre garde la TVA du versement annulé en novembre");
        VatOf(november, declarations).Should().Be(-9_000m, "novembre régularise : la TVA est reprise en négatif");
    }

    private async Task<DailyClosingReportDto> BuildReportAsync(Guid sessionId)
    {
        await using var ctx = _db.NewAppContext(Ecole);
        var generator = new CapturingGenerator();
        var handler = new GetDailyClosingReportPdfQueryHandler(ctx, new FixedTenant(Ecole), new NoLogo(), generator);

        await handler.Handle(new GetDailyClosingReportPdfQuery(sessionId), CancellationToken.None);

        return generator.Captured!;
    }

    private sealed class FixedTenant(Guid schoolId) : ITenantProvider
    {
        public Guid? CurrentSchoolId => schoolId;
    }

    private sealed class NoLogo : ISchoolLogoProvider
    {
        public Task<byte[]?> TryFetchAsync(string? logoUrl, CancellationToken cancellationToken) => Task.FromResult<byte[]?>(null);
    }

    private sealed class CapturingGenerator : IDailyClosingReportPdfGenerator
    {
        public DailyClosingReportDto? Captured { get; private set; }

        public byte[] Generate(DailyClosingReportDto report, byte[]? schoolLogo)
        {
            Captured = report;
            return [1];
        }
    }
}
````


#### `tests/SamaEcole.IntegrationTests/Finance/CashierSessionClosingTests.cs`

````csharp
using FluentAssertions;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Commands.CloseCashierSession;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Xunit;
using ValidationException = SamaEcole.Application.Common.Exceptions.ValidationException;

namespace SamaEcole.IntegrationTests.Finance;

/// <summary>
/// Ticket JGK-F09 — contrôle du comptage physique et écarts de caisse à la clôture. L'écart se
/// compare aux ESPÈCES attendues (fonds initial + encaissements EN ESPÈCES uniquement de la session),
/// jamais au total encaissé toutes méthodes confondues — un virement ou un versement mobile money ne
/// transite jamais par le tiroir-caisse physique, il ne peut donc jamais participer à un manquant.
/// </summary>
public class CashierSessionClosingTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("11111111-7777-1111-1111-111111111111");
    private static readonly Guid Classe = Guid.Parse("aaaaaaaa-7777-0000-0000-00000000000a");
    private static readonly Guid Annee = Guid.Parse("bbbbbbbb-7777-0000-0000-00000000000b");
    private static readonly Guid Eleve = Guid.Parse("eeeeeeee-7777-0000-0000-00000000000e");
    private static readonly Guid Inscription = Guid.Parse("11111111-7777-0000-0000-0000000000f1");
    private static readonly Guid Caissier = Guid.Parse("dddddddd-7777-0000-0000-00000000000d");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();

        owner.Schools.Add(new School { Id = Ecole, Name = "École A" });
        owner.Classrooms.Add(new Classroom { Id = Classe, SchoolId = Ecole, Name = "CM2", Level = "Primaire", Capacity = 40 });
        owner.SchoolYears.Add(new SchoolYear
        {
            Id = Annee, SchoolId = Ecole, Label = "2026-2027",
            StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true
        });
        owner.Students.Add(new Student
        {
            Id = Eleve, SchoolId = Ecole, Matricule = "ELEV-2026-0001",
            FullName = "Awa Fall", BirthDate = new DateOnly(2015, 5, 20), BirthPlace = "Dakar", Gender = "F", ClassroomId = Classe
        });
        owner.Enrollments.Add(new Enrollment
        {
            Id = Inscription, SchoolId = Ecole, StudentId = Eleve, SchoolYearId = Annee, ClassroomId = Classe,
            Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed,
            TotalDue = 200_000m, AmountPaid = 0m, ReceiptNumber = "REC-2026-0001",
            EnrolledAt = DateTimeOffset.UtcNow
        });
        owner.Users.Add(new User { Id = Caissier, SchoolId = Ecole, Email = "caissier.a@ecole-a.sn", PasswordHash = "hash", FullName = "Caissier A", Role = Role.Finance });

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private static CloseCashierSessionCommandHandler NewHandler(IApplicationDbContext ctx) =>
        new(ctx, new FixedTenantProvider(Ecole), TimeProvider.System);

    private async Task<Guid> SeedOpenSessionAsync(decimal openingBalance, params (decimal amount, PaymentMethod method)[] payments)
    {
        var sessionId = Guid.NewGuid();
        await using var ctx = _db.NewAppContext(Ecole);

        ctx.CashierSessions.Add(new CashierSession
        {
            Id = sessionId, SchoolId = Ecole, CashierId = Caissier,
            Status = CashierSessionStatus.Open, OpenedAt = DateTimeOffset.UtcNow, OpeningBalance = openingBalance
        });
        await ctx.SaveChangesAsync(CancellationToken.None);

        foreach (var (amount, method) in payments)
        {
            ctx.Payments.Add(new Payment
            {
                Id = Guid.NewGuid(), SchoolId = Ecole, EnrollmentId = Inscription, CashierSessionId = sessionId,
                Amount = amount, Method = method, Status = PaymentStatus.Paid,
                ReceiptNumber = $"REC-{Guid.NewGuid():N}"[..12], BalanceAfter = 0m,
                PaidAt = DateTimeOffset.UtcNow, ReceivedByUserId = Caissier
            });
        }
        await ctx.SaveChangesAsync(CancellationToken.None);

        return sessionId;
    }

    [Fact]
    public async Task Closing_With_The_Exact_Expected_Cash_Requires_No_Reason()
    {
        var sessionId = await SeedOpenSessionAsync(5_000m, (10_000m, PaymentMethod.Cash));

        await using var ctx = _db.NewAppContext(Ecole);
        var result = await NewHandler(ctx).Handle(
            new CloseCashierSessionCommand(sessionId, 15_000m), CancellationToken.None);

        result.DiscrepancyAmount.Should().Be(0);
        result.DiscrepancyReason.Should().BeNull();
        result.ExpectedCashAmount.Should().Be(15_000m);

        await using var check = _db.NewAppContext(Ecole);
        var session = await check.CashierSessions.AsNoTracking().FirstAsync(s => s.Id == sessionId);
        session.Status.Should().Be(CashierSessionStatus.Closed);
        session.ActualCashAmount.Should().Be(15_000m);
        session.DiscrepancyAmount.Should().Be(0);
        session.DiscrepancyReason.Should().BeNull();
    }

    [Fact]
    public async Task Closing_With_A_Discrepancy_And_No_Reason_Is_Rejected()
    {
        var sessionId = await SeedOpenSessionAsync(5_000m, (10_000m, PaymentMethod.Cash));

        await using var ctx = _db.NewAppContext(Ecole);
        var act = async () => await NewHandler(ctx).Handle(
            new CloseCashierSessionCommand(sessionId, 14_000m), CancellationToken.None);

        (await act.Should().ThrowAsync<ValidationException>())
            .Which.Message.Should().NotBeNullOrEmpty();

        // La session doit rester OUVERTE : un écart non justifié ne clôture jamais silencieusement.
        await using var check = _db.NewAppContext(Ecole);
        (await check.CashierSessions.AsNoTracking().FirstAsync(s => s.Id == sessionId)).Status
            .Should().Be(CashierSessionStatus.Open);
    }

    [Fact]
    public async Task Closing_With_A_Discrepancy_And_A_Reason_Records_The_Adjustment()
    {
        var sessionId = await SeedOpenSessionAsync(5_000m, (10_000m, PaymentMethod.Cash));

        await using var ctx = _db.NewAppContext(Ecole);
        var result = await NewHandler(ctx).Handle(
            new CloseCashierSessionCommand(sessionId, 14_000m, "Erreur de rendu monnaie sur un encaissement."),
            CancellationToken.None);

        result.DiscrepancyAmount.Should().Be(-1_000m, "1 000 FCFA manquent par rapport aux espèces attendues");
        result.DiscrepancyReason.Should().Be("Erreur de rendu monnaie sur un encaissement.");

        await using var check = _db.NewAppContext(Ecole);
        var session = await check.CashierSessions.AsNoTracking().FirstAsync(s => s.Id == sessionId);
        session.DiscrepancyAmount.Should().Be(-1_000m);
        session.DiscrepancyReason.Should().Be("Erreur de rendu monnaie sur un encaissement.");
    }

    [Fact]
    public async Task A_Surplus_Also_Requires_A_Reason()
    {
        var sessionId = await SeedOpenSessionAsync(5_000m, (10_000m, PaymentMethod.Cash));

        await using var ctx = _db.NewAppContext(Ecole);
        var act = async () => await NewHandler(ctx).Handle(
            new CloseCashierSessionCommand(sessionId, 16_000m), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>("un surplus est un écart comme un autre — pas seulement un manquant");
    }

    [Fact]
    public async Task Non_Cash_Payments_Never_Enter_The_Expected_Cash_Amount()
    {
        // 10 000 en espèces + 50 000 par virement : le tiroir-caisse ne doit jamais s'attendre à
        // contenir les 60 000 — seul le virement n'y a jamais transité.
        var sessionId = await SeedOpenSessionAsync(0m,
            (10_000m, PaymentMethod.Cash),
            (50_000m, PaymentMethod.Transfer));

        await using var ctx = _db.NewAppContext(Ecole);
        var result = await NewHandler(ctx).Handle(
            new CloseCashierSessionCommand(sessionId, 10_000m), CancellationToken.None);

        result.ExpectedCashAmount.Should().Be(10_000m, "le virement n'entre jamais dans les espèces attendues");
        result.TotalCollected.Should().Be(60_000m, "le total toutes méthodes, lui, reste inchangé");
        result.DiscrepancyAmount.Should().Be(0);
        result.DiscrepancyReason.Should().BeNull();
    }

    [Fact]
    public async Task An_Already_Closed_Session_Cannot_Be_Closed_Again()
    {
        var sessionId = await SeedOpenSessionAsync(1_000m);

        await using (var ctx = _db.NewAppContext(Ecole))
        {
            await NewHandler(ctx).Handle(new CloseCashierSessionCommand(sessionId, 1_000m), CancellationToken.None);
        }

        await using var ctx2 = _db.NewAppContext(Ecole);
        var act = async () => await NewHandler(ctx2).Handle(
            new CloseCashierSessionCommand(sessionId, 1_000m), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }
}

file sealed class FixedTenantProvider(Guid schoolId) : ITenantProvider
{
    public Guid? CurrentSchoolId => schoolId;
}
````


#### `tests/SamaEcole.IntegrationTests/Finance/ClassFeeConcurrencyTests.cs`

````csharp
using FluentAssertions;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Domain.Entities;
using SamaEcole.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace SamaEcole.IntegrationTests.Finance;

/// <summary>
/// Ticket JGK-F01, AGENTS.md règle #5 — le verrou optimiste (xmin) tient-il RÉELLEMENT ?
///
/// Le scénario est celui de deux directeurs qui éditent le même montant en même temps. On le
/// reproduit avec deux DbContext branchés sur le rôle applicatif, chacun ayant lu la ligne à son
/// propre instant. Le premier qui écrit gagne ; le second doit se heurter à un 409, jamais écraser
/// en silence le travail du premier.
/// </summary>
[Trait("Category", "MultiTenant")]
public class ClassFeeConcurrencyTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Classe = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid Categorie = Guid.Parse("cccccccc-0000-0000-0000-00000000000a");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();
        owner.Schools.Add(new School { Id = Ecole, Name = "École A" });
        owner.Classrooms.Add(new Classroom { Id = Classe, SchoolId = Ecole, Name = "CM2", Level = "Primaire", Capacity = 40 });
        owner.FeeCategories.Add(new FeeCategory { Id = Categorie, SchoolId = Ecole, Name = "Mensualité", IsRecurring = true });
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task Two_Concurrent_Edits_The_Second_One_Should_Be_Refused_With_A_Conflict()
    {
        Guid feeId;
        await using (var seed = _db.NewAppContext(Ecole))
        {
            var fee = new ClassFee { SchoolId = Ecole, FeeCategoryId = Categorie, ClassroomId = Classe, Amount = 15000 };
            seed.ClassFees.Add(fee);
            await seed.SaveChangesAsync(CancellationToken.None);
            feeId = fee.Id;
        }

        // Deux sessions ont chacune LU la ligne : elles détiennent le même jeton xmin.
        await using var ctxA = _db.NewAppContext(Ecole);
        await using var ctxB = _db.NewAppContext(Ecole);

        var feeA = await ctxA.ClassFees.FirstAsync(f => f.Id == feeId);
        var feeB = await ctxB.ClassFees.FirstAsync(f => f.Id == feeId);

        // A écrit en premier : xmin bascule en base.
        feeA.Amount = 12000;
        await ctxA.SaveChangesAsync(CancellationToken.None);

        // B écrit avec le jeton qu'il détenait — désormais périmé. Son UPDATE « WHERE xmin = <ancien> »
        // ne touche aucune ligne : SaveChanges refuse plutôt que d'écraser le montant fixé par A.
        feeB.Amount = 18000;
        var act = async () => await ctxB.SaveChangesAsync(CancellationToken.None);

        await act.Should().ThrowAsync<ConcurrencyConflictException>(
            "deux écritures concurrentes sur le barème ne doivent jamais s'écraser en silence (règle #5)");

        // Le montant en base est bien celui de A, intact.
        await using var check = _db.NewAppContext(Ecole);
        var finalAmount = await check.ClassFees.Where(f => f.Id == feeId).Select(f => f.Amount).FirstAsync();
        finalAmount.Should().Be(12000);
    }

    [Fact]
    public async Task A_Sequential_Edit_With_The_Fresh_Token_Should_Succeed()
    {
        // Contre-épreuve : sans conflit, l'écriture passe normalement. Sinon le test précédent
        // pourrait être vert pour une mauvaise raison (une écriture qui échoue TOUJOURS).
        Guid feeId;
        await using (var seed = _db.NewAppContext(Ecole))
        {
            var fee = new ClassFee { SchoolId = Ecole, FeeCategoryId = Categorie, ClassroomId = Classe, Amount = 15000 };
            seed.ClassFees.Add(fee);
            await seed.SaveChangesAsync(CancellationToken.None);
            feeId = fee.Id;
        }

        await using var ctx = _db.NewAppContext(Ecole);
        var fee2 = await ctx.ClassFees.FirstAsync(f => f.Id == feeId);
        fee2.Amount = 20000;

        var act = async () => await ctx.SaveChangesAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}
````


#### `tests/SamaEcole.IntegrationTests/Finance/CreateFeeCategoryBoardingFeeTests.cs`

````csharp
using FluentAssertions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Commands.CreateFeeCategory;
using SamaEcole.Domain.Entities;
using SamaEcole.IntegrationTests.Common;
using SamaEcole.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace SamaEcole.IntegrationTests.Finance;

/// <summary>
/// Module Internat, Task 9 — <see cref="FeeCategory.IsBoardingFee"/> doit pouvoir être positionné à
/// la création d'une catégorie de frais, et round-tripper réellement en base (pas seulement dans le
/// DTO retourné) : c'est ce qui distingue une pension (Task 1/2, colonne déjà migrée) d'un frais
/// scolaire ordinaire pour Task 12 (checkbox Fees) et pour le calcul de pension à l'inscription.
/// </summary>
[Trait("Category", "MultiTenant")]
public class CreateFeeCategoryBoardingFeeTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("33333333-1111-1111-1111-111111111111");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();
        owner.Schools.Add(new School { Id = EcoleA, Name = "École Internat A" });
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task Sets_IsBoardingFee_When_Requested_And_Persists_It()
    {
        await using var db = _db.NewAppContext(EcoleA);
        var handler = new CreateFeeCategoryCommandHandler(db, new StubTenantProvider(EcoleA));

        var result = await handler.Handle(
            new CreateFeeCategoryCommand { Name = "Pension", IsRecurring = true, IsBoardingFee = true },
            CancellationToken.None);

        result.IsBoardingFee.Should().BeTrue();

        await using var check = _db.NewAppContext(EcoleA);
        var persisted = await check.FeeCategories.SingleAsync(c => c.Id == result.Id);
        persisted.IsBoardingFee.Should().BeTrue("la colonne is_boarding_fee doit réellement porter la valeur, pas seulement le DTO renvoyé");
    }

    private sealed class StubTenantProvider(Guid? schoolId) : ITenantProvider
    {
        public Guid? CurrentSchoolId => schoolId;
    }
}
````


#### `tests/SamaEcole.IntegrationTests/Finance/DebtorReminderBatchIsolationTests.cs`

````csharp
using FluentAssertions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Commands.DismissDebtorReminderBatch;
using SamaEcole.Application.Finance.Commands.GenerateDebtorReminderBatches;
using SamaEcole.Application.Finance.Commands.SendDebtorReminderBatch;
using SamaEcole.Application.Finance.Queries.GetDebtorReminderBatches;
using SamaEcole.Application.Notifications;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace SamaEcole.IntegrationTests.Finance;

/// <summary>
/// Étape 5 — le job d'ancienneté (DebtorAgingHostedService) itère TOUTES les écoles sous
/// TenantProvider.RunAsSchoolAsync, un mécanisme neuf qui contourne la résolution JWT habituelle
/// (voir sa doc). Ces tests exercent GenerateDebtorReminderBatchesCommand/SendDebtorReminderBatchCommand
/// exactement comme le fait ce job — via un ITenantProvider fixé à une école, contre un vrai
/// PostgreSQL sous le rôle applicatif — pour prouver que la RLS + le Global Query Filter empêchent
/// structurellement un lot ou un débiteur de fuiter d'une école à l'autre.
/// </summary>
[Trait("Category", "MultiTenant")]
public class DebtorReminderBatchIsolationTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);
    private static readonly DateOnly SchoolYearStart = Today.AddMonths(-6);

    private Guid _enrollmentA;
    private Guid _enrollmentB;
    private Guid _studentA;
    private Guid _studentB;

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();

        owner.Schools.AddRange(
            new School { Id = EcoleA, Name = "École A" },
            new School { Id = EcoleB, Name = "École B" });

        foreach (var schoolId in new[] { EcoleA, EcoleB })
        {
            owner.Subscriptions.Add(new Subscription
            {
                SchoolId = schoolId,
                Plan = SubscriptionPlan.Premium,
                Status = SubscriptionStatus.Active,
                ExpiresAt = new DateOnly(2027, 1, 1)
            });

            owner.SchoolSettings.Add(new SchoolSettings
            {
                SchoolId = schoolId,
                SmsCreditBalance = 100,
                SmsOnDuesReminder = true,
                DebtorReminderThresholdDays = 1
            });
        }

        (_enrollmentA, _studentA) = SeedDebtor(owner, EcoleA, "Awa Débitrice", "+221770000001");
        (_enrollmentB, _studentB) = SeedDebtor(owner, EcoleB, "Baba Débiteur", "+221770000002");

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private static (Guid EnrollmentId, Guid StudentId) SeedDebtor(
        Persistence.ApplicationDbContext owner, Guid schoolId, string studentName, string guardianPhone)
    {
        var classroomId = Guid.NewGuid();
        var yearId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        var enrollmentId = Guid.NewGuid();
        var feeCategoryId = Guid.NewGuid();

        owner.FeeCategories.Add(new FeeCategory
        {
            Id = feeCategoryId, SchoolId = schoolId, Name = "Mensualité", IsRecurring = true
        });

        owner.Classrooms.Add(new Classroom
        {
            Id = classroomId, SchoolId = schoolId, Name = "CM2", Level = "Primaire", Capacity = 40
        });

        owner.SchoolYears.Add(new SchoolYear
        {
            Id = yearId, SchoolId = schoolId, Label = "2026-2027",
            StartDate = SchoolYearStart, EndDate = SchoolYearStart.AddYears(1), IsActive = true
        });

        owner.Students.Add(new Student
        {
            Id = studentId, SchoolId = schoolId, Matricule = $"ELEV-{schoolId.ToString()[..4]}",
            FullName = studentName, BirthDate = new DateOnly(2015, 1, 1), BirthPlace = "Dakar",
            Gender = "F", ClassroomId = classroomId, GuardianPhone = guardianPhone
        });

        // 6 mois de mensualité à 15 000, jamais payés : toutes les échéances passées sont en retard.
        owner.Enrollments.Add(new Enrollment
        {
            Id = enrollmentId, SchoolId = schoolId, StudentId = studentId, SchoolYearId = yearId,
            ClassroomId = classroomId, Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed,
            TotalDue = 90_000m, AmountPaid = 0m, ReceiptNumber = $"REC-{schoolId.ToString()[..4]}",
            EnrolledAt = DateTimeOffset.UtcNow
        });

        owner.EnrollmentFeeLines.Add(new EnrollmentFeeLine
        {
            SchoolId = schoolId, EnrollmentId = enrollmentId, FeeCategoryId = feeCategoryId,
            Designation = "Mensualité", IsRecurring = true, UnitAmount = 15_000m, Months = 6,
            LineTotal = 90_000m
        });

        return (enrollmentId, studentId);
    }

    private sealed class FixedTenantProvider(Guid schoolId) : ITenantProvider
    {
        public Guid? CurrentSchoolId => schoolId;
    }

    private async Task<int> GenerateBatchesAsync(Guid schoolId)
    {
        await using var context = _db.NewAppContext(schoolId);
        var handler = new GenerateDebtorReminderBatchesCommandHandler(
            context, new FixedTenantProvider(schoolId), TimeProvider.System,
            NullLogger<GenerateDebtorReminderBatchesCommandHandler>.Instance);

        return await handler.Handle(new GenerateDebtorReminderBatchesCommand(), CancellationToken.None);
    }

    [Fact]
    public async Task Generating_Batches_For_One_School_Creates_A_Draft_With_Only_That_Schools_Debtors()
    {
        var createdForA = await GenerateBatchesAsync(EcoleA);

        createdForA.Should().Be(1);

        await using var owner = _db.NewOwnerContext();

        var batches = await owner.DebtorReminderBatches.IgnoreQueryFilters().ToListAsync();
        batches.Should().ContainSingle().Which.SchoolId.Should().Be(EcoleA);

        var items = await owner.DebtorReminderBatchItems.IgnoreQueryFilters().ToListAsync();
        items.Should().ContainSingle().Which.EnrollmentId.Should().Be(_enrollmentA);
        items[0].StudentId.Should().Be(_studentA);
        items[0].SchoolId.Should().Be(EcoleA);
    }

    [Fact]
    public async Task Generating_Batches_Sequentially_For_Both_Schools_Never_Mixes_Their_Debtors()
    {
        await GenerateBatchesAsync(EcoleA);
        await GenerateBatchesAsync(EcoleB);

        await using var owner = _db.NewOwnerContext();

        var batches = await owner.DebtorReminderBatches.IgnoreQueryFilters().ToListAsync();
        batches.Should().HaveCount(2);
        batches.Select(b => b.SchoolId).Should().BeEquivalentTo([EcoleA, EcoleB]);

        var itemsByBatch = await owner.DebtorReminderBatchItems.IgnoreQueryFilters()
            .ToDictionaryAsync(i => i.DebtorReminderBatchId);

        foreach (var batch in batches)
        {
            var item = itemsByBatch[batch.Id];
            item.SchoolId.Should().Be(batch.SchoolId, "un débiteur ne doit jamais apparaître dans le lot d'une autre école");
        }
    }

    [Fact]
    public async Task Tenant_Scoped_List_Query_Never_Returns_The_Other_Schools_Batch()
    {
        await GenerateBatchesAsync(EcoleA);
        await GenerateBatchesAsync(EcoleB);

        await using var contextA = _db.NewAppContext(EcoleA);
        var listForA = await new GetDebtorReminderBatchesQueryHandler(contextA)
            .Handle(new GetDebtorReminderBatchesQuery(Status: null), CancellationToken.None);

        listForA.Should().ContainSingle();
        listForA[0].Items.Should().ContainSingle().Which.StudentId.Should().Be(_studentA);
    }

    [Fact]
    public async Task Sending_A_Batch_Only_Queues_Sms_For_Its_Own_Schools_Debtors()
    {
        await GenerateBatchesAsync(EcoleA);
        await GenerateBatchesAsync(EcoleB);

        await using var contextA = _db.NewAppContext(EcoleA);
        var batchIdA = await contextA.DebtorReminderBatches.Select(b => b.Id).SingleAsync();

        var handler = new SendDebtorReminderBatchCommandHandler(
            contextA,
            new FixedTenantProvider(EcoleA),
            new FixedCurrentUser(Guid.NewGuid()),
            new SmsDispatcher(contextA, TimeProvider.System, NullLogger<SmsDispatcher>.Instance),
            TimeProvider.System,
            NullLogger<SendDebtorReminderBatchCommandHandler>.Instance);

        var result = await handler.Handle(new SendDebtorReminderBatchCommand(batchIdA), CancellationToken.None);

        result.QueuedCount.Should().Be(1);

        await using var owner = _db.NewOwnerContext();
        var messages = await owner.SmsMessages.IgnoreQueryFilters().ToListAsync();
        messages.Should().ContainSingle().Which.SchoolId.Should().Be(EcoleA);

        var sentBatch = await owner.DebtorReminderBatches.IgnoreQueryFilters().SingleAsync(b => b.Id == batchIdA);
        sentBatch.Status.Should().Be(DebtorReminderBatchStatus.Sent);

        // Le lot de l'école B n'a jamais été touché : toujours Draft.
        var otherBatch = await owner.DebtorReminderBatches.IgnoreQueryFilters().SingleAsync(b => b.SchoolId == EcoleB);
        otherBatch.Status.Should().Be(DebtorReminderBatchStatus.Draft);
    }

    [Fact]
    public async Task A_School_Cannot_Send_Or_Dismiss_Another_Schools_Batch()
    {
        await GenerateBatchesAsync(EcoleA);
        await GenerateBatchesAsync(EcoleB);

        await using var ownerLookup = _db.NewOwnerContext();
        var batchIdB = await ownerLookup.DebtorReminderBatches.IgnoreQueryFilters()
            .Where(b => b.SchoolId == EcoleB).Select(b => b.Id).SingleAsync();

        // Sous le tenant A, le Global Query Filter rend le lot de B structurellement introuvable —
        // jamais un 403 métier, un 404 pur et simple, comme partout ailleurs dans le module Finance.
        await using var contextA = _db.NewAppContext(EcoleA);

        var sendHandler = new SendDebtorReminderBatchCommandHandler(
            contextA, new FixedTenantProvider(EcoleA), new FixedCurrentUser(Guid.NewGuid()),
            new SmsDispatcher(contextA, TimeProvider.System, NullLogger<SmsDispatcher>.Instance),
            TimeProvider.System, NullLogger<SendDebtorReminderBatchCommandHandler>.Instance);

        var sendAct = () => sendHandler.Handle(new SendDebtorReminderBatchCommand(batchIdB), CancellationToken.None);
        await sendAct.Should().ThrowAsync<KeyNotFoundException>();

        await using var contextA2 = _db.NewAppContext(EcoleA);
        var dismissHandler = new DismissDebtorReminderBatchCommandHandler(contextA2);
        var dismissAct = () => dismissHandler.Handle(new DismissDebtorReminderBatchCommand(batchIdB), CancellationToken.None);
        await dismissAct.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Dismissing_A_Draft_Batch_Never_Sends_Any_Sms()
    {
        await GenerateBatchesAsync(EcoleA);

        await using var contextA = _db.NewAppContext(EcoleA);
        var batchId = await contextA.DebtorReminderBatches.Select(b => b.Id).SingleAsync();

        await new DismissDebtorReminderBatchCommandHandler(contextA)
            .Handle(new DismissDebtorReminderBatchCommand(batchId), CancellationToken.None);

        await using var owner = _db.NewOwnerContext();
        (await owner.SmsMessages.IgnoreQueryFilters().CountAsync()).Should().Be(0);

        var batch = await owner.DebtorReminderBatches.IgnoreQueryFilters().SingleAsync(b => b.Id == batchId);
        batch.Status.Should().Be(DebtorReminderBatchStatus.Dismissed);
    }

    private sealed class FixedCurrentUser(Guid userId) : ICurrentUserService
    {
        public Guid? UserId => userId;
        public SamaEcole.Domain.Enums.Role? Role => SamaEcole.Domain.Enums.Role.Finance;
        public string? IpAddress => null;
    }
}
````


#### `tests/SamaEcole.IntegrationTests/Finance/DeletedStudentFinancialHistoryTests.cs`

````csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance;
using SamaEcole.Application.Finance.Queries.GetDailyCashRegisterPdf;
using SamaEcole.Application.Finance.Queries.GetDailyClosingReportPdf;
using SamaEcole.Application.Finance.Queries.GetPaymentReceipt;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Finance;

/// <summary>
/// Un élève supprimé laisse derrière lui des paiements ANNULÉS, conservés en base (preuve comptable). Les journaux de
/// caisse et le reçu joignent l'inscription et l'élève, tous deux filtrés une fois archivés : sans correction, le
/// rapport de clôture plante (inscription nulle) et le reçu annulé devient introuvable. Ils doivent rester lisibles,
/// sans jamais ouvrir l'accès à une autre école.
/// </summary>
[Trait("Category", "MultiTenant")]
public class DeletedStudentFinancialHistoryTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("11111111-cccc-1111-1111-111111111111");
    private static readonly Guid AutreEcole = Guid.Parse("22222222-cccc-1111-1111-111111111111");
    private static readonly Guid Classe = Guid.Parse("aaaaaaaa-cccc-0000-0000-00000000000a");
    private static readonly Guid Annee = Guid.Parse("bbbbbbbb-cccc-0000-0000-00000000000b");
    private static readonly Guid Eleve = Guid.Parse("eeeeeeee-cccc-0000-0000-00000000000e");
    private static readonly Guid Inscription = Guid.Parse("11111111-cccc-0000-0000-0000000000f1");
    private static readonly Guid Caissier = Guid.Parse("dddddddd-cccc-0000-0000-00000000000d");
    private static readonly Guid SessionLundi = Guid.Parse("cccccccc-cccc-0000-0000-0000000000c1");
    private static readonly Guid SessionMardi = Guid.Parse("cccccccc-cccc-0000-0000-0000000000c2");
    private static readonly Guid PaiementLundi = Guid.Parse("99999999-cccc-0000-0000-0000000000a1");

    private static readonly DateTimeOffset Lundi = new(2026, 10, 12, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Mardi = Lundi.AddDays(1);

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();
        owner.Schools.Add(new School { Id = Ecole, Name = "École A" });
        owner.Schools.Add(new School { Id = AutreEcole, Name = "École B" });
        owner.Classrooms.Add(new Classroom { Id = Classe, SchoolId = Ecole, Name = "CM2", Level = "Primaire", Capacity = 40 });
        owner.SchoolYears.Add(new SchoolYear
        {
            Id = Annee, SchoolId = Ecole, Label = "2026-2027",
            StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true
        });
        owner.Students.Add(new Student
        {
            Id = Eleve, SchoolId = Ecole, Matricule = "ELEV-2026-0001", FullName = "Awa Fall",
            BirthDate = new DateOnly(2015, 5, 20), BirthPlace = "Dakar", Gender = "F", ClassroomId = Classe
        });
        owner.Enrollments.Add(new Enrollment
        {
            Id = Inscription, SchoolId = Ecole, StudentId = Eleve, SchoolYearId = Annee, ClassroomId = Classe,
            Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed,
            TotalDue = 500_000m, AmountPaid = 0m, ReceiptNumber = "REC-2026-0001", EnrolledAt = Lundi
        });
        owner.Users.Add(new User
        {
            Id = Caissier, SchoolId = Ecole, Email = "c@ecole-a.sn", PasswordHash = "h", FullName = "Caissier", Role = Role.Finance
        });
        owner.CashierSessions.Add(new CashierSession
        {
            Id = SessionLundi, SchoolId = Ecole, CashierId = Caissier, Status = CashierSessionStatus.Closed,
            OpenedAt = Lundi.AddHours(8), ClosedAt = Lundi.AddHours(18), OpeningBalance = 0m
        });
        owner.CashierSessions.Add(new CashierSession
        {
            Id = SessionMardi, SchoolId = Ecole, CashierId = Caissier, Status = CashierSessionStatus.Open,
            OpenedAt = Mardi.AddHours(8), OpeningBalance = 0m
        });
        // Lundi : 50 000 en espèces, annulé mardi — puis l'élève est supprimé.
        owner.Payments.Add(new Payment
        {
            Id = PaiementLundi, SchoolId = Ecole, EnrollmentId = Inscription, CashierSessionId = SessionLundi,
            Amount = 50_000m, Method = PaymentMethod.Cash, Status = PaymentStatus.Cancelled, BalanceAfter = 0m,
            ReceiptNumber = "REC-LUNDI", ReceivedByUserId = Caissier, PaidAt = Lundi.AddHours(10),
            CancelledAt = Mardi.AddHours(9), CancelledByUserId = Caissier, CancellationReason = "Mauvais élève",
            CancellationOutcome = PaymentCancellationOutcome.KeptBySchool, CancellationSessionId = SessionMardi
        });
        await owner.SaveChangesAsync(CancellationToken.None);

        var enrollment = await owner.Enrollments.IgnoreQueryFilters().SingleAsync(e => e.Id == Inscription);
        var student = await owner.Students.IgnoreQueryFilters().SingleAsync(s => s.Id == Eleve);
        enrollment.SoftDelete("directeur");
        student.SoftDelete("directeur");
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task The_Closing_Report_Still_Names_The_Deleted_Student()
    {
        await using var ctx = _db.NewAppContext(Ecole);
        var generator = new CapturingClosingGenerator();
        var handler = new GetDailyClosingReportPdfQueryHandler(ctx, new FixedTenant(Ecole), new NoLogo(), generator);

        await handler.Handle(new GetDailyClosingReportPdfQuery(SessionLundi), CancellationToken.None);

        var row = generator.Captured!.Transactions.Should().ContainSingle().Subject;
        row.StudentName.Should().Contain("Awa Fall").And.Contain("(élève supprimé)");
        row.CancelledNote.Should().Contain("Mauvais élève");
    }

    [Fact]
    public async Task The_Closing_Report_Of_The_Cancelling_Session_Still_Lists_The_Reversal()
    {
        await using var ctx = _db.NewAppContext(Ecole);
        var generator = new CapturingClosingGenerator();
        var handler = new GetDailyClosingReportPdfQueryHandler(ctx, new FixedTenant(Ecole), new NoLogo(), generator);

        await handler.Handle(new GetDailyClosingReportPdfQuery(SessionMardi), CancellationToken.None);

        var reversal = generator.Captured!.Reversals.Should().ContainSingle().Subject;
        reversal.StudentName.Should().Contain("Awa Fall").And.Contain("(élève supprimé)");
        reversal.Amount.Should().Be(-50_000m);
    }

    [Fact]
    public async Task The_Daily_Cash_Register_Still_Names_The_Deleted_Student()
    {
        await using var ctx = _db.NewAppContext(Ecole);
        var generator = new CapturingCashRegisterGenerator();
        var handler = new GetDailyCashRegisterPdfQueryHandler(ctx, new FixedTenant(Ecole), generator, new NoLogo());

        await handler.Handle(new GetDailyCashRegisterPdfQuery(DateOnly.FromDateTime(Lundi.UtcDateTime)), CancellationToken.None);

        var line = generator.Captured!.Payments.Should().ContainSingle().Subject;
        line.StudentFullName.Should().Contain("Awa Fall").And.Contain("(élève supprimé)");
        line.Matricule.Should().Be("ELEV-2026-0001");
    }

    [Fact]
    public async Task The_Cancelled_Receipt_Of_A_Deleted_Student_Is_Still_Readable()
    {
        await using var ctx = _db.NewAppContext(Ecole);
        var handler = new GetPaymentReceiptQueryHandler(ctx, new FixedTenant(Ecole));

        var receipt = await handler.Handle(new GetPaymentReceiptQuery(PaiementLundi), CancellationToken.None);

        receipt.StudentFullName.Should().Contain("Awa Fall");
        receipt.CancellationNotice.Should().NotBeNull().And.Contain("Mauvais élève");
    }

    [Fact]
    public async Task Another_School_Cannot_Read_The_Deleted_Students_Receipt()
    {
        await using var ctx = _db.NewAppContext(AutreEcole);
        var handler = new GetPaymentReceiptQueryHandler(ctx, new FixedTenant(AutreEcole));

        var act = async () => await handler.Handle(new GetPaymentReceiptQuery(PaiementLundi), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>(
            "IgnoreQueryFilters retire aussi le filtre de tenant : il est rétabli à la main, et la RLS protège en dernier recours");
    }

    private sealed class FixedTenant(Guid schoolId) : ITenantProvider
    {
        public Guid? CurrentSchoolId => schoolId;
    }

    private sealed class NoLogo : ISchoolLogoProvider
    {
        public Task<byte[]?> TryFetchAsync(string? logoUrl, CancellationToken cancellationToken) => Task.FromResult<byte[]?>(null);
    }

    private sealed class CapturingClosingGenerator : IDailyClosingReportPdfGenerator
    {
        public DailyClosingReportDto? Captured { get; private set; }

        public byte[] Generate(DailyClosingReportDto report, byte[]? schoolLogo)
        {
            Captured = report;
            return [1];
        }
    }

    private sealed class CapturingCashRegisterGenerator : IDailyCashRegisterPdfGenerator
    {
        public DailyCashRegisterDto? Captured { get; private set; }

        public byte[] Generate(DailyCashRegisterDto data, byte[]? schoolLogo)
        {
            Captured = data;
            return [1];
        }
    }
}
````


#### `tests/SamaEcole.IntegrationTests/Finance/EmployeeContractLifecycleTests.cs`

````csharp
using FluentAssertions;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Commands.CloseEmployeeContract;
using SamaEcole.Application.Finance.Commands.GenerateFichePaie;
using SamaEcole.Application.Finance.Commands.UpdateEmployeeContract;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace SamaEcole.IntegrationTests.Finance;

/// <summary>
/// Volume 1 §14.1 — modification et clôture d'un contrat, côté base réelle (verrou optimiste xmin,
/// RLS). Un contrat n'est jamais supprimé physiquement : ces tests prouvent que la clôture pose
/// seulement <see cref="EmployeeContract.EndDate"/>, jamais un DELETE, et que l'historique
/// (<see cref="EmployeeContractHistory"/>) trace fidèlement chaque changement.
/// </summary>
[Trait("Category", "MultiTenant")]
public class EmployeeContractLifecycleTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("11111111-3333-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("22222222-3333-1111-1111-111111111112");
    private static readonly Guid UtilisateurA = Guid.Parse("aaaaaaaa-3333-0000-0000-00000000000a");
    private static readonly Guid ContratA = Guid.Parse("cccccccc-3333-0000-0000-00000000000a");

    private Guid _actorId;

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();

        owner.Schools.AddRange(
            new School { Id = EcoleA, Name = "École A" },
            new School { Id = EcoleB, Name = "École B" });

        var directeurA = new User
        {
            SchoolId = EcoleA, Email = "directeur.a@ecole-a.sn", PasswordHash = "hash",
            FullName = "Directeur A", Role = Role.Directeur
        };
        owner.Users.Add(directeurA);

        owner.Users.Add(new User
        {
            Id = UtilisateurA, SchoolId = EcoleA, Email = "employe.a@ecole-a.sn", PasswordHash = "hash",
            FullName = "Fatou Employée", Role = Role.Secretariat
        });

        owner.EmployeeContracts.Add(new EmployeeContract
        {
            Id = ContratA, SchoolId = EcoleA, UserId = UtilisateurA,
            Type = ContractType.Permanent, BaseSalary = 250_000m, HourlyRate = 0m, TransportAllowance = 15_000m
        });

        await owner.SaveChangesAsync(CancellationToken.None);
        _actorId = directeurA.Id;
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private async Task<uint> ReadRowVersionAsync(IApplicationDbContext ctx) =>
        await ctx.EmployeeContracts.AsNoTracking()
            .Where(c => c.Id == ContratA)
            .Select(c => EF.Property<uint>(c, "xmin"))
            .SingleAsync();

    // ------------------------------------------------------------ Modification

    [Fact]
    public async Task Updating_A_Contract_Should_Change_Terms_And_Record_History()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var rowVersion = await ReadRowVersionAsync(ctx);

        var handler = new UpdateEmployeeContractCommandHandler(
            ctx, new FixedTenantProvider(EcoleA), new FixedCurrentUser(_actorId), TimeProvider.System);

        var result = await handler.Handle(
            new UpdateEmployeeContractCommand(ContratA, 300_000m, 0m, 20_000m, "Augmentation annuelle actée en conseil.", rowVersion),
            CancellationToken.None);

        result.BaseSalary.Should().Be(300_000m);

        await using var check = _db.NewAppContext(EcoleA);
        var contract = await check.EmployeeContracts.AsNoTracking().SingleAsync(c => c.Id == ContratA);
        contract.BaseSalary.Should().Be(300_000m);
        contract.TransportAllowance.Should().Be(20_000m);

        var history = await check.EmployeeContractHistories.AsNoTracking().SingleAsync(h => h.EmployeeContractId == ContratA);
        history.ChangeType.Should().Be(EmployeeContractChangeType.Amended);
        history.PreviousBaseSalary.Should().Be(250_000m);
        history.NewBaseSalary.Should().Be(300_000m);
        history.Reason.Should().Be("Augmentation annuelle actée en conseil.");
    }

    [Fact]
    public async Task Updating_A_Contract_With_A_Stale_RowVersion_Should_Throw_A_Concurrency_Conflict()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var staleRowVersion = await ReadRowVersionAsync(ctx);

        // Une première modification, hors scope du test, fait avancer xmin en base.
        await using (var firstEdit = _db.NewAppContext(EcoleA))
        {
            var firstHandler = new UpdateEmployeeContractCommandHandler(
                firstEdit, new FixedTenantProvider(EcoleA), new FixedCurrentUser(_actorId), TimeProvider.System);
            await firstHandler.Handle(
                new UpdateEmployeeContractCommand(ContratA, 260_000m, 0m, 15_000m, "Première révision.", staleRowVersion),
                CancellationToken.None);
        }

        var handler = new UpdateEmployeeContractCommandHandler(
            ctx, new FixedTenantProvider(EcoleA), new FixedCurrentUser(_actorId), TimeProvider.System);

        var act = async () => await handler.Handle(
            new UpdateEmployeeContractCommand(ContratA, 999_999m, 0m, 15_000m, "Seconde révision, jeton périmé.", staleRowVersion),
            CancellationToken.None);

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
    }

    [Fact]
    public async Task Updating_Only_PayoutMethod_Should_Not_Create_A_History_Entry()
    {
        // Ticket JGK-K02 : EmployeeContractHistory historise les MONTANTS (AGENTS.md règle #4), pas
        // les coordonnées de règlement. Un changement de PayoutMethod seul ne doit jamais produire
        // une ligne Previous==New qui affirmerait un changement de rémunération inexistant.
        await using var ctx = _db.NewAppContext(EcoleA);
        var rowVersion = await ReadRowVersionAsync(ctx);

        var handler = new UpdateEmployeeContractCommandHandler(
            ctx, new FixedTenantProvider(EcoleA), new FixedCurrentUser(_actorId), TimeProvider.System);

        var result = await handler.Handle(
            new UpdateEmployeeContractCommand(
                ContratA, 250_000m, 0m, 15_000m, "Passage au virement bancaire.", rowVersion,
                PayoutMethod.BankTransfer, "SN08 SN01 0152 0000 0000 1234 5678"),
            CancellationToken.None);

        result.PayoutMethod.Should().Be(PayoutMethod.BankTransfer);

        await using var check = _db.NewAppContext(EcoleA);
        var contract = await check.EmployeeContracts.AsNoTracking().SingleAsync(c => c.Id == ContratA);
        contract.PayoutMethod.Should().Be(PayoutMethod.BankTransfer);
        contract.PayoutAccountReference.Should().Be("SN08 SN01 0152 0000 0000 1234 5678");

        (await check.EmployeeContractHistories.AsNoTracking().AnyAsync(h => h.EmployeeContractId == ContratA))
            .Should().BeFalse("aucun montant n'a changé, l'historique ne doit pas s'en trouver rempli");
    }

    [Fact]
    public async Task Updating_A_Contract_From_Another_School_Should_Be_Not_Found()
    {
        await using var ctx = _db.NewAppContext(EcoleB);

        var handler = new UpdateEmployeeContractCommandHandler(
            ctx, new FixedTenantProvider(EcoleB), new FixedCurrentUser(_actorId), TimeProvider.System);

        var act = async () => await handler.Handle(
            new UpdateEmployeeContractCommand(ContratA, 300_000m, 0m, 15_000m, "Tentative depuis une autre école.", 0),
            CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>("la RLS + le filtre EF rendent le contrat de l'École A invisible à l'École B");
    }

    // ------------------------------------------------------------ Clôture

    [Fact]
    public async Task Closing_A_Contract_Should_Set_EndDate_Without_Deleting_It()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var rowVersion = await ReadRowVersionAsync(ctx);

        var handler = new CloseEmployeeContractCommandHandler(
            ctx, new FixedTenantProvider(EcoleA), new FixedCurrentUser(_actorId), TimeProvider.System);

        await handler.Handle(
            new CloseEmployeeContractCommand(ContratA, new DateOnly(2026, 3, 31), "Fin de contrat.", rowVersion),
            CancellationToken.None);

        await using var check = _db.NewAppContext(EcoleA);
        var contract = await check.EmployeeContracts.AsNoTracking().SingleAsync(c => c.Id == ContratA);

        contract.Should().NotBeNull("AGENTS.md règle #6 : aucune suppression physique");
        contract.EndDate.Should().Be(new DateOnly(2026, 3, 31));
        contract.IsDeleted.Should().BeFalse();

        var history = await check.EmployeeContractHistories.AsNoTracking().SingleAsync(h => h.EmployeeContractId == ContratA);
        history.ChangeType.Should().Be(EmployeeContractChangeType.Closed);
        history.EndDate.Should().Be(new DateOnly(2026, 3, 31));
        history.PreviousBaseSalary.Should().Be(history.NewBaseSalary, "la clôture ne change pas les termes du contrat");
    }

    [Fact]
    public async Task Closing_An_Already_Closed_Contract_Should_Throw_A_Business_Rule_Exception()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var rowVersion = await ReadRowVersionAsync(ctx);

        var handler = new CloseEmployeeContractCommandHandler(
            ctx, new FixedTenantProvider(EcoleA), new FixedCurrentUser(_actorId), TimeProvider.System);

        await handler.Handle(
            new CloseEmployeeContractCommand(ContratA, new DateOnly(2026, 3, 31), "Fin de contrat.", rowVersion),
            CancellationToken.None);

        await using var secondCtx = _db.NewAppContext(EcoleA);
        var secondHandler = new CloseEmployeeContractCommandHandler(
            secondCtx, new FixedTenantProvider(EcoleA), new FixedCurrentUser(_actorId), TimeProvider.System);

        var act = async () => await secondHandler.Handle(
            new CloseEmployeeContractCommand(ContratA, new DateOnly(2026, 4, 30), "Nouvelle tentative.", rowVersion),
            CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task A_Closed_Contract_Should_No_Longer_Generate_A_Payslip()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var rowVersion = await ReadRowVersionAsync(ctx);

        var closeHandler = new CloseEmployeeContractCommandHandler(
            ctx, new FixedTenantProvider(EcoleA), new FixedCurrentUser(_actorId), TimeProvider.System);
        await closeHandler.Handle(
            new CloseEmployeeContractCommand(ContratA, new DateOnly(2026, 3, 31), "Fin de contrat.", rowVersion),
            CancellationToken.None);

        await using var payrollCtx = _db.NewAppContext(EcoleA);
        var payrollHandler = new GenerateFichePaieCommandHandler(payrollCtx, new FixedTenantProvider(EcoleA));

        var act = async () => await payrollHandler.Handle(
            new GenerateFichePaieCommand(ContratA, 4, 2026), CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task Closing_A_Contract_Should_Allow_A_New_Contract_For_The_Same_Person()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var rowVersion = await ReadRowVersionAsync(ctx);

        var closeHandler = new CloseEmployeeContractCommandHandler(
            ctx, new FixedTenantProvider(EcoleA), new FixedCurrentUser(_actorId), TimeProvider.System);
        await closeHandler.Handle(
            new CloseEmployeeContractCommand(ContratA, new DateOnly(2026, 3, 31), "Fin de contrat.", rowVersion),
            CancellationToken.None);

        await using var createCtx = _db.NewAppContext(EcoleA);
        createCtx.EmployeeContracts.Add(new EmployeeContract
        {
            SchoolId = EcoleA, UserId = UtilisateurA,
            Type = ContractType.Vacataire, BaseSalary = 0m, HourlyRate = 5_000m, TransportAllowance = 0m
        });

        // Ne doit PAS lever de violation d'index unique : le contrat précédent est clôturé, l'index
        // "TeacherId/UserId IS NOT NULL AND EndDate IS NULL" ne le considère plus.
        var act = async () => await createCtx.SaveChangesAsync(CancellationToken.None);
        await act.Should().NotThrowAsync();
    }
}

file sealed class FixedTenantProvider(Guid schoolId) : ITenantProvider
{
    public Guid? CurrentSchoolId => schoolId;
}

file sealed class FixedCurrentUser(Guid userId) : ICurrentUserService
{
    public Guid? UserId => userId;
    public Role? Role => SamaEcole.Domain.Enums.Role.Directeur;
    public string? IpAddress => null;
}
````


#### `tests/SamaEcole.IntegrationTests/Finance/FeeIsolationTests.cs`

````csharp
using FluentAssertions;
using SamaEcole.Domain.Entities;
using SamaEcole.IntegrationTests.Common;
using Npgsql;
using Xunit;

namespace SamaEcole.IntegrationTests.Finance;

/// <summary>
/// Ticket JGK-F01 — l'isolation du barème tient-elle dans la BASE, et le journal des changements
/// est-il vraiment append-only ?
///
/// Tout est en SQL BRUT, avec le rôle applicatif : ni EF, ni Handler. Seules la policy RLS et les
/// GRANT de la migration font foi.
/// </summary>
[Trait("Category", "MultiTenant")]
public class FeeIsolationTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly Guid ClasseA = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid ClasseB = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");
    private static readonly Guid CategorieA = Guid.Parse("cccccccc-0000-0000-0000-00000000000a");
    private static readonly Guid CategorieB = Guid.Parse("dddddddd-0000-0000-0000-00000000000b");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();

        owner.Schools.AddRange(
            new School { Id = EcoleA, Name = "École A" },
            new School { Id = EcoleB, Name = "École B" });

        owner.Classrooms.AddRange(
            new Classroom { Id = ClasseA, SchoolId = EcoleA, Name = "CM2", Level = "Primaire", Capacity = 40 },
            new Classroom { Id = ClasseB, SchoolId = EcoleB, Name = "6e", Level = "Collège", Capacity = 45 });

        owner.FeeCategories.AddRange(
            new FeeCategory { Id = CategorieA, SchoolId = EcoleA, Name = "Mensualité", IsRecurring = true },
            new FeeCategory { Id = CategorieB, SchoolId = EcoleB, Name = "Mensualité", IsRecurring = true });

        owner.ClassFees.AddRange(
            new ClassFee { SchoolId = EcoleA, FeeCategoryId = CategorieA, ClassroomId = ClasseA, Amount = 15000 },
            new ClassFee { SchoolId = EcoleB, FeeCategoryId = CategorieB, ClassroomId = ClasseB, Amount = 25000 });

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task RawSqlQuery_Should_Never_Return_Other_School_Fees()
    {
        // SQL brut, sans EF : si le barème de l'École B remonte ici, la RLS ne protège rien.
        var amounts = await ReadFeeAmountsAsync(EcoleA);

        amounts.Should().ContainSingle().Which.Should().Be(15000);
        amounts.Should().NotContain(25000, "la RLS doit masquer le barème des autres écoles");
    }

    [Fact]
    public async Task Session_Without_Tenant_Should_See_No_Fee_At_All()
    {
        var amounts = await ReadFeeAmountsAsync(schoolId: null);

        amounts.Should().BeEmpty();
    }

    [Fact]
    public async Task The_History_Journal_Must_Reject_Updates_By_The_Application_Role()
    {
        // On sème une ligne d'historique avec le propriétaire (il possède les tables, aucun GRANT ne
        // le bride), puis on prouve que le rôle APPLICATIF ne peut pas la réécrire.
        var classFeeId = await FirstClassFeeIdAsync(EcoleA);
        await SeedHistoryRowAsync(EcoleA, classFeeId);

        await using var connection = await _db.OpenRawAppConnectionAsync(EcoleA);
        await using var command = connection.CreateCommand();
        command.CommandText = """UPDATE fee_change_history SET "NewAmount" = 1 WHERE "SchoolId" = @s;""";
        command.Parameters.AddWithValue("s", EcoleA);

        var act = async () => await command.ExecuteNonQueryAsync();

        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.InsufficientPrivilege,
                "le journal des changements de barème est append-only (Volume 1 §7.4)");
    }

    [Fact]
    public async Task The_History_Journal_Must_Reject_Deletes_By_The_Application_Role()
    {
        var classFeeId = await FirstClassFeeIdAsync(EcoleA);
        await SeedHistoryRowAsync(EcoleA, classFeeId);

        await using var connection = await _db.OpenRawAppConnectionAsync(EcoleA);
        await using var command = connection.CreateCommand();
        command.CommandText = """DELETE FROM fee_change_history WHERE "SchoolId" = @s;""";
        command.Parameters.AddWithValue("s", EcoleA);

        var act = async () => await command.ExecuteNonQueryAsync();

        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.InsufficientPrivilege);
    }

    private async Task<Guid> FirstClassFeeIdAsync(Guid schoolId)
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(schoolId);
        await using var command = connection.CreateCommand();
        command.CommandText = """SELECT "Id" FROM class_fees LIMIT 1;""";
        return (Guid)(await command.ExecuteScalarAsync())!;
    }

    /// <summary>Insertion par le PROPRIÉTAIRE : il possède la table, les GRANT append-only ne le concernent pas.</summary>
    private async Task SeedHistoryRowAsync(Guid schoolId, Guid classFeeId)
    {
        await using var owner = _db.NewOwnerContext();
        owner.FeeChangeHistory.Add(new FeeChangeHistory
        {
            SchoolId = schoolId,
            ClassFeeId = classFeeId,
            OldAmount = null,
            NewAmount = 15000,
            ChangedByUserId = Guid.NewGuid(),
            ChangedAt = DateTimeOffset.UtcNow
        });
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    private async Task<List<decimal>> ReadFeeAmountsAsync(Guid? schoolId)
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(schoolId);
        await using var command = connection.CreateCommand();
        command.CommandText = """SELECT "Amount" FROM class_fees ORDER BY "Amount";""";

        var amounts = new List<decimal>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            amounts.Add(reader.GetDecimal(0));
        }

        return amounts;
    }
}
````


#### `tests/SamaEcole.IntegrationTests/Finance/GenerateTaxDeclarationCommandTests.cs`

````csharp
using FluentAssertions;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Finance.Commands.GenerateTaxDeclaration;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Finance;

/// <summary>
/// Declaration fiscale - jusqu'ici sans aucune couverture, alors que TVA collectee/deductible etaient
/// cablees a 0 (voir GenerateTaxDeclarationCommand : le calcul est maintenant une vraie agregation de
/// Payment.VatAmount/Disbursement.VatAmount sur la periode). Ces tests couvrent : le sens du calcul
/// (encaisse - deductible), les exclusions (paiement annule, hors periode), et l'isolation multi-tenant
/// - exerces contre un vrai PostgreSQL (RLS active).
/// </summary>
[Trait("Category", "MultiTenant")]
public class GenerateTaxDeclarationCommandTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Classe = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid ClasseEcoleB = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000b");
    private static readonly Guid Annee = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");
    private static readonly Guid AnneeEcoleB = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000c");
    private static readonly Guid Eleve = Guid.Parse("eeeeeeee-0000-0000-0000-00000000000a");
    private static readonly Guid EleveEcoleB = Guid.Parse("eeeeeeee-0000-0000-0000-00000000000b");
    private static readonly Guid Inscription = Guid.Parse("11111111-0000-0000-0000-0000000000f1");
    private static readonly Guid InscriptionEcoleB = Guid.Parse("11111111-0000-0000-0000-0000000000f2");
    private static readonly Guid Caissier = Guid.Parse("dddddddd-0000-0000-0000-00000000000d");
    private static readonly Guid Enseignant = Guid.Parse("cccccccc-0000-0000-0000-00000000000c");
    private static readonly Guid Contrat = Guid.Parse("ffffffff-0000-0000-0000-00000000000f");

    private const int Mois = 3;
    private const int Annee2026 = 2026;

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();

        owner.Schools.AddRange(
            new School { Id = EcoleA, Name = "Ecole A" },
            new School { Id = EcoleB, Name = "Ecole B" });

        owner.Classrooms.AddRange(
            new Classroom { Id = Classe, SchoolId = EcoleA, Name = "CM2", Level = "Primaire", Capacity = 40 },
            new Classroom { Id = ClasseEcoleB, SchoolId = EcoleB, Name = "CM2", Level = "Primaire", Capacity = 40 });

        owner.SchoolYears.AddRange(
            new SchoolYear
            {
                Id = Annee, SchoolId = EcoleA, Label = "2025-2026",
                StartDate = new DateOnly(2025, 10, 1), EndDate = new DateOnly(2026, 6, 30), IsActive = true
            },
            new SchoolYear
            {
                Id = AnneeEcoleB, SchoolId = EcoleB, Label = "2025-2026",
                StartDate = new DateOnly(2025, 10, 1), EndDate = new DateOnly(2026, 6, 30), IsActive = true
            });

        owner.Students.AddRange(
            new Student { Id = Eleve, SchoolId = EcoleA, Matricule = "ELEV-2026-0001", FullName = "Awa Fall", BirthDate = new DateOnly(2015, 5, 20), BirthPlace = "Dakar", Gender = "F", ClassroomId = Classe },
            new Student { Id = EleveEcoleB, SchoolId = EcoleB, Matricule = "ELEV-B-0001", FullName = "Moussa Diop", BirthDate = new DateOnly(2013, 2, 10), BirthPlace = "Thies", Gender = "M", ClassroomId = ClasseEcoleB });

        owner.Enrollments.AddRange(
            new Enrollment
            {
                Id = Inscription, SchoolId = EcoleA, StudentId = Eleve, SchoolYearId = Annee, ClassroomId = Classe,
                Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed,
                TotalDue = 1_000_000m, AmountPaid = 0m, ReceiptNumber = "REC-2026-0001", EnrolledAt = DateTimeOffset.UtcNow
            },
            new Enrollment
            {
                Id = InscriptionEcoleB, SchoolId = EcoleB, StudentId = EleveEcoleB, SchoolYearId = AnneeEcoleB, ClassroomId = ClasseEcoleB,
                Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed,
                TotalDue = 1_000_000m, AmountPaid = 0m, ReceiptNumber = "REC-B-0001", EnrolledAt = DateTimeOffset.UtcNow
            });

        owner.Teachers.Add(new Teacher
        {
            Id = Enseignant, SchoolId = EcoleA, Matricule = "ENS-2026-0001", FullName = "Ndeye Sarr",
            Email = "ndeye@ecole-a.sn", BirthDate = new DateOnly(1985, 4, 12)
        });
        owner.EmployeeContracts.Add(new EmployeeContract
        {
            Id = Contrat, SchoolId = EcoleA, TeacherId = Enseignant, Type = ContractType.Permanent,
            BaseSalary = 200_000m, HourlyRate = 0m, TransportAllowance = 0m
        });
        owner.FichePaies.Add(Application.Finance.Services.PayrollCalculator.CalculateFichePaie(
            EcoleA, Contrat, Mois, Annee2026, baseSalary: 200_000m, hourlyRate: 0m, hoursWorked: 0m, transportAllowance: 0m));

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private static Payment NewPayment(Guid schoolId, Guid enrollmentId, decimal amount, decimal? vatRate, PaymentStatus status, DateTimeOffset paidAt, string receiptNumber) => new()
    {
        SchoolId = schoolId, EnrollmentId = enrollmentId, Amount = amount, Method = PaymentMethod.Cash,
        Status = status, BalanceAfter = 1_000_000m - amount, ReceiptNumber = receiptNumber,
        ReceivedByUserId = Caissier, PaidAt = paidAt,
        VatRate = vatRate, VatAmount = Application.Finance.Services.VatCalculator.ComputeVatAmount(amount, vatRate)
    };

    private static Disbursement NewDisbursement(Guid schoolId, decimal amount, decimal? vatRate, DateOnly date, string reason) => new()
    {
        SchoolId = schoolId, Reason = reason, Category = DisbursementCategory.Fournitures, Amount = amount,
        PaymentMethod = PaymentMethod.Cash, Date = date, Beneficiary = "Fournisseur Test",
        VatRate = vatRate, VatAmount = Application.Finance.Services.VatCalculator.ComputeVatAmount(amount, vatRate)
    };

    [Fact]
    public async Task Vat_Collected_And_Deductible_Are_Aggregated_From_The_Periods_Transactions()
    {
        await using var owner = _db.NewOwnerContext();
        owner.Payments.AddRange(
            NewPayment(EcoleA, Inscription, 118_000m, 0.18m, PaymentStatus.Partial, new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero), "REC-2026-0002"),
            NewPayment(EcoleA, Inscription, 50_000m, null, PaymentStatus.Partial, new DateTimeOffset(2026, 3, 15, 9, 0, 0, TimeSpan.Zero), "REC-2026-0003"));
        owner.Disbursements.Add(NewDisbursement(EcoleA, 59_000m, 0.18m, new DateOnly(2026, 3, 12), "Fournitures de bureau"));
        await owner.SaveChangesAsync(CancellationToken.None);

        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new GenerateTaxDeclarationCommandHandler(ctx, new FixedTenantProvider(EcoleA));

        var id = await handler.Handle(new GenerateTaxDeclarationCommand(Mois, Annee2026), CancellationToken.None);

        var declaration = await ctx.TaxeDeclarations.FindAsync([id], CancellationToken.None);
        declaration.Should().NotBeNull();
        declaration!.TvaCollected.Should().Be(18_000m, "118 000 TTC a 18% -> 18 000 de TVA, le versement exonere (50 000) n'apporte rien");
        declaration.TvaDeductible.Should().Be(9_000m, "59 000 TTC a 18% -> 9 000 de TVA recuperable");
        declaration.NetTva.Should().Be(9_000m, "18 000 collectee moins 9 000 deductible");
    }

    [Fact]
    public async Task A_Cancelled_Payment_Never_Contributes_To_Vat_Collected()
    {
        await using var owner = _db.NewOwnerContext();
        owner.Payments.Add(NewPayment(EcoleA, Inscription, 118_000m, 0.18m, PaymentStatus.Cancelled, new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero), "REC-2026-0004"));
        await owner.SaveChangesAsync(CancellationToken.None);

        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new GenerateTaxDeclarationCommandHandler(ctx, new FixedTenantProvider(EcoleA));

        var id = await handler.Handle(new GenerateTaxDeclarationCommand(Mois, Annee2026), CancellationToken.None);

        var declaration = await ctx.TaxeDeclarations.FindAsync([id], CancellationToken.None);
        declaration!.TvaCollected.Should().Be(0m, "un paiement annule n'a jamais represente un encaissement reel");
    }

    [Fact]
    public async Task Transactions_Outside_The_Declared_Month_Are_Excluded()
    {
        await using var owner = _db.NewOwnerContext();
        // Fevrier, alors que la declaration porte sur mars.
        owner.Payments.Add(NewPayment(EcoleA, Inscription, 118_000m, 0.18m, PaymentStatus.Partial, new DateTimeOffset(2026, 2, 28, 9, 0, 0, TimeSpan.Zero), "REC-2026-0005"));
        owner.Disbursements.Add(NewDisbursement(EcoleA, 59_000m, 0.18m, new DateOnly(2026, 4, 1), "Hors periode"));
        await owner.SaveChangesAsync(CancellationToken.None);

        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new GenerateTaxDeclarationCommandHandler(ctx, new FixedTenantProvider(EcoleA));

        var id = await handler.Handle(new GenerateTaxDeclarationCommand(Mois, Annee2026), CancellationToken.None);

        var declaration = await ctx.TaxeDeclarations.FindAsync([id], CancellationToken.None);
        declaration!.TvaCollected.Should().Be(0m);
        declaration.TvaDeductible.Should().Be(0m);
    }

    [Fact]
    public async Task Another_Schools_Vat_Never_Leaks_Into_The_Declaration()
    {
        await using var owner = _db.NewOwnerContext();
        owner.Payments.Add(NewPayment(EcoleB, InscriptionEcoleB, 500_000m, 0.18m, PaymentStatus.Partial, new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero), "REC-B-0002"));
        owner.Disbursements.Add(NewDisbursement(EcoleB, 200_000m, 0.18m, new DateOnly(2026, 3, 12), "Decaissement Ecole B"));
        await owner.SaveChangesAsync(CancellationToken.None);

        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new GenerateTaxDeclarationCommandHandler(ctx, new FixedTenantProvider(EcoleA));

        var id = await handler.Handle(new GenerateTaxDeclarationCommand(Mois, Annee2026), CancellationToken.None);

        var declaration = await ctx.TaxeDeclarations.FindAsync([id], CancellationToken.None);
        declaration!.TvaCollected.Should().Be(0m, "le paiement de l'Ecole B ne doit jamais compter dans la declaration de l'Ecole A");
        declaration.TvaDeductible.Should().Be(0m);
    }

    [Fact]
    public async Task The_Payroll_Aggregation_Still_Works_Alongside_The_New_Vat_Computation()
    {
        // Non-regression : IPRES/CSS/VRS/BRS restent agreges depuis les FichePaies, inchanges par l'ajout de la TVA.
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new GenerateTaxDeclarationCommandHandler(ctx, new FixedTenantProvider(EcoleA));

        var id = await handler.Handle(new GenerateTaxDeclarationCommand(Mois, Annee2026), CancellationToken.None);

        var declaration = await ctx.TaxeDeclarations.FindAsync([id], CancellationToken.None);
        declaration!.TotalIpres.Should().BeGreaterThan(0m);
        declaration.TotalDueToState.Should().BeGreaterThan(0m, "VRS + BRS restent dus independamment de la TVA");
    }

    [Fact]
    public async Task A_Duplicate_Declaration_For_The_Same_Period_Is_Rejected()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new GenerateTaxDeclarationCommandHandler(ctx, new FixedTenantProvider(EcoleA));
        await handler.Handle(new GenerateTaxDeclarationCommand(Mois, Annee2026), CancellationToken.None);

        await using var ctx2 = _db.NewAppContext(EcoleA);
        var handler2 = new GenerateTaxDeclarationCommandHandler(ctx2, new FixedTenantProvider(EcoleA));

        var act = async () => await handler2.Handle(new GenerateTaxDeclarationCommand(Mois, Annee2026), CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    private sealed class FixedTenantProvider(Guid schoolId) : Application.Common.Interfaces.ITenantProvider
    {
        public Guid? CurrentSchoolId => schoolId;
    }
}
````


#### `tests/SamaEcole.IntegrationTests/Finance/GetCurrentCashierSessionQueryTests.cs`

````csharp
using FluentAssertions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Queries.GetCurrentCashierSession;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace SamaEcole.IntegrationTests.Finance;

/// <summary>
/// GET /finance/sessions/current (câblage écran /caisse, 27/08/2026) : c'est ce que l'écran interroge
/// AVANT toute tentative d'encaissement pour savoir s'il doit proposer d'ouvrir une session ou afficher
/// le statut de celle déjà ouverte — <see cref="Application.Finance.Commands.RecordPayment.RecordPaymentCommandHandler"/>
/// refusait déjà (422) tout encaissement hors session ouverte, mais rien ne permettait à l'écran de le
/// savoir sans essayer et échouer.
/// </summary>
public class GetCurrentCashierSessionQueryTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("11111111-6666-1111-1111-111111111111");
    private static readonly Guid Classe = Guid.Parse("aaaaaaaa-6666-0000-0000-00000000000a");
    private static readonly Guid Annee = Guid.Parse("bbbbbbbb-6666-0000-0000-00000000000b");
    private static readonly Guid Eleve = Guid.Parse("eeeeeeee-6666-0000-0000-00000000000e");
    private static readonly Guid Inscription = Guid.Parse("11111111-6666-0000-0000-0000000000f1");
    private static readonly Guid Caissier = Guid.Parse("dddddddd-6666-0000-0000-00000000000d");
    private static readonly Guid AutreCaissier = Guid.Parse("dddddddd-6666-0000-0000-00000000000e");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();

        owner.Schools.Add(new School { Id = Ecole, Name = "École A" });
        owner.Classrooms.Add(new Classroom { Id = Classe, SchoolId = Ecole, Name = "CM2", Level = "Primaire", Capacity = 40 });
        owner.SchoolYears.Add(new SchoolYear
        {
            Id = Annee, SchoolId = Ecole, Label = "2026-2027",
            StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true
        });
        owner.Students.Add(new Student
        {
            Id = Eleve, SchoolId = Ecole, Matricule = "ELEV-2026-0001",
            FullName = "Awa Fall", BirthDate = new DateOnly(2015, 5, 20), BirthPlace = "Dakar", Gender = "F", ClassroomId = Classe
        });
        owner.Enrollments.Add(new Enrollment
        {
            Id = Inscription, SchoolId = Ecole, StudentId = Eleve, SchoolYearId = Annee, ClassroomId = Classe,
            Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed,
            TotalDue = 100_000m, AmountPaid = 0m, ReceiptNumber = "REC-2026-0001",
            EnrolledAt = DateTimeOffset.UtcNow
        });
        owner.Users.AddRange(
            new User { Id = Caissier, SchoolId = Ecole, Email = "caissier.a@ecole-a.sn", PasswordHash = "hash", FullName = "Caissier A", Role = Role.Finance },
            new User { Id = AutreCaissier, SchoolId = Ecole, Email = "caissier.b@ecole-a.sn", PasswordHash = "hash", FullName = "Caissier B", Role = Role.Finance });

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private static GetCurrentCashierSessionQueryHandler NewHandler(IApplicationDbContext ctx, Guid userId) =>
        new(ctx, new FixedCurrentUser(userId));

    [Fact]
    public async Task Returns_Null_When_The_User_Has_No_Open_Session()
    {
        await using var ctx = _db.NewAppContext(Ecole);

        var result = await NewHandler(ctx, Caissier).Handle(new GetCurrentCashierSessionQuery(), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Returns_The_Open_Session_With_Its_Running_Total()
    {
        var sessionId = Guid.NewGuid();
        await using (var seedCtx = _db.NewAppContext(Ecole))
        {
            seedCtx.CashierSessions.Add(new CashierSession
            {
                Id = sessionId, SchoolId = Ecole, CashierId = Caissier,
                Status = CashierSessionStatus.Open, OpenedAt = DateTimeOffset.UtcNow, OpeningBalance = 5_000m
            });
            await seedCtx.SaveChangesAsync(CancellationToken.None);

            seedCtx.Payments.AddRange(
                new Payment
                {
                    Id = Guid.NewGuid(), SchoolId = Ecole, EnrollmentId = Inscription, CashierSessionId = sessionId,
                    Amount = 10_000m, Method = PaymentMethod.Cash, Status = PaymentStatus.Paid,
                    ReceiptNumber = "REC-A", BalanceAfter = 90_000m, PaidAt = DateTimeOffset.UtcNow, ReceivedByUserId = Caissier
                },
                new Payment
                {
                    Id = Guid.NewGuid(), SchoolId = Ecole, EnrollmentId = Inscription, CashierSessionId = sessionId,
                    Amount = 7_000m, Method = PaymentMethod.Cash, Status = PaymentStatus.Paid,
                    ReceiptNumber = "REC-B", BalanceAfter = 83_000m, PaidAt = DateTimeOffset.UtcNow, ReceivedByUserId = Caissier
                },
                // Annulé : ne doit JAMAIS entrer dans le total affiché — même exclusion que
                // CloseCashierSessionCommandHandler (Volume 1 §15.2), sans quoi le chiffre montré
                // pendant la journée contredirait celui du rapport de clôture.
                new Payment
                {
                    Id = Guid.NewGuid(), SchoolId = Ecole, EnrollmentId = Inscription, CashierSessionId = sessionId,
                    Amount = 99_999m, Method = PaymentMethod.Cash, Status = PaymentStatus.Cancelled,
                    ReceiptNumber = "REC-C", BalanceAfter = 0m, PaidAt = DateTimeOffset.UtcNow, ReceivedByUserId = Caissier
                });
            await seedCtx.SaveChangesAsync(CancellationToken.None);
        }

        await using var ctx = _db.NewAppContext(Ecole);
        var result = await NewHandler(ctx, Caissier).Handle(new GetCurrentCashierSessionQuery(), CancellationToken.None);

        result.Should().NotBeNull();
        result!.SessionId.Should().Be(sessionId);
        result.OpeningBalance.Should().Be(5_000m);
        result.TotalCollected.Should().Be(17_000m, "les deux versements complétés, jamais le paiement annulé");
        result.PaymentsCount.Should().Be(2);
    }

    [Fact]
    public async Task Never_Returns_Another_Cashiers_Open_Session()
    {
        await using (var seedCtx = _db.NewAppContext(Ecole))
        {
            seedCtx.CashierSessions.Add(new CashierSession
            {
                Id = Guid.NewGuid(), SchoolId = Ecole, CashierId = AutreCaissier,
                Status = CashierSessionStatus.Open, OpenedAt = DateTimeOffset.UtcNow, OpeningBalance = 1_000m
            });
            await seedCtx.SaveChangesAsync(CancellationToken.None);
        }

        await using var ctx = _db.NewAppContext(Ecole);
        var result = await NewHandler(ctx, Caissier).Handle(new GetCurrentCashierSessionQuery(), CancellationToken.None);

        result.Should().BeNull("la session ouverte par un autre caissier n'est pas la sienne");
    }

    [Fact]
    public async Task Never_Returns_A_Closed_Session()
    {
        await using (var seedCtx = _db.NewAppContext(Ecole))
        {
            seedCtx.CashierSessions.Add(new CashierSession
            {
                Id = Guid.NewGuid(), SchoolId = Ecole, CashierId = Caissier,
                Status = CashierSessionStatus.Closed, OpenedAt = DateTimeOffset.UtcNow.AddHours(-8),
                ClosedAt = DateTimeOffset.UtcNow, OpeningBalance = 2_000m, ClosingBalance = 2_000m
            });
            await seedCtx.SaveChangesAsync(CancellationToken.None);
        }

        await using var ctx = _db.NewAppContext(Ecole);
        var result = await NewHandler(ctx, Caissier).Handle(new GetCurrentCashierSessionQuery(), CancellationToken.None);

        result.Should().BeNull("la journée d'hier est clôturée : elle ne doit pas rouvrir le formulaire d'encaissement aujourd'hui");
    }
}

file sealed class FixedCurrentUser(Guid userId) : ICurrentUserService
{
    public Guid? UserId => userId;
    public Role? Role => Domain.Enums.Role.Finance;
    public string? IpAddress => "127.0.0.1";
}
````


#### `tests/SamaEcole.IntegrationTests/Finance/GetDebtorAgingReportTests.cs`

````csharp
using FluentAssertions;
using SamaEcole.Application.Finance.Queries.GetDebtorAgingReport;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Finance;

/// <summary>
/// Rapport d'ancienneté des débiteurs (Volume 1 §7.5), contre un vrai PostgreSQL sous le rôle
/// applicatif. Plusieurs débiteurs dans la même école : le handler charge lignes de frais et
/// échéanciers EN LOT pour tous les débiteurs (plus une requête par débiteur) — ces tests prouvent
/// que chaque inscription retrouve bien SES lignes, dans le bon ordre, et SON échéancier actif.
/// </summary>
public class GetDebtorAgingReportTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateOnly Today = new(2027, 1, 15);
    private static readonly DateOnly SchoolYearStart = new(2026, 10, 1);

    private readonly Guid _classroomId = Guid.NewGuid();
    private readonly Guid _yearId = Guid.NewGuid();
    private readonly Guid _inscriptionCategoryId = Guid.NewGuid();
    private readonly Guid _monthlyCategoryId = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();

        owner.Schools.Add(new School { Id = Ecole, Name = "École A" });
        owner.FeeCategories.AddRange(
            new FeeCategory { Id = _inscriptionCategoryId, SchoolId = Ecole, Name = "Inscription", IsRecurring = false },
            new FeeCategory { Id = _monthlyCategoryId, SchoolId = Ecole, Name = "Mensualité", IsRecurring = true });
        owner.Classrooms.Add(new Classroom { Id = _classroomId, SchoolId = Ecole, Name = "CM2", Level = "Primaire", Capacity = 40 });
        owner.SchoolYears.Add(new SchoolYear
        {
            Id = _yearId, SchoolId = Ecole, Label = "2026-2027",
            StartDate = SchoolYearStart, EndDate = SchoolYearStart.AddYears(1), IsActive = true
        });

        // Awa — inscription 15 000 + 9 mensualités de 10 000, 35 000 payés. Les frais ponctuels sont
        // soldés EN PREMIER (ordre IsRecurring puis Designation) : inscription, octobre et novembre
        // payés, la plus ancienne échéance en retard est le 1er décembre → 45 jours.
        var awa = AddDebtor(owner, "ELEV-0001", "Awa Fall", totalDue: 105_000m, amountPaid: 35_000m);
        AddLine(owner, awa, _monthlyCategoryId, "Mensualité", isRecurring: true, unitAmount: 10_000m, months: 9);
        AddLine(owner, awa, _inscriptionCategoryId, "Inscription", isRecurring: false, unitAmount: 15_000m, months: 1);

        // Baba — l'échéancier ACTIF prime sur les lignes de frais (qui le mettraient en retard depuis le
        // 1er octobre, 106 jours). 30 000 payés couvrent la 1re échéance : retard depuis le 5 novembre
        // → 71 jours. Un échéancier ANNULÉ, plus ancien, doit être ignoré.
        var baba = AddDebtor(owner, "ELEV-0002", "Baba Diop", totalDue: 90_000m, amountPaid: 30_000m);
        AddLine(owner, baba, _inscriptionCategoryId, "Scolarité", isRecurring: false, unitAmount: 90_000m, months: 1);
        AddPlan(owner, baba, FeeInstallmentPlanStatus.Cancelled,
            (1, new DateOnly(2026, 9, 1), 90_000m));
        AddPlan(owner, baba, FeeInstallmentPlanStatus.Active,
            (1, new DateOnly(2026, 10, 5), 30_000m),
            (2, new DateOnly(2026, 11, 5), 30_000m),
            (3, new DateOnly(2027, 2, 5), 30_000m));

        // Cheikh — solde dû, mais aucune échéance encore dépassée : hors du rapport.
        var cheikh = AddDebtor(owner, "ELEV-0003", "Cheikh Ba", totalDue: 50_000m, amountPaid: 0m);
        AddPlan(owner, cheikh, FeeInstallmentPlanStatus.Active, (1, new DateOnly(2027, 2, 1), 50_000m));

        // Diary — entièrement soldé : jamais débiteur.
        var diary = AddDebtor(owner, "ELEV-0004", "Diary Sow", totalDue: 15_000m, amountPaid: 15_000m);
        AddLine(owner, diary, _inscriptionCategoryId, "Inscription", isRecurring: false, unitAmount: 15_000m, months: 1);

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task Each_Debtor_Is_Aged_From_Its_Own_Fee_Lines_Or_Active_Plan_Most_Overdue_First()
    {
        await using var context = _db.NewAppContext(Ecole);
        var handler = new GetDebtorAgingReportQueryHandler(
            context, new FixedTimeProvider(new DateTimeOffset(Today.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero)));

        var report = await handler.Handle(new GetDebtorAgingReportQuery(), CancellationToken.None);

        report.GeneratedOn.Should().Be(Today);
        report.Debtors.Select(d => (d.Matricule, d.RemainingBalance, d.DaysOverdue)).Should().Equal(
            ("ELEV-0002", 60_000m, 71),
            ("ELEV-0001", 70_000m, 45));
        report.Debtors.Should().AllSatisfy(d => d.ClassroomName.Should().Be("CM2"));
    }

    private Guid AddDebtor(
        Persistence.ApplicationDbContext owner, string matricule, string fullName, decimal totalDue, decimal amountPaid)
    {
        var studentId = Guid.NewGuid();
        var enrollmentId = Guid.NewGuid();

        owner.Students.Add(new Student
        {
            Id = studentId, SchoolId = Ecole, Matricule = matricule, FullName = fullName,
            BirthDate = new DateOnly(2015, 1, 1), BirthPlace = "Dakar", Gender = "F", ClassroomId = _classroomId
        });
        owner.Enrollments.Add(new Enrollment
        {
            Id = enrollmentId, SchoolId = Ecole, StudentId = studentId, SchoolYearId = _yearId,
            ClassroomId = _classroomId, Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed,
            TotalDue = totalDue, AmountPaid = amountPaid, ReceiptNumber = $"REC-{matricule}",
            EnrolledAt = DateTimeOffset.UtcNow
        });

        return enrollmentId;
    }

    private static void AddLine(
        Persistence.ApplicationDbContext owner, Guid enrollmentId, Guid feeCategoryId, string designation,
        bool isRecurring, decimal unitAmount, int months) =>
        owner.EnrollmentFeeLines.Add(new EnrollmentFeeLine
        {
            SchoolId = Ecole, EnrollmentId = enrollmentId, FeeCategoryId = feeCategoryId,
            Designation = designation, IsRecurring = isRecurring, UnitAmount = unitAmount, Months = months,
            LineTotal = unitAmount * months
        });

    private static void AddPlan(
        Persistence.ApplicationDbContext owner, Guid enrollmentId, FeeInstallmentPlanStatus status,
        params (int SequenceNo, DateOnly DueDate, decimal Amount)[] installments)
    {
        var plan = new FeeInstallmentPlan { Id = Guid.NewGuid(), SchoolId = Ecole, EnrollmentId = enrollmentId, Status = status };
        owner.FeeInstallmentPlans.Add(plan);
        owner.FeeInstallments.AddRange(installments.Select(i => new FeeInstallment
        {
            SchoolId = Ecole, FeeInstallmentPlanId = plan.Id, SequenceNo = i.SequenceNo,
            Label = $"Échéance {i.SequenceNo}", Amount = i.Amount, DueDate = i.DueDate
        }));
    }
}

file sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
````


#### `tests/SamaEcole.IntegrationTests/Finance/GetSuggestedPayrollHoursQueryHandlerTests.cs`

````csharp
using FluentAssertions;
using SamaEcole.Application.Finance.Queries.GetSuggestedPayrollHours;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Finance;

/// <summary>
/// Ticket JGK-K01 — suggestion des heures de paie, rapprochée de l'emploi du temps. Purement
/// consultatif (Volume 1 §14.3 amendé) : ces tests portent sur l'agrégation et la détection d'écart,
/// jamais sur une écriture — la requête ne modifie jamais le contrat ni les enregistrements d'heures.
/// </summary>
[Trait("Category", "MultiTenant")]
public class GetSuggestedPayrollHoursQueryHandlerTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("11111111-4444-1111-1111-111111111111");
    private static readonly Guid EnseignantA = Guid.Parse("eeeeeeee-4444-0000-0000-00000000000a");
    private static readonly Guid ContratVacataire = Guid.Parse("cccccccc-4444-0000-0000-00000000000a");
    private static readonly Guid ContratPersonnel = Guid.Parse("cccccccc-4444-0000-0000-00000000000b");
    private static readonly Guid UtilisateurPersonnel = Guid.Parse("aaaaaaaa-4444-0000-0000-00000000000a");
    private static readonly Guid ClasseA = Guid.Parse("ccccdddd-4444-0000-0000-00000000000a");
    private static readonly Guid MatiereA = Guid.Parse("ccccffff-4444-0000-0000-00000000000a");

    // Deux lundis du même mois : le créneau planifié (2h) est identique pour les deux, seules les
    // heures DÉCLARÉES diffèrent — ce qui isole proprement le cas "conforme" du cas "écart".
    private static readonly DateOnly PremierLundi = new(2026, 9, 7);
    private static readonly DateOnly SecondLundi = new(2026, 9, 14);

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();

        owner.Schools.Add(new School { Id = EcoleA, Name = "École A" });

        owner.Teachers.Add(new Teacher
        {
            Id = EnseignantA, SchoolId = EcoleA, Matricule = "ENS-A-0001", FullName = "Cheikh Ndiaye",
            Email = "cheikh.ndiaye@ecole-a.sn", BirthDate = new DateOnly(1985, 1, 1)
        });

        owner.Users.Add(new User
        {
            Id = UtilisateurPersonnel, SchoolId = EcoleA, Email = "surveillant.a@ecole-a.sn", PasswordHash = "hash",
            FullName = "Surveillant A", Role = Role.Surveillant
        });

        owner.EmployeeContracts.AddRange(
            new EmployeeContract
            {
                Id = ContratVacataire, SchoolId = EcoleA, TeacherId = EnseignantA,
                Type = ContractType.Vacataire, BaseSalary = 0m, HourlyRate = 3_000m, TransportAllowance = 0m
            },
            new EmployeeContract
            {
                // Contrat lié à un UserId (personnel non-enseignant) : aucun ScheduleSlot possible.
                Id = ContratPersonnel, SchoolId = EcoleA, UserId = UtilisateurPersonnel,
                Type = ContractType.Vacataire, BaseSalary = 0m, HourlyRate = 2_000m, TransportAllowance = 0m
            });

        owner.Classrooms.Add(new Classroom { Id = ClasseA, SchoolId = EcoleA, Name = "3e A", Level = "Collège", Cycle = CycleType.College, Capacity = 40 });
        owner.Subjects.Add(new Subject { Id = MatiereA, SchoolId = EcoleA, Name = "Mathématiques", Level = "Collège" });

        // Créneau planifié : lundi 08h-10h, soit 2 heures — identique pour les deux lundis du test.
        owner.ScheduleSlots.Add(new ScheduleSlot
        {
            SchoolId = EcoleA, TeacherId = EnseignantA, ClassroomId = ClasseA, SubjectId = MatiereA,
            DayOfWeek = PremierLundi.DayOfWeek, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(10, 0)
        });

        owner.TeacherHourRecords.AddRange(
            // Conforme : 2h déclarées == 2h planifiées.
            new TeacherHourRecord { SchoolId = EcoleA, EmployeeContractId = ContratVacataire, Date = PremierLundi, Hours = 2m },
            // Écart : 3h déclarées != 2h planifiées.
            new TeacherHourRecord { SchoolId = EcoleA, EmployeeContractId = ContratVacataire, Date = SecondLundi, Hours = 3m },
            new TeacherHourRecord { SchoolId = EcoleA, EmployeeContractId = ContratPersonnel, Date = PremierLundi, Hours = 4m });

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task Suggested_Hours_Should_Sum_All_Declared_Hours_Of_The_Month()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new GetSuggestedPayrollHoursQueryHandler(ctx);

        var result = await handler.Handle(
            new GetSuggestedPayrollHoursQuery(ContratVacataire, PremierLundi.Month, PremierLundi.Year),
            CancellationToken.None);

        result.SuggestedHours.Should().Be(5m, "2h le premier lundi + 3h le second");
    }

    [Fact]
    public async Task A_Day_Matching_The_Schedule_Should_Not_Be_Reported_As_A_Discrepancy()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new GetSuggestedPayrollHoursQueryHandler(ctx);

        var result = await handler.Handle(
            new GetSuggestedPayrollHoursQuery(ContratVacataire, PremierLundi.Month, PremierLundi.Year),
            CancellationToken.None);

        result.Discrepancies.Should().NotContain(d => d.Date == PremierLundi);
    }

    [Fact]
    public async Task A_Day_Diverging_From_The_Schedule_Should_Be_Reported_But_Not_Block_Anything()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new GetSuggestedPayrollHoursQueryHandler(ctx);

        var result = await handler.Handle(
            new GetSuggestedPayrollHoursQuery(ContratVacataire, PremierLundi.Month, PremierLundi.Year),
            CancellationToken.None);

        // Purement informatif (Volume 1 §14.3 amendé) : la requête renvoie l'écart, elle ne lève rien.
        result.Discrepancies.Should().ContainSingle(d => d.Date == SecondLundi)
            .Which.Should().BeEquivalentTo(new PayrollHoursDiscrepancyDto(SecondLundi, 3m, 2m));
    }

    [Fact]
    public async Task A_Contract_Linked_To_A_User_Should_Get_No_Discrepancy_Check()
    {
        // Personnel non-enseignant (UserId) : aucun ScheduleSlot n'existe pour lui, le rapprochement
        // n'a pas de sens — seule la suggestion agrégée est renvoyée.
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new GetSuggestedPayrollHoursQueryHandler(ctx);

        var result = await handler.Handle(
            new GetSuggestedPayrollHoursQuery(ContratPersonnel, PremierLundi.Month, PremierLundi.Year),
            CancellationToken.None);

        result.SuggestedHours.Should().Be(4m);
        result.Discrepancies.Should().BeEmpty();
    }

    [Fact]
    public async Task Unknown_Contract_Should_Throw_Not_Found()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new GetSuggestedPayrollHoursQueryHandler(ctx);

        var act = async () => await handler.Handle(
            new GetSuggestedPayrollHoursQuery(Guid.NewGuid(), 9, 2026), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
````


#### `tests/SamaEcole.IntegrationTests/Finance/GetTreasuryDashboardQueryTests.cs`

````csharp
using FluentAssertions;
using SamaEcole.Application.Finance.Queries.GetTreasuryDashboard;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Finance;

/// <summary>
/// Module Comptabilité & Fiscalité (JGK) — le tableau de bord Trésorerie agrège les encaissements
/// (<c>Payments</c>) et décaissements (<c>Disbursements</c>) déjà existants, sans nouveau registre.
///
/// Deux garanties critiques :
/// - Le total encaissé compte TOUT paiement non annulé, y compris les versements PARTIELS (Status =
///   Partial reflète le solde de l'INSCRIPTION, pas si l'argent de CE versement a été reçu — un bug
///   de la première version filtrait sur Status == Paid et excluait donc tout paiement échelonné).
/// - L'isolation multi-tenant tient : les mouvements de l'École B ne remontent jamais dans le tableau
///   de l'École A (RLS + filtre EF, AGENTS.md règle #2), exercé ici contre un vrai PostgreSQL sous le
///   rôle applicatif.
/// </summary>
[Trait("Category", "MultiTenant")]
public class GetTreasuryDashboardQueryTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Classe = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid Annee = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");
    private static readonly Guid Eleve = Guid.Parse("eeeeeeee-0000-0000-0000-00000000000e");
    private static readonly Guid Inscription = Guid.Parse("11111111-0000-0000-0000-0000000000f1");
    private static readonly Guid Caissier = Guid.Parse("dddddddd-0000-0000-0000-00000000000d");

    // École B : fiches propres (une inscription liée à l'École A ne satisferait pas la FK composite
    // FK_payments_enrollments_SchoolId_EnrollmentId — impossible d'y accrocher un paiement d'École B).
    private static readonly Guid ClasseB = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000b");
    private static readonly Guid AnneeB = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000c");
    private static readonly Guid EleveB = Guid.Parse("eeeeeeee-0000-0000-0000-00000000000f");
    private static readonly Guid InscriptionB = Guid.Parse("11111111-0000-0000-0000-0000000000f2");

    private static readonly DateOnly PeriodStart = new(2026, 3, 1);
    private static readonly DateOnly PeriodEnd = new(2026, 3, 31);

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();

        owner.Schools.AddRange(
            new School { Id = EcoleA, Name = "École A" },
            new School { Id = EcoleB, Name = "École B" });

        owner.Classrooms.Add(new Classroom { Id = Classe, SchoolId = EcoleA, Name = "CM2", Level = "Primaire", Capacity = 40 });
        owner.SchoolYears.Add(new SchoolYear
        {
            Id = Annee, SchoolId = EcoleA, Label = "2025-2026",
            StartDate = new DateOnly(2025, 10, 1), EndDate = new DateOnly(2026, 6, 30), IsActive = true
        });
        owner.Students.Add(new Student
        {
            Id = Eleve, SchoolId = EcoleA, Matricule = "ELEV-2025-0001",
            FullName = "Awa Fall", BirthDate = new DateOnly(2015, 5, 20), BirthPlace = "Dakar", Gender = "F", ClassroomId = Classe
        });
        owner.Enrollments.Add(new Enrollment
        {
            Id = Inscription, SchoolId = EcoleA, StudentId = Eleve, SchoolYearId = Annee, ClassroomId = Classe,
            Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed,
            TotalDue = 100_000m, AmountPaid = 40_000m, ReceiptNumber = "REC-2025-0001",
            EnrolledAt = DateTimeOffset.UtcNow
        });

        // École A — dans la période : un versement PARTIEL (doit compter comme encaissement malgré
        // Status = Partial) et un décaissement.
        owner.Payments.Add(new Payment
        {
            SchoolId = EcoleA, EnrollmentId = Inscription, Amount = 40_000m, Method = PaymentMethod.Cash,
            Status = PaymentStatus.Partial, BalanceAfter = 60_000m, ReceiptNumber = "REC-2025-0002",
            ReceivedByUserId = Caissier, PaidAt = new DateTimeOffset(2026, 3, 15, 9, 0, 0, TimeSpan.Zero)
        });
        owner.Disbursements.Add(new Disbursement
        {
            SchoolId = EcoleA, Reason = "Fournitures", Category = DisbursementCategory.Fournitures,
            Amount = 10_000m, PaymentMethod = PaymentMethod.Cash, Date = new DateOnly(2026, 3, 10), Beneficiary = "Papeterie du Marché"
        });

        // École A — hors période (mars) : un paiement de février et un paiement ANNULÉ en mars. Ni l'un
        // ni l'autre ne doivent entrer dans le total de la période testée.
        owner.Payments.Add(new Payment
        {
            SchoolId = EcoleA, EnrollmentId = Inscription, Amount = 25_000m, Method = PaymentMethod.Cash,
            Status = PaymentStatus.Partial, BalanceAfter = 75_000m, ReceiptNumber = "REC-2025-0003",
            ReceivedByUserId = Caissier, PaidAt = new DateTimeOffset(2026, 2, 20, 9, 0, 0, TimeSpan.Zero)
        });
        owner.Payments.Add(new Payment
        {
            SchoolId = EcoleA, EnrollmentId = Inscription, Amount = 99_999m, Method = PaymentMethod.Cash,
            Status = PaymentStatus.Cancelled, BalanceAfter = 100_000m, ReceiptNumber = "REC-2025-0004",
            ReceivedByUserId = Caissier, PaidAt = new DateTimeOffset(2026, 3, 12, 9, 0, 0, TimeSpan.Zero)
        });

        // École B — même période : ne doit JAMAIS apparaître dans le tableau de l'École A.
        owner.Classrooms.Add(new Classroom { Id = ClasseB, SchoolId = EcoleB, Name = "6e", Level = "Collège", Capacity = 45 });
        owner.SchoolYears.Add(new SchoolYear
        {
            Id = AnneeB, SchoolId = EcoleB, Label = "2025-2026",
            StartDate = new DateOnly(2025, 10, 1), EndDate = new DateOnly(2026, 6, 30), IsActive = true
        });
        owner.Students.Add(new Student
        {
            Id = EleveB, SchoolId = EcoleB, Matricule = "ELEV-B-0001",
            FullName = "Moussa Diop", BirthDate = new DateOnly(2013, 2, 10), BirthPlace = "Thiès", Gender = "M", ClassroomId = ClasseB
        });
        owner.Enrollments.Add(new Enrollment
        {
            Id = InscriptionB, SchoolId = EcoleB, StudentId = EleveB, SchoolYearId = AnneeB, ClassroomId = ClasseB,
            Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed,
            TotalDue = 500_000m, AmountPaid = 500_000m, ReceiptNumber = "REC-B-0000",
            EnrolledAt = DateTimeOffset.UtcNow
        });
        owner.Payments.Add(new Payment
        {
            SchoolId = EcoleB, EnrollmentId = InscriptionB, Amount = 500_000m, Method = PaymentMethod.MobileMoney,
            Status = PaymentStatus.Paid, BalanceAfter = 0m, ReceiptNumber = "REC-B-0001",
            ReceivedByUserId = Caissier, PaidAt = new DateTimeOffset(2026, 3, 15, 9, 0, 0, TimeSpan.Zero)
        });
        owner.Disbursements.Add(new Disbursement
        {
            SchoolId = EcoleB, Reason = "Salaires École B", Category = DisbursementCategory.Salaires,
            Amount = 300_000m, PaymentMethod = PaymentMethod.Transfer, Date = new DateOnly(2026, 3, 5), Beneficiary = "Personnel"
        });

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task Total_Collected_Includes_Partial_Payments_Not_Only_Fully_Paid_Enrollments()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new GetTreasuryDashboardQueryHandler(ctx);

        var result = await handler.Handle(new GetTreasuryDashboardQuery(PeriodStart, PeriodEnd), CancellationToken.None);

        result.TotalCollected.Should().Be(40_000m,
            "le versement partiel de mars est un encaissement réel, Status=Partial ne veut pas dire « argent non reçu »");
    }

    [Fact]
    public async Task Cancelled_And_Out_Of_Period_Payments_Are_Excluded()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new GetTreasuryDashboardQueryHandler(ctx);

        var result = await handler.Handle(new GetTreasuryDashboardQuery(PeriodStart, PeriodEnd), CancellationToken.None);

        result.TotalCollected.Should().NotBe(40_000m + 25_000m + 99_999m,
            "le paiement de février et le paiement annulé ne doivent pas être comptés");
        result.RecentTransactions.Should().NotContain(t => t.Amount == 99_999m, "un paiement annulé n'est pas une transaction de trésorerie");
    }

    [Fact]
    public async Task Net_Position_Is_Collected_Minus_Disbursed()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new GetTreasuryDashboardQueryHandler(ctx);

        var result = await handler.Handle(new GetTreasuryDashboardQuery(PeriodStart, PeriodEnd), CancellationToken.None);

        result.TotalDisbursed.Should().Be(10_000m);
        result.NetPosition.Should().Be(30_000m, "40 000 encaissés − 10 000 décaissés");
    }

    [Fact]
    public async Task Outstanding_Debt_Reflects_The_Current_Enrollment_Balance()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new GetTreasuryDashboardQueryHandler(ctx);

        var result = await handler.Handle(new GetTreasuryDashboardQuery(PeriodStart, PeriodEnd), CancellationToken.None);

        result.TotalOutstandingDebt.Should().Be(60_000m, "TotalDue 100 000 − AmountPaid 40 000, indépendant du filtre de période");
    }

    [Fact]
    public async Task Other_School_Movements_Never_Leak_Into_The_Dashboard()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new GetTreasuryDashboardQueryHandler(ctx);

        var result = await handler.Handle(new GetTreasuryDashboardQuery(PeriodStart, PeriodEnd), CancellationToken.None);

        result.TotalCollected.Should().NotBe(40_000m + 500_000m, "le paiement de l'École B ne doit jamais compter dans le total de l'École A");
        result.TotalDisbursed.Should().NotBe(10_000m + 300_000m, "le décaissement de l'École B ne doit jamais compter dans le total de l'École A");
    }

    [Fact]
    public async Task Default_Period_Falls_Back_To_The_Current_Month_When_No_Dates_Are_Given()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new GetTreasuryDashboardQueryHandler(ctx);

        var result = await handler.Handle(new GetTreasuryDashboardQuery(), CancellationToken.None);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        result.StartDate.Should().Be(new DateOnly(today.Year, today.Month, 1));
        result.EndDate.Should().Be(today);
    }
}
````


#### `tests/SamaEcole.IntegrationTests/Finance/OptionalFeeCategoryTests.cs`

````csharp
using FluentAssertions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Commands.CreateFeeCategory;
using SamaEcole.Application.Finance.Commands.UpdateFeeCategory;
using SamaEcole.Application.Finance.Queries.GetFeeCategories;
using SamaEcole.Domain.Entities;
using SamaEcole.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace SamaEcole.IntegrationTests.Finance;

/// <summary>
/// Frais optionnels, Tâche 1 — <see cref="FeeCategory.IsOptional"/> doit réellement round-tripper en
/// base (colonne IsOptional), se modifier après coup, et rester cloisonné par école.
/// </summary>
[Trait("Category", "MultiTenant")]
public class OptionalFeeCategoryTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("33333333-2222-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("33333333-2222-2222-2222-222222222222");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();
        owner.Schools.AddRange(
            new School { Id = EcoleA, Name = "École Optionnels A" },
            new School { Id = EcoleB, Name = "École Optionnels B" });
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private async Task<CreateFeeCategoryResult> CreateAsync(Guid school, CreateFeeCategoryCommand command)
    {
        await using var db = _db.NewAppContext(school);
        return await new CreateFeeCategoryCommandHandler(db, new StubTenantProvider(school))
            .Handle(command, CancellationToken.None);
    }

    [Fact]
    public async Task Create_Persists_IsOptional()
    {
        var result = await CreateAsync(EcoleA, new CreateFeeCategoryCommand { Name = "Uniforme", IsOptional = true });

        result.IsOptional.Should().BeTrue();

        await using var check = _db.NewAppContext(EcoleA);
        (await check.FeeCategories.SingleAsync(c => c.Id == result.Id)).IsOptional
            .Should().BeTrue("la colonne IsOptional doit réellement porter la valeur, pas seulement le DTO renvoyé");
    }

    [Fact]
    public async Task Create_Without_The_Flag_Stays_Mandatory()
    {
        var result = await CreateAsync(EcoleA, new CreateFeeCategoryCommand { Name = "Inscription" });

        result.IsOptional.Should().BeFalse();
    }

    [Fact]
    public async Task Update_Toggles_IsOptional_Both_Ways()
    {
        var created = await CreateAsync(EcoleA, new CreateFeeCategoryCommand { Name = "Tenue de sport" });

        await using (var db = _db.NewAppContext(EcoleA))
        {
            await new UpdateFeeCategoryCommandHandler(db)
                .Handle(new UpdateFeeCategoryCommand(created.Id, IsOptional: true), CancellationToken.None);
        }

        await using (var check = _db.NewAppContext(EcoleA))
        {
            (await check.FeeCategories.SingleAsync(c => c.Id == created.Id)).IsOptional.Should().BeTrue();
        }

        await using (var db = _db.NewAppContext(EcoleA))
        {
            await new UpdateFeeCategoryCommandHandler(db)
                .Handle(new UpdateFeeCategoryCommand(created.Id, IsOptional: false), CancellationToken.None);
        }

        await using var checkAgain = _db.NewAppContext(EcoleA);
        (await checkAgain.FeeCategories.SingleAsync(c => c.Id == created.Id)).IsOptional.Should().BeFalse();
    }

    [Fact]
    public async Task Update_Refuses_To_Make_A_Boarding_Category_Optional()
    {
        var pension = await CreateAsync(EcoleA, new CreateFeeCategoryCommand
        {
            Name = "Pension", IsRecurring = true, IsBoardingFee = true
        });

        await using var db = _db.NewAppContext(EcoleA);
        var act = () => new UpdateFeeCategoryCommandHandler(db)
            .Handle(new UpdateFeeCategoryCommand(pension.Id, IsOptional: true), CancellationToken.None);

        await act.Should().ThrowAsync<SamaEcole.Application.Common.Exceptions.ValidationException>();
    }

    [Fact]
    public async Task Update_Cannot_Reach_Another_Schools_Category()
    {
        var chezB = await CreateAsync(EcoleB, new CreateFeeCategoryCommand { Name = "Uniforme" });

        await using var db = _db.NewAppContext(EcoleA);
        var act = () => new UpdateFeeCategoryCommandHandler(db)
            .Handle(new UpdateFeeCategoryCommand(chezB.Id, IsOptional: true), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();

        await using var check = _db.NewAppContext(EcoleB);
        (await check.FeeCategories.SingleAsync(c => c.Id == chezB.Id)).IsOptional.Should().BeFalse();
    }

    [Fact]
    public async Task List_Exposes_IsOptional()
    {
        await CreateAsync(EcoleA, new CreateFeeCategoryCommand { Name = "Uniforme", IsOptional = true });
        await CreateAsync(EcoleA, new CreateFeeCategoryCommand { Name = "Scolarité", IsRecurring = true });

        await using var db = _db.NewAppContext(EcoleA);
        var list = await new GetFeeCategoriesQueryHandler(db)
            .Handle(new GetFeeCategoriesQuery(), CancellationToken.None);

        list.Single(c => c.Name == "Uniforme").IsOptional.Should().BeTrue();
        list.Single(c => c.Name == "Scolarité").IsOptional.Should().BeFalse();
    }

    private sealed class StubTenantProvider(Guid? schoolId) : ITenantProvider
    {
        public Guid? CurrentSchoolId => schoolId;
    }
}
````


#### `tests/SamaEcole.IntegrationTests/Finance/OptionalFeesAnnualDueTests.cs`

````csharp
using SamaEcole.Application.Boarding.Assignments;
using FluentAssertions;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Enrollments.Commands.CreateEnrollment;
using SamaEcole.Application.Finance.Commands.ApplyFeeInstallmentPlanToClassroom;
using SamaEcole.Application.Finance.Commands.UpdateFeeCategory;
using SamaEcole.Application.Finance.Queries.GetDebtorAgingReport;
using SamaEcole.Application.Finance.Queries.GetDuesNotice;
using SamaEcole.Application.Finance.Queries.GetFinanceDashboard;
using SamaEcole.Application.Finance.Queries.GetStudentBalance;
using SamaEcole.Application.Finance.Queries.SearchStudentsForCashier;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using SamaEcole.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace SamaEcole.IntegrationTests.Finance;

/// <summary>
/// Frais optionnels, Tâche 3 — le dû annuel FILTRÉ par les cases cochées doit traverser tous les lecteurs
/// de la Finance : solde de l'élève, sommation, rapport des débiteurs, recherche caisse, tableau de bord,
/// échéancier de classe. Ces tests inscrivent de vrais élèves (vrai handler, vraie sélection) puis
/// interrogent chaque lecteur : ils prouvent qu'aucun ne recalcule le dû depuis le barème de la classe,
/// où le frais décoché figure encore.
///
/// Trois élèves de la même classe, aux factures différentes :
///   Awa    — tous les frais optionnels cochés ;
///   Fatou  — l'uniforme seul ;
///   Modou  — aucun frais optionnel.
/// </summary>
[Trait("Category", "MultiTenant")]
public class OptionalFeesAnnualDueTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("44444444-0000-0000-0000-000000000001");
    private static readonly DateOnly SchoolYearStart = new(2026, 10, 1);
    private static readonly DateOnly Today = new(2027, 1, 15);
    private static readonly DateTimeOffset Now = new(Today.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero);

    private const int TuitionMonths = 9;
    private const decimal Inscription = 10_000m;
    private const decimal Mensualite = 15_000m;
    private const decimal Uniforme = 25_000m;
    private const decimal TenueSport = 8_000m;

    private const decimal Mandatory = Inscription + Mensualite * TuitionMonths; // 145 000
    private const decimal AwaTotal = Mandatory + Uniforme + TenueSport;          // 178 000
    private const decimal FatouTotal = Mandatory + Uniforme;                     // 170 000
    private const decimal ModouTotal = Mandatory;                                // 145 000

    private readonly Guid _classroomId = Guid.NewGuid();
    private readonly Guid _inscriptionId = Guid.NewGuid();
    private readonly Guid _mensualiteId = Guid.NewGuid();
    private readonly Guid _uniformeId = Guid.NewGuid();
    private readonly Guid _tenueId = Guid.NewGuid();

    private Guid _awa, _fatou, _modou;
    private Guid _awaEnrollment, _fatouEnrollment, _modouEnrollment;

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using (var owner = _db.NewOwnerContext())
        {
            owner.Schools.Add(new School { Id = Ecole, Name = "École Frais Optionnels" });
            owner.TenantSubscriptions.Add(RlsTestDatabase.UnlimitedSubscription(Ecole));
            owner.Classrooms.Add(new Classroom { Id = _classroomId, SchoolId = Ecole, Name = "6e A", Level = "Collège", Capacity = 40 });
            owner.SchoolYears.Add(new SchoolYear
            {
                Id = Guid.NewGuid(), SchoolId = Ecole, Label = "2026-2027",
                StartDate = SchoolYearStart, EndDate = SchoolYearStart.AddYears(1), IsActive = true
            });
            owner.SchoolSettings.Add(new SchoolSettings { SchoolId = Ecole, TuitionMonthsPerYear = TuitionMonths });
            owner.FeeCategories.AddRange(
                new FeeCategory { Id = _inscriptionId, SchoolId = Ecole, Name = "Inscription" },
                new FeeCategory { Id = _mensualiteId, SchoolId = Ecole, Name = "Mensualité", IsRecurring = true },
                new FeeCategory { Id = _uniformeId, SchoolId = Ecole, Name = "Uniforme", IsOptional = true },
                new FeeCategory { Id = _tenueId, SchoolId = Ecole, Name = "Tenue de sport", IsOptional = true });
            owner.ClassFees.AddRange(
                new ClassFee { SchoolId = Ecole, FeeCategoryId = _inscriptionId, ClassroomId = _classroomId, Amount = Inscription },
                new ClassFee { SchoolId = Ecole, FeeCategoryId = _mensualiteId, ClassroomId = _classroomId, Amount = Mensualite },
                new ClassFee { SchoolId = Ecole, FeeCategoryId = _uniformeId, ClassroomId = _classroomId, Amount = Uniforme },
                new ClassFee { SchoolId = Ecole, FeeCategoryId = _tenueId, ClassroomId = _classroomId, Amount = TenueSport });
            await owner.SaveChangesAsync(CancellationToken.None);
        }

        (_awa, _awaEnrollment) = await EnrollAsync("Awa Ndiaye", [_uniformeId, _tenueId]);
        (_fatou, _fatouEnrollment) = await EnrollAsync("Fatou Sow", [_uniformeId]);
        (_modou, _modouEnrollment) = await EnrollAsync("Modou Fall", []);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private async Task<(Guid StudentId, Guid EnrollmentId)> EnrollAsync(string fullName, IReadOnlyList<Guid> optionalFees)
    {
        await using var db = _db.NewAppContext(Ecole);
        var handler = new CreateEnrollmentCommandHandler(
            db, new StubTenantProvider(Ecole), _db.NewGenerator(db), TimeProvider.System,
            new NoOpKpiCache(), new TestCurrentUser(), _db.NewQuotaGuard(db, Ecole),
            new BoardingAssignmentService(db, TimeProvider.System));

        var receipt = await handler.Handle(new CreateEnrollmentCommand
        {
            Type = EnrollmentType.NewEnrollment,
            ClassroomId = _classroomId,
            FullName = fullName,
            BirthDate = new DateOnly(2013, 3, 3),
            BirthPlace = "Dakar",
            Gender = "M",
            OptionalFeeCategoryIds = optionalFees
        }, CancellationToken.None);

        await using var check = _db.NewAppContext(Ecole);
        var studentId = await check.Enrollments.Where(e => e.Id == receipt.EnrollmentId).Select(e => e.StudentId).SingleAsync();
        return (studentId, receipt.EnrollmentId);
    }

    // ------------------------------------------------------------------ Solde de l'élève

    [Fact]
    public async Task The_Student_Balance_Only_Counts_The_Checked_Fees()
    {
        await using var db = _db.NewAppContext(Ecole);
        var handler = new GetStudentBalanceQueryHandler(db, new FixedTime(Now));

        var awa = await handler.Handle(new GetStudentBalanceQuery(_awa), CancellationToken.None);
        var fatou = await handler.Handle(new GetStudentBalanceQuery(_fatou), CancellationToken.None);
        var modou = await handler.Handle(new GetStudentBalanceQuery(_modou), CancellationToken.None);

        awa.TotalDue.Should().Be(AwaTotal);
        fatou.TotalDue.Should().Be(FatouTotal);
        modou.TotalDue.Should().Be(ModouTotal);
        modou.RemainingBalance.Should().Be(ModouTotal);

        // Le calendrier d'échéances se recompose depuis les lignes figées : il additionne exactement le dû.
        modou.Installments.Sum(i => i.Amount).Should().Be(ModouTotal);
        modou.Installments.Should().NotContain(i => i.Designation.Contains("Uniforme") || i.Designation.Contains("Tenue"));
        fatou.Installments.Should().Contain(i => i.Designation.Contains("Uniforme"));
        fatou.Installments.Should().NotContain(i => i.Designation.Contains("Tenue"));
        awa.Installments.Sum(i => i.Amount).Should().Be(AwaTotal);
    }

    // ------------------------------------------------------------------ Sommation

    [Fact]
    public async Task The_Dues_Notice_Does_Not_Claim_An_Unchecked_Fee()
    {
        await using var db = _db.NewAppContext(Ecole);
        var handler = new GetDuesNoticeQueryHandler(db, new FixedTime(Now));

        var modou = await handler.Handle(new GetDuesNoticeQuery(_modouEnrollment), CancellationToken.None);
        var awa = await handler.Handle(new GetDuesNoticeQuery(_awaEnrollment), CancellationToken.None);

        modou.TotalDue.Should().Be(ModouTotal);
        modou.RemainingBalance.Should().Be(ModouTotal);
        modou.OverdueInstallments.Should().NotContain(i => i.Designation.Contains("Uniforme") || i.Designation.Contains("Tenue"));

        awa.TotalDue.Should().Be(AwaTotal);
        awa.OverdueInstallments.Should().Contain(i => i.Designation.Contains("Uniforme"));
        awa.OverdueInstallments.Should().Contain(i => i.Designation.Contains("Tenue"));
    }

    // ------------------------------------------------------------------ Rapport des débiteurs

    [Fact]
    public async Task The_Debtor_Report_Shows_Each_Students_Own_Balance()
    {
        await using var db = _db.NewAppContext(Ecole);
        var handler = new GetDebtorAgingReportQueryHandler(db, new FixedTime(Now));

        var report = await handler.Handle(new GetDebtorAgingReportQuery(), CancellationToken.None);

        report.Debtors.Select(d => (d.StudentFullName, d.RemainingBalance)).Should().BeEquivalentTo(new[]
        {
            ("Awa Ndiaye", AwaTotal),
            ("Fatou Sow", FatouTotal),
            ("Modou Fall", ModouTotal)
        });
    }

    // ------------------------------------------------------------------ Recherche caisse

    [Fact]
    public async Task The_Cashier_Search_Quotes_The_Filtered_Due_And_The_Filtered_Amount_Due_Now()
    {
        await using var db = _db.NewAppContext(Ecole);
        var handler = new SearchStudentsForCashierQueryHandler(db, new FixedTime(Now));

        var results = await handler.Handle(new SearchStudentsForCashierQuery("elev"), CancellationToken.None);

        // Le 15/01/2027, l'année ayant démarré le 01/10/2026, les mensualités des mois 1 à 4 (échéances
        // des 01/10, 01/11, 01/12 et 01/01) sont atteintes : elles sont à régler maintenant, pas
        // seulement le 1er mois.
        const int MonthsDueOnTheSearchDate = 4;

        var modou = results.Single(r => r.FullName == "Modou Fall");
        modou.TotalDue.Should().Be(ModouTotal);
        modou.RemainingBalance.Should().Be(ModouTotal);
        // Sans uniforme ni tenue pour Modou.
        modou.DueNowTotal.Should().Be(Inscription + Mensualite * MonthsDueOnTheSearchDate);

        var awa = results.Single(r => r.FullName == "Awa Ndiaye");
        awa.TotalDue.Should().Be(AwaTotal);
        awa.DueNowTotal.Should().Be(Inscription + Uniforme + TenueSport + Mensualite * MonthsDueOnTheSearchDate);
    }

    [Fact]
    public async Task The_Cashier_Search_Quotes_Only_The_Initial_Commitment_Before_The_School_Year_Starts()
    {
        await using var db = _db.NewAppContext(Ecole);
        var beforeStart = new DateTimeOffset(SchoolYearStart.AddDays(-20).ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero);
        var handler = new SearchStudentsForCashierQueryHandler(db, new FixedTime(beforeStart));

        var results = await handler.Handle(new SearchStudentsForCashierQuery("elev"), CancellationToken.None);

        // Aucune échéance n'est encore atteinte : seul l'engagement initial (inscription + 1er mois) est dû.
        results.Single(r => r.FullName == "Modou Fall").DueNowTotal.Should().Be(Inscription + Mensualite);
    }

    // ------------------------------------------------------------------ Tableau de bord

    [Fact]
    public async Task The_Finance_Dashboard_Sums_The_Filtered_Annual_Dues()
    {
        await using var db = _db.NewAppContext(Ecole);
        var handler = new GetFinanceDashboardQueryHandler(db, new FixedTime(Now), new NoOpKpiCache());

        var dashboard = await handler.Handle(new GetFinanceDashboardQuery(), CancellationToken.None);

        dashboard.OutstandingBalance.Should().Be(AwaTotal + FatouTotal + ModouTotal, "rien n'est encore encaissé");
    }

    // ------------------------------------------------------------------ Échéancier de classe

    [Fact]
    public async Task A_Class_Installment_Plan_Is_Computed_On_Each_Students_Own_Filtered_Due()
    {
        await using (var db = _db.NewAppContext(Ecole))
        {
            var handler = new ApplyFeeInstallmentPlanToClassroomCommandHandler(db, new StubTenantProvider(Ecole), new FixedTime(Now));
            var result = await handler.Handle(new ApplyFeeInstallmentPlanToClassroomCommand(
                _classroomId, "Deux tranches",
                [new InstallmentTemplateLine("Tranche 1", 0.5m, 0), new InstallmentTemplateLine("Tranche 2", 0.5m, 30)]),
                CancellationToken.None);

            result.AppliedCount.Should().Be(3);
        }

        await using var check = _db.NewAppContext(Ecole);
        async Task<List<decimal>> AmountsOf(Guid enrollmentId) => await (
            from p in check.FeeInstallmentPlans
            join i in check.FeeInstallments on p.Id equals i.FeeInstallmentPlanId
            where p.EnrollmentId == enrollmentId && p.Status == FeeInstallmentPlanStatus.Active
            orderby i.SequenceNo
            select i.Amount).ToListAsync();

        (await AmountsOf(_modouEnrollment)).Should().Equal(72_500m, 72_500m);   // 145 000 / 2
        (await AmountsOf(_fatouEnrollment)).Should().Equal(85_000m, 85_000m);   // 170 000 / 2
        (await AmountsOf(_awaEnrollment)).Should().Equal(89_000m, 89_000m);     // 178 000 / 2
    }

    // ------------------------------------------------------------------ Protection des frais obligatoires

    [Fact]
    public async Task Making_A_Category_Mandatory_Later_Never_Rewrites_An_Existing_Enrollment()
    {
        await using (var db = _db.NewAppContext(Ecole))
        {
            await new UpdateFeeCategoryCommandHandler(db)
                .Handle(new UpdateFeeCategoryCommand(_uniformeId, IsOptional: false), CancellationToken.None);
        }

        await using var read = _db.NewAppContext(Ecole);
        var balance = await new GetStudentBalanceQueryHandler(read, new FixedTime(Now))
            .Handle(new GetStudentBalanceQuery(_modou), CancellationToken.None);

        balance.TotalDue.Should().Be(ModouTotal, "l'inscription est un instantané : le dû déjà posé ne bouge jamais (règle #4)");
    }

    [Fact]
    public async Task Once_A_Category_Is_Mandatory_It_Is_Billed_To_Every_New_Student_Whatever_The_Form_Sends()
    {
        await using (var db = _db.NewAppContext(Ecole))
        {
            await new UpdateFeeCategoryCommandHandler(db)
                .Handle(new UpdateFeeCategoryCommand(_uniformeId, IsOptional: false), CancellationToken.None);
        }

        // Un formulaire périmé (ancien onglet) décoche l'uniforme : il est devenu obligatoire, il est dû.
        var (studentId, _) = await EnrollAsync("Ibrahima Ba", [_tenueId]);

        await using var read = _db.NewAppContext(Ecole);
        var balance = await new GetStudentBalanceQueryHandler(read, new FixedTime(Now))
            .Handle(new GetStudentBalanceQuery(studentId), CancellationToken.None);

        balance.TotalDue.Should().Be(Mandatory + Uniforme + TenueSport);
    }

    [Fact]
    public async Task A_Mandatory_Fee_Cannot_Be_Removed_By_A_Forged_Payload()
    {
        await using var db = _db.NewAppContext(Ecole);
        var handler = new CreateEnrollmentCommandHandler(
            db, new StubTenantProvider(Ecole), _db.NewGenerator(db), TimeProvider.System,
            new NoOpKpiCache(), new TestCurrentUser(), _db.NewQuotaGuard(db, Ecole),
            new BoardingAssignmentService(db, TimeProvider.System));

        // Ni la mensualité ni l'inscription ne se « cochent » ni ne se décochent : c'est refusé, pas ignoré.
        var act = async () => await handler.Handle(new CreateEnrollmentCommand
        {
            Type = EnrollmentType.NewEnrollment,
            ClassroomId = _classroomId,
            FullName = "Requête Forgée",
            BirthDate = new DateOnly(2013, 3, 3),
            BirthPlace = "Dakar",
            Gender = "F",
            OptionalFeeCategoryIds = [_mensualiteId]
        }, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    private sealed class StubTenantProvider(Guid? schoolId) : ITenantProvider
    {
        public Guid? CurrentSchoolId => schoolId;
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class NoOpKpiCache : IKpiCacheService
    {
        public Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken)
            => factory(cancellationToken);

        public void Invalidate(string key) { }
    }
}
````


#### `tests/SamaEcole.IntegrationTests/Finance/PaymentConcurrencyTests.cs`

````csharp
using FluentAssertions;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace SamaEcole.IntegrationTests.Finance;

/// <summary>
/// Ticket JGK-F02, AGENTS.md règle #5 — CRITÈRE OBLIGATOIRE du ticket : « deux paiements concurrents sur
/// le même solde ne produisent jamais un état incohérent ».
///
/// Le solde vit sur l'inscription (<c>TotalDue − AmountPaid</c>). Deux caissiers qui encaissent en même
/// temps sur la même inscription ont chacun LU le même jeton xmin ; le premier qui écrit gagne, le second
/// se heurte à un 409 (ConcurrencyConflictException) plutôt que d'écraser le solde — jamais de sur-crédit
/// silencieux. On l'exerce contre un vrai PostgreSQL sous le rôle applicatif (RLS active).
/// </summary>
[Trait("Category", "MultiTenant")]
public class PaymentConcurrencyTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Classe = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid Annee = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");
    private static readonly Guid Eleve = Guid.Parse("eeeeeeee-0000-0000-0000-00000000000e");
    private static readonly Guid Inscription = Guid.Parse("11111111-0000-0000-0000-0000000000f1");
    private static readonly Guid Caissier = Guid.Parse("dddddddd-0000-0000-0000-00000000000d");

    private const decimal TotalDue = 100_000m;

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();

        owner.Schools.Add(new School { Id = Ecole, Name = "École A" });
        owner.Classrooms.Add(new Classroom { Id = Classe, SchoolId = Ecole, Name = "CM2", Level = "Primaire", Capacity = 40 });
        owner.SchoolYears.Add(new SchoolYear
        {
            Id = Annee, SchoolId = Ecole, Label = "2025-2026",
            StartDate = new DateOnly(2025, 10, 1), EndDate = new DateOnly(2026, 6, 30), IsActive = true
        });
        owner.Students.Add(new Student
        {
            Id = Eleve, SchoolId = Ecole, Matricule = "ELEV-2025-0001",
            FullName = "Awa Fall", BirthDate = new DateOnly(2015, 5, 20), BirthPlace = "Dakar", Gender = "F", ClassroomId = Classe
        });
        owner.Enrollments.Add(new Enrollment
        {
            Id = Inscription, SchoolId = Ecole, StudentId = Eleve, SchoolYearId = Annee, ClassroomId = Classe,
            Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed,
            TotalDue = TotalDue, AmountPaid = 0m, ReceiptNumber = "REC-2025-0001",
            EnrolledAt = DateTimeOffset.UtcNow
        });

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private static Payment NewPayment(decimal amount, string receiptNumber) => new()
    {
        SchoolId = Ecole,
        EnrollmentId = Inscription,
        Amount = amount,
        Method = PaymentMethod.Cash,
        Status = PaymentStatus.Partial,
        BalanceAfter = TotalDue - amount,
        ReceiptNumber = receiptNumber,
        ReceivedByUserId = Caissier,
        PaidAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task Two_Concurrent_Payments_On_The_Same_Balance_The_Second_One_Is_Refused()
    {
        // Deux caissiers ont chacun ouvert la fiche : ils détiennent le même jeton xmin de l'inscription.
        await using var ctxA = _db.NewAppContext(Ecole);
        await using var ctxB = _db.NewAppContext(Ecole);

        var enrollmentA = await ctxA.Enrollments.FirstAsync(e => e.Id == Inscription);
        var enrollmentB = await ctxB.Enrollments.FirstAsync(e => e.Id == Inscription);

        // A encaisse 30 000 en premier : le xmin de l'inscription bascule en base.
        ctxA.Payments.Add(NewPayment(30_000m, "REC-2025-0002"));
        enrollmentA.AmountPaid += 30_000m;
        await ctxA.SaveChangesAsync(CancellationToken.None);

        // B encaisse avec le jeton qu'il détenait — désormais périmé. Son UPDATE de l'inscription
        // « WHERE xmin = <ancien> » ne touche aucune ligne : SaveChanges refuse, et son INSERT de paiement
        // est rembobiné avec (SaveChanges est atomique) plutôt que de créer un sur-crédit.
        ctxB.Payments.Add(NewPayment(50_000m, "REC-2025-0003"));
        enrollmentB.AmountPaid += 50_000m;
        var act = async () => await ctxB.SaveChangesAsync(CancellationToken.None);

        await act.Should().ThrowAsync<ConcurrencyConflictException>(
            "deux encaissements concurrents sur le même solde ne doivent jamais s'écraser (règle #5)");

        // État final cohérent : un SEUL paiement, et le cumul encaissé est celui de A, intact.
        await using var check = _db.NewAppContext(Ecole);
        (await check.Payments.CountAsync(p => p.EnrollmentId == Inscription)).Should().Be(1, "le paiement de B a été rembobiné");
        (await check.Enrollments.Where(e => e.Id == Inscription).Select(e => e.AmountPaid).FirstAsync())
            .Should().Be(30_000m, "seul l'encaissement de A a été appliqué au solde");
    }

    [Fact]
    public async Task A_Sequential_Payment_With_A_Fresh_Token_Succeeds()
    {
        // Contre-épreuve : sans conflit, l'encaissement passe. Sinon le test précédent pourrait être vert
        // pour une mauvaise raison (une écriture qui échouerait TOUJOURS).
        await using var ctx = _db.NewAppContext(Ecole);
        var enrollment = await ctx.Enrollments.FirstAsync(e => e.Id == Inscription);

        ctx.Payments.Add(NewPayment(40_000m, "REC-2025-0004"));
        enrollment.AmountPaid += 40_000m;

        var act = async () => await ctx.SaveChangesAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}
````


#### `tests/SamaEcole.IntegrationTests/Finance/PaymentIdempotencyTests.cs`

````csharp
using FluentAssertions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Commands.RecordPayment;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace SamaEcole.IntegrationTests.Finance;

/// <summary>
/// Ticket JGK-L01 — résilience réseau côté caisse, dans le cadre strict de D-01 (Volume_0 §0.13) :
/// aucune base locale, aucune file de mutations, la seule protection contre un retry après coupure
/// est cette clé côté serveur. Ces tests prouvent qu'un retry avec la MÊME clé ne crée jamais un
/// second encaissement, et rejoue le résultat du premier (le caissier récupère son reçu).
/// </summary>
[Trait("Category", "MultiTenant")]
public class PaymentIdempotencyTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("11111111-5555-1111-1111-111111111111");
    private static readonly Guid Classe = Guid.Parse("aaaaaaaa-5555-0000-0000-00000000000a");
    private static readonly Guid Annee = Guid.Parse("bbbbbbbb-5555-0000-0000-00000000000b");
    private static readonly Guid Eleve = Guid.Parse("eeeeeeee-5555-0000-0000-00000000000e");
    private static readonly Guid Inscription = Guid.Parse("11111111-5555-0000-0000-0000000000f1");
    private static readonly Guid Caissier = Guid.Parse("dddddddd-5555-0000-0000-00000000000d");
    private static readonly Guid SessionCaisse = Guid.Parse("cccccccc-5555-0000-0000-00000000000c");

    private const decimal TotalDue = 100_000m;

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();

        owner.Schools.Add(new School { Id = Ecole, Name = "École A" });
        owner.Classrooms.Add(new Classroom { Id = Classe, SchoolId = Ecole, Name = "CM2", Level = "Primaire", Capacity = 40 });
        owner.SchoolYears.Add(new SchoolYear
        {
            Id = Annee, SchoolId = Ecole, Label = "2026-2027",
            StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true
        });
        owner.Students.Add(new Student
        {
            Id = Eleve, SchoolId = Ecole, Matricule = "ELEV-2026-0001",
            FullName = "Awa Fall", BirthDate = new DateOnly(2015, 5, 20), BirthPlace = "Dakar", Gender = "F", ClassroomId = Classe
        });
        owner.Enrollments.Add(new Enrollment
        {
            Id = Inscription, SchoolId = Ecole, StudentId = Eleve, SchoolYearId = Annee, ClassroomId = Classe,
            Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed,
            TotalDue = TotalDue, AmountPaid = 0m, ReceiptNumber = "REC-2026-0001",
            EnrolledAt = DateTimeOffset.UtcNow
        });
        owner.Users.Add(new User
        {
            Id = Caissier, SchoolId = Ecole, Email = "caissier.a@ecole-a.sn", PasswordHash = "hash",
            FullName = "Caissier A", Role = Role.Finance
        });
        owner.CashierSessions.Add(new CashierSession
        {
            Id = SessionCaisse, SchoolId = Ecole, CashierId = Caissier,
            Status = CashierSessionStatus.Open, OpenedAt = DateTimeOffset.UtcNow, OpeningBalance = 0m
        });

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private RecordPaymentCommandHandler NewHandler(IApplicationDbContext ctx) => new(
        ctx, new FixedTenantProvider(Ecole), new FixedCurrentUser(Caissier),
        new SamaEcole.Persistence.MatriculeGenerator((SamaEcole.Persistence.ApplicationDbContext)ctx, TimeProvider.System),
        new FakeSmsDispatcher(), TimeProvider.System, new FakeKpiCacheService());

    [Fact]
    public async Task Retrying_With_The_Same_Idempotency_Key_Does_Not_Create_A_Duplicate()
    {
        var idempotencyKey = Guid.NewGuid();

        await using var firstCtx = _db.NewAppContext(Ecole);
        var firstResult = await NewHandler(firstCtx).Handle(
            new RecordPaymentCommand(Inscription, 30_000m, PaymentMethod.Cash, IdempotencyKey: idempotencyKey),
            CancellationToken.None);

        // Simule un retry après coupure réseau : même clé, nouvelle requête, nouveau contexte (comme
        // le ferait un second appel HTTP réel).
        await using var retryCtx = _db.NewAppContext(Ecole);
        var retryResult = await NewHandler(retryCtx).Handle(
            new RecordPaymentCommand(Inscription, 30_000m, PaymentMethod.Cash, IdempotencyKey: idempotencyKey),
            CancellationToken.None);

        retryResult.PaymentId.Should().Be(firstResult.PaymentId, "le retry doit rejouer le MÊME paiement, pas en créer un second");
        retryResult.ReceiptNumber.Should().Be(firstResult.ReceiptNumber, "le caissier doit récupérer le reçu déjà émis");

        await using var check = _db.NewAppContext(Ecole);
        (await check.Payments.CountAsync(p => p.EnrollmentId == Inscription)).Should().Be(1, "un seul encaissement, malgré les deux appels");
        (await check.Enrollments.Where(e => e.Id == Inscription).Select(e => e.AmountPaid).FirstAsync())
            .Should().Be(30_000m, "le solde ne doit pas être débité deux fois par le même paiement");
    }

    [Fact]
    public async Task Two_Payments_With_Different_Idempotency_Keys_Are_Both_Recorded()
    {
        await using var ctx1 = _db.NewAppContext(Ecole);
        await NewHandler(ctx1).Handle(
            new RecordPaymentCommand(Inscription, 20_000m, PaymentMethod.Cash, IdempotencyKey: Guid.NewGuid()),
            CancellationToken.None);

        await using var ctx2 = _db.NewAppContext(Ecole);
        await NewHandler(ctx2).Handle(
            new RecordPaymentCommand(Inscription, 15_000m, PaymentMethod.Cash, IdempotencyKey: Guid.NewGuid()),
            CancellationToken.None);

        await using var check = _db.NewAppContext(Ecole);
        (await check.Payments.CountAsync(p => p.EnrollmentId == Inscription)).Should().Be(2, "deux clés distinctes, deux versements réels");
        (await check.Enrollments.Where(e => e.Id == Inscription).Select(e => e.AmountPaid).FirstAsync())
            .Should().Be(35_000m);
    }

    [Fact]
    public async Task A_Payment_Without_An_Idempotency_Key_Still_Works()
    {
        // Rétrocompatibilité : la clé est optionnelle, un appel qui n'en fournit pas (client non
        // encore mis à jour) doit continuer à fonctionner exactement comme avant ce ticket.
        await using var ctx = _db.NewAppContext(Ecole);

        var act = async () => await NewHandler(ctx).Handle(
            new RecordPaymentCommand(Inscription, 10_000m, PaymentMethod.Cash), CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}

file sealed class FixedTenantProvider(Guid schoolId) : ITenantProvider
{
    public Guid? CurrentSchoolId => schoolId;
}

file sealed class FixedCurrentUser(Guid userId) : ICurrentUserService
{
    public Guid? UserId => userId;
    public Role? Role => SamaEcole.Domain.Enums.Role.Finance;
    public string? IpAddress => null;
}

file sealed class FakeSmsDispatcher : ISmsDispatcher
{
    public Task<SmsDispatchOutcome> DispatchAsync(SmsDispatchRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(SmsDispatchOutcome.Skipped("Désactivé dans ce test."));
}

file sealed class FakeKpiCacheService : IKpiCacheService
{
    public Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken) =>
        factory(cancellationToken);

    public void Invalidate(string key) { }
}
````


#### `tests/SamaEcole.IntegrationTests/Finance/PaymentLedgerTests.cs`

````csharp
using FluentAssertions;
using SamaEcole.Application.Finance;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Finance;

/// <summary>
/// Registre daté des encaissements (spec 2026-10-17, §2.4) : une erreur de caisse se corrige sans réécrire le passé
/// ni faire disparaître la trace. Les versements sont insérés directement avec leurs dates (aucune horloge simulée).
/// </summary>
[Trait("Category", "MultiTenant")]
public class PaymentLedgerTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("11111111-7777-1111-1111-111111111111");
    private static readonly Guid Classe = Guid.Parse("aaaaaaaa-7777-0000-0000-00000000000a");
    private static readonly Guid Annee = Guid.Parse("bbbbbbbb-7777-0000-0000-00000000000b");
    private static readonly Guid Eleve = Guid.Parse("eeeeeeee-7777-0000-0000-00000000000e");
    private static readonly Guid Inscription = Guid.Parse("11111111-7777-0000-0000-0000000000f1");
    private static readonly Guid Caissier = Guid.Parse("dddddddd-7777-0000-0000-00000000000d");
    private static readonly Guid SessionLundi = Guid.Parse("cccccccc-7777-0000-0000-0000000000c1");
    private static readonly Guid SessionMardi = Guid.Parse("cccccccc-7777-0000-0000-0000000000c2");

    private static readonly DateTimeOffset Lundi = new(2026, 10, 12, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Mardi = Lundi.AddDays(1);
    private static readonly DateTimeOffset Mercredi = Lundi.AddDays(2);

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();
        owner.Schools.Add(new School { Id = Ecole, Name = "École A" });
        owner.Classrooms.Add(new Classroom { Id = Classe, SchoolId = Ecole, Name = "CM2", Level = "Primaire", Capacity = 40 });
        owner.SchoolYears.Add(new SchoolYear
        {
            Id = Annee, SchoolId = Ecole, Label = "2026-2027",
            StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true
        });
        owner.Students.Add(new Student
        {
            Id = Eleve, SchoolId = Ecole, Matricule = "ELEV-2026-0001", FullName = "Awa Fall",
            BirthDate = new DateOnly(2015, 5, 20), BirthPlace = "Dakar", Gender = "F", ClassroomId = Classe
        });
        owner.Enrollments.Add(new Enrollment
        {
            Id = Inscription, SchoolId = Ecole, StudentId = Eleve, SchoolYearId = Annee, ClassroomId = Classe,
            Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed,
            TotalDue = 500_000m, AmountPaid = 0m, ReceiptNumber = "REC-2026-0001", EnrolledAt = Lundi
        });
        owner.Users.Add(new User
        {
            Id = Caissier, SchoolId = Ecole, Email = "c@ecole-a.sn", PasswordHash = "h", FullName = "Caissier", Role = Role.Finance
        });
        // Lundi : session clôturée en fin de journée. Mardi : session ouverte (celle où l'on annule).
        owner.CashierSessions.Add(new CashierSession
        {
            Id = SessionLundi, SchoolId = Ecole, CashierId = Caissier, Status = CashierSessionStatus.Closed,
            OpenedAt = Lundi.AddHours(8), ClosedAt = Lundi.AddHours(18), OpeningBalance = 0m
        });
        owner.CashierSessions.Add(new CashierSession
        {
            Id = SessionMardi, SchoolId = Ecole, CashierId = Caissier, Status = CashierSessionStatus.Open,
            OpenedAt = Mardi.AddHours(8), OpeningBalance = 0m
        });
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private async Task<Guid> AddPaymentAsync(
        string receipt, decimal amount, DateTimeOffset paidAt, Guid? sessionId,
        PaymentMethod method = PaymentMethod.Cash, decimal vat = 0m,
        DateTimeOffset? cancelledAt = null, Guid? cancellationSessionId = null,
        PaymentCancellationOutcome outcome = PaymentCancellationOutcome.KeptBySchool)
    {
        await using var owner = _db.NewOwnerContext();
        var payment = new Payment
        {
            SchoolId = Ecole, EnrollmentId = Inscription, CashierSessionId = sessionId, Amount = amount,
            VatAmount = vat, Method = method, Status = PaymentStatus.Partial, BalanceAfter = 0m,
            ReceiptNumber = receipt, ReceivedByUserId = Caissier, PaidAt = paidAt
        };
        if (cancelledAt is not null)
        {
            payment.Status = PaymentStatus.Cancelled;
            payment.CancelledAt = cancelledAt;
            payment.CancelledByUserId = Caissier;
            payment.CancellationReason = "Erreur de saisie";
            payment.CancellationOutcome = outcome;
            payment.CancellationSessionId = cancellationSessionId;
        }
        owner.Payments.Add(payment);
        await owner.SaveChangesAsync(CancellationToken.None);
        return payment.Id;
    }

    private async Task<List<PaymentLedgerEntry>> DayAsync(DateTimeOffset day)
    {
        await using var ctx = _db.NewAppContext(Ecole);
        return await PaymentLedger.EntriesAsync(ctx, day, day.AddDays(1), CancellationToken.None);
    }

    [Fact]
    public async Task A_Payment_Cancelled_The_Next_Day_Stays_In_Its_Original_Day_And_Is_Flagged()
    {
        var p = await AddPaymentAsync("REC-1", 50_000m, Lundi.AddHours(10), SessionLundi,
            cancelledAt: Mardi.AddHours(9), cancellationSessionId: SessionMardi);

        var lundi = await DayAsync(Lundi);

        var entry = lundi.Should().ContainSingle().Subject;
        entry.PaymentId.Should().Be(p);
        entry.Amount.Should().Be(50_000m, "les chiffres de lundi restent ceux de lundi");
        entry.IsReversal.Should().BeFalse();
        entry.CancelledLater.Should().BeTrue("le journal de lundi doit dire que ce paiement a été annulé depuis");
        entry.CancellationReason.Should().Be("Erreur de saisie");
    }

    [Fact]
    public async Task The_Cancellation_Is_A_Negative_Line_In_The_Day_It_Is_Made()
    {
        var p = await AddPaymentAsync("REC-1", 50_000m, Lundi.AddHours(10), SessionLundi,
            vat: 9_000m, cancelledAt: Mardi.AddHours(9), cancellationSessionId: SessionMardi);

        var mardi = await DayAsync(Mardi);

        var entry = mardi.Should().ContainSingle().Subject;
        entry.PaymentId.Should().Be(p);
        entry.IsReversal.Should().BeTrue();
        entry.Amount.Should().Be(-50_000m);
        entry.VatAmount.Should().Be(-9_000m, "la TVA collectée est reprise avec le versement annulé");
        entry.At.Should().Be(Mardi.AddHours(9));
    }

    [Fact]
    public async Task A_Payment_Cancelled_The_Same_Day_Disappears_Without_A_Reversal()
    {
        await AddPaymentAsync("REC-1", 50_000m, Lundi.AddHours(10), SessionLundi, cancelledAt: Lundi.AddHours(11));

        (await DayAsync(Lundi)).Should().BeEmpty("l'erreur et sa correction se compensent dans la même journée");
        (await DayAsync(Mardi)).Should().BeEmpty();
    }

    [Fact]
    public async Task Net_Total_Over_The_Two_Days_Is_Zero_Once_Cancelled_And_Each_Day_Is_Coherent()
    {
        await AddPaymentAsync("REC-1", 50_000m, Lundi.AddHours(10), SessionLundi,
            cancelledAt: Mardi.AddHours(9), cancellationSessionId: SessionMardi);

        await using var ctx = _db.NewAppContext(Ecole);
        var lundi = await PaymentLedger.NetAsync(ctx, Lundi, Mardi, p => p.Amount, CancellationToken.None);
        var mardi = await PaymentLedger.NetAsync(ctx, Mardi, Mercredi, p => p.Amount, CancellationToken.None);
        var deuxJours = await PaymentLedger.NetAsync(ctx, Lundi, Mercredi, p => p.Amount, CancellationToken.None);

        lundi.Should().Be(50_000m);
        mardi.Should().Be(-50_000m);
        deuxJours.Should().Be(0m, "sur toute la période, le paiement annulé ne compte plus — rien n'est compté deux fois");
    }

    [Fact]
    public async Task The_Closed_Monday_Session_Keeps_The_Payment_And_The_Open_Tuesday_Session_Carries_The_Reversal()
    {
        await AddPaymentAsync("REC-1", 50_000m, Lundi.AddHours(10), SessionLundi,
            cancelledAt: Mardi.AddHours(9), cancellationSessionId: SessionMardi);
        await AddPaymentAsync("REC-2", 20_000m, Mardi.AddHours(10), SessionMardi);

        await using var ctx = _db.NewAppContext(Ecole);
        var lundiSession = ctx.CashierSessions.Single(s => s.Id == SessionLundi);
        var mardiSession = ctx.CashierSessions.Single(s => s.Id == SessionMardi);

        var lundi = await PaymentLedger.SessionEntriesAsync(ctx, lundiSession, CancellationToken.None);
        lundi.Should().ContainSingle();
        lundi[0].Amount.Should().Be(50_000m);
        lundi[0].CancelledLater.Should().BeTrue();

        var mardi = await PaymentLedger.SessionEntriesAsync(ctx, mardiSession, CancellationToken.None);
        mardi.Should().HaveCount(2);
        mardi.Sum(e => e.Amount).Should().Be(20_000m - 50_000m);
        mardi.Single(e => e.IsReversal).ReceiptNumber.Should().Be("REC-1");
    }

    [Fact]
    public async Task Cancelling_A_Payment_Of_A_Session_Still_Open_Is_Not_Counted_Twice()
    {
        // Le versement de mardi, annulé mardi alors que SA session est encore ouverte : il sort de cette session,
        // et ne doit PAS réapparaître en négatif dans la session de l'auteur (qui est ici la même).
        await AddPaymentAsync("REC-2", 20_000m, Mardi.AddHours(10), SessionMardi,
            cancelledAt: Mardi.AddHours(11), cancellationSessionId: SessionMardi);

        await using var ctx = _db.NewAppContext(Ecole);
        var mardiSession = ctx.CashierSessions.Single(s => s.Id == SessionMardi);

        (await PaymentLedger.SessionEntriesAsync(ctx, mardiSession, CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_Cancellation_Without_An_Open_Session_Is_In_The_Day_Statements_But_In_No_Session()
    {
        await AddPaymentAsync("REC-1", 50_000m, Lundi.AddHours(10), SessionLundi,
            cancelledAt: Mardi.AddHours(9), cancellationSessionId: null);

        (await DayAsync(Mardi)).Should().ContainSingle(e => e.IsReversal);

        await using var ctx = _db.NewAppContext(Ecole);
        var mardiSession = ctx.CashierSessions.Single(s => s.Id == SessionMardi);
        (await PaymentLedger.SessionEntriesAsync(ctx, mardiSession, CancellationToken.None)).Should().BeEmpty();
    }
}
````


#### `tests/SamaEcole.IntegrationTests/Finance/PayrollIsolationTests.cs`

````csharp
using FluentAssertions;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Finance.Commands.CreateEmployeeContract;
using SamaEcole.Application.Finance.Queries.GetEmployeeContracts;
using SamaEcole.Application.Finance.Queries.GetFichePaies;
using SamaEcole.Application.Finance.Queries.GetTaxDeclarations;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace SamaEcole.IntegrationTests.Finance;

/// <summary>
/// Module Comptabilité & Fiscalité (JGK) — CRUD EmployeeContract et lectures FichePaie/TaxeDeclaration.
///
/// Deux garanties critiques, exercées contre un vrai PostgreSQL (RLS active) :
/// - Un contrat lié à un enseignant/utilisateur d'une AUTRE école ne doit jamais pouvoir être créé
///   (le filtre EF + RLS le rend invisible, donc introuvable — <see cref="NotFoundException"/>).
/// - Aucune fuite entre écoles sur les listes de contrats, fiches de paie ou déclarations fiscales.
/// </summary>
[Trait("Category", "MultiTenant")]
public class PayrollIsolationTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid EnseignantA = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid EnseignantB = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");
    private static readonly Guid ContratA = Guid.Parse("cccccccc-0000-0000-0000-00000000000a");
    private static readonly Guid ContratB = Guid.Parse("cccccccc-0000-0000-0000-00000000000b");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();

        owner.Schools.AddRange(
            new School { Id = EcoleA, Name = "École A" },
            new School { Id = EcoleB, Name = "École B" });

        owner.Teachers.AddRange(
            new Teacher { Id = EnseignantA, SchoolId = EcoleA, Matricule = "ENS-A-0001", FullName = "Awa Fall", Email = "awa@ecole-a.sn", BirthDate = new DateOnly(1985, 4, 12) },
            new Teacher { Id = EnseignantB, SchoolId = EcoleB, Matricule = "ENS-B-0001", FullName = "Moussa Diop", Email = "moussa@ecole-b.sn", BirthDate = new DateOnly(1980, 1, 5) });

        owner.EmployeeContracts.AddRange(
            new EmployeeContract { Id = ContratA, SchoolId = EcoleA, TeacherId = EnseignantA, Type = ContractType.Permanent, BaseSalary = 250_000m, HourlyRate = 0m, TransportAllowance = 15_000m },
            new EmployeeContract { Id = ContratB, SchoolId = EcoleB, TeacherId = EnseignantB, Type = ContractType.Permanent, BaseSalary = 400_000m, HourlyRate = 0m, TransportAllowance = 20_000m });

        owner.FichePaies.AddRange(
            PayrollCalculator(ContratA, EcoleA, 3, 2026, 250_000m),
            PayrollCalculator(ContratB, EcoleB, 3, 2026, 400_000m));

        owner.TaxeDeclarations.AddRange(
            new TaxeDeclaration { SchoolId = EcoleA, Month = 3, Year = 2026, TotalIpres = 1_000m, TotalCss = 500m, TotalVrs = 300m, TotalBrs = 200m, TvaCollected = 0m, TvaDeductible = 0m, NetTva = 0m, TotalDueToState = 500m },
            new TaxeDeclaration { SchoolId = EcoleB, Month = 3, Year = 2026, TotalIpres = 9_000m, TotalCss = 4_500m, TotalVrs = 2_700m, TotalBrs = 1_800m, TvaCollected = 0m, TvaDeductible = 0m, NetTva = 0m, TotalDueToState = 4_500m });

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private static FichePaie PayrollCalculator(Guid contractId, Guid schoolId, int month, int year, decimal baseSalary) =>
        Application.Finance.Services.PayrollCalculator.CalculateFichePaie(schoolId, contractId, month, year, baseSalary, 0m, 0m, 0m);

    [Fact]
    public async Task Creating_A_Contract_For_Another_Schools_Teacher_Is_Rejected_As_Not_Found()
    {
        // EnseignantB appartient à l'École B : sous le rôle applicatif de l'École A, RLS + filtre EF le
        // rendent invisible — la commande doit échouer par NotFoundException, jamais créer le contrat.
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new CreateEmployeeContractCommandHandler(ctx, new FixedTenantProvider(EcoleA));

        var command = new CreateEmployeeContractCommand(EnseignantB, null, ContractType.Permanent, 200_000m, 0m, 0m);
        var act = async () => await handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Employee_Contracts_List_Never_Leaks_Another_Schools_Contract()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new GetEmployeeContractsQueryHandler(ctx);

        var result = await handler.Handle(new GetEmployeeContractsQuery(), CancellationToken.None);

        result.Should().ContainSingle().Which.EmployeeFullName.Should().Be("Awa Fall");
        result.Should().NotContain(c => c.Id == ContratB, "le contrat de l'École B ne doit jamais apparaître");
    }

    [Fact]
    public async Task FichePaies_List_Never_Leaks_Another_Schools_Payslip()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new GetFichePaiesQueryHandler(ctx);

        var result = await handler.Handle(new GetFichePaiesQuery(Month: 3, Year: 2026), CancellationToken.None);

        result.Should().ContainSingle().Which.EmployeeFullName.Should().Be("Awa Fall");
        result.Single().GrossSalary.Should().Be(250_000m);
    }

    [Fact]
    public async Task Tax_Declarations_List_Never_Leaks_Another_Schools_Declaration()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new GetTaxDeclarationsQueryHandler(ctx);

        var result = await handler.Handle(new GetTaxDeclarationsQuery(Year: 2026), CancellationToken.None);

        result.Should().ContainSingle().Which.TotalDueToState.Should().Be(500m,
            "500 est le total de l'École A ; 4 500 (École B) ne doit jamais apparaître");
    }
}

/// <summary>Fournit un SchoolId fixe, sans passer par le contexte HTTP — suffisant pour un handler appelé directement en test.</summary>
file sealed class FixedTenantProvider(Guid schoolId) : Application.Common.Interfaces.ITenantProvider
{
    public Guid? CurrentSchoolId => schoolId;
}
````


#### `tests/SamaEcole.IntegrationTests/Finance/PayslipCorrectionTests.cs`

````csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Commands.CancelFichePaie;
using SamaEcole.Application.Finance.Commands.CreateEmployeeContract;
using SamaEcole.Application.Finance.Commands.GenerateFichePaie;
using SamaEcole.Application.Finance.Commands.PayFichePaie;
using SamaEcole.Application.Finance.Commands.RegenerateFichePaie;
using SamaEcole.Application.Finance.Commands.UpdateEmployeeContract;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Finance;

/// <summary>
/// Volume 1 §14.5 à §14.7 — annulation / régénération d'une fiche de paie, règlement en trésorerie et personnel
/// sans compte, sur une vraie base (RLS, verrou xmin, index unique partiel). Une fiche n'est jamais supprimée :
/// elle s'annule, et une fiche payée se contre-passe en trésorerie.
/// </summary>
[Trait("Category", "MultiTenant")]
public class PayslipCorrectionTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("11111111-4444-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("22222222-4444-1111-1111-111111111112");
    private static readonly Guid UtilisateurA = Guid.Parse("aaaaaaaa-4444-0000-0000-00000000000a");
    private static readonly Guid ContratA = Guid.Parse("cccccccc-4444-0000-0000-00000000000a");

    private Guid _actorId;

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();

        owner.Schools.AddRange(
            new School { Id = EcoleA, Name = "École A" },
            new School { Id = EcoleB, Name = "École B" });

        var directeur = new User
        {
            SchoolId = EcoleA, Email = "directeur.a@ecole-a.sn", PasswordHash = "hash",
            FullName = "Directeur A", Role = Role.Directeur
        };
        owner.Users.Add(directeur);
        owner.Users.Add(new User
        {
            Id = UtilisateurA, SchoolId = EcoleA, Email = "employe.a@ecole-a.sn", PasswordHash = "hash",
            FullName = "Fatou Employée", Role = Role.Secretariat
        });
        owner.EmployeeContracts.Add(new EmployeeContract
        {
            Id = ContratA, SchoolId = EcoleA, UserId = UtilisateurA,
            Type = ContractType.Permanent, BaseSalary = 250_000m, HourlyRate = 0m, TransportAllowance = 0m
        });

        await owner.SaveChangesAsync(CancellationToken.None);
        _actorId = directeur.Id;
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    // ------------------------------------------------------------------ aides

    private async Task<Guid> GenerateAsync(Guid contractId = default, int month = 9, int year = 2026)
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new GenerateFichePaieCommandHandler(ctx, new FixedTenantProvider(EcoleA));
        return await handler.Handle(
            new GenerateFichePaieCommand(contractId == default ? ContratA : contractId, month, year),
            CancellationToken.None);
    }

    private async Task<uint> FicheVersionAsync(Guid ficheId, Guid? school = null)
    {
        await using var ctx = _db.NewAppContext(school ?? EcoleA);
        return await ctx.FichePaies.AsNoTracking().Where(f => f.Id == ficheId)
            .Select(f => EF.Property<uint>(f, "xmin")).SingleAsync();
    }

    private async Task PayAsync(Guid ficheId, PaymentMethod method = PaymentMethod.Cash)
    {
        var version = await FicheVersionAsync(ficheId);
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new PayFichePaieCommandHandler(
            ctx, new FixedTenantProvider(EcoleA), new FixedCurrentUser(_actorId), TimeProvider.System, new NoKpiCache());
        await handler.Handle(new PayFichePaieCommand(ficheId, method, new DateOnly(2026, 9, 30), version), CancellationToken.None);
    }

    private async Task<decimal> NetDisbursementsAsync()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        return await ctx.Disbursements.AsNoTracking().SumAsync(d => d.Amount);
    }

    // ------------------------------------------------------------------ annulation

    [Fact]
    public async Task Cancelling_An_Unpaid_Payslip_Should_Keep_The_Row_And_Free_The_Period()
    {
        var ficheId = await GenerateAsync();
        var version = await FicheVersionAsync(ficheId);

        await using (var ctx = _db.NewAppContext(EcoleA))
        {
            var handler = new CancelFichePaieCommandHandler(ctx, new FixedCurrentUser(_actorId), TimeProvider.System, new NoKpiCache());
            var result = await handler.Handle(new CancelFichePaieCommand(ficheId, "Montant erroné sur le contrat.", version), CancellationToken.None);
            result.PaymentReversed.Should().BeFalse();
        }

        await using var check = _db.NewAppContext(EcoleA);
        var fiche = await check.FichePaies.AsNoTracking().SingleAsync(f => f.Id == ficheId);
        fiche.IsCancelled.Should().BeTrue();
        fiche.CancelledByUserId.Should().Be(_actorId);
        fiche.CancellationReason.Should().Be("Montant erroné sur le contrat.");
        fiche.IsDeleted.Should().BeFalse("une fiche annulée est conservée, jamais supprimée");

        // La période est libérée : une nouvelle fiche active peut être générée pour le même mois.
        var again = async () => await GenerateAsync();
        await again.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Cancelling_Twice_Or_Generating_A_Duplicate_Active_Payslip_Should_Be_Refused()
    {
        var ficheId = await GenerateAsync();

        var duplicate = async () => await GenerateAsync();
        await duplicate.Should().ThrowAsync<BusinessRuleException>();

        var version = await FicheVersionAsync(ficheId);
        await using (var ctx = _db.NewAppContext(EcoleA))
        {
            var handler = new CancelFichePaieCommandHandler(ctx, new FixedCurrentUser(_actorId), TimeProvider.System, new NoKpiCache());
            await handler.Handle(new CancelFichePaieCommand(ficheId, "Première annulation.", version), CancellationToken.None);
        }

        await using var ctx2 = _db.NewAppContext(EcoleA);
        var handler2 = new CancelFichePaieCommandHandler(ctx2, new FixedCurrentUser(_actorId), TimeProvider.System, new NoKpiCache());
        var version2 = await FicheVersionAsync(ficheId);
        var twice = async () => await handler2.Handle(new CancelFichePaieCommand(ficheId, "Seconde annulation.", version2), CancellationToken.None);
        await twice.Should().ThrowAsync<BusinessRuleException>().WithMessage("*déjà annulée*");
    }

    [Fact]
    public async Task Cancelling_With_A_Stale_RowVersion_Should_Throw_A_Concurrency_Conflict()
    {
        var ficheId = await GenerateAsync();
        var stale = await FicheVersionAsync(ficheId);
        await PayAsync(ficheId); // fait avancer xmin

        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new CancelFichePaieCommandHandler(ctx, new FixedCurrentUser(_actorId), TimeProvider.System, new NoKpiCache());
        var act = async () => await handler.Handle(new CancelFichePaieCommand(ficheId, "Jeton périmé.", stale), CancellationToken.None);
        await act.Should().ThrowAsync<ConcurrencyConflictException>();
    }

    [Fact]
    public async Task Another_School_Cannot_Cancel_The_Payslip()
    {
        var ficheId = await GenerateAsync();
        var version = await FicheVersionAsync(ficheId);

        await using var ctx = _db.NewAppContext(EcoleB);
        var handler = new CancelFichePaieCommandHandler(ctx, new FixedCurrentUser(_actorId), TimeProvider.System, new NoKpiCache());
        var act = async () => await handler.Handle(new CancelFichePaieCommand(ficheId, "Tentative inter-école.", version), CancellationToken.None);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Cancelling_Should_Be_Refused_Once_A_Tax_Declaration_Exists_For_The_Period()
    {
        var ficheId = await GenerateAsync();
        await using (var owner = _db.NewOwnerContext())
        {
            owner.TaxeDeclarations.Add(new TaxeDeclaration { SchoolId = EcoleA, Month = 9, Year = 2026 });
            await owner.SaveChangesAsync(CancellationToken.None);
        }

        var version = await FicheVersionAsync(ficheId);
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new CancelFichePaieCommandHandler(ctx, new FixedCurrentUser(_actorId), TimeProvider.System, new NoKpiCache());
        var act = async () => await handler.Handle(new CancelFichePaieCommand(ficheId, "Après déclaration.", version), CancellationToken.None);
        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "PAYSLIP_PERIOD_DECLARED");
    }

    // ------------------------------------------------------------------ régénération

    [Fact]
    public async Task Regenerating_After_A_Contract_Correction_Should_Replace_The_Payslip_With_The_Right_Amount()
    {
        var ficheId = await GenerateAsync();

        // Le directeur corrige le montant du contrat (250 000 -> 180 000).
        await using (var ctx = _db.NewAppContext(EcoleA))
        {
            var contractVersion = await ctx.EmployeeContracts.AsNoTracking().Where(c => c.Id == ContratA)
                .Select(c => EF.Property<uint>(c, "xmin")).SingleAsync();
            var update = new UpdateEmployeeContractCommandHandler(
                ctx, new FixedTenantProvider(EcoleA), new FixedCurrentUser(_actorId), TimeProvider.System);
            await update.Handle(
                new UpdateEmployeeContractCommand(ContratA, 180_000m, 0m, 0m, "Erreur de saisie du montant.", contractVersion),
                CancellationToken.None);
        }

        var version = await FicheVersionAsync(ficheId);
        Guid newId;
        await using (var ctx = _db.NewAppContext(EcoleA))
        {
            var handler = new RegenerateFichePaieCommandHandler(
                ctx, new FixedTenantProvider(EcoleA), new FixedCurrentUser(_actorId), TimeProvider.System, new NoKpiCache());
            var result = await handler.Handle(new RegenerateFichePaieCommand(ficheId, "Montant du contrat corrigé.", version), CancellationToken.None);
            newId = result.NewFichePaieId;
        }

        await using var check = _db.NewAppContext(EcoleA);
        var old = await check.FichePaies.AsNoTracking().SingleAsync(f => f.Id == ficheId);
        var replacement = await check.FichePaies.AsNoTracking().SingleAsync(f => f.Id == newId);

        old.IsCancelled.Should().BeTrue();
        old.ReplacedByFichePaieId.Should().Be(newId);
        replacement.IsCancelled.Should().BeFalse();
        replacement.GrossSalary.Should().Be(180_000m);
        replacement.GrossSalary.Should().NotBe(old.GrossSalary);
        (replacement.Month, replacement.Year).Should().Be((old.Month, old.Year));
    }

    // ------------------------------------------------------------------ règlement et trésorerie

    [Fact]
    public async Task Paying_A_Payslip_Should_Create_A_Salaires_Disbursement_For_The_Net_Amount()
    {
        var ficheId = await GenerateAsync();
        await PayAsync(ficheId, PaymentMethod.MobileMoney);

        await using var check = _db.NewAppContext(EcoleA);
        var fiche = await check.FichePaies.AsNoTracking().SingleAsync(f => f.Id == ficheId);
        fiche.IsPaid.Should().BeTrue();
        fiche.PaidWith.Should().Be(PaymentMethod.MobileMoney);

        var disbursement = await check.Disbursements.AsNoTracking().SingleAsync(d => d.Id == fiche.DisbursementId);
        disbursement.Category.Should().Be(DisbursementCategory.Salaires);
        disbursement.Amount.Should().Be(fiche.NetSalary);
        disbursement.Beneficiary.Should().Be("Fatou Employée");
        disbursement.VatAmount.Should().Be(0m);
    }

    [Fact]
    public async Task Paying_Twice_Should_Be_Refused_And_Not_Duplicate_The_Disbursement()
    {
        var ficheId = await GenerateAsync();
        await PayAsync(ficheId);

        var again = async () => await PayAsync(ficheId);
        await again.Should().ThrowAsync<BusinessRuleException>().WithMessage("*déjà été payée*");

        await using var check = _db.NewAppContext(EcoleA);
        (await check.Disbursements.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Cancelling_A_Paid_Payslip_Should_Reverse_The_Disbursement_And_Net_To_Zero()
    {
        var ficheId = await GenerateAsync();
        await PayAsync(ficheId);
        (await NetDisbursementsAsync()).Should().BeGreaterThan(0m);

        var version = await FicheVersionAsync(ficheId);
        await using (var ctx = _db.NewAppContext(EcoleA))
        {
            var handler = new CancelFichePaieCommandHandler(ctx, new FixedCurrentUser(_actorId), TimeProvider.System, new NoKpiCache());
            var result = await handler.Handle(new CancelFichePaieCommand(ficheId, "Payée par erreur.", version), CancellationToken.None);
            result.PaymentReversed.Should().BeTrue();
        }

        (await NetDisbursementsAsync()).Should().Be(0m);

        await using var check = _db.NewAppContext(EcoleA);
        var rows = await check.Disbursements.AsNoTracking().ToListAsync();
        rows.Should().HaveCount(2, "l'écriture d'origine est conservée, une contre-écriture s'y ajoute");
        rows.Single(d => d.ReversalOfId != null).Amount.Should().BeNegative();
    }

    [Fact]
    public async Task Paying_A_Cancelled_Payslip_Should_Be_Refused()
    {
        var ficheId = await GenerateAsync();
        var version = await FicheVersionAsync(ficheId);
        await using (var ctx = _db.NewAppContext(EcoleA))
        {
            var handler = new CancelFichePaieCommandHandler(ctx, new FixedCurrentUser(_actorId), TimeProvider.System, new NoKpiCache());
            await handler.Handle(new CancelFichePaieCommand(ficheId, "Fiche erronée.", version), CancellationToken.None);
        }

        var act = async () => await PayAsync(ficheId);
        await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("*annulée*");
    }

    // ------------------------------------------------------------------ personnel sans compte

    [Fact]
    public async Task A_Staff_Member_Without_Account_Can_Be_Contracted_Paid_And_Appears_In_Treasury()
    {
        Guid contractId;
        await using (var ctx = _db.NewAppContext(EcoleA))
        {
            var handler = new CreateEmployeeContractCommandHandler(ctx, new FixedTenantProvider(EcoleA));
            contractId = await handler.Handle(
                new CreateEmployeeContractCommand(
                    null, null, ContractType.Permanent, 60_000m, 0m, 0m,
                    ExternalFullName: "Moussa Diop", ExternalJobTitle: "Gardien", ExternalPhone: "770000000"),
                CancellationToken.None);
        }

        var ficheId = await GenerateAsync(contractId);
        await PayAsync(ficheId);

        await using var check = _db.NewAppContext(EcoleA);
        var disbursement = await check.Disbursements.AsNoTracking().SingleAsync();
        disbursement.Beneficiary.Should().Be("Moussa Diop");
        disbursement.Category.Should().Be(DisbursementCategory.Salaires);
        disbursement.Reason.Should().Contain("Moussa Diop");

        // Aucun utilisateur n'a été créé pour lui.
        (await check.Users.CountAsync(u => u.FullName == "Moussa Diop")).Should().Be(0);
    }

    [Fact]
    public async Task Creating_Two_Active_Contracts_For_The_Same_Staff_Without_Account_Should_Be_Refused()
    {
        async Task CreateAsync()
        {
            await using var ctx = _db.NewAppContext(EcoleA);
            var handler = new CreateEmployeeContractCommandHandler(ctx, new FixedTenantProvider(EcoleA));
            await handler.Handle(
                new CreateEmployeeContractCommand(null, null, ContractType.Permanent, 60_000m, 0m, 0m, ExternalFullName: "Moussa Diop"),
                CancellationToken.None);
        }

        await CreateAsync();
        var act = async () => await CreateAsync();
        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public void A_Contract_Must_Target_Exactly_One_Kind_Of_Person()
    {
        var validator = new CreateEmployeeContractCommandValidator();

        validator.Validate(new CreateEmployeeContractCommand(null, null, ContractType.Permanent, 1m, 0m, 0m, ExternalFullName: "Moussa Diop"))
            .IsValid.Should().BeTrue();
        validator.Validate(new CreateEmployeeContractCommand(Guid.NewGuid(), null, ContractType.Permanent, 1m, 0m, 0m, ExternalFullName: "Moussa Diop"))
            .IsValid.Should().BeFalse("enseignant ET personne sans compte : jamais les deux");
        validator.Validate(new CreateEmployeeContractCommand(null, null, ContractType.Permanent, 1m, 0m, 0m))
            .IsValid.Should().BeFalse("aucune personne désignée");
    }

    private sealed class NoKpiCache : IKpiCacheService
    {
        public Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken) =>
            factory(cancellationToken);

        public void Invalidate(string key) { }
    }
}

file sealed class FixedTenantProvider(Guid schoolId) : ITenantProvider
{
    public Guid? CurrentSchoolId => schoolId;
}

file sealed class FixedCurrentUser(Guid userId) : ICurrentUserService
{
    public Guid? UserId => userId;
    public Role? Role => SamaEcole.Domain.Enums.Role.Directeur;
    public string? IpAddress => null;
}
````


#### `tests/SamaEcole.IntegrationTests/SoftDelete/ImmutableRegistersTests.cs`

````csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Features.Disbursements;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using SamaEcole.Persistence.Migrations;
using Xunit;

namespace SamaEcole.IntegrationTests.SoftDelete;

/// <summary>
/// Conception soft delete 2026-10-01 §3.3 / §8 — les registres financiers, l'audit et les mouvements de stock
/// sont append-only pour l'application : la BASE (rôle <c>sama_ecole_app</c>, NOBYPASSRLS) refuse la suppression,
/// et une correction se fait par contre-écriture. Les privilèges sont lus et exercés avec le rôle APPLICATIF, pas
/// avec le propriétaire (qui contournerait tout).
/// </summary>
[Trait("Category", "MultiTenant")]
public class ImmutableRegistersTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("71111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("72222222-2222-2222-2222-222222222222");
    private static readonly Guid Financier = Guid.Parse("7f000000-0000-0000-0000-000000000001");

    /// <summary>Écritures comptables, preuves et journaux : jamais supprimables par l'application.</summary>
    public static TheoryData<string> ImmutableRegisters => new()
    {
        "payments", "payment_breakdowns", "subscription_payments", "fiche_paies", "taxe_declarations", "Disbursements",
        "audit_logs", "stock_movements", "fee_change_history", "employee_contract_histories", "user_status_history",
        "student_mutation_certificates",
    };

    /// <summary>Journaux strictement append-only : ni DELETE ni UPDATE.</summary>
    public static TheoryData<string> AppendOnlyLogs => new()
    {
        "audit_logs", "stock_movements", "fee_change_history", "employee_contract_histories", "user_status_history",
    };

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();
        owner.Schools.AddRange(new School { Id = EcoleA, Name = "École A" }, new School { Id = EcoleB, Name = "École B" });
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private async Task<bool> HasPrivilegeAsync(string table, string privilege)
    {
        await using var connection = new NpgsqlConnection(_db.AppConnectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT has_table_privilege('sama_ecole_app', format('public.%I', @t), @p)";
        cmd.Parameters.AddWithValue("t", table);
        cmd.Parameters.AddWithValue("p", privilege);
        return (bool)(await cmd.ExecuteScalarAsync())!;
    }

    /// <summary>
    /// <c>relkind IN ('r', 'p', 'v', 'm', 'f')</c> : tables ordinaires/partitionnées, vues, vues
    /// matérialisées, tables étrangères. <c>public_school_directory</c> (vue mono-table sur
    /// <c>schools</c>, "updatable" par défaut par PostgreSQL) doit être balayée elle aussi — une vue
    /// n'est pas une table, mais un DELETE à travers elle s'exécute avec les privilèges du
    /// propriétaire sur la table de base, et aurait échappé à une boucle limitée à 'r'/'p'.
    /// </summary>
    [Fact]
    public async Task App_Role_Can_Delete_Only_From_The_Technical_Allowlist()
    {
        await using var connection = new NpgsqlConnection(_db.AppConnectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT c.relname FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'public' AND c.relkind IN ('r', 'p', 'v', 'm', 'f')
              AND has_table_privilege('sama_ecole_app', c.oid, 'DELETE')
            ORDER BY c.relname
            """;

        var deletable = new List<string>();
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync()) deletable.Add(reader.GetString(0));
        }

        deletable.Should().BeEquivalentTo(RevokeDeleteOnBusinessTables.TechnicalTablesKeepingDelete,
            "toute nouvelle table supprimable par l'application doit être une décision explicite, jamais un héritage");
    }

    /// <summary>
    /// TRUNCATE est un privilège DISTINCT de DELETE qui ignore la RLS : même la liste blanche technique
    /// (jetons, compteur de séquence) n'a aucune raison métier de le garder. Aucune table ne doit
    /// l'avoir — contrairement à DELETE, cette liste blanche est volontairement VIDE.
    /// </summary>
    [Fact]
    public async Task App_Role_Can_Truncate_No_Table()
    {
        await using var connection = new NpgsqlConnection(_db.AppConnectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT c.relname FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'public' AND c.relkind IN ('r', 'p', 'v', 'm', 'f')
              AND has_table_privilege('sama_ecole_app', c.oid, 'TRUNCATE')
            ORDER BY c.relname
            """;

        var truncatable = new List<string>();
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync()) truncatable.Add(reader.GetString(0));
        }

        truncatable.Should().BeEmpty("TRUNCATE ignore la RLS ; aucune table ne doit rester vidable par l'application");
    }

    /// <summary>
    /// Aucun chemin de code ne modifie jamais un décaissement (la correction est une contre-écriture,
    /// voir Cancelling_A_Disbursement_Adds_A_Negative_Reversal_And_Never_Touches_The_Original) : son
    /// immutabilité ne doit plus reposer sur une convention de code seule, mais sur la BASE elle-même.
    /// Payments et les autres registres gardent UPDATE : eux en ont légitimement besoin.
    /// </summary>
    [Fact]
    public async Task Disbursements_Cannot_Be_Updated_By_The_Application_Role()
    {
        (await HasPrivilegeAsync("Disbursements", "UPDATE")).Should().BeFalse();

        await using var connection = await _db.OpenRawAppConnectionAsync(EcoleA);
        await using var update = connection.CreateCommand();
        update.CommandText = "UPDATE \"Disbursements\" SET \"Reason\" = 'x' WHERE false";

        var act = async () => await update.ExecuteNonQueryAsync();

        (await act.Should().ThrowAsync<PostgresException>()).Which.SqlState
            .Should().Be(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Theory]
    [MemberData(nameof(ImmutableRegisters))]
    public async Task Immutable_Registers_Cannot_Be_Deleted_By_The_Application_Role(string table)
    {
        (await HasPrivilegeAsync(table, "DELETE")).Should().BeFalse();

        await using var connection = await _db.OpenRawAppConnectionAsync(EcoleA);
        await using var delete = connection.CreateCommand();
        delete.CommandText = $"DELETE FROM \"{table}\" WHERE false";

        var act = async () => await delete.ExecuteNonQueryAsync();

        (await act.Should().ThrowAsync<PostgresException>()).Which.SqlState
            .Should().Be(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Theory]
    [MemberData(nameof(AppendOnlyLogs))]
    public async Task Append_Only_Logs_Cannot_Be_Updated_Either(string table)
    {
        (await HasPrivilegeAsync(table, "UPDATE")).Should().BeFalse();
        (await HasPrivilegeAsync(table, "INSERT")).Should().BeTrue("un journal reste alimentable par l'application");
    }

    private async Task<Guid> SeedDisbursementAsync(Guid school, decimal amount, decimal vat)
    {
        await using var owner = _db.NewOwnerContext();
        var d = new Disbursement
        {
            SchoolId = school, Reason = "Achat de craies", Category = DisbursementCategory.Fournitures, Amount = amount,
            VatRate = 0.18m, VatAmount = vat, PaymentMethod = PaymentMethod.Cash, Date = new DateOnly(2026, 10, 1), Beneficiary = "Papeterie Sow"
        };
        owner.Disbursements.Add(d);
        await owner.SaveChangesAsync(CancellationToken.None);
        return d.Id;
    }

    private async Task CancelAsync(Guid school, Guid id)
    {
        await using var ctx = _db.NewAppContext(school);
        await new DeleteDisbursementCommandHandler(ctx, TimeProvider.System, new NoOpKpiCache())
            .Handle(new DeleteDisbursementCommand(id), CancellationToken.None);
    }

    [Fact]
    public async Task Cancelling_A_Disbursement_Adds_A_Negative_Reversal_And_Never_Touches_The_Original()
    {
        var id = await SeedDisbursementAsync(EcoleA, 10_000m, 1_525.42m);

        await CancelAsync(EcoleA, id);

        await using var owner = _db.NewOwnerContext();
        var rows = await owner.Disbursements.IgnoreQueryFilters().Where(d => d.SchoolId == EcoleA).ToListAsync();
        rows.Should().HaveCount(2);

        var original = rows.Single(d => d.Id == id);
        original.IsDeleted.Should().BeFalse("l'écriture d'origine n'est jamais supprimée");
        original.Amount.Should().Be(10_000m);
        original.ReversalOfId.Should().BeNull();

        var reversal = rows.Single(d => d.ReversalOfId == id);
        reversal.Amount.Should().Be(-10_000m);
        reversal.VatAmount.Should().Be(-1_525.42m);
        reversal.VatRate.Should().Be(0.18m);
        reversal.Category.Should().Be(DisbursementCategory.Fournitures);
        reversal.IsDeleted.Should().BeFalse();

        rows.Sum(d => d.Amount).Should().Be(0m, "les agrégats (solde, trésorerie) s'annulent");
        rows.Sum(d => d.VatAmount).Should().Be(0m, "la TVA déductible s'annule aussi");
    }

    [Fact]
    public async Task Cancelling_Twice_Or_Cancelling_A_Reversal_Is_Refused()
    {
        var id = await SeedDisbursementAsync(EcoleA, 5_000m, 0m);
        await CancelAsync(EcoleA, id);

        var twice = async () => await CancelAsync(EcoleA, id);
        (await twice.Should().ThrowAsync<BusinessRuleException>()).Which.Code
            .Should().Be(DeleteDisbursementCommandHandler.AlreadyReversedCode);

        Guid reversalId;
        await using (var owner = _db.NewOwnerContext())
        {
            reversalId = (await owner.Disbursements.IgnoreQueryFilters().SingleAsync(d => d.ReversalOfId == id)).Id;
        }

        var reversalOfReversal = async () => await CancelAsync(EcoleA, reversalId);
        (await reversalOfReversal.Should().ThrowAsync<BusinessRuleException>()).Which.Code
            .Should().Be(DeleteDisbursementCommandHandler.CannotReverseReversalCode);

        await using var check = _db.NewOwnerContext();
        (await check.Disbursements.IgnoreQueryFilters().CountAsync(d => d.SchoolId == EcoleA)).Should().Be(2);
    }

    [Fact]
    public async Task The_Database_Refuses_A_Second_Reversal_And_A_Positive_Reversal()
    {
        var id = await SeedDisbursementAsync(EcoleA, 3_000m, 0m);

        Disbursement Reversal(decimal amount) => new()
        {
            SchoolId = EcoleA, Reason = "x", Category = DisbursementCategory.Divers, Amount = amount,
            PaymentMethod = PaymentMethod.Cash, Date = new DateOnly(2026, 10, 2), Beneficiary = "y", ReversalOfId = id
        };

        await using (var first = _db.NewOwnerContext())
        {
            first.Disbursements.Add(Reversal(-3_000m));
            await first.SaveChangesAsync(CancellationToken.None);
        }

        await using (var second = _db.NewOwnerContext())
        {
            second.Disbursements.Add(Reversal(-3_000m));
            // IX_Disbursements_ReversalOfId est unique (filtrée sur ReversalOfId IS NOT NULL) : une seconde
            // contre-écriture viole 23505 unique_violation, qu'ApplicationDbContext.SaveChangesAsync traduit
            // en DuplicateRecordException (jamais un DbUpdateException ni un PostgresException nu jusqu'ici).
            await second.Invoking(c => c.SaveChangesAsync(CancellationToken.None)).Should().ThrowAsync<DuplicateRecordException>(
                "une seule contre-écriture par décaissement, même en concurrence");
        }

        var other = await SeedDisbursementAsync(EcoleA, 100m, 0m);
        await using var positive = _db.NewOwnerContext();
        var wrongSign = Reversal(+100m);
        wrongSign.ReversalOfId = other;
        positive.Disbursements.Add(wrongSign);
        // CK_disbursements_reversal_negative (ReversalOfId IS NULL OR Amount < 0) n'est pas traduite par
        // ApplicationDbContext (seule UniqueViolation l'est) : l'exception reste un DbUpdateException dont
        // l'InnerException est le PostgresException 23514 check_violation d'origine.
        var wrongSignAct = await positive.Invoking(c => c.SaveChangesAsync(CancellationToken.None)).Should().ThrowAsync<DbUpdateException>(
            "une contre-écriture est toujours en montant négatif");
        wrongSignAct.Which.InnerException.Should().BeOfType<PostgresException>()
            .Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task Another_Tenant_Cannot_Cancel_The_Disbursement()
    {
        var id = await SeedDisbursementAsync(EcoleA, 7_000m, 0m);

        var act = async () => await CancelAsync(EcoleB, id);

        await act.Should().ThrowAsync<NotFoundException>();
        await using var owner = _db.NewOwnerContext();
        (await owner.Disbursements.IgnoreQueryFilters().CountAsync(d => d.ReversalOfId == id)).Should().Be(0);
    }
}

file sealed class NoOpKpiCache : IKpiCacheService
{
    public Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken) =>
        factory(cancellationToken);

    public void Invalidate(string key) { }
}
````


#### `tests/SamaEcole.IntegrationTests/Deletion/InfraFeesInventoryPoliciesTests.cs`

````csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Buildings.Deletion;
using SamaEcole.Application.Common.Deletion;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Finance.Deletion;
using SamaEcole.Application.Inventory.Deletion;
using SamaEcole.Application.Rooms.Deletion;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Deletion;

/// <summary>
/// Suppression guidée des bâtiments, salles, catégories de frais, catégories et biens d'inventaire (spec 2026-10-17, §6) :
/// blocage financier, liens énumérés, rupture explicite (archivage ou détachement), liens non rompables, isolation.
/// </summary>
[Trait("Category", "MultiTenant")]
public class InfraFeesInventoryPoliciesTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("11111111-eeee-1111-1111-111111111111");
    private static readonly Guid AutreEcole = Guid.Parse("22222222-eeee-1111-1111-111111111111");
    private static readonly Guid Classe = Guid.Parse("aaaaaaaa-eeee-0000-0000-00000000000a");
    private static readonly Guid Annee = Guid.Parse("bbbbbbbb-eeee-0000-0000-00000000000b");
    private static readonly Guid Eleve = Guid.Parse("eeeeeeee-eeee-0000-0000-00000000000e");
    private static readonly Guid Inscription = Guid.Parse("11111111-eeee-0000-0000-0000000000f1");
    private static readonly Guid Caissier = Guid.Parse("dddddddd-eeee-0000-0000-00000000000d");
    private static readonly Guid Batiment = Guid.Parse("bb000000-eeee-0000-0000-000000000001");
    private static readonly Guid BatimentLibre = Guid.Parse("bb000000-eeee-0000-0000-000000000002");
    private static readonly Guid Salle = Guid.Parse("ff000000-eeee-0000-0000-000000000001");
    private static readonly Guid SalleLibre = Guid.Parse("ff000000-eeee-0000-0000-000000000002");
    private static readonly Guid Frais = Guid.Parse("fc000000-eeee-0000-0000-000000000001");
    private static readonly Guid FraisLibre = Guid.Parse("fc000000-eeee-0000-0000-000000000002");
    private static readonly Guid CategorieStock = Guid.Parse("cc000000-eeee-0000-0000-000000000001");
    private static readonly Guid CategorieStockLibre = Guid.Parse("cc000000-eeee-0000-0000-000000000002");
    private static readonly Guid Bien = Guid.Parse("1b000000-eeee-0000-0000-000000000001");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();
        owner.Schools.Add(new School { Id = Ecole, Name = "École A" });
        owner.Schools.Add(new School { Id = AutreEcole, Name = "École B" });
        owner.Classrooms.Add(new Classroom { Id = Classe, SchoolId = Ecole, Name = "CM2", Level = "Primaire", Capacity = 40 });
        owner.SchoolYears.Add(new SchoolYear
        {
            Id = Annee, SchoolId = Ecole, Label = "2026-2027",
            StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true
        });
        owner.Students.Add(new Student
        {
            Id = Eleve, SchoolId = Ecole, Matricule = "ELEV-2026-0001", FullName = "Awa Fall",
            BirthDate = new DateOnly(2015, 5, 20), BirthPlace = "Dakar", Gender = "F", ClassroomId = Classe
        });
        owner.Enrollments.Add(new Enrollment
        {
            Id = Inscription, SchoolId = Ecole, StudentId = Eleve, SchoolYearId = Annee, ClassroomId = Classe,
            Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed,
            TotalDue = 100_000m, AmountPaid = 0m, ReceiptNumber = "REC-2026-0001", EnrolledAt = DateTimeOffset.UtcNow
        });
        owner.Users.Add(new User
        {
            Id = Caissier, SchoolId = Ecole, Email = "c@ecole-a.sn", PasswordHash = "h", FullName = "Caissier", Role = Role.Finance
        });
        owner.Buildings.Add(new Building { Id = Batiment, SchoolId = Ecole, Name = "Bâtiment A" });
        owner.Buildings.Add(new Building { Id = BatimentLibre, SchoolId = Ecole, Name = "Bâtiment B" });
        owner.Rooms.Add(new Room { Id = Salle, SchoolId = Ecole, Name = "Salle 1", BuildingId = Batiment });
        owner.Rooms.Add(new Room { Id = SalleLibre, SchoolId = Ecole, Name = "Salle 2", BuildingId = BatimentLibre });
        owner.FeeCategories.Add(new FeeCategory { Id = Frais, SchoolId = Ecole, Name = "Scolarité" });
        owner.FeeCategories.Add(new FeeCategory { Id = FraisLibre, SchoolId = Ecole, Name = "Cantine" });
        owner.InventoryCategories.Add(new InventoryCategory { Id = CategorieStock, SchoolId = Ecole, Name = "Manuels" });
        owner.InventoryCategories.Add(new InventoryCategory { Id = CategorieStockLibre, SchoolId = Ecole, Name = "Mobilier" });
        owner.InventoryItems.Add(new InventoryItem
        {
            Id = Bien, SchoolId = Ecole, Name = "Manuel de maths", CategoryId = CategorieStock, QuantityTotal = 10, QuantityAvailable = 9
        });
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private async Task<DeletionImpact> AnalyzeAsync(EntityDeletionPolicy policy, Guid id, Guid? school = null)
    {
        await using var ctx = _db.NewAppContext(school ?? Ecole);
        return await EntityDeletionAnalyzer.AnalyzeAsync(ctx, policy, id, CancellationToken.None);
    }

    private async Task<DeletionImpact> UnlinkAsync(EntityDeletionPolicy policy, Guid id)
    {
        await using var ctx = _db.NewAppContext(Ecole);
        return await EntityDeletionAnalyzer.UnlinkAsync(ctx, policy, id, "directeur", CancellationToken.None);
    }

    // ------------------------------------------------------------------------------------------ Bâtiments

    [Fact]
    public async Task A_Building_With_Rooms_Lists_Them_As_Not_Breakable()
    {
        var impact = await AnalyzeAsync(BuildingDeletionPolicy.Policy, Batiment);

        impact.CanDelete.Should().BeFalse();
        impact.Links.Should().ContainSingle(l => l.Kind == "Room" && !l.Breakable).Which.Label.Should().Be("1 salle");
        impact.Message.Should().StartWith("Ce bâtiment est actuellement rattaché à 1 salle");

        var act = async () => await UnlinkAsync(BuildingDeletionPolicy.Policy, Batiment);
        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be(DeletionErrorCodes.DependenciesNotBreakable);
    }

    // ------------------------------------------------------------------------------------------ Salles

    [Fact]
    public async Task Unlinking_A_Room_Detaches_Its_Inventory_Items_Without_Archiving_Them()
    {
        await using (var owner = _db.NewOwnerContext())
        {
            var item = await owner.InventoryItems.IgnoreQueryFilters().SingleAsync(i => i.Id == Bien);
            item.RoomId = Salle;
            await owner.SaveChangesAsync(CancellationToken.None);
        }

        var before = await AnalyzeAsync(RoomDeletionPolicy.Policy, Salle);
        before.Links.Should().ContainSingle(l => l.Kind == "InventoryItem" && l.Breakable);

        var after = await UnlinkAsync(RoomDeletionPolicy.Policy, Salle);
        after.CanDelete.Should().BeTrue();

        await using var check = _db.NewAppContext(Ecole);
        var item2 = await check.InventoryItems.SingleAsync(i => i.Id == Bien);
        item2.IsDeleted.Should().BeFalse("le bien n'est ni archivé ni supprimé");
        item2.RoomId.Should().BeNull("seul son emplacement est vidé");
    }

    [Fact]
    public async Task A_Room_And_A_Building_Without_Links_Can_Be_Deleted()
    {
        (await AnalyzeAsync(RoomDeletionPolicy.Policy, SalleLibre)).CanDelete.Should().BeTrue();
        // Bâtiment B porte la salle libre : on la retire pour tester un bâtiment vide.
        await using (var owner = _db.NewOwnerContext())
        {
            var room = await owner.Rooms.IgnoreQueryFilters().SingleAsync(r => r.Id == SalleLibre);
            room.SoftDelete("directeur");
            await owner.SaveChangesAsync(CancellationToken.None);
        }
        (await AnalyzeAsync(BuildingDeletionPolicy.Policy, BatimentLibre)).CanDelete.Should().BeTrue();
    }

    // ------------------------------------------------------------------------------------------ Catégories de frais

    private async Task AddEnrollmentFeeLineAsync()
    {
        await using var owner = _db.NewOwnerContext();
        owner.EnrollmentFeeLines.Add(new EnrollmentFeeLine
        {
            SchoolId = Ecole, EnrollmentId = Inscription, FeeCategoryId = Frais, Designation = "Scolarité",
            UnitAmount = 100_000m, Months = 1, LineTotal = 100_000m
        });
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    private async Task AddBreakdownAsync(PaymentStatus status)
    {
        await using var owner = _db.NewOwnerContext();
        var payment = new Payment
        {
            Id = Guid.NewGuid(), SchoolId = Ecole, EnrollmentId = Inscription, Amount = 30_000m, Method = PaymentMethod.Cash,
            Status = status, BalanceAfter = 70_000m, ReceiptNumber = "REC-" + Guid.NewGuid().ToString()[..6],
            ReceivedByUserId = Caissier, PaidAt = DateTimeOffset.UtcNow
        };
        if (status == PaymentStatus.Cancelled)
        {
            payment.CancelledAt = DateTimeOffset.UtcNow;
            payment.CancellationOutcome = PaymentCancellationOutcome.KeptBySchool;
        }
        owner.Payments.Add(payment);
        owner.PaymentBreakdowns.Add(new PaymentBreakdown
        {
            SchoolId = Ecole, PaymentId = payment.Id, FeeCategoryId = Frais, AmountAllocated = 30_000m
        });
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    [Fact]
    public async Task A_Fee_Category_Billed_To_An_Enrollment_Is_Financially_Blocked()
    {
        await AddEnrollmentFeeLineAsync();

        var impact = await AnalyzeAsync(FeeCategoryDeletionPolicy.Policy, Frais);

        impact.FinancialBlock.Should().BeTrue();
        impact.Message.Should().Be(FeeCategoryDeletionPolicy.FinancialBlock);
        impact.Links.Should().BeEmpty();
    }

    [Fact]
    public async Task A_Fee_Category_Used_By_An_Active_Payment_Is_Blocked_But_Not_By_A_Cancelled_One()
    {
        await AddBreakdownAsync(PaymentStatus.Cancelled);
        (await AnalyzeAsync(FeeCategoryDeletionPolicy.Policy, Frais)).FinancialBlock.Should().BeFalse("un paiement annulé ne bloque plus");

        await AddBreakdownAsync(PaymentStatus.Partial);
        (await AnalyzeAsync(FeeCategoryDeletionPolicy.Policy, Frais)).FinancialBlock.Should().BeTrue();
    }

    [Fact]
    public async Task Unlinking_A_Fee_Category_Archives_Its_Fee_Schedule_Lines_Explicitly()
    {
        await using (var owner = _db.NewOwnerContext())
        {
            owner.ClassFees.Add(new ClassFee { SchoolId = Ecole, FeeCategoryId = FraisLibre, ClassroomId = Classe, Amount = 25_000m });
            await owner.SaveChangesAsync(CancellationToken.None);
        }

        var before = await AnalyzeAsync(FeeCategoryDeletionPolicy.Policy, FraisLibre);
        before.CanDelete.Should().BeFalse("l'ancienne cascade silencieuse sur le barème n'existe plus");
        before.Links.Should().ContainSingle(l => l.Kind == "ClassFee" && l.Breakable).Which.Label.Should().Be("1 ligne du barème");

        (await UnlinkAsync(FeeCategoryDeletionPolicy.Policy, FraisLibre)).CanDelete.Should().BeTrue();

        await using var owner2 = _db.NewOwnerContext();
        (await owner2.ClassFees.IgnoreQueryFilters().SingleAsync(f => f.FeeCategoryId == FraisLibre)).IsDeleted.Should().BeTrue("archivé, pas effacé");
    }

    // ------------------------------------------------------------------------------------------ Inventaire

    [Fact]
    public async Task An_Inventory_Category_With_Items_Lists_Them_As_Not_Breakable()
    {
        var impact = await AnalyzeAsync(InventoryCategoryDeletionPolicy.Policy, CategorieStock);

        impact.Links.Should().ContainSingle(l => l.Kind == "InventoryItem" && !l.Breakable).Which.Label.Should().Be("1 bien");
        (await AnalyzeAsync(InventoryCategoryDeletionPolicy.Policy, CategorieStockLibre)).CanDelete.Should().BeTrue();
    }

    private async Task AddLoanAsync(AssignmentStatus status)
    {
        await using var owner = _db.NewOwnerContext();
        owner.ItemAssignments.Add(new ItemAssignment
        {
            SchoolId = Ecole, ItemId = Bien, Quantity = 1, BeneficiaryType = AssignmentBeneficiaryType.Eleve,
            StudentId = Eleve, BeneficiaryLabel = "Awa Fall", AssignedOn = new DateOnly(2026, 10, 12), Status = status
        });
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    [Fact]
    public async Task An_Open_Loan_Blocks_An_Inventory_Item_And_Cannot_Be_Unlinked()
    {
        await AddLoanAsync(AssignmentStatus.EnCours);

        var impact = await AnalyzeAsync(InventoryItemDeletionPolicy.Policy, Bien);

        impact.CanDelete.Should().BeFalse();
        impact.Links.Should().ContainSingle(l => l.Kind == "OpenLoan" && !l.Breakable);

        var act = async () => await UnlinkAsync(InventoryItemDeletionPolicy.Policy, Bien);
        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be(DeletionErrorCodes.DependenciesNotBreakable);
    }

    [Fact]
    public async Task Closed_Loans_Are_Intrinsic_And_Archived_With_The_Item_While_The_Stock_Journal_Stays()
    {
        await AddLoanAsync(AssignmentStatus.Restitue);

        var impact = await AnalyzeAsync(InventoryItemDeletionPolicy.Policy, Bien);
        impact.CanDelete.Should().BeTrue("l'historique des prêts clos part avec le bien, sans rupture manuelle");

        await using (var ctx = _db.NewAppContext(Ecole))
        {
            await EntityDeletionAnalyzer.ArchiveIntrinsicAsync(ctx, InventoryItemDeletionPolicy.Policy, Bien, "directeur", CancellationToken.None);
            await ctx.SaveChangesAsync(CancellationToken.None);
        }

        await using var owner = _db.NewOwnerContext();
        (await owner.ItemAssignments.IgnoreQueryFilters().SingleAsync(a => a.ItemId == Bien)).IsDeleted.Should().BeTrue();
    }

    // ------------------------------------------------------------------------------------------ Isolation

    [Fact]
    public async Task Another_School_Cannot_Analyze_These_Elements()
    {
        foreach (var (policy, id) in new[]
                 {
                     (BuildingDeletionPolicy.Policy, Batiment), (RoomDeletionPolicy.Policy, Salle),
                     (FeeCategoryDeletionPolicy.Policy, Frais), (InventoryCategoryDeletionPolicy.Policy, CategorieStock),
                     (InventoryItemDeletionPolicy.Policy, Bien)
                 })
        {
            var analyze = async () => await AnalyzeAsync(policy, id, AutreEcole);
            await analyze.Should().ThrowAsync<KeyNotFoundException>(policy.Resource);
        }
    }
}
````


## B.4 Tests fonctionnels

#### `tests/SamaEcole.FunctionalTests/Finance/DailyCashRegisterTest.cs`

````csharp
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Persistence;
using SamaEcole.Domain.Entities;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Queries.GetDailyCashRegisterPdf;
using Xunit;
using SamaEcole.FunctionalTests.Common;
using MediatR;
using FluentAssertions;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Net;

namespace SamaEcole.FunctionalTests.Finance
{
    public class DailyCashRegisterTest(AuthApiFactory factory) : IClassFixture<AuthApiFactory>, IAsyncLifetime
    {
        private readonly HttpClient _client = factory.CreateClient();

        public Task InitializeAsync() => factory.ResetTestUsersAsync();
        public Task DisposeAsync() => Task.CompletedTask;

        private record Tokens(string AccessToken, string RefreshToken, int ExpiresIn);

        private async Task<Tokens> LoginAsync(string email, string password)
        {
            var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            return (await response.Content.ReadFromJsonAsync<Tokens>())!;
        }

        [Fact]
        public async Task TestGetDailyCashRegister()
        {
            var tokens = await LoginAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

            var response = await _client.GetAsync("/api/v1/finance/daily-cash-register/pdf");
            
            var content = await response.Content.ReadAsStringAsync();
            response.IsSuccessStatusCode.Should().BeTrue("Erreur: " + content);
        }
    }
}
````


#### `tests/SamaEcole.FunctionalTests/Finance/FeeInstallmentPlanEndpointsTests.cs`

````csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace SamaEcole.FunctionalTests.Finance;

/// <summary>
/// Étape 5 — échéanciers personnalisés, de bout en bout contre un vrai PostgreSQL.
///
/// Le critère central : la somme des échéances doit égaler EXACTEMENT le montant dû de l'inscription,
/// et un plan personnalisé doit primer sur la synthèse mensuelle uniforme dans GetStudentBalanceQuery
/// dès qu'il existe (InstallmentScheduleCalculator).
/// </summary>
public class FeeInstallmentPlanEndpointsTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public FeeInstallmentPlanEndpointsTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public Task InitializeAsync() => _factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private const decimal Inscription = 10_000m;
    private const decimal Mensualite = 15_000m;
    private const int DefaultTuitionMonths = 9;
    private const decimal ExpectedTotal = Inscription + Mensualite * DefaultTuitionMonths; // 145 000

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private record Tokens(string AccessToken, int ExpiresIn);
    private record ClassroomDto(Guid Id, string Name, string Level, int Capacity, int StudentCount);
    private record FeeCategoryDto(Guid Id, string Name, bool IsRecurring);
    private record Receipt(Guid EnrollmentId, string Matricule, decimal TotalDue);
    private record InstallmentDto(string Id, string Designation, decimal Amount, decimal AmountPaid, decimal RemainingDue, DateTimeOffset DueDate, string Status);
    private record StudentBalanceDto(Guid EnrollmentId, Guid StudentId, decimal TotalDue, decimal AmountPaid, List<InstallmentDto> Installments);
    private record StudentSearchItem(Guid Id, string Matricule, string FullName);
    private record StudentSearchPage(List<StudentSearchItem> Items);
    private record ApplyToClassResult(int AppliedCount, int SkippedCount);

    private async Task<string> TokenAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!.AccessToken;
    }

    private Task<string> DirecteurTokenAsync() => TokenAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);
    private Task<string> SecretaireTokenAsync() => TokenAsync(AuthApiFactory.SecretaireEmail, AuthApiFactory.SecretairePassword);
    private Task<string> FinanceTokenAsync() => TokenAsync(AuthApiFactory.FinanceEmail, AuthApiFactory.FinancePassword);

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await _client.SendAsync(request);
    }

    private async Task SeedFeeAsync(string token, string name, bool isRecurring, decimal amount)
    {
        var categoryResponse = await SendAsync(HttpMethod.Post, "/api/v1/finance/fee-categories", token, new { name, isRecurring });
        categoryResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var category = (await categoryResponse.Content.ReadFromJsonAsync<FeeCategoryDto>())!;

        var applyResponse = await SendAsync(HttpMethod.Post, "/api/v1/finance/fees/apply-standard", token,
            new { feeCategoryId = category.Id, amount, overwriteExisting = false });
        applyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>Prépare une école prête à inscrire : une classe, une année active, un barème complet.</summary>
    private async Task<Guid> SeedEnrollableSchoolAsync(string directeurToken)
    {
        var classroomResponse = await SendAsync(HttpMethod.Post, "/api/v1/classrooms", directeurToken,
            new { name = "CM2", level = "Primaire", capacity = 40 });
        classroomResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var classroom = (await classroomResponse.Content.ReadFromJsonAsync<ClassroomDto>())!;

        var yearResponse = await SendAsync(HttpMethod.Post, "/api/v1/school-years", directeurToken, new
        {
            label = $"{Today.Year}-{Today.Year + 1}",
            startDate = Today.AddDays(-150).ToString("yyyy-MM-dd"),
            endDate = Today.AddDays(+120).ToString("yyyy-MM-dd")
        });
        yearResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        await SeedFeeAsync(directeurToken, "Inscription", isRecurring: false, amount: Inscription);
        await SeedFeeAsync(directeurToken, "Mensualité", isRecurring: true, amount: Mensualite);

        return classroom.Id;
    }

    private async Task<Receipt> EnrollAsync(string secretaireToken, Guid classroomId, string fullName)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/v1/enrollments", secretaireToken, new
        {
            type = "NewEnrollment",
            classroomId,
            fullName,
            birthDate = "2015-05-20",
            birthPlace = "Dakar",
            gender = "F"
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<Receipt>())!;
    }

    private async Task<Guid> FindStudentIdByMatriculeAsync(string token, string matricule)
    {
        var response = await SendAsync(HttpMethod.Get, $"/api/v1/students?page=1&pageSize=10&search={matricule}", token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = (await response.Content.ReadFromJsonAsync<StudentSearchPage>())!;
        return page.Items.Single().Id;
    }

    private async Task<StudentBalanceDto> GetBalanceAsync(string token, Guid studentId)
    {
        var response = await SendAsync(HttpMethod.Get, $"/api/v1/finance/students/{studentId}/balance", token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<StudentBalanceDto>())!;
    }

    [Fact]
    public async Task Creating_A_Well_Formed_Plan_Replaces_The_Derived_Monthly_Schedule_On_The_Balance()
    {
        var directeur = await DirecteurTokenAsync();
        var classroomId = await SeedEnrollableSchoolAsync(directeur);
        var secretaire = await SecretaireTokenAsync();
        var receipt = await EnrollAsync(secretaire, classroomId, "Awa Échéancier");

        receipt.TotalDue.Should().Be(ExpectedTotal);

        var createResponse = await SendAsync(
            HttpMethod.Post, $"/api/v1/finance/enrollments/{receipt.EnrollmentId}/installment-plan", directeur,
            new
            {
                reason = "Accord négocié avec la famille",
                installments = new object[]
                {
                    new { label = "Versement 1", amount = 60_000m, dueDate = Today.ToString("yyyy-MM-dd") },
                    new { label = "Versement 2", amount = 85_000m, dueDate = Today.AddMonths(2).ToString("yyyy-MM-dd") }
                }
            });

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var studentId = await FindStudentIdByMatriculeAsync(directeur, receipt.Matricule);
        var balance = await GetBalanceAsync(directeur, studentId);

        // Le plan personnalisé (2 échéances) remplace la synthèse mensuelle uniforme (10 lignes :
        // 1 inscription + 9 mensualités) — preuve qu'InstallmentScheduleCalculator a bien priorisé le plan.
        balance.Installments.Should().HaveCount(2);
        balance.Installments.Select(i => i.Designation).Should().BeEquivalentTo(["Versement 1", "Versement 2"]);
        balance.Installments.Sum(i => i.Amount).Should().Be(ExpectedTotal);
    }

    [Fact]
    public async Task A_Plan_Whose_Sum_Does_Not_Match_The_Total_Due_Is_Rejected()
    {
        var directeur = await DirecteurTokenAsync();
        var classroomId = await SeedEnrollableSchoolAsync(directeur);
        var secretaire = await SecretaireTokenAsync();
        var receipt = await EnrollAsync(secretaire, classroomId, "Modou Somme Fausse");

        var response = await SendAsync(
            HttpMethod.Post, $"/api/v1/finance/enrollments/{receipt.EnrollmentId}/installment-plan", directeur,
            new
            {
                reason = (string?)null,
                installments = new object[]
                {
                    new { label = "Versement unique", amount = 1_000m, dueDate = Today.ToString("yyyy-MM-dd") }
                }
            });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Secretaire_Cannot_Create_An_Installment_Plan()
    {
        var directeur = await DirecteurTokenAsync();
        var classroomId = await SeedEnrollableSchoolAsync(directeur);
        var secretaire = await SecretaireTokenAsync();
        var receipt = await EnrollAsync(secretaire, classroomId, "Fatou Non Autorisée");

        var response = await SendAsync(
            HttpMethod.Post, $"/api/v1/finance/enrollments/{receipt.EnrollmentId}/installment-plan", secretaire,
            new
            {
                reason = (string?)null,
                installments = new object[]
                {
                    new { label = "Versement unique", amount = ExpectedTotal, dueDate = Today.ToString("yyyy-MM-dd") }
                }
            });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_Second_Plan_On_The_Same_Enrollment_Replaces_The_First_On_The_Balance()
    {
        var directeur = await DirecteurTokenAsync();
        var classroomId = await SeedEnrollableSchoolAsync(directeur);
        var secretaire = await SecretaireTokenAsync();
        var receipt = await EnrollAsync(secretaire, classroomId, "Ibrahima Renégociation");

        async Task<HttpResponseMessage> PostPlan(string label) => await SendAsync(
            HttpMethod.Post, $"/api/v1/finance/enrollments/{receipt.EnrollmentId}/installment-plan", directeur,
            new
            {
                reason = (string?)null,
                installments = new object[] { new { label, amount = ExpectedTotal, dueDate = Today.ToString("yyyy-MM-dd") } }
            });

        (await PostPlan("Premier accord")).StatusCode.Should().Be(HttpStatusCode.Created);
        (await PostPlan("Second accord")).StatusCode.Should().Be(HttpStatusCode.Created);

        var studentId = await FindStudentIdByMatriculeAsync(directeur, receipt.Matricule);
        var balance = await GetBalanceAsync(directeur, studentId);

        balance.Installments.Should().ContainSingle().Which.Designation.Should().Be("Second accord");
    }

    [Fact]
    public async Task Applying_A_Template_To_A_Classroom_Gives_Every_Active_Enrollment_Its_Own_Plan()
    {
        var directeur = await DirecteurTokenAsync();
        var classroomId = await SeedEnrollableSchoolAsync(directeur);
        var secretaire = await SecretaireTokenAsync();

        var receiptA = await EnrollAsync(secretaire, classroomId, "Awa Classe Template");
        var receiptB = await EnrollAsync(secretaire, classroomId, "Baba Classe Template");

        var response = await SendAsync(
            HttpMethod.Post, $"/api/v1/finance/classrooms/{classroomId}/installment-plan", directeur,
            new
            {
                reason = "Facilité de fin d'année",
                template = new object[]
                {
                    new { label = "Versement 1", percentage = 0.4m, offsetDays = 0 },
                    new { label = "Versement 2", percentage = 0.3m, offsetDays = 30 },
                    new { label = "Versement 3", percentage = 0.3m, offsetDays = 60 }
                }
            });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<ApplyToClassResult>())!;
        result.AppliedCount.Should().Be(2);
        result.SkippedCount.Should().Be(0);

        var studentIdA = await FindStudentIdByMatriculeAsync(directeur, receiptA.Matricule);
        var balanceA = await GetBalanceAsync(directeur, studentIdA);

        balanceA.Installments.Should().HaveCount(3);
        balanceA.Installments.Sum(i => i.Amount).Should().Be(ExpectedTotal, "le dernier versement absorbe l'écart d'arrondi");

        var studentIdB = await FindStudentIdByMatriculeAsync(directeur, receiptB.Matricule);
        var balanceB = await GetBalanceAsync(directeur, studentIdB);
        balanceB.Installments.Should().HaveCount(3);
    }
}
````


#### `tests/SamaEcole.FunctionalTests/Finance/FeesEndpointsTests.cs`

````csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace SamaEcole.FunctionalTests.Finance;

/// <summary>
/// Ticket JGK-F01 — paramétrage des frais, de bout en bout contre un vrai PostgreSQL.
///
/// On y vérifie les deux options du Volume 1 §7.4 (montant standard en masse, puis exceptions par
/// classe), le verrouillage optimiste de la règle #5 (deux éditions concurrentes → 409),
/// l'historisation, et les droits (Directeur en écriture, lecture ouverte).
/// </summary>
public class FeesEndpointsTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public FeesEndpointsTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public Task InitializeAsync() => _factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private record Tokens(string AccessToken, int ExpiresIn);
    private record ClassroomDto(Guid Id, string Name, string Level, int Capacity, int StudentCount);
    private record FeeCategoryDto(Guid Id, string Name, bool IsRecurring);
    private record ClassFeeDto(Guid Id, Guid FeeCategoryId, string FeeCategoryName, Guid ClassroomId,
        string ClassroomName, string Level, decimal Amount, uint RowVersion);
    private record ApplyResult(int Created, int Updated, int Skipped);
    private record UpdateResult(Guid Id, decimal Amount, uint RowVersion);
    private record HistoryDto(decimal? OldAmount, decimal NewAmount, Guid ChangedByUserId, string ChangedByName, DateTimeOffset ChangedAt);

    private async Task<string> AccessTokenAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!.AccessToken;
    }

    private Task<string> DirecteurTokenAsync() =>
        AccessTokenAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);

    private Task<string> SecretaireTokenAsync() =>
        AccessTokenAsync(AuthApiFactory.SecretaireEmail, AuthApiFactory.SecretairePassword);

    private Task<string> FinanceTokenAsync() =>
        AccessTokenAsync(AuthApiFactory.FinanceEmail, AuthApiFactory.FinancePassword);

    /// <summary>
    /// Seul geste du Directeur capable d'activer la délégation Finance (matrice d'autorisation
    /// "Photoshop") : PUT /schools/current/settings, comme EnableSecretaryDelegationAsync dans
    /// UpdateGradingScaleEndpointsTests pour le barème.
    /// </summary>
    private async Task EnableFinanceDelegationAsync(string directeurToken, bool allowModify, bool allowDelete)
    {
        var response = await SendAsync(HttpMethod.Put, "/api/v1/schools/current/settings", directeurToken, new
        {
            gradingScale = "20",
            studentMatriculeFormat = "ELEV-{YEAR}-{SEQ:4}",
            teacherMatriculeFormat = "ENS-{YEAR}-{SEQ:3}",
            autoLogoutMinutes = 10,
            dateFormat = "dd/MM/yyyy",
            tuitionMonthsPerYear = 9,
            allowSecretaryToManageGrading = false,
            allowFinanceToModifyFees = allowModify,
            allowFinanceToDeleteFees = allowDelete
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await _client.SendAsync(request);
    }

    private async Task<ClassroomDto> CreateClassroomAsync(string token, string name, string level = "Primaire")
    {
        var response = await SendAsync(HttpMethod.Post, "/api/v1/classrooms", token,
            new { name, level, capacity = 40 });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ClassroomDto>())!;
    }

    private async Task<FeeCategoryDto> CreateCategoryAsync(string token, string name, bool isRecurring = true)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/v1/finance/fee-categories", token,
            new { name, isRecurring });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<FeeCategoryDto>())!;
    }

    /// <summary><paramref name="level"/> null = toutes les classes ; sinon, le seul niveau ciblé.</summary>
    private async Task<ApplyResult> ApplyStandardAsync(string token, Guid categoryId, decimal amount, bool overwrite, string? level = null)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/v1/finance/fees/apply-standard", token,
            new { feeCategoryId = categoryId, amount, level, overwriteExisting = overwrite });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<ApplyResult>())!;
    }

    private async Task<List<ClassFeeDto>> ListFeesAsync(string token)
    {
        var response = await SendAsync(HttpMethod.Get, "/api/v1/finance/fees", token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<List<ClassFeeDto>>())!;
    }

    [Fact]
    public async Task Listing_Fees_Without_A_Token_Should_Return_401()
    {
        var response = await _client.GetAsync("/api/v1/finance/fees");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Applying_A_Standard_Should_Create_One_Fee_Per_Classroom()
    {
        var token = await DirecteurTokenAsync();
        await CreateClassroomAsync(token, "CI");
        await CreateClassroomAsync(token, "CP");
        var category = await CreateCategoryAsync(token, "Mensualité");

        var result = await ApplyStandardAsync(token, category.Id, 15000, overwrite: false);

        result.Created.Should().Be(2);
        result.Updated.Should().Be(0);

        var fees = await ListFeesAsync(token);
        fees.Should().HaveCount(2);
        fees.Should().OnlyContain(f => f.Amount == 15000);
    }

    [Fact]
    public async Task Applying_A_Standard_On_A_School_Without_Classrooms_Should_Return_422()
    {
        // Aucune classe : rien à facturer. On ne crée pas un barème sans cible.
        var token = await DirecteurTokenAsync();
        var category = await CreateCategoryAsync(token, "Inscription", isRecurring: false);

        var response = await SendAsync(HttpMethod.Post, "/api/v1/finance/fees/apply-standard", token,
            new { feeCategoryId = category.Id, amount = 15000m, overwriteExisting = false });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task An_Exception_Set_Per_Class_Should_Survive_A_Re_Applied_Standard()
    {
        var token = await DirecteurTokenAsync();
        await CreateClassroomAsync(token, "CI");
        await CreateClassroomAsync(token, "CP");
        var category = await CreateCategoryAsync(token, "Mensualité");

        await ApplyStandardAsync(token, category.Id, 15000, overwrite: false);

        // Exception : une classe passe à 20 000.
        var fees = await ListFeesAsync(token);
        var exception = fees.First();
        var put = await SendAsync(HttpMethod.Put, $"/api/v1/finance/fees/{exception.Id}", token,
            new { amount = 20000m, rowVersion = exception.RowVersion });
        put.StatusCode.Should().Be(HttpStatusCode.OK);

        // On rejoue le standard SANS écraser : l'exception doit être PRÉSERVÉE (Volume 1 §7.4).
        var result = await ApplyStandardAsync(token, category.Id, 18000, overwrite: false);
        result.Created.Should().Be(0);
        result.Updated.Should().Be(0);
        result.Skipped.Should().Be(2, "sans écrasement, les lignes existantes sont laissées telles quelles");

        var after = await ListFeesAsync(token);
        after.Single(f => f.Id == exception.Id).Amount.Should().Be(20000, "l'exception ne doit pas être effacée");
    }

    [Fact]
    public async Task Re_Applying_A_Standard_With_Overwrite_Should_Realign_Every_Class()
    {
        var token = await DirecteurTokenAsync();
        await CreateClassroomAsync(token, "CI");
        await CreateClassroomAsync(token, "CP");
        var category = await CreateCategoryAsync(token, "Mensualité");

        await ApplyStandardAsync(token, category.Id, 15000, overwrite: false);

        var fees = await ListFeesAsync(token);
        var exception = fees.First();
        var put = await SendAsync(HttpMethod.Put, $"/api/v1/finance/fees/{exception.Id}", token,
            new { amount = 20000m, rowVersion = exception.RowVersion });
        put.StatusCode.Should().Be(HttpStatusCode.OK);

        // Remise à plat assumée : tout revient au standard, exception comprise.
        var result = await ApplyStandardAsync(token, category.Id, 18000, overwrite: true);
        result.Updated.Should().Be(2, "les deux classes changent de montant (15 000 et 20 000 → 18 000)");

        var after = await ListFeesAsync(token);
        after.Should().OnlyContain(f => f.Amount == 18000);
    }

    [Fact]
    public async Task Applying_A_Standard_To_One_Level_Should_Only_Cover_That_Level()
    {
        // Périmètre par niveau : une école ne facture pas le Primaire au tarif du Lycée.
        var token = await DirecteurTokenAsync();
        await CreateClassroomAsync(token, "CI", level: "Primaire");
        await CreateClassroomAsync(token, "6e A", level: "Collège");
        await CreateClassroomAsync(token, "5e A", level: "Collège");
        var category = await CreateCategoryAsync(token, "Mensualité");

        var result = await ApplyStandardAsync(token, category.Id, 25000, overwrite: false, level: "Collège");

        result.Created.Should().Be(2, "seules les deux classes du Collège sont dans le périmètre");
        result.Skipped.Should().Be(0, "le Primaire n'est même pas parcouru");

        var fees = await ListFeesAsync(token);
        fees.Should().HaveCount(2, "aucune ligne ne doit naître hors du niveau ciblé");
        fees.Should().OnlyContain(f => f.Level == "Collège" && f.Amount == 25000);
    }

    [Fact]
    public async Task Re_Applying_A_Standard_On_One_Level_Should_Not_Touch_The_Other_Levels()
    {
        // Le cas qui coûte cher : une remise à plat (overwrite) d'un cycle ne doit pas déborder sur
        // les montants des autres — ils ne sont ni réalignés, ni comptés.
        var token = await DirecteurTokenAsync();
        await CreateClassroomAsync(token, "CI", level: "Primaire");
        await CreateClassroomAsync(token, "6e A", level: "Collège");
        var category = await CreateCategoryAsync(token, "Mensualité");

        await ApplyStandardAsync(token, category.Id, 10000, overwrite: false);

        // Niveau saisi en minuscules : la nomenclature est libre côté école, le ciblage ignore la casse.
        var result = await ApplyStandardAsync(token, category.Id, 25000, overwrite: true, level: "collège");

        result.Updated.Should().Be(1, "seule la classe de Collège est réalignée");
        result.Created.Should().Be(0);
        result.Skipped.Should().Be(0);

        var fees = await ListFeesAsync(token);
        fees.Single(f => f.Level == "Collège").Amount.Should().Be(25000);
        fees.Single(f => f.Level == "Primaire").Amount.Should().Be(10000, "les autres cycles gardent leur montant");
    }

    [Fact]
    public async Task Applying_A_Standard_To_A_Level_Without_Classrooms_Should_Return_422()
    {
        // Un périmètre qui ne désigne aucune classe est une erreur de saisie, pas un no-op silencieux.
        var token = await DirecteurTokenAsync();
        await CreateClassroomAsync(token, "CI", level: "Primaire");
        var category = await CreateCategoryAsync(token, "Mensualité");

        var response = await SendAsync(HttpMethod.Post, "/api/v1/finance/fees/apply-standard", token,
            new { feeCategoryId = category.Id, amount = 15000m, level = "Lycée", overwriteExisting = false });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var fees = await ListFeesAsync(token);
        fees.Should().BeEmpty("un périmètre vide n'écrit rien, pas même sur les autres niveaux");
    }

    [Fact]
    public async Task A_Stale_RowVersion_Should_Be_Refused_With_A_409()
    {
        var token = await DirecteurTokenAsync();
        await CreateClassroomAsync(token, "CI");
        var category = await CreateCategoryAsync(token, "Mensualité");
        await ApplyStandardAsync(token, category.Id, 15000, overwrite: false);

        var fee = (await ListFeesAsync(token)).Single();
        var staleVersion = fee.RowVersion;

        // Première édition : réussit et fait tourner le jeton.
        var first = await SendAsync(HttpMethod.Put, $"/api/v1/finance/fees/{fee.Id}", token,
            new { amount = 16000m, rowVersion = staleVersion });
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        // Seconde édition avec le jeton D'ORIGINE, désormais périmé : conflit, pas d'écrasement (règle #5).
        var second = await SendAsync(HttpMethod.Put, $"/api/v1/finance/fees/{fee.Id}", token,
            new { amount = 17000m, rowVersion = staleVersion });
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Le montant en base est bien celui de la première édition, intact.
        (await ListFeesAsync(token)).Single().Amount.Should().Be(16000);
    }

    [Fact]
    public async Task Updating_A_Fee_Should_Record_Its_History()
    {
        var token = await DirecteurTokenAsync();
        await CreateClassroomAsync(token, "CI");
        var category = await CreateCategoryAsync(token, "Mensualité");
        await ApplyStandardAsync(token, category.Id, 15000, overwrite: false);

        var fee = (await ListFeesAsync(token)).Single();
        var put = await SendAsync(HttpMethod.Put, $"/api/v1/finance/fees/{fee.Id}", token,
            new { amount = 18000m, rowVersion = fee.RowVersion });
        put.StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await SendAsync(HttpMethod.Get, $"/api/v1/finance/fees/{fee.Id}/history", token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var history = (await response.Content.ReadFromJsonAsync<List<HistoryDto>>())!;

        // Deux entrées, la plus récente d'abord : la modification (15 000 → 18 000) puis la création
        // (null → 15 000).
        history.Should().HaveCount(2);
        history[0].OldAmount.Should().Be(15000);
        history[0].NewAmount.Should().Be(18000);
        history[0].ChangedByName.Should().Be("Directeur de test");
        history[1].OldAmount.Should().BeNull("la première entrée est une création");
        history[1].NewAmount.Should().Be(15000);
    }

    [Fact]
    public async Task Updating_An_Unknown_Fee_Should_Return_404()
    {
        var token = await DirecteurTokenAsync();

        var response = await SendAsync(HttpMethod.Put, $"/api/v1/finance/fees/{Guid.NewGuid()}", token,
            new { amount = 15000m, rowVersion = (uint)1 });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Duplicate_Category_Name_Should_Return_409()
    {
        var token = await DirecteurTokenAsync();
        await CreateCategoryAsync(token, "Cantine");

        var duplicate = await SendAsync(HttpMethod.Post, "/api/v1/finance/fee-categories", token,
            new { name = "Cantine", isRecurring = false });

        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_Secretary_Must_Not_Configure_Fees()
    {
        // Le barème est un paramètre d'établissement : Directeur uniquement (Volume 7 §15, règle #4).
        var directeur = await DirecteurTokenAsync();
        await CreateClassroomAsync(directeur, "CI");
        var category = await CreateCategoryAsync(directeur, "Mensualité");
        await ApplyStandardAsync(directeur, category.Id, 15000, overwrite: false);
        var fee = (await ListFeesAsync(directeur)).Single();

        var secretaire = await SecretaireTokenAsync();

        var create = await SendAsync(HttpMethod.Post, "/api/v1/finance/fee-categories", secretaire,
            new { name = "Transport", isRecurring = false });
        create.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var apply = await SendAsync(HttpMethod.Post, "/api/v1/finance/fees/apply-standard", secretaire,
            new { feeCategoryId = category.Id, amount = 9000m, overwriteExisting = true });
        apply.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var update = await SendAsync(HttpMethod.Put, $"/api/v1/finance/fees/{fee.Id}", secretaire,
            new { amount = 9000m, rowVersion = fee.RowVersion });
        update.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_Secretary_Should_Still_Read_The_Fees()
    {
        // La lecture reste ouverte : le secrétariat compose les montants dus à l'inscription.
        var directeur = await DirecteurTokenAsync();
        await CreateClassroomAsync(directeur, "CI");
        var category = await CreateCategoryAsync(directeur, "Mensualité");
        await ApplyStandardAsync(directeur, category.Id, 15000, overwrite: false);

        var secretaire = await SecretaireTokenAsync();
        var fees = await ListFeesAsync(secretaire);

        fees.Should().ContainSingle().Which.Amount.Should().Be(15000);
    }

    // ---------------------------------------------------------- Délégation Finance (matrice "Photoshop")

    [Fact]
    public async Task A_Finance_Must_Not_Modify_A_Fee_By_Default()
    {
        var directeur = await DirecteurTokenAsync();
        await CreateClassroomAsync(directeur, "CI");
        var category = await CreateCategoryAsync(directeur, "Mensualité");
        await ApplyStandardAsync(directeur, category.Id, 15000, overwrite: false);
        var fee = (await ListFeesAsync(directeur)).Single();

        var finance = await FinanceTokenAsync();
        var response = await SendAsync(HttpMethod.Put, $"/api/v1/finance/fees/{fee.Id}", finance,
            new { amount = 18000m, rowVersion = fee.RowVersion });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_Finance_Can_Modify_A_Fee_Once_The_Directeur_Enables_Delegation()
    {
        var directeur = await DirecteurTokenAsync();
        await CreateClassroomAsync(directeur, "CI");
        var category = await CreateCategoryAsync(directeur, "Mensualité");
        await ApplyStandardAsync(directeur, category.Id, 15000, overwrite: false);
        var fee = (await ListFeesAsync(directeur)).Single();

        await EnableFinanceDelegationAsync(directeur, allowModify: true, allowDelete: false);

        var finance = await FinanceTokenAsync();
        var response = await SendAsync(HttpMethod.Put, $"/api/v1/finance/fees/{fee.Id}", finance,
            new { amount = 18000m, rowVersion = fee.RowVersion });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ListFeesAsync(directeur)).Single().Amount.Should().Be(18000);
    }

    [Fact]
    public async Task A_Finance_Loses_Modify_Access_Again_Once_The_Directeur_Disables_Delegation()
    {
        var directeur = await DirecteurTokenAsync();
        await CreateClassroomAsync(directeur, "CI");
        var category = await CreateCategoryAsync(directeur, "Mensualité");
        await ApplyStandardAsync(directeur, category.Id, 15000, overwrite: false);
        var fee = (await ListFeesAsync(directeur)).Single();

        await EnableFinanceDelegationAsync(directeur, allowModify: true, allowDelete: false);
        await EnableFinanceDelegationAsync(directeur, allowModify: false, allowDelete: false);

        var finance = await FinanceTokenAsync();
        var response = await SendAsync(HttpMethod.Put, $"/api/v1/finance/fees/{fee.Id}", finance,
            new { amount = 18000m, rowVersion = fee.RowVersion });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ---------------------------------------------------- Feature D : autonomie Finance (créer/appliquer)

    [Fact]
    public async Task A_Finance_Must_Not_Create_A_Fee_Category_By_Default()
    {
        var directeur = await DirecteurTokenAsync();
        var finance = await FinanceTokenAsync();

        var response = await SendAsync(HttpMethod.Post, "/api/v1/finance/fee-categories", finance,
            new { name = "Transport", isRecurring = false });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_Finance_Can_Create_A_Fee_Category_Once_The_Directeur_Enables_Delegation()
    {
        var directeur = await DirecteurTokenAsync();
        await EnableFinanceDelegationAsync(directeur, allowModify: true, allowDelete: false);

        var finance = await FinanceTokenAsync();
        var response = await SendAsync(HttpMethod.Post, "/api/v1/finance/fee-categories", finance,
            new { name = "Transport", isRecurring = false });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_Finance_Must_Not_Apply_A_Standard_Fee_By_Default()
    {
        var directeur = await DirecteurTokenAsync();
        await CreateClassroomAsync(directeur, "CI");
        var category = await CreateCategoryAsync(directeur, "Mensualité");

        var finance = await FinanceTokenAsync();
        var response = await SendAsync(HttpMethod.Post, "/api/v1/finance/fees/apply-standard", finance,
            new { feeCategoryId = category.Id, amount = 15000m, overwriteExisting = false });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_Finance_Can_Apply_A_Standard_Fee_Once_The_Directeur_Enables_Delegation()
    {
        var directeur = await DirecteurTokenAsync();
        await CreateClassroomAsync(directeur, "CI");
        var category = await CreateCategoryAsync(directeur, "Mensualité");
        await EnableFinanceDelegationAsync(directeur, allowModify: true, allowDelete: false);

        var finance = await FinanceTokenAsync();
        var response = await SendAsync(HttpMethod.Post, "/api/v1/finance/fees/apply-standard", finance,
            new { feeCategoryId = category.Id, amount = 15000m, overwriteExisting = false });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ListFeesAsync(directeur)).Should().ContainSingle(f => f.Amount == 15000);
    }

    [Fact]
    public async Task A_Delete_Only_Delegation_Must_Not_Grant_Create_Or_Apply_Standard_Access()
    {
        // Créer une catégorie / appliquer un standard façonnent le barème (même délégation que
        // Modifier) — la délégation SUPPRIMER, plus lourde de conséquences, ne doit pas suffire.
        var directeur = await DirecteurTokenAsync();
        await CreateClassroomAsync(directeur, "CI");
        var category = await CreateCategoryAsync(directeur, "Mensualité");
        await EnableFinanceDelegationAsync(directeur, allowModify: false, allowDelete: true);

        var finance = await FinanceTokenAsync();

        var create = await SendAsync(HttpMethod.Post, "/api/v1/finance/fee-categories", finance,
            new { name = "Transport", isRecurring = false });
        create.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var apply = await SendAsync(HttpMethod.Post, "/api/v1/finance/fees/apply-standard", finance,
            new { feeCategoryId = category.Id, amount = 15000m, overwriteExisting = false });
        apply.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_Directeur_Can_Still_Create_And_Apply_Regardless_Of_Delegation_Settings()
    {
        // Contre-épreuve : le Directeur ne doit jamais dépendre de sa propre délégation.
        var directeur = await DirecteurTokenAsync();
        await CreateClassroomAsync(directeur, "CI");

        var category = await CreateCategoryAsync(directeur, "Cantine");
        var result = await ApplyStandardAsync(directeur, category.Id, 12000, overwrite: false);

        result.Created.Should().Be(1);
    }

    [Fact]
    public async Task A_Directeur_Can_Delete_A_Fee()
    {
        var directeur = await DirecteurTokenAsync();
        await CreateClassroomAsync(directeur, "CI");
        var category = await CreateCategoryAsync(directeur, "Mensualité");
        await ApplyStandardAsync(directeur, category.Id, 15000, overwrite: false);
        var fee = (await ListFeesAsync(directeur)).Single();

        var response = await SendAsync(
            HttpMethod.Delete, $"/api/v1/finance/fees/{fee.Id}?rowVersion={fee.RowVersion}", directeur);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ListFeesAsync(directeur)).Should().BeEmpty("la ligne supprimée ne doit plus apparaître dans le barème");
    }

    [Fact]
    public async Task A_Finance_Must_Not_Delete_A_Fee_By_Default()
    {
        var directeur = await DirecteurTokenAsync();
        await CreateClassroomAsync(directeur, "CI");
        var category = await CreateCategoryAsync(directeur, "Mensualité");
        await ApplyStandardAsync(directeur, category.Id, 15000, overwrite: false);
        var fee = (await ListFeesAsync(directeur)).Single();

        var finance = await FinanceTokenAsync();
        var response = await SendAsync(
            HttpMethod.Delete, $"/api/v1/finance/fees/{fee.Id}?rowVersion={fee.RowVersion}", finance);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ListFeesAsync(directeur)).Should().ContainSingle("la Finance non déléguée ne doit rien supprimer");
    }

    [Fact]
    public async Task A_Finance_Can_Delete_A_Fee_Once_The_Directeur_Enables_Delegation()
    {
        var directeur = await DirecteurTokenAsync();
        await CreateClassroomAsync(directeur, "CI");
        var category = await CreateCategoryAsync(directeur, "Mensualité");
        await ApplyStandardAsync(directeur, category.Id, 15000, overwrite: false);
        var fee = (await ListFeesAsync(directeur)).Single();

        await EnableFinanceDelegationAsync(directeur, allowModify: false, allowDelete: true);

        var finance = await FinanceTokenAsync();
        var response = await SendAsync(
            HttpMethod.Delete, $"/api/v1/finance/fees/{fee.Id}?rowVersion={fee.RowVersion}", finance);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ListFeesAsync(directeur)).Should().BeEmpty();
    }

    [Fact]
    public async Task Deleting_A_Fee_With_A_Stale_RowVersion_Should_Return_409()
    {
        var directeur = await DirecteurTokenAsync();
        await CreateClassroomAsync(directeur, "CI");
        var category = await CreateCategoryAsync(directeur, "Mensualité");
        await ApplyStandardAsync(directeur, category.Id, 15000, overwrite: false);
        var fee = (await ListFeesAsync(directeur)).Single();
        var staleVersion = fee.RowVersion;

        // Une modification entre-temps fait tourner le jeton xmin.
        var put = await SendAsync(HttpMethod.Put, $"/api/v1/finance/fees/{fee.Id}", directeur,
            new { amount = 16000m, rowVersion = staleVersion });
        put.StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await SendAsync(
            HttpMethod.Delete, $"/api/v1/finance/fees/{fee.Id}?rowVersion={staleVersion}", directeur);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ListFeesAsync(directeur)).Should().ContainSingle("le conflit ne doit jamais entraîner une suppression silencieuse");
    }

    [Fact]
    public async Task Deleting_An_Unknown_Fee_Should_Return_404()
    {
        var directeur = await DirecteurTokenAsync();

        var response = await SendAsync(
            HttpMethod.Delete, $"/api/v1/finance/fees/{Guid.NewGuid()}?rowVersion=1", directeur);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_Directeur_Can_Delete_A_Fee_Category_After_Explicitly_Breaking_Its_Fee_Schedule()
    {
        var directeur = await DirecteurTokenAsync();
        await CreateClassroomAsync(directeur, "CI");
        await CreateClassroomAsync(directeur, "CP");
        var category = await CreateCategoryAsync(directeur, "Mensualité");
        await ApplyStandardAsync(directeur, category.Id, 15000, overwrite: false);

        // Plus de cascade silencieuse (spec 2026-10-17) : le barème est un lien listé, à rompre explicitement.
        (await SendAsync(HttpMethod.Delete, $"/api/v1/finance/fee-categories/{category.Id}", directeur))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await SendAsync(HttpMethod.Post, $"/api/v1/finance/fee-categories/{category.Id}/unlink-dependencies", directeur))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await SendAsync(HttpMethod.Delete, $"/api/v1/finance/fee-categories/{category.Id}", directeur);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var categories = await SendAsync(HttpMethod.Get, "/api/v1/finance/fee-categories", directeur);
        (await categories.Content.ReadFromJsonAsync<List<FeeCategoryDto>>())!
            .Should().BeEmpty("la catégorie supprimée ne doit plus apparaître");

        (await ListFeesAsync(directeur)).Should()
            .BeEmpty("ses lignes de barème doivent disparaître avec elle (archivées par la rupture explicite)");
    }

    [Fact]
    public async Task A_Finance_Must_Not_Delete_A_Fee_Category_By_Default()
    {
        var directeur = await DirecteurTokenAsync();
        var category = await CreateCategoryAsync(directeur, "Cantine");

        var finance = await FinanceTokenAsync();
        var response = await SendAsync(HttpMethod.Delete, $"/api/v1/finance/fee-categories/{category.Id}", finance);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_Finance_Can_Delete_A_Fee_Category_Once_The_Directeur_Enables_Delegation()
    {
        var directeur = await DirecteurTokenAsync();
        var category = await CreateCategoryAsync(directeur, "Cantine");
        await EnableFinanceDelegationAsync(directeur, allowModify: false, allowDelete: true);

        var finance = await FinanceTokenAsync();
        var response = await SendAsync(HttpMethod.Delete, $"/api/v1/finance/fee-categories/{category.Id}", finance);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Deleting_An_Unknown_Fee_Category_Should_Return_404()
    {
        var directeur = await DirecteurTokenAsync();

        var response = await SendAsync(
            HttpMethod.Delete, $"/api/v1/finance/fee-categories/{Guid.NewGuid()}", directeur);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
````


#### `tests/SamaEcole.FunctionalTests/Finance/FinanceDashboardEndpointsTests.cs`

````csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace SamaEcole.FunctionalTests.Finance;

/// <summary>
/// Ticket JGK-F04 — tableau de bord financier, de bout en bout contre un vrai PostgreSQL.
///
/// Deux critères du ticket :
///   • séparation des rôles (règle #4) — Directeur et Finance voient le tableau de bord, le
///     Secrétariat NON (il compose le dû à l'inscription, il ne pilote pas la trésorerie) ;
///   • les agrégats (encaissé jour/mois/année, solde dû, taux de recouvrement) reflètent RÉELLEMENT
///     les paiements enregistrés via la caisse, jamais une valeur inventée.
/// </summary>
public class FinanceDashboardEndpointsTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public FinanceDashboardEndpointsTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public Task InitializeAsync() => _factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private const decimal Inscription = 10_000m;
    private const decimal Mensualite = 15_000m;
    private const int DefaultTuitionMonths = 9;
    private const decimal ExpectedTotal = Inscription + Mensualite * DefaultTuitionMonths; // 145 000

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private record Tokens(string AccessToken, int ExpiresIn);
    private record ClassroomDto(Guid Id, string Name, string Level, int Capacity, int StudentCount);
    private record FeeCategoryDto(Guid Id, string Name, bool IsRecurring);
    private record Receipt(Guid EnrollmentId, string ReceiptNumber, string Matricule, decimal TotalDue);
    private record PaymentResult(Guid PaymentId, string ReceiptNumber, decimal Amount);
    private record RecentPayment(Guid PaymentId, string ReceiptNumber, string Matricule, string StudentFullName, decimal Amount, string Method, DateTimeOffset PaidAt);
    private record DashboardDto(
        decimal CollectedToday, decimal CollectedThisMonth, decimal CollectedThisYear,
        decimal OutstandingBalance, decimal RecoveryRate, List<RecentPayment> RecentPayments);

    private async Task<string> TokenAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!.AccessToken;
    }

    private Task<string> DirecteurTokenAsync() => TokenAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);
    private Task<string> SecretaireTokenAsync() => TokenAsync(AuthApiFactory.SecretaireEmail, AuthApiFactory.SecretairePassword);
    private Task<string> FinanceTokenAsync() => TokenAsync(AuthApiFactory.FinanceEmail, AuthApiFactory.FinancePassword);

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await _client.SendAsync(request);
    }

    private async Task SeedFeeAsync(string token, string name, bool isRecurring, decimal amount)
    {
        var categoryResponse = await SendAsync(HttpMethod.Post, "/api/v1/finance/fee-categories", token,
            new { name, isRecurring });
        categoryResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var category = (await categoryResponse.Content.ReadFromJsonAsync<FeeCategoryDto>())!;

        var applyResponse = await SendAsync(HttpMethod.Post, "/api/v1/finance/fees/apply-standard", token,
            new { feeCategoryId = category.Id, amount, overwriteExisting = false });
        applyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>École prête + un élève inscrit : renvoie l'inscription à encaisser (Id + montant dû).</summary>
    private async Task<Receipt> SeedEnrolledStudentAsync()
    {
        var directeur = await DirecteurTokenAsync();

        var classroomResponse = await SendAsync(HttpMethod.Post, "/api/v1/classrooms", directeur,
            new { name = "CM2", level = "Primaire", capacity = 40 });
        classroomResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var classroom = (await classroomResponse.Content.ReadFromJsonAsync<ClassroomDto>())!;

        var yearResponse = await SendAsync(HttpMethod.Post, "/api/v1/school-years", directeur, new
        {
            label = $"{Today.Year}-{Today.Year + 1}",
            startDate = Today.AddDays(-150).ToString("yyyy-MM-dd"),
            endDate = Today.AddDays(+120).ToString("yyyy-MM-dd")
        });
        yearResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        await SeedFeeAsync(directeur, "Inscription", isRecurring: false, amount: Inscription);
        await SeedFeeAsync(directeur, "Mensualité", isRecurring: true, amount: Mensualite);

        var secretaire = await SecretaireTokenAsync();
        var enrollResponse = await SendAsync(HttpMethod.Post, "/api/v1/enrollments", secretaire, new
        {
            type = "NewEnrollment",
            classroomId = classroom.Id,
            fullName = "Awa Ndiaye",
            birthDate = "2015-05-20",
            birthPlace = "Dakar",
            gender = "F"
        });
        enrollResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var receipt = (await enrollResponse.Content.ReadFromJsonAsync<Receipt>())!;
        receipt.TotalDue.Should().Be(ExpectedTotal);
        return receipt;
    }

    [Fact]
    public async Task Dashboard_Reflects_Payments_Just_Recorded_Through_The_Caisse()
    {
        var enrollment = await SeedEnrolledStudentAsync();
        var finance = await FinanceTokenAsync();

        await SendAsync(HttpMethod.Post, "/api/v1/finance/sessions/open", finance, new { openingBalance = 0m });

        var first = await SendAsync(HttpMethod.Post, "/api/v1/finance/payments", finance,
            new { enrollmentId = enrollment.EnrollmentId, amount = 50_000m, method = "Cash" });
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        var second = await SendAsync(HttpMethod.Post, "/api/v1/finance/payments", finance,
            new { enrollmentId = enrollment.EnrollmentId, amount = 30_000m, method = "MobileMoney" });
        second.StatusCode.Should().Be(HttpStatusCode.Created);

        const decimal totalCollected = 80_000m;

        var response = await SendAsync(HttpMethod.Get, "/api/v1/finance/dashboard", finance);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dashboard = (await response.Content.ReadFromJsonAsync<DashboardDto>())!;

        // Les deux versements ont lieu "maintenant" : ils tombent forcément dans les trois fenêtres.
        dashboard.CollectedToday.Should().Be(totalCollected);
        dashboard.CollectedThisMonth.Should().Be(totalCollected);
        dashboard.CollectedThisYear.Should().Be(totalCollected);
        dashboard.OutstandingBalance.Should().Be(ExpectedTotal - totalCollected);
        dashboard.RecoveryRate.Should().Be(totalCollected / ExpectedTotal);

        dashboard.RecentPayments.Should().HaveCount(2);
        dashboard.RecentPayments.Select(p => p.Amount).Should().Contain([50_000m, 30_000m]);
        dashboard.RecentPayments.Should().OnlyContain(p => p.Matricule == enrollment.Matricule);
        // Le plus récent d'abord : le second versement (MobileMoney, 30 000) a été encaissé après le premier.
        dashboard.RecentPayments[0].Method.Should().Be("MobileMoney");
    }

    [Fact]
    public async Task Dashboard_Shows_Zero_When_Nothing_Is_Due_Or_Collected()
    {
        var directeur = await DirecteurTokenAsync();

        var response = await SendAsync(HttpMethod.Get, "/api/v1/finance/dashboard", directeur);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dashboard = (await response.Content.ReadFromJsonAsync<DashboardDto>())!;

        dashboard.CollectedToday.Should().Be(0m);
        dashboard.CollectedThisMonth.Should().Be(0m);
        dashboard.CollectedThisYear.Should().Be(0m);
        dashboard.OutstandingBalance.Should().Be(0m);
        dashboard.RecoveryRate.Should().Be(0m, "aucune inscription active ne doit produire une division par zéro");
        dashboard.RecentPayments.Should().BeEmpty();
    }

    [Fact]
    public async Task A_Secretariat_Must_Not_Be_Allowed_To_View_The_Dashboard()
    {
        // Miroir du critère JGK-F02 : la Finance/le Directeur pilotent la trésorerie, le Secrétariat
        // compose le dû à l'inscription mais n'a pas accès à la vue agrégée (règle #4).
        var secretaire = await SecretaireTokenAsync();

        var response = await SendAsync(HttpMethod.Get, "/api/v1/finance/dashboard", secretaire);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
````


#### `tests/SamaEcole.FunctionalTests/Finance/PaymentsEndpointsTests.cs`

````csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace SamaEcole.FunctionalTests.Finance;

/// <summary>
/// Ticket JGK-F02 — caisse, de bout en bout contre un vrai PostgreSQL.
///
/// Deux critères du ticket :
///   • séparation des rôles (règle #4) — ce que la règle sépare est FIXER LE DÛ (Secrétariat/Directeur,
///     jamais Finance) de la santé financière AGRÉGÉE (Directeur/Finance) ; ENCAISSER, lui, est ouvert
///     au Secrétariat qui tient sa propre caisse (cf. Volume 7). On le prouve par le test positif
///     Secrétariat + session ouverte → 201 sur POST /finance/payments ;
///   • on n'encaisse jamais au-delà du solde, et un reçu officiel sort du pipeline.
/// (La non-régression sous concurrence est prouvée à part par PaymentConcurrencyTests, en intégration.)
/// </summary>
public class PaymentsEndpointsTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public PaymentsEndpointsTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public Task InitializeAsync() => _factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private const decimal Inscription = 10_000m;
    private const decimal Mensualite = 15_000m;
    private const int DefaultTuitionMonths = 9;
    private const decimal ExpectedTotal = Inscription + Mensualite * DefaultTuitionMonths; // 145 000

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private record Tokens(string AccessToken, int ExpiresIn);
    private record ClassroomDto(Guid Id, string Name, string Level, int Capacity, int StudentCount);
    private record FeeCategoryDto(Guid Id, string Name, bool IsRecurring);
    private record Receipt(Guid EnrollmentId, string ReceiptNumber, string Matricule, decimal TotalDue);
    private record PaymentResult(
        Guid PaymentId, string ReceiptNumber, decimal Amount, decimal TotalDue,
        decimal AmountPaid, decimal RemainingBalance, string Status);
    private record StudentListItem(Guid Id, string Matricule, string FullName);
    private record PaginatedStudents(List<StudentListItem> Items, int TotalCount);
    private record StudentBalance(
        Guid EnrollmentId, Guid StudentId, string Matricule, string StudentFullName,
        string ClassroomName, string SchoolYearLabel,
        decimal TotalDue, decimal AmountPaid, decimal RemainingBalance, string Status,
        List<Installment> Installments, decimal DueNowTotal);

    private record Installment(
        string Id, string Designation, decimal Amount, decimal AmountPaid, decimal RemainingDue,
        DateTimeOffset DueDate, string Status, bool IsInitialScope, bool IsDueNow, Guid? FeeCategoryId);

    private record CashierSearchResult(
        Guid Id, string Matricule, string FullName, string? ClassroomName,
        bool HasActiveEnrollment, decimal TotalDue, decimal AmountPaid,
        decimal RemainingBalance, decimal DueNowTotal);

    private record PaymentReceiptLine(string Designation, string? Label, decimal Amount);
    private record PaymentReceipt(
        string ReceiptNumber, decimal Amount, decimal TotalDue, decimal AlreadyPaid,
        decimal RemainingBalance, List<PaymentReceiptLine> Lines, bool HasBalancedLines);

    // Engagement initial de SeedEnrolledStudentAsync : inscription (ponctuel) + 1er mois de scolarité.
    private const decimal ExpectedDueNow = Inscription + Mensualite; // 25 000

    private async Task<string> TokenAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!.AccessToken;
    }

    private Task<string> DirecteurTokenAsync() => TokenAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);
    private Task<string> SecretaireTokenAsync() => TokenAsync(AuthApiFactory.SecretaireEmail, AuthApiFactory.SecretairePassword);
    private Task<string> FinanceTokenAsync() => TokenAsync(AuthApiFactory.FinanceEmail, AuthApiFactory.FinancePassword);
    private Task<string> EnseignantTokenAsync() => TokenAsync(AuthApiFactory.EnseignantEmail, AuthApiFactory.EnseignantPassword);

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await _client.SendAsync(request);
    }

    private async Task SeedFeeAsync(string token, string name, bool isRecurring, decimal amount)
    {
        var categoryResponse = await SendAsync(HttpMethod.Post, "/api/v1/finance/fee-categories", token,
            new { name, isRecurring });
        categoryResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var category = (await categoryResponse.Content.ReadFromJsonAsync<FeeCategoryDto>())!;

        var applyResponse = await SendAsync(HttpMethod.Post, "/api/v1/finance/fees/apply-standard", token,
            new { feeCategoryId = category.Id, amount, overwriteExisting = false });
        applyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// École prête + un élève inscrit : renvoie l'inscription à encaisser (Id + montant dû).
    /// <paramref name="yearStartedDaysAgo"/> règle l'ancienneté de l'année active : à 10 jours seul l'engagement
    /// initial (inscription + 1er mois) est échu ; plus ancienne, des mensualités s'y ajoutent.
    /// </summary>
    private async Task<Receipt> SeedEnrolledStudentAsync(int yearStartedDaysAgo = 10)
    {
        var directeur = await DirecteurTokenAsync();

        var classroomResponse = await SendAsync(HttpMethod.Post, "/api/v1/classrooms", directeur,
            new { name = "CM2", level = "Primaire", capacity = 40 });
        classroomResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var classroom = (await classroomResponse.Content.ReadFromJsonAsync<ClassroomDto>())!;

        var yearResponse = await SendAsync(HttpMethod.Post, "/api/v1/school-years", directeur, new
        {
            label = $"{Today.Year}-{Today.Year + 1}",
            startDate = Today.AddDays(-yearStartedDaysAgo).ToString("yyyy-MM-dd"),
            endDate = Today.AddDays(+120).ToString("yyyy-MM-dd")
        });
        yearResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        await SeedFeeAsync(directeur, "Inscription", isRecurring: false, amount: Inscription);
        await SeedFeeAsync(directeur, "Mensualité", isRecurring: true, amount: Mensualite);

        var secretaire = await SecretaireTokenAsync();
        var enrollResponse = await SendAsync(HttpMethod.Post, "/api/v1/enrollments", secretaire, new
        {
            type = "NewEnrollment",
            classroomId = classroom.Id,
            fullName = "Awa Ndiaye",
            birthDate = "2015-05-20",
            birthPlace = "Dakar",
            gender = "F"
        });
        enrollResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var receipt = (await enrollResponse.Content.ReadFromJsonAsync<Receipt>())!;
        receipt.TotalDue.Should().Be(ExpectedTotal);
        return receipt;
    }

    private static object PaymentBody(Guid enrollmentId, decimal amount, string method = "Cash", string category = "Tuition") =>
        new { enrollmentId, amount, method, category };

    private async Task OpenFinanceSessionAsync(string token)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/v1/finance/sessions/open", token, new { openingBalance = 0m });
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Résout un élève par matricule, comme le fera la recherche de l'écran caisse.</summary>
    private async Task<Guid> ResolveStudentIdAsync(string token, string matricule)
    {
        var response = await SendAsync(HttpMethod.Get, $"/api/v1/students?search={matricule}", token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = (await response.Content.ReadFromJsonAsync<PaginatedStudents>())!;
        return page.Items.Single(s => s.Matricule == matricule).Id;
    }

    [Fact]
    public async Task The_Student_Balance_Endpoint_Reflects_The_Active_Enrollment()
    {
        var enrollment = await SeedEnrolledStudentAsync();
        var finance = await FinanceTokenAsync();
        await OpenFinanceSessionAsync(finance);
        var studentId = await ResolveStudentIdAsync(finance, enrollment.Matricule);

        var before = await SendAsync(HttpMethod.Get, $"/api/v1/finance/students/{studentId}/balance", finance);
        before.StatusCode.Should().Be(HttpStatusCode.OK);
        var balanceBefore = (await before.Content.ReadFromJsonAsync<StudentBalance>())!;
        balanceBefore.EnrollmentId.Should().Be(enrollment.EnrollmentId);
        balanceBefore.TotalDue.Should().Be(ExpectedTotal);
        balanceBefore.AmountPaid.Should().Be(0m);
        balanceBefore.RemainingBalance.Should().Be(ExpectedTotal);
        balanceBefore.Status.Should().Be("Partial");

        var pay = await SendAsync(HttpMethod.Post, "/api/v1/finance/payments", finance,
            PaymentBody(enrollment.EnrollmentId, 60_000m));
        pay.StatusCode.Should().Be(HttpStatusCode.Created);

        var after = await SendAsync(HttpMethod.Get, $"/api/v1/finance/students/{studentId}/balance", finance);
        var balanceAfter = (await after.Content.ReadFromJsonAsync<StudentBalance>())!;
        balanceAfter.AmountPaid.Should().Be(60_000m);
        balanceAfter.RemainingBalance.Should().Be(ExpectedTotal - 60_000m);
    }

    [Fact]
    public async Task The_Balance_Of_An_Unknown_Student_Returns_404()
    {
        var directeur = await DirecteurTokenAsync();

        var response = await SendAsync(
            HttpMethod.Get, $"/api/v1/finance/students/{Guid.NewGuid()}/balance", directeur);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Finance_Records_A_Partial_Payment_And_Gets_A_Receipt_Number_And_Balance()
    {
        var enrollment = await SeedEnrolledStudentAsync();
        var finance = await FinanceTokenAsync();
        await OpenFinanceSessionAsync(finance);

        var response = await SendAsync(HttpMethod.Post, "/api/v1/finance/payments", finance,
            PaymentBody(enrollment.EnrollmentId, 50_000m, "MobileMoney"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var result = (await response.Content.ReadFromJsonAsync<PaymentResult>())!;

        result.ReceiptNumber.Should().MatchRegex(@"^REC-\d{4}-\d{4}$", "le reçu officiel est numéroté comme celui d'inscription");
        result.Amount.Should().Be(50_000m);
        result.RemainingBalance.Should().Be(ExpectedTotal - 50_000m);
        result.Status.Should().Be("Partial", "il reste un solde après ce versement");
    }

    [Fact]
    public async Task Paying_The_Full_Balance_Settles_It_And_A_Further_Payment_Is_Refused()
    {
        var enrollment = await SeedEnrolledStudentAsync();
        var finance = await FinanceTokenAsync();
        await OpenFinanceSessionAsync(finance);

        var full = await SendAsync(HttpMethod.Post, "/api/v1/finance/payments", finance,
            PaymentBody(enrollment.EnrollmentId, ExpectedTotal));
        full.StatusCode.Should().Be(HttpStatusCode.Created);
        var settled = (await full.Content.ReadFromJsonAsync<PaymentResult>())!;
        settled.RemainingBalance.Should().Be(0m);
        settled.Status.Should().Be("Paid", "le solde est nul après le versement intégral");

        // Une inscription déjà soldée n'accepte plus d'encaissement.
        var again = await SendAsync(HttpMethod.Post, "/api/v1/finance/payments", finance,
            PaymentBody(enrollment.EnrollmentId, 1_000m));
        again.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_Payment_Larger_Than_The_Remaining_Balance_Is_Refused()
    {
        var enrollment = await SeedEnrolledStudentAsync();
        var finance = await FinanceTokenAsync();
        await OpenFinanceSessionAsync(finance);

        var response = await SendAsync(HttpMethod.Post, "/api/v1/finance/payments", finance,
            PaymentBody(enrollment.EnrollmentId, ExpectedTotal + 1m));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_Secretariat_With_An_Open_Session_Can_Record_A_Payment()
    {
        // Le Secrétariat tient sa propre caisse (Volume 7) : session ouverte à son nom, il encaisse
        // comme la Finance. La règle #4 lui interdit la santé financière agrégée, pas l'encaissement.
        var enrollment = await SeedEnrolledStudentAsync();

        var secretaire = await SecretaireTokenAsync();
        await OpenFinanceSessionAsync(secretaire);

        var response = await SendAsync(HttpMethod.Post, "/api/v1/finance/payments", secretaire,
            PaymentBody(enrollment.EnrollmentId, 30_000m));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var result = (await response.Content.ReadFromJsonAsync<PaymentResult>())!;
        result.ReceiptNumber.Should().MatchRegex(@"^REC-\d{4}-\d{4}$");
        result.Amount.Should().Be(30_000m);
    }

    [Fact]
    public async Task Recording_A_Payment_Without_Any_Open_Session_Is_Refused()
    {
        // Invariant de caisse (Volume 1 §14) : pas de session ouverte pour l'opérateur ⇒ 422, quel que
        // soit le rôle. Vérifié ici pour la Finance, hors de tout OpenFinanceSessionAsync.
        var enrollment = await SeedEnrolledStudentAsync();
        var finance = await FinanceTokenAsync();

        var response = await SendAsync(HttpMethod.Post, "/api/v1/finance/payments", finance,
            PaymentBody(enrollment.EnrollmentId, 5_000m));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task The_Payment_Receipt_Can_Be_Downloaded_As_A_Pdf()
    {
        var enrollment = await SeedEnrolledStudentAsync();
        var finance = await FinanceTokenAsync();
        await OpenFinanceSessionAsync(finance);

        var payResponse = await SendAsync(HttpMethod.Post, "/api/v1/finance/payments", finance,
            PaymentBody(enrollment.EnrollmentId, 25_000m));
        payResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var result = (await payResponse.Content.ReadFromJsonAsync<PaymentResult>())!;

        var pdfResponse = await SendAsync(
            HttpMethod.Get, $"/api/v1/finance/payments/{result.PaymentId}/receipt/pdf", finance);

        pdfResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        pdfResponse.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        pdfResponse.Content.Headers.ContentDisposition!.FileName.Should().Contain(result.ReceiptNumber);

        var bytes = await pdfResponse.Content.ReadAsByteArrayAsync();
        Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-", "l'en-tête magique d'un PDF");
    }

    [Fact]
    public async Task Listing_Payments_Is_Reserved_To_Directeur_And_Finance()
    {
        // GET /finance/payments expose montants, méthodes et élèves pour toute l'école — pas un reçu
        // individuel où le tenant vient du JWT. Un Enseignant ne doit pas pouvoir la parcourir.
        var enseignant = await EnseignantTokenAsync();

        var response = await SendAsync(HttpMethod.Get, "/api/v1/finance/payments", enseignant);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Finance_Can_List_Payments()
    {
        var enrollment = await SeedEnrolledStudentAsync();
        var finance = await FinanceTokenAsync();
        await OpenFinanceSessionAsync(finance);

        var pay = await SendAsync(HttpMethod.Post, "/api/v1/finance/payments", finance,
            PaymentBody(enrollment.EnrollmentId, 30_000m));
        pay.StatusCode.Should().Be(HttpStatusCode.Created);

        var response = await SendAsync(HttpMethod.Get, "/api/v1/finance/payments", finance);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Paying_An_Unknown_Enrollment_Returns_404()
    {
        var finance = await FinanceTokenAsync();
        await OpenFinanceSessionAsync(finance);

        var response = await SendAsync(HttpMethod.Post, "/api/v1/finance/payments", finance,
            PaymentBody(Guid.NewGuid(), 5_000m));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Cashier_Search_And_Balance_Expose_The_Due_Now_Total_Not_The_Annual_Total()
    {
        var enrollment = await SeedEnrolledStudentAsync();
        var finance = await FinanceTokenAsync();
        await OpenFinanceSessionAsync(finance);

        // La recherche caisse : le badge affiche le MONTANT ÉCHU (inscription + 1er mois), jamais le
        // cumul annuel.
        var searchResponse = await SendAsync(
            HttpMethod.Get, $"/api/v1/finance/students/search?q={enrollment.Matricule}", finance);
        searchResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var hits = (await searchResponse.Content.ReadFromJsonAsync<List<CashierSearchResult>>())!;
        var hit = hits.Single(h => h.Matricule == enrollment.Matricule);
        hit.TotalDue.Should().Be(ExpectedTotal);        // le cumul annuel existe toujours…
        hit.DueNowTotal.Should().Be(ExpectedDueNow);    // …mais ce n'est pas ce que le badge montre

        // Le solde détaillé porte le même montant échu et marque les échéances de l'engagement initial.
        var studentId = await ResolveStudentIdAsync(finance, enrollment.Matricule);
        var balanceResponse = await SendAsync(
            HttpMethod.Get, $"/api/v1/finance/students/{studentId}/balance", finance);
        var balance = (await balanceResponse.Content.ReadFromJsonAsync<StudentBalance>())!;
        balance.DueNowTotal.Should().Be(ExpectedDueNow);

        var initialScope = balance.Installments.Where(i => i.IsInitialScope).ToList();
        initialScope.Select(i => i.Designation).Should().BeEquivalentTo(["Inscription", "Mensualité (Mois 1)"]);
        initialScope.Should().OnlyContain(i => i.FeeCategoryId != null);
        balance.Installments.Where(i => !i.IsInitialScope)
            .Should().OnlyContain(i => i.Designation.StartsWith("Mensualité (Mois "));
    }

    [Fact]
    public async Task Cashier_Search_And_Balance_Flag_The_Overdue_Months_Once_The_Year_Has_Started()
    {
        // Année commencée il y a 75 jours : les mensualités des mois 1, 2 et 3 sont échues, pas le mois 4.
        var enrollment = await SeedEnrolledStudentAsync(yearStartedDaysAgo: 75);
        var finance = await FinanceTokenAsync();
        await OpenFinanceSessionAsync(finance);
        const decimal expectedDueNow = Inscription + Mensualite * 3;

        var studentId = await ResolveStudentIdAsync(finance, enrollment.Matricule);
        var balance = (await (await SendAsync(
            HttpMethod.Get, $"/api/v1/finance/students/{studentId}/balance", finance))
            .Content.ReadFromJsonAsync<StudentBalance>())!;
        balance.DueNowTotal.Should().Be(expectedDueNow);
        balance.Installments.Where(i => i.IsDueNow).Select(i => i.Designation).Should().BeEquivalentTo(
            ["Inscription", "Mensualité (Mois 1)", "Mensualité (Mois 2)", "Mensualité (Mois 3)"]);

        // Le tuteur règle l'engagement initial seulement : les mois 2 et 3, échus, doivent rester signalés.
        var initial = balance.Installments.Where(i => i.IsInitialScope).ToList();
        var payResponse = await SendAsync(HttpMethod.Post, "/api/v1/finance/payments", finance, new
        {
            enrollmentId = enrollment.EnrollmentId,
            amount = Inscription + Mensualite,
            method = "Cash",
            breakdowns = initial.Select(i => new { feeCategoryId = i.FeeCategoryId, amountAllocated = i.RemainingDue, label = (string?)null }).ToArray()
        });
        payResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var searchResponse = await SendAsync(
            HttpMethod.Get, $"/api/v1/finance/students/search?q={enrollment.Matricule}", finance);
        var hit = (await searchResponse.Content.ReadFromJsonAsync<List<CashierSearchResult>>())!
            .Single(h => h.Matricule == enrollment.Matricule);
        hit.DueNowTotal.Should().Be(Mensualite * 2, "les mois 2 et 3 sont échus et impayés");

        var after = (await (await SendAsync(
            HttpMethod.Get, $"/api/v1/finance/students/{studentId}/balance", finance))
            .Content.ReadFromJsonAsync<StudentBalance>())!;
        after.DueNowTotal.Should().Be(Mensualite * 2);
    }

    [Fact]
    public async Task Settling_The_Due_Fees_In_One_Payment_Ventilates_The_Receipt_And_Marks_Each_Line_Paid()
    {
        var enrollment = await SeedEnrolledStudentAsync();
        var finance = await FinanceTokenAsync();
        await OpenFinanceSessionAsync(finance);

        var studentId = await ResolveStudentIdAsync(finance, enrollment.Matricule);
        var balance = (await (await SendAsync(
            HttpMethod.Get, $"/api/v1/finance/students/{studentId}/balance", finance))
            .Content.ReadFromJsonAsync<StudentBalance>())!;

        // Un SEUL versement pour toutes les échéances dues, ventilé poste par poste (le guichet rapide
        // envoie exactement cela).
        var due = balance.Installments.Where(i => i.IsInitialScope).ToList();
        var breakdowns = due.Select(i => new
        {
            feeCategoryId = i.FeeCategoryId,
            amountAllocated = i.RemainingDue,
            label = i.Designation.Contains("(Mois 1)") ? "Mois 1" : (string?)null
        }).ToArray();

        var payResponse = await SendAsync(HttpMethod.Post, "/api/v1/finance/payments", finance, new
        {
            enrollmentId = enrollment.EnrollmentId,
            amount = ExpectedDueNow,
            method = "Cash",
            breakdowns
        });
        payResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var result = (await payResponse.Content.ReadFromJsonAsync<PaymentResult>())!;

        // UN reçu, deux lignes qui totalisent exactement l'encaissement.
        var receipt = (await (await SendAsync(
            HttpMethod.Get, $"/api/v1/finance/payments/{result.PaymentId}/receipt", finance))
            .Content.ReadFromJsonAsync<PaymentReceipt>())!;
        receipt.Amount.Should().Be(ExpectedDueNow);
        receipt.HasBalancedLines.Should().BeTrue();
        receipt.Lines.Should().HaveCount(2);
        receipt.Lines.Sum(l => l.Amount).Should().Be(ExpectedDueNow);
        receipt.Lines.Select(l => l.Designation).Should().BeEquivalentTo(["Inscription", "Mensualité"]);
        receipt.Lines.Single(l => l.Designation == "Mensualité").Label.Should().Be("Mois 1");

        // Les postes réglés passent individuellement à « Réglé » ; le mois 2 reste en attente.
        var after = (await (await SendAsync(
            HttpMethod.Get, $"/api/v1/finance/students/{studentId}/balance", finance))
            .Content.ReadFromJsonAsync<StudentBalance>())!;
        after.DueNowTotal.Should().Be(0m);
        after.Installments.Single(i => i.Designation == "Inscription").Status.Should().Be("Paid");
        after.Installments.Single(i => i.Designation == "Mensualité (Mois 1)").Status.Should().Be("Paid");
        // Le mois suivant n'est pas touché — encore dû (échu ou à venir selon la date), jamais « Réglé ».
        var monthTwo = after.Installments.Single(i => i.Designation == "Mensualité (Mois 2)");
        monthTwo.Status.Should().BeOneOf("Pending", "Overdue");
        monthTwo.RemainingDue.Should().Be(Mensualite);
    }

    private record CancelResult(
        Guid PaymentId, string ReceiptNumber, decimal CancelledAmount, decimal TotalDue,
        decimal AmountPaid, decimal RemainingBalance, bool IsReversal = false);

    [Fact]
    public async Task Directeur_And_Finance_Can_Cancel_A_Payment_But_Not_The_Secretariat()
    {
        var enrollment = await SeedEnrolledStudentAsync();
        var finance = await FinanceTokenAsync();
        await OpenFinanceSessionAsync(finance);
        var pay = await SendAsync(HttpMethod.Post, "/api/v1/finance/payments", finance,
            PaymentBody(enrollment.EnrollmentId, 60_000m));
        pay.StatusCode.Should().Be(HttpStatusCode.Created);
        var payment = (await pay.Content.ReadFromJsonAsync<PaymentResult>())!;

        var cancelUrl = $"/api/v1/finance/payments/{payment.PaymentId}/cancel";

        // Le Secrétariat encaisse mais n'annule pas : Directeur et Finance (le comptable) uniquement.
        (await SendAsync(HttpMethod.Post, cancelUrl, await SecretaireTokenAsync(), new { reason = "Erreur de saisie" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var directeur = await DirecteurTokenAsync();

        // Le motif est obligatoire.
        (await SendAsync(HttpMethod.Post, cancelUrl, directeur, new { reason = "  " }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        // Le comptable corrige sans attendre la direction.
        var cancel = await SendAsync(HttpMethod.Post, cancelUrl, finance, new { reason = "Saisi sur le mauvais élève" });
        cancel.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await cancel.Content.ReadFromJsonAsync<CancelResult>())!;
        result.ReceiptNumber.Should().Be(payment.ReceiptNumber);
        result.AmountPaid.Should().Be(0m);
        result.RemainingBalance.Should().Be(ExpectedTotal);

        // Le reçu annulé reste consultable : la ligne n'a pas été effacée.
        (await SendAsync(HttpMethod.Get, $"/api/v1/finance/payments/{payment.PaymentId}/receipt", directeur))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Une seconde annulation est refusée, et ne retire pas le montant deux fois.
        (await SendAsync(HttpMethod.Post, cancelUrl, directeur, new { reason = "Encore" }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Theory]
    [InlineData("KeptBySchool")]
    [InlineData("ReusedForEnrollment")]
    [InlineData("RefundedToParent")]
    public async Task The_Destination_Of_The_Money_Is_Accepted_As_A_String(string outcome)
    {
        var enrollment = await SeedEnrolledStudentAsync();
        var finance = await FinanceTokenAsync();
        await OpenFinanceSessionAsync(finance);
        var pay = await SendAsync(HttpMethod.Post, "/api/v1/finance/payments", finance,
            PaymentBody(enrollment.EnrollmentId, 10_000m));
        var payment = (await pay.Content.ReadFromJsonAsync<PaymentResult>())!;

        var cancel = await SendAsync(HttpMethod.Post, $"/api/v1/finance/payments/{payment.PaymentId}/cancel", finance,
            new { reason = "Argent réutilisé", outcome });

        cancel.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_Unknown_Destination_Of_The_Money_Is_Refused()
    {
        var enrollment = await SeedEnrolledStudentAsync();
        var finance = await FinanceTokenAsync();
        await OpenFinanceSessionAsync(finance);
        var pay = await SendAsync(HttpMethod.Post, "/api/v1/finance/payments", finance,
            PaymentBody(enrollment.EnrollmentId, 10_000m));
        var payment = (await pay.Content.ReadFromJsonAsync<PaymentResult>())!;

        var cancel = await SendAsync(HttpMethod.Post, $"/api/v1/finance/payments/{payment.PaymentId}/cancel", finance,
            new { reason = "Erreur", outcome = "Donné à un tiers" });

        cancel.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Cancelling_An_Unknown_Payment_Returns_404()
    {
        var directeur = await DirecteurTokenAsync();

        var response = await SendAsync(
            HttpMethod.Post, $"/api/v1/finance/payments/{Guid.NewGuid()}/cancel", directeur, new { reason = "Erreur" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ----------------------------------------------------------------------------- Réglage du Directeur : la Finance peut-elle annuler ?

    private async Task SetFinanceMayCancelAsync(string directeurToken, bool allow)
    {
        var response = await SendAsync(HttpMethod.Put, "/api/v1/schools/current/settings", directeurToken, new
        {
            gradingScale = "20",
            studentMatriculeFormat = "ELEV-{YEAR}-{SEQ:4}",
            teacherMatriculeFormat = "ENS-{YEAR}-{SEQ:3}",
            autoLogoutMinutes = 10,
            dateFormat = "dd/MM/yyyy",
            tuitionMonthsPerYear = 9,
            allowSecretaryToManageGrading = false,
            allowFinanceToModifyFees = false,
            allowFinanceToDeleteFees = false,
            allowFinanceToCancelPayments = allow
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<PaymentResult> PayAsync(string financeToken, Guid enrollmentId, decimal amount)
    {
        var pay = await SendAsync(HttpMethod.Post, "/api/v1/finance/payments", financeToken, PaymentBody(enrollmentId, amount));
        pay.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await pay.Content.ReadFromJsonAsync<PaymentResult>())!;
    }

    [Fact]
    public async Task The_Director_Can_Withdraw_And_Restore_The_Finance_Right_To_Cancel_A_Payment()
    {
        var enrollment = await SeedEnrolledStudentAsync();
        var directeur = await DirecteurTokenAsync();
        var finance = await FinanceTokenAsync();
        await OpenFinanceSessionAsync(finance);
        var first = await PayAsync(finance, enrollment.EnrollmentId, 10_000m);
        var second = await PayAsync(finance, enrollment.EnrollmentId, 20_000m);

        // Par défaut le droit est OUVERT (le caissier corrige sa propre erreur) ; le Directeur peut le retirer.
        await SetFinanceMayCancelAsync(directeur, allow: false);
        (await SendAsync(HttpMethod.Post, $"/api/v1/finance/payments/{first.PaymentId}/cancel", finance, new { reason = "Erreur de saisie" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Post, $"/api/v1/finance/payments/{first.PaymentId}/cancel", directeur, new { reason = "Erreur de saisie" }))
            .StatusCode.Should().Be(HttpStatusCode.OK, "le Directeur n'est jamais concerné par le réglage");

        await SetFinanceMayCancelAsync(directeur, allow: true);
        (await SendAsync(HttpMethod.Post, $"/api/v1/finance/payments/{second.PaymentId}/cancel", finance, new { reason = "Erreur de saisie" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_Setting_Is_Exposed_With_The_Open_Default_And_Survives_An_Older_Client_That_Omits_It()
    {
        var directeur = await DirecteurTokenAsync();

        var before = await (await SendAsync(HttpMethod.Get, "/api/v1/schools/current/settings", directeur)).Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        before.GetProperty("allowFinanceToCancelPayments").GetBoolean().Should().BeTrue("ouvert par défaut");

        await SetFinanceMayCancelAsync(directeur, allow: false);

        // Un ancien client n'envoie pas le champ : il ne doit pas réinitialiser le réglage.
        var oldClient = await SendAsync(HttpMethod.Put, "/api/v1/schools/current/settings", directeur, new
        {
            gradingScale = "20",
            studentMatriculeFormat = "ELEV-{YEAR}-{SEQ:4}",
            teacherMatriculeFormat = "ENS-{YEAR}-{SEQ:3}",
            autoLogoutMinutes = 10,
            dateFormat = "dd/MM/yyyy",
            tuitionMonthsPerYear = 9,
            allowSecretaryToManageGrading = false,
            allowFinanceToModifyFees = false,
            allowFinanceToDeleteFees = false
        });
        oldClient.StatusCode.Should().Be(HttpStatusCode.OK);

        var after = await (await SendAsync(HttpMethod.Get, "/api/v1/schools/current/settings", directeur)).Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        after.GetProperty("allowFinanceToCancelPayments").GetBoolean().Should().BeFalse();
    }

    // ----------------------------------------------------------------------------- Contre-passation (hors délai de 48 h)

    [Fact]
    public async Task Past_The_Grace_Period_Only_The_Director_Can_Pass_A_Reversal_And_Only_When_Asked_Explicitly()
    {
        var enrollment = await SeedEnrolledStudentAsync();
        var directeur = await DirecteurTokenAsync();
        var finance = await FinanceTokenAsync();
        await OpenFinanceSessionAsync(finance);
        var payment = await PayAsync(finance, enrollment.EnrollmentId, 40_000m);

        await _factory.SeedAsOwnerAsync(async db =>
        {
            var row = await db.Payments.IgnoreQueryFilters().SingleAsync(p => p.Id == payment.PaymentId);
            row.PaidAt = DateTimeOffset.UtcNow.AddHours(-72);
        });

        var url = $"/api/v1/finance/payments/{payment.PaymentId}/cancel";

        var ordinary = await SendAsync(HttpMethod.Post, url, directeur, new { reason = "Doublon constaté à l'audit" });
        ordinary.StatusCode.Should().Be(HttpStatusCode.Conflict, "hors délai, l'annulation ordinaire reste refusée");
        (await ordinary.Content.ReadAsStringAsync()).Should().Contain("PAYMENT_CANCELLATION_WINDOW_EXPIRED").And.Contain("contre-passation");

        (await SendAsync(HttpMethod.Post, url, finance, new { reason = "Tentative", reversal = true }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden, "la Finance ne peut pas passer de contre-passation");

        var reversal = await SendAsync(HttpMethod.Post, url, directeur, new { reason = "Doublon constaté à l'audit", reversal = true });
        reversal.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await reversal.Content.ReadFromJsonAsync<CancelResult>())!;
        result.IsReversal.Should().BeTrue();
        result.AmountPaid.Should().Be(0m);
    }
}
````


#### `tests/SamaEcole.FunctionalTests/Finance/PayrollEndpointsTests.cs`

````csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Xunit;

namespace SamaEcole.FunctionalTests.Finance;

/// <summary>
/// Module Comptabilité &amp; Fiscalité — Paie (Volume 1 §14). Surface HTTP jusqu'ici testée seulement en
/// dessous (PayrollCalculator en unitaire, isolation multi-tenant en intégration) : aucun test
/// fonctionnel n'exerçait le contrôle des rôles, le rejet d'un doublon mensuel, ni le cycle de vie d'un
/// contrat (modification, clôture) via l'API réelle. Critères couverts : rôles Directeur/Finance
/// uniquement ; anti-doublon de fiche par mois (verrou DB) ; verrou optimiste sur la modification d'un
/// contrat ; un contrat clôturé ne génère plus jamais de fiche ; la clôture n'est pas une suppression
/// (§14.1) et libère l'index unique pour un nouveau contrat sur la même personne.
/// </summary>
public class PayrollEndpointsTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    public Task InitializeAsync() => factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private record Tokens(string AccessToken, int ExpiresIn);
    private record ContractDto(
        Guid Id, Guid? TeacherId, Guid? UserId, string EmployeeFullName, string EmployeeRole,
        string Type, decimal BaseSalary, decimal HourlyRate, decimal TransportAllowance,
        DateOnly? EndDate, uint RowVersion);

    private async Task<string> TokenAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!.AccessToken;
    }

    private Task<string> DirecteurTokenAsync() => TokenAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);
    private Task<string> SecretariatTokenAsync() => TokenAsync(AuthApiFactory.SecretaireEmail, AuthApiFactory.SecretairePassword);

    private async Task<HttpResponseMessage> SendAsync(string? token, HttpMethod method, string path, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return await _client.SendAsync(request);
    }

    /// <summary>Contrat Permanent sur le compte Secrétariat semé (JGK-A05) : évite de dépendre du module Teachers.</summary>
    private async Task<ContractDto> CreatePermanentContractAsync(string directorToken, decimal baseSalary = 250_000m)
    {
        var response = await SendAsync(directorToken, HttpMethod.Post, "/api/v1/finance/employee-contracts", new
        {
            teacherId = (Guid?)null,
            userId = AuthApiFactory.SecretaireId,
            type = "Permanent",
            baseSalary,
            hourlyRate = 0m,
            transportAllowance = 15_000m
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var list = await SendAsync(directorToken, HttpMethod.Get, "/api/v1/finance/employee-contracts");
        var contracts = (await list.Content.ReadFromJsonAsync<List<ContractDto>>())!;
        return contracts.Single(c => c.UserId == AuthApiFactory.SecretaireId);
    }

    // ------------------------------------------------------------ Rôles

    [Fact]
    public async Task A_Secretariat_Must_Not_Be_Allowed_To_Read_Employee_Contracts()
    {
        var secretariat = await SecretariatTokenAsync();

        var response = await SendAsync(secretariat, HttpMethod.Get, "/api/v1/finance/employee-contracts");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_Secretariat_Must_Not_Be_Allowed_To_Generate_A_Payslip()
    {
        var secretariat = await SecretariatTokenAsync();

        var response = await SendAsync(secretariat, HttpMethod.Post, "/api/v1/finance/payroll", new
        {
            employeeContractId = Guid.NewGuid(),
            month = 3,
            year = 2026
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ------------------------------------------------------------ Cycle contrat -> fiche de paie

    [Fact]
    public async Task Directeur_Should_Create_A_Contract_And_Generate_Its_First_Payslip()
    {
        var director = await DirecteurTokenAsync();
        var contract = await CreatePermanentContractAsync(director);

        contract.Type.Should().Be("Permanent");
        contract.EndDate.Should().BeNull();

        var generate = await SendAsync(director, HttpMethod.Post, "/api/v1/finance/payroll", new
        {
            employeeContractId = contract.Id,
            month = 3,
            year = 2026,
            hoursWorked = 0,
            transportAllowance = 0
        });

        generate.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Generating_A_Second_Payslip_For_The_Same_Month_Should_Be_Rejected()
    {
        var director = await DirecteurTokenAsync();
        var contract = await CreatePermanentContractAsync(director);
        var payload = new { employeeContractId = contract.Id, month = 4, year = 2026, hoursWorked = 0, transportAllowance = 0 };

        var first = await SendAsync(director, HttpMethod.Post, "/api/v1/finance/payroll", payload);
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        var second = await SendAsync(director, HttpMethod.Post, "/api/v1/finance/payroll", payload);

        second.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "une seule fiche de paie par employé et par mois — verrouillé par un index unique en base");
    }

    // ------------------------------------------------------------ Modification (Volume 1 §14.1)

    [Fact]
    public async Task Updating_A_Contract_Should_Change_Its_Terms()
    {
        var director = await DirecteurTokenAsync();
        var contract = await CreatePermanentContractAsync(director, baseSalary: 250_000m);

        var response = await SendAsync(director, HttpMethod.Patch, $"/api/v1/finance/employee-contracts/{contract.Id}", new
        {
            baseSalary = 300_000m,
            hourlyRate = 0m,
            transportAllowance = 20_000m,
            reason = "Augmentation annuelle actée en conseil.",
            rowVersion = contract.RowVersion
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var list = await SendAsync(director, HttpMethod.Get, "/api/v1/finance/employee-contracts");
        var updated = (await list.Content.ReadFromJsonAsync<List<ContractDto>>())!.Single(c => c.Id == contract.Id);
        updated.BaseSalary.Should().Be(300_000m);
        updated.TransportAllowance.Should().Be(20_000m);
    }

    [Fact]
    public async Task Updating_A_Contract_With_A_Stale_RowVersion_Should_Return_409()
    {
        var director = await DirecteurTokenAsync();
        var contract = await CreatePermanentContractAsync(director);

        // Première modification, réussie : le jeton xmin change en base.
        await SendAsync(director, HttpMethod.Patch, $"/api/v1/finance/employee-contracts/{contract.Id}", new
        {
            baseSalary = 260_000m, hourlyRate = 0m, transportAllowance = 15_000m,
            reason = "Première révision.", rowVersion = contract.RowVersion
        });

        // Seconde modification avec le jeton PÉRIMÉ (celui lu avant la première modification).
        var stale = await SendAsync(director, HttpMethod.Patch, $"/api/v1/finance/employee-contracts/{contract.Id}", new
        {
            baseSalary = 999_999m, hourlyRate = 0m, transportAllowance = 15_000m,
            reason = "Seconde révision, jeton périmé.", rowVersion = contract.RowVersion
        });

        stale.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "un jeton xmin périmé doit être refusé plutôt que d'écraser la première modification");
    }

    [Fact]
    public async Task Updating_A_Contract_Without_A_Reason_Should_Return_422()
    {
        var director = await DirecteurTokenAsync();
        var contract = await CreatePermanentContractAsync(director);

        var response = await SendAsync(director, HttpMethod.Patch, $"/api/v1/finance/employee-contracts/{contract.Id}", new
        {
            baseSalary = 300_000m, hourlyRate = 0m, transportAllowance = 15_000m,
            reason = "", rowVersion = contract.RowVersion
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    // ------------------------------------------------------------ Clôture (Volume 1 §14.1)

    [Fact]
    public async Task Closing_A_Contract_Should_Prevent_Any_New_Payslip_Generation()
    {
        var director = await DirecteurTokenAsync();
        var contract = await CreatePermanentContractAsync(director);

        var close = await SendAsync(director, HttpMethod.Post, $"/api/v1/finance/employee-contracts/{contract.Id}/close", new
        {
            endDate = "2026-03-31",
            reason = "Fin de contrat — départ de l'établissement.",
            rowVersion = contract.RowVersion
        });
        close.StatusCode.Should().Be(HttpStatusCode.OK);

        var generate = await SendAsync(director, HttpMethod.Post, "/api/v1/finance/payroll", new
        {
            employeeContractId = contract.Id, month = 4, year = 2026, hoursWorked = 0, transportAllowance = 0
        });

        generate.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "un contrat clôturé ne doit plus jamais générer de nouvelle fiche de paie");
    }

    [Fact]
    public async Task Closing_A_Contract_Should_Not_Delete_It()
    {
        var director = await DirecteurTokenAsync();
        var contract = await CreatePermanentContractAsync(director);

        await SendAsync(director, HttpMethod.Post, $"/api/v1/finance/employee-contracts/{contract.Id}/close", new
        {
            endDate = "2026-03-31", reason = "Fin de contrat.", rowVersion = contract.RowVersion
        });

        var list = await SendAsync(director, HttpMethod.Get, "/api/v1/finance/employee-contracts");
        var contracts = (await list.Content.ReadFromJsonAsync<List<ContractDto>>())!;

        contracts.Should().Contain(c => c.Id == contract.Id,
            "AGENTS.md règle #6 : aucune suppression physique — un contrat clôturé reste consultable");
        contracts.Single(c => c.Id == contract.Id).EndDate.Should().Be(new DateOnly(2026, 3, 31));
    }

    [Fact]
    public async Task Closing_An_Already_Closed_Contract_Should_Be_Rejected()
    {
        var director = await DirecteurTokenAsync();
        var contract = await CreatePermanentContractAsync(director);

        var firstClose = await SendAsync(director, HttpMethod.Post, $"/api/v1/finance/employee-contracts/{contract.Id}/close", new
        {
            endDate = "2026-03-31", reason = "Fin de contrat.", rowVersion = contract.RowVersion
        });
        firstClose.StatusCode.Should().Be(HttpStatusCode.OK);

        var secondClose = await SendAsync(director, HttpMethod.Post, $"/api/v1/finance/employee-contracts/{contract.Id}/close", new
        {
            endDate = "2026-04-30", reason = "Nouvelle tentative.", rowVersion = contract.RowVersion
        });

        secondClose.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Updating_A_Closed_Contract_Should_Be_Rejected()
    {
        var director = await DirecteurTokenAsync();
        var contract = await CreatePermanentContractAsync(director);

        await SendAsync(director, HttpMethod.Post, $"/api/v1/finance/employee-contracts/{contract.Id}/close", new
        {
            endDate = "2026-03-31", reason = "Fin de contrat.", rowVersion = contract.RowVersion
        });

        var update = await SendAsync(director, HttpMethod.Patch, $"/api/v1/finance/employee-contracts/{contract.Id}", new
        {
            baseSalary = 500_000m, hourlyRate = 0m, transportAllowance = 0m,
            reason = "Tentative sur contrat clôturé.", rowVersion = contract.RowVersion
        });

        update.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Closing_A_Contract_Should_Free_Its_Slot_For_A_New_Contract_On_The_Same_Person()
    {
        // Volume 1 §14.1 : un départ suivi d'une reprise doit rester possible — l'index unique sur
        // UserId ne doit plus considérer un contrat clôturé comme actif.
        var director = await DirecteurTokenAsync();
        var contract = await CreatePermanentContractAsync(director);

        await SendAsync(director, HttpMethod.Post, $"/api/v1/finance/employee-contracts/{contract.Id}/close", new
        {
            endDate = "2026-03-31", reason = "Fin de contrat.", rowVersion = contract.RowVersion
        });

        var rehire = await SendAsync(director, HttpMethod.Post, "/api/v1/finance/employee-contracts", new
        {
            teacherId = (Guid?)null,
            userId = AuthApiFactory.SecretaireId,
            type = "Vacataire",
            baseSalary = 0m,
            hourlyRate = 5_000m,
            transportAllowance = 0m
        });

        rehire.StatusCode.Should().Be(HttpStatusCode.Created);
    }
}
````


#### `tests/SamaEcole.FunctionalTests/Finance/PayrollWithoutDeductionsEndpointsTests.cs`

````csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Xunit;

namespace SamaEcole.FunctionalTests.Finance;

/// <summary>
/// Paie « sans retenues » (structure qui ne cotise pas, ex. un Daara) via l'API réelle : le régime se règle par contrat, les
/// deux régimes coexistent dans une même école, chaque fiche garde un instantané du régime utilisé, et ne pas fournir le champ
/// ne change jamais le régime d'un contrat existant.
/// </summary>
public class PayrollWithoutDeductionsEndpointsTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    public Task InitializeAsync() => factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private record Tokens(string AccessToken, int ExpiresIn);
    private record ContractDto(
        Guid Id, Guid? UserId, string Type, decimal BaseSalary, decimal TransportAllowance,
        DateOnly? EndDate, uint RowVersion, bool ApplyStatutoryDeductions);
    private record FicheDto(
        Guid Id, Guid EmployeeContractId, decimal GrossSalary, decimal TransportAllowance, decimal IpresEmployee,
        decimal IpresEmployer, decimal CssEmployer, decimal Vrs, decimal Brs, decimal NetSalary, bool DeductionsApplied);

    private async Task<string> DirecteurTokenAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = AuthApiFactory.DirecteurEmail, password = AuthApiFactory.DirecteurPassword });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!.AccessToken;
    }

    private async Task<HttpResponseMessage> SendAsync(string token, HttpMethod method, string path, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(request);
    }

    /// <summary>Contrat Permanent sur le compte Secrétariat semé ; <paramref name="applyDeductions"/> null = champ omis (défaut serveur).</summary>
    private async Task<ContractDto> CreateContractAsync(string token, decimal baseSalary, decimal transport, bool? applyDeductions)
    {
        var body = new Dictionary<string, object?>
        {
            ["userId"] = AuthApiFactory.SecretaireId,
            ["type"] = "Permanent",
            ["baseSalary"] = baseSalary,
            ["hourlyRate"] = 0m,
            ["transportAllowance"] = transport
        };
        if (applyDeductions is { } apply) body["applyStatutoryDeductions"] = apply;

        var created = await SendAsync(token, HttpMethod.Post, "/api/v1/finance/employee-contracts", body);
        created.StatusCode.Should().Be(HttpStatusCode.Created);

        return await GetContractAsync(token);
    }

    private async Task<ContractDto> GetContractAsync(string token)
    {
        var list = await SendAsync(token, HttpMethod.Get, "/api/v1/finance/employee-contracts");
        return (await list.Content.ReadFromJsonAsync<List<ContractDto>>())!.Single(c => c.UserId == AuthApiFactory.SecretaireId);
    }

    private async Task<FicheDto> GenerateAndReadAsync(string token, Guid contractId, int month)
    {
        var generate = await SendAsync(token, HttpMethod.Post, "/api/v1/finance/payroll",
            new { employeeContractId = contractId, month, year = 2026, hoursWorked = 0, transportAllowance = 5_000 });
        generate.StatusCode.Should().Be(HttpStatusCode.Created);

        var list = await SendAsync(token, HttpMethod.Get, $"/api/v1/finance/payroll?month={month}&year=2026");
        return (await list.Content.ReadFromJsonAsync<List<FicheDto>>())!.Single(f => f.EmployeeContractId == contractId);
    }

    [Fact]
    public async Task A_Contract_Without_Deductions_Pays_The_Gross_Salary_Plus_Transport_With_Nothing_Withheld()
    {
        var director = await DirecteurTokenAsync();
        var contract = await CreateContractAsync(director, baseSalary: 40_000m, transport: 0m, applyDeductions: false);
        contract.ApplyStatutoryDeductions.Should().BeFalse();

        var fiche = await GenerateAndReadAsync(director, contract.Id, month: 3);

        fiche.DeductionsApplied.Should().BeFalse();
        fiche.GrossSalary.Should().Be(40_000m);
        fiche.NetSalary.Should().Be(45_000m, "40 000 de salaire + 5 000 de prime de transport, aucune retenue");
        fiche.IpresEmployee.Should().Be(0m);
        fiche.IpresEmployer.Should().Be(0m);
        fiche.CssEmployer.Should().Be(0m);
        fiche.Vrs.Should().Be(0m);
        fiche.Brs.Should().Be(0m);
    }

    [Fact]
    public async Task A_Contract_Created_Without_The_Field_Keeps_The_Standard_Deductions()
    {
        // Le comportement existant ne doit pas changer pour les écoles qui n'utilisent pas ce réglage.
        var director = await DirecteurTokenAsync();
        var contract = await CreateContractAsync(director, baseSalary: 250_000m, transport: 0m, applyDeductions: null);
        contract.ApplyStatutoryDeductions.Should().BeTrue();

        var fiche = await GenerateAndReadAsync(director, contract.Id, month: 4);

        fiche.DeductionsApplied.Should().BeTrue();
        fiche.IpresEmployee.Should().BeGreaterThan(0m);
        fiche.Brs.Should().BeGreaterThan(0m);
        fiche.NetSalary.Should().BeLessThan(fiche.GrossSalary + fiche.TransportAllowance);
    }

    [Fact]
    public async Task Switching_The_Regime_Applies_To_New_Payslips_But_Never_Rewrites_An_Existing_One()
    {
        var director = await DirecteurTokenAsync();
        var contract = await CreateContractAsync(director, baseSalary: 250_000m, transport: 0m, applyDeductions: true);
        var before = await GenerateAndReadAsync(director, contract.Id, month: 5);
        before.DeductionsApplied.Should().BeTrue();

        var update = await SendAsync(director, HttpMethod.Patch, $"/api/v1/finance/employee-contracts/{contract.Id}", new
        {
            baseSalary = 250_000m, hourlyRate = 0m, transportAllowance = 0m,
            reason = "Passage en paie sans retenues : l'établissement ne cotise pas.",
            rowVersion = contract.RowVersion, applyStatutoryDeductions = false
        });
        update.StatusCode.Should().Be(HttpStatusCode.OK);

        (await GetContractAsync(director)).ApplyStatutoryDeductions.Should().BeFalse();

        var stillBefore = (await (await SendAsync(director, HttpMethod.Get, "/api/v1/finance/payroll?month=5&year=2026"))
            .Content.ReadFromJsonAsync<List<FicheDto>>())!.Single();
        stillBefore.DeductionsApplied.Should().BeTrue("une fiche déjà éditée garde son régime");
        stillBefore.NetSalary.Should().Be(before.NetSalary);

        var after = await GenerateAndReadAsync(director, contract.Id, month: 6);
        after.DeductionsApplied.Should().BeFalse();
        after.NetSalary.Should().Be(255_000m);
    }

    [Fact]
    public async Task Updating_A_Contract_Without_The_Field_Must_Not_Change_Its_Regime()
    {
        // Un client qui ne connaît pas encore ce champ ne doit jamais rebasculer un contrat « sans retenues » vers « avec ».
        var director = await DirecteurTokenAsync();
        var contract = await CreateContractAsync(director, baseSalary: 40_000m, transport: 0m, applyDeductions: false);

        var update = await SendAsync(director, HttpMethod.Patch, $"/api/v1/finance/employee-contracts/{contract.Id}", new
        {
            baseSalary = 45_000m, hourlyRate = 0m, transportAllowance = 0m,
            reason = "Augmentation annuelle actée en conseil.", rowVersion = contract.RowVersion
        });
        update.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await GetContractAsync(director);
        updated.BaseSalary.Should().Be(45_000m);
        updated.ApplyStatutoryDeductions.Should().BeFalse();
    }

    [Fact]
    public async Task Regenerating_A_Payslip_Keeps_The_Regime_Of_The_Contract()
    {
        var director = await DirecteurTokenAsync();
        var contract = await CreateContractAsync(director, baseSalary: 40_000m, transport: 5_000m, applyDeductions: false);
        var fiche = await GenerateAndReadAsync(director, contract.Id, month: 7);

        // Se relit avec son jeton de version pour la régénération.
        var raw = await SendAsync(director, HttpMethod.Get, "/api/v1/finance/payroll?month=7&year=2026");
        var rowVersion = (await raw.Content.ReadFromJsonAsync<List<System.Text.Json.JsonElement>>())!
            .Single().GetProperty("rowVersion").GetUInt32();

        var regenerate = await SendAsync(director, HttpMethod.Post, $"/api/v1/finance/payroll/{fiche.Id}/regenerate", new
        {
            reason = "Heures corrigées après vérification.", rowVersion
        });
        regenerate.StatusCode.Should().Be(HttpStatusCode.OK, await regenerate.Content.ReadAsStringAsync());

        var current = (await (await SendAsync(director, HttpMethod.Get, "/api/v1/finance/payroll?month=7&year=2026"))
            .Content.ReadFromJsonAsync<List<System.Text.Json.JsonElement>>())!
            .Single(f => f.GetProperty("status").GetString() != "Cancelled");
        current.GetProperty("deductionsApplied").GetBoolean().Should().BeFalse();
        current.GetProperty("netSalary").GetDecimal().Should().Be(45_000m);
    }
}
````


#### `tests/SamaEcole.FunctionalTests/Finance/PayslipCancellationDelegationTests.cs`

````csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Xunit;

namespace SamaEcole.FunctionalTests.Finance;

/// <summary>
/// Volume 1 §14.5 — le Directeur peut retirer à la Finance le droit d'annuler ou de corriger une fiche de paie
/// (SchoolSettings.AllowFinanceToCancelPayslips, ouvert par défaut). Retiré : la Finance reçoit 403 sur l'annulation et la
/// régénération, mais peut toujours payer ; le Directeur garde la main dans tous les cas. Le serveur est l'arbitre, pas
/// l'écran.
/// </summary>
public class PayslipCancellationDelegationTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    public Task InitializeAsync() => factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private record Tokens(string AccessToken, int ExpiresIn);
    private record ContractDto(Guid Id, Guid? UserId, uint RowVersion);
    private record PayslipDto(Guid Id, Guid EmployeeContractId, int Month, int Year, string Status, uint RowVersion);

    private async Task<string> TokenAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!.AccessToken;
    }

    private Task<string> DirecteurTokenAsync() => TokenAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);
    private Task<string> FinanceTokenAsync() => TokenAsync(AuthApiFactory.FinanceEmail, AuthApiFactory.FinancePassword);

    private async Task<HttpResponseMessage> SendAsync(string token, HttpMethod method, string path, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await _client.SendAsync(request);
    }

    private async Task SetFinanceMayCancelPayslipsAsync(string directeurToken, bool allowed)
    {
        var response = await SendAsync(directeurToken, HttpMethod.Put, "/api/v1/schools/current/settings", new
        {
            gradingScale = "20",
            studentMatriculeFormat = "ELEV-{YEAR}-{SEQ:4}",
            teacherMatriculeFormat = "ENS-{YEAR}-{SEQ:3}",
            autoLogoutMinutes = 10,
            dateFormat = "dd/MM/yyyy",
            tuitionMonthsPerYear = 9,
            allowSecretaryToManageGrading = false,
            allowFinanceToModifyFees = false,
            allowFinanceToDeleteFees = false,
            allowFinanceToCancelPayslips = allowed
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>Contrat sur le compte Secrétariat semé, puis une fiche pour le mois donné ; renvoie la fiche telle que listée.</summary>
    private async Task<PayslipDto> CreatePayslipAsync(string directeurToken, int month)
    {
        var contractResponse = await SendAsync(directeurToken, HttpMethod.Post, "/api/v1/finance/employee-contracts", new
        {
            userId = AuthApiFactory.SecretaireId,
            type = "Permanent",
            baseSalary = 250_000m,
            hourlyRate = 0m,
            transportAllowance = 0m
        });
        if (contractResponse.StatusCode != HttpStatusCode.Created)
        {
            // Le contrat actif existe déjà d'un test précédent de la même classe : on le réutilise.
            contractResponse.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        }

        var contracts = (await (await SendAsync(directeurToken, HttpMethod.Get, "/api/v1/finance/employee-contracts"))
            .Content.ReadFromJsonAsync<List<ContractDto>>())!;
        var contract = contracts.First(c => c.UserId == AuthApiFactory.SecretaireId);

        var generate = await SendAsync(directeurToken, HttpMethod.Post, "/api/v1/finance/payroll",
            new { employeeContractId = contract.Id, month, year = 2026, hoursWorked = 0m, transportAllowance = 0m });
        generate.StatusCode.Should().Be(HttpStatusCode.Created);

        var payslips = (await (await SendAsync(directeurToken, HttpMethod.Get, $"/api/v1/finance/payroll?month={month}&year=2026"))
            .Content.ReadFromJsonAsync<List<PayslipDto>>())!;
        return payslips.Single(p => p.Status == "Generated");
    }

    [Fact]
    public async Task Finance_Can_Cancel_A_Payslip_By_Default()
    {
        var directeur = await DirecteurTokenAsync();
        var payslip = await CreatePayslipAsync(directeur, 3);

        var finance = await FinanceTokenAsync();
        var response = await SendAsync(finance, HttpMethod.Post, $"/api/v1/finance/payroll/{payslip.Id}/cancel",
            new { reason = "Montant erroné sur le contrat.", rowVersion = payslip.RowVersion });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Finance_Cannot_Cancel_Or_Regenerate_Once_The_Director_Withdraws_The_Right_But_Can_Still_Pay()
    {
        var directeur = await DirecteurTokenAsync();
        var payslip = await CreatePayslipAsync(directeur, 4);
        await SetFinanceMayCancelPayslipsAsync(directeur, allowed: false);

        var finance = await FinanceTokenAsync();

        var cancel = await SendAsync(finance, HttpMethod.Post, $"/api/v1/finance/payroll/{payslip.Id}/cancel",
            new { reason = "Tentative de la Finance.", rowVersion = payslip.RowVersion });
        cancel.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var regenerate = await SendAsync(finance, HttpMethod.Post, $"/api/v1/finance/payroll/{payslip.Id}/regenerate",
            new { reason = "Tentative de la Finance.", rowVersion = payslip.RowVersion });
        regenerate.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Payer n'est pas concerné par le retrait : la Finance garde ce geste.
        var pay = await SendAsync(finance, HttpMethod.Post, $"/api/v1/finance/payroll/{payslip.Id}/pay",
            new { paymentMethod = "Cash", date = "2026-04-30", rowVersion = payslip.RowVersion });
        pay.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_Director_Can_Always_Cancel_Even_When_The_Right_Is_Withdrawn_From_Finance()
    {
        var directeur = await DirecteurTokenAsync();
        var payslip = await CreatePayslipAsync(directeur, 5);
        await SetFinanceMayCancelPayslipsAsync(directeur, allowed: false);

        var response = await SendAsync(directeur, HttpMethod.Post, $"/api/v1/finance/payroll/{payslip.Id}/cancel",
            new { reason = "Fiche générée par erreur.", rowVersion = payslip.RowVersion });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
````


#### `tests/SamaEcole.FunctionalTests/Deletion/InfraFeesInventoryDeletionEndpointsTests.cs`

````csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace SamaEcole.FunctionalTests.Deletion;

/// <summary>
/// Suppression guidée des bâtiments, salles, catégories de frais, catégories d'inventaire et biens, de bout en bout
/// contre un vrai PostgreSQL (spec 2026-10-17, §6) : liens énumérés, rupture explicite, blocage financier, accès par
/// rôle, élément inconnu.
/// </summary>
public class InfraFeesInventoryDeletionEndpointsTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public InfraFeesInventoryDeletionEndpointsTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public Task InitializeAsync() => _factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private record Tokens(string AccessToken, int ExpiresIn);
    private record Created(Guid Id);
    private record Versioned(Guid Id, uint RowVersion);
    private record Link(string Kind, string Label, int Count, bool Breakable, string BreakAction);
    private record Impact(bool CanDelete, bool FinancialBlock, string? Message, List<Link> Links);
    private record ApiError(string Code, string Message);

    private async Task<string> TokenAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!.AccessToken;
    }

    private Task<string> DirecteurTokenAsync() => TokenAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);
    private Task<string> SecretaireTokenAsync() => TokenAsync(AuthApiFactory.SecretaireEmail, AuthApiFactory.SecretairePassword);
    private Task<string> FinanceTokenAsync() => TokenAsync(AuthApiFactory.FinanceEmail, AuthApiFactory.FinancePassword);

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await _client.SendAsync(request);
    }

    private async Task<Versioned> CreateAsync(string token, string url, object body)
    {
        var response = await SendAsync(HttpMethod.Post, url, token, body);
        response.StatusCode.Should().Be(HttpStatusCode.Created, url);
        return (await response.Content.ReadFromJsonAsync<Versioned>())!;
    }

    private async Task<Impact> ImpactAsync(string token, string route)
    {
        var response = await SendAsync(HttpMethod.Get, $"/api/v1/{route}/deletion-impact", token);
        response.StatusCode.Should().Be(HttpStatusCode.OK, route);
        return (await response.Content.ReadFromJsonAsync<Impact>())!;
    }

    private Task<Versioned> CreateBuildingAsync(string token, string name)
        => CreateAsync(token, "/api/v1/buildings", new { name, description = "Test" });

    private Task<Versioned> CreateRoomAsync(string token, string name, Guid buildingId)
        => CreateAsync(token, "/api/v1/rooms", new { name, capacity = 30, type = "SalleDeClasse", buildingId });

    private Task<Versioned> CreateInventoryCategoryAsync(string token, string name)
        => CreateAsync(token, "/api/v1/inventory/categories", new { name, description = "Test" });

    private Task<Versioned> CreateItemAsync(string token, string name, Guid categoryId, Guid? roomId = null)
        => CreateAsync(token, "/api/v1/inventory/items", new { name, categoryId, initialQuantity = 5, roomId });

    // ------------------------------------------------------------------------------------------ Bâtiments et salles

    [Fact]
    public async Task A_Building_With_A_Room_Is_Blocked_Until_The_Room_Is_Deleted()
    {
        var directeur = await DirecteurTokenAsync();
        var building = await CreateBuildingAsync(directeur, "Bâtiment Chargé");
        var room = await CreateRoomAsync(directeur, "Salle 12", building.Id);

        var impact = await ImpactAsync(directeur, $"buildings/{building.Id}");
        impact.CanDelete.Should().BeFalse();
        impact.Links.Should().ContainSingle(l => l.Kind == "Room" && !l.Breakable).Which.Label.Should().Be("1 salle");

        var blocked = await SendAsync(HttpMethod.Delete, $"/api/v1/buildings/{building.Id}?rowVersion={building.RowVersion}", directeur);
        blocked.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var error = (await blocked.Content.ReadFromJsonAsync<ApiError>())!;
        error.Code.Should().Be("DEPENDENCIES_EXIST");
        error.Message.Should().Contain("1 salle").And.Contain("Vous devez rompre ces liens");

        var unlink = await SendAsync(HttpMethod.Post, $"/api/v1/buildings/{building.Id}/unlink-dependencies", directeur);
        unlink.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await unlink.Content.ReadFromJsonAsync<ApiError>())!.Code.Should().Be("DEPENDENCIES_NOT_BREAKABLE");

        (await SendAsync(HttpMethod.Delete, $"/api/v1/rooms/{room.Id}?rowVersion={room.RowVersion}", directeur))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await ImpactAsync(directeur, $"buildings/{building.Id}")).CanDelete.Should().BeTrue();
        (await SendAsync(HttpMethod.Delete, $"/api/v1/buildings/{building.Id}?rowVersion={building.RowVersion}", directeur))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task A_Room_Holding_Inventory_Items_Is_Unlinked_Then_Deleted_Without_Losing_The_Items()
    {
        var directeur = await DirecteurTokenAsync();
        var building = await CreateBuildingAsync(directeur, "Bâtiment Stock");
        var room = await CreateRoomAsync(directeur, "Réserve", building.Id);
        var category = await CreateInventoryCategoryAsync(directeur, "Manuels");
        var item = await CreateItemAsync(directeur, "Manuel de maths", category.Id, room.Id);

        var blocked = await SendAsync(HttpMethod.Delete, $"/api/v1/rooms/{room.Id}?rowVersion={room.RowVersion}", directeur);
        blocked.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await blocked.Content.ReadFromJsonAsync<ApiError>())!.Code.Should().Be("DEPENDENCIES_EXIST");

        var impact = await ImpactAsync(directeur, $"rooms/{room.Id}");
        impact.Links.Should().ContainSingle(l => l.Kind == "InventoryItem" && l.Breakable);

        (await SendAsync(HttpMethod.Post, $"/api/v1/rooms/{room.Id}/unlink-dependencies", directeur)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Delete, $"/api/v1/rooms/{room.Id}?rowVersion={room.RowVersion}", directeur))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Le bien existe toujours : seul son emplacement a été vidé.
        (await SendAsync(HttpMethod.Get, $"/api/v1/inventory/items/{item.Id}", directeur)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ------------------------------------------------------------------------------------------ Inventaire

    [Fact]
    public async Task An_Inventory_Category_With_Items_Is_Blocked_Until_The_Items_Are_Deleted()
    {
        var directeur = await DirecteurTokenAsync();
        var category = await CreateInventoryCategoryAsync(directeur, "Mobilier");
        var item = await CreateItemAsync(directeur, "Chaise", category.Id);

        var impact = await ImpactAsync(directeur, $"inventory/categories/{category.Id}");
        impact.CanDelete.Should().BeFalse();
        impact.Links.Should().ContainSingle(l => l.Kind == "InventoryItem" && !l.Breakable);

        (await SendAsync(HttpMethod.Delete, $"/api/v1/inventory/categories/{category.Id}?rowVersion={category.RowVersion}", directeur))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await ImpactAsync(directeur, $"inventory/items/{item.Id}")).CanDelete.Should().BeTrue();
        (await SendAsync(HttpMethod.Delete, $"/api/v1/inventory/items/{item.Id}?rowVersion={item.RowVersion}", directeur))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await SendAsync(HttpMethod.Delete, $"/api/v1/inventory/categories/{category.Id}?rowVersion={category.RowVersion}", directeur))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ------------------------------------------------------------------------------------------ Catégories de frais

    [Fact]
    public async Task A_Fee_Category_With_A_Fee_Schedule_Is_Unlinked_Then_Deleted()
    {
        var directeur = await DirecteurTokenAsync();
        (await SendAsync(HttpMethod.Post, "/api/v1/classrooms", directeur, new { name = "CP Barème", level = "Primaire", capacity = 40 }))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        var category = await CreateAsync(directeur, "/api/v1/finance/fee-categories", new { name = "Cantine", isRecurring = true });
        (await SendAsync(HttpMethod.Post, "/api/v1/finance/fees/apply-standard", directeur,
            new { feeCategoryId = category.Id, amount = 8_000m, overwriteExisting = false })).StatusCode.Should().Be(HttpStatusCode.OK);

        var impact = await ImpactAsync(directeur, $"finance/fee-categories/{category.Id}");
        impact.FinancialBlock.Should().BeFalse();
        impact.CanDelete.Should().BeFalse();
        impact.Links.Should().ContainSingle(l => l.Kind == "ClassFee" && l.Breakable);

        var blocked = await SendAsync(HttpMethod.Delete, $"/api/v1/finance/fee-categories/{category.Id}", directeur);
        blocked.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await blocked.Content.ReadFromJsonAsync<ApiError>())!.Code.Should().Be("DEPENDENCIES_EXIST");

        (await SendAsync(HttpMethod.Post, $"/api/v1/finance/fee-categories/{category.Id}/unlink-dependencies", directeur))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Delete, $"/api/v1/finance/fee-categories/{category.Id}", directeur))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task A_Fee_Category_Billed_To_An_Enrollment_Is_Blocked_Financially_Without_Any_Break_Option()
    {
        var directeur = await DirecteurTokenAsync();
        var classroom = await CreateAsync(directeur, "/api/v1/classrooms", new { name = "CM1 Facturée", level = "Primaire", capacity = 40 });
        (await SendAsync(HttpMethod.Post, "/api/v1/school-years", directeur, new
        {
            label = $"{Today.Year}-{Today.Year + 1}",
            startDate = Today.AddDays(-150).ToString("yyyy-MM-dd"),
            endDate = Today.AddDays(+120).ToString("yyyy-MM-dd")
        })).StatusCode.Should().Be(HttpStatusCode.Created);

        var category = await CreateAsync(directeur, "/api/v1/finance/fee-categories", new { name = "Inscription", isRecurring = false });
        (await SendAsync(HttpMethod.Post, "/api/v1/finance/fees/apply-standard", directeur,
            new { feeCategoryId = category.Id, amount = 10_000m, overwriteExisting = false })).StatusCode.Should().Be(HttpStatusCode.OK);

        var secretaire = await SecretaireTokenAsync();
        (await SendAsync(HttpMethod.Post, "/api/v1/enrollments", secretaire, new
        {
            type = "NewEnrollment", classroomId = classroom.Id, fullName = "Awa Ndiaye", birthDate = "2015-05-20", birthPlace = "Dakar", gender = "F"
        })).StatusCode.Should().Be(HttpStatusCode.Created);

        var impact = await ImpactAsync(directeur, $"finance/fee-categories/{category.Id}");
        impact.FinancialBlock.Should().BeTrue();
        impact.CanDelete.Should().BeFalse();
        impact.Message.Should().Contain("Impossible de supprimer cette catégorie de frais");

        var blocked = await SendAsync(HttpMethod.Delete, $"/api/v1/finance/fee-categories/{category.Id}", directeur);
        blocked.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await blocked.Content.ReadFromJsonAsync<ApiError>())!.Code.Should().Be("FINANCIAL_RECORDS_EXIST");

        var unlink = await SendAsync(HttpMethod.Post, $"/api/v1/finance/fee-categories/{category.Id}/unlink-dependencies", directeur);
        unlink.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await unlink.Content.ReadFromJsonAsync<ApiError>())!.Code.Should().Be("FINANCIAL_RECORDS_EXIST");
    }

    // ------------------------------------------------------------------------------------------ Accès et 404

    [Fact]
    public async Task Finance_Cannot_Use_The_Deletion_Routes_Of_Buildings_Rooms_And_Inventory()
    {
        var directeur = await DirecteurTokenAsync();
        var building = await CreateBuildingAsync(directeur, "Bâtiment Interdit Finance");
        var room = await CreateRoomAsync(directeur, "Salle F", building.Id);
        var category = await CreateInventoryCategoryAsync(directeur, "Cat Finance");
        var item = await CreateItemAsync(directeur, "Bien Finance", category.Id);
        var finance = await FinanceTokenAsync();

        foreach (var resource in new[]
                 {
                     $"buildings/{building.Id}", $"rooms/{room.Id}",
                     $"inventory/categories/{category.Id}", $"inventory/items/{item.Id}"
                 })
        {
            (await SendAsync(HttpMethod.Get, $"/api/v1/{resource}/deletion-impact", finance)).StatusCode.Should().Be(HttpStatusCode.Forbidden, resource);
            (await SendAsync(HttpMethod.Post, $"/api/v1/{resource}/unlink-dependencies", finance)).StatusCode.Should().Be(HttpStatusCode.Forbidden, resource);
        }
    }

    [Fact]
    public async Task The_Deletion_Routes_Return_404_For_Unknown_Elements()
    {
        var directeur = await DirecteurTokenAsync();

        foreach (var resource in new[] { "buildings", "rooms", "inventory/categories", "inventory/items", "finance/fee-categories" })
        {
            (await SendAsync(HttpMethod.Get, $"/api/v1/{resource}/{Guid.NewGuid()}/deletion-impact", directeur))
                .StatusCode.Should().Be(HttpStatusCode.NotFound, resource);
            (await SendAsync(HttpMethod.Post, $"/api/v1/{resource}/{Guid.NewGuid()}/unlink-dependencies", directeur))
                .StatusCode.Should().Be(HttpStatusCode.NotFound, resource);
        }
    }
}
````


## B.5 Tests JavaScript

#### `src/SamaEcole.Web/tests/js/caisse-quick-pay.test.mjs`

````javascript
/**
 * Guichet rapide de la caisse (`caisseView`, wwwroot/js/caisse.js) — la pop-up « Encaissement des
 * frais dus » et sa sélection par cases à cocher.
 *
 * Règle métier verrouillée ici : la sélection est CONTIGUË. L'imputation en base suit l'ordre des
 * échéances (InstallmentScheduleCalculator, oldest-first), donc seul un PRÉFIXE des frais dus peut
 * passer proprement à « Réglé » — décocher une ligne doit décocher toutes les suivantes, et le
 * total à encaisser suit.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts } from './harness.mjs';

function caisseView() {
    const ctx = loadScripts(['payment-cancel.js', 'caisse.js'], {
        preload: {
            auth: { role: 'Directeur' },
            pdfPreview: { state: () => ({}) },
            networkGuard: { newIdempotencyKey: () => 'key' },
            formatFCFA: (n) => String(n)
        }
    });
    const components = ctx.initAlpine();
    // Sans init() : il appelle $watch (absent hors Alpine). On teste la logique de sélection, pure.
    return components.get('caisseView')();
}

/** Échéancier type : inscription + uniforme + 9 mensualités, rien de versé. */
function balanceWith3DueLines() {
    return {
        enrollmentId: 'enr-1',
        installments: [
            { id: 'i1', designation: 'Inscription', remainingDue: 35000, isInitialScope: true, feeCategoryId: 'c-insc' },
            { id: 'i2', designation: 'Uniforme', remainingDue: 12000, isInitialScope: true, feeCategoryId: 'c-unif' },
            { id: 'i3', designation: 'Mensualité (Mois 1)', remainingDue: 22000, isInitialScope: true, feeCategoryId: 'c-scol' },
            { id: 'i4', designation: 'Mensualité (Mois 2)', remainingDue: 22000, isInitialScope: false, feeCategoryId: 'c-scol' }
        ]
    };
}

test('quickPayRows ne retient que les échéances de l\'engagement initial encore dues', () => {
    const v = caisseView();
    v.balance = balanceWith3DueLines();
    assert.deepEqual(v.quickPayRows().map((r) => r.designation),
        ['Inscription', 'Uniforme', 'Mensualité (Mois 1)']);
});

test('openQuickPay coche tout par défaut et pré-remplit le montant avec le total', () => {
    const v = caisseView();
    v.balance = balanceWith3DueLines();
    v.openQuickPay();
    assert.deepEqual(v.quickPay.checked, [true, true, true]);
    assert.equal(v.quickPayTotal(), 69000);
    assert.equal(v.quickPay.amount, 69000);
    assert.equal(v.quickPay.open, true);
});

test('décocher une ligne du milieu décoche aussi toutes les suivantes, et le total suit', () => {
    const v = caisseView();
    v.balance = balanceWith3DueLines();
    v.openQuickPay();

    // L'utilisateur décoche « Uniforme » (index 1).
    v.quickPay.checked[1] = false;
    v.onQuickRowToggle(1);

    assert.deepEqual(v.quickPay.checked, [true, false, false], 'Mois 1 doit suivre Uniforme');
    assert.equal(v.quickPayTotal(), 35000, 'seul l\'inscription reste');
    assert.equal(v.quickPay.amount, 35000);
});

test('recocher une ligne recoche aussi toutes celles qui précèdent', () => {
    const v = caisseView();
    v.balance = balanceWith3DueLines();
    v.openQuickPay();
    v.quickPay.checked = [false, false, false];

    // L'utilisateur coche « Mensualité (Mois 1) » (index 2).
    v.quickPay.checked[2] = true;
    v.onQuickRowToggle(2);

    assert.deepEqual(v.quickPay.checked, [true, true, true]);
    assert.equal(v.quickPayTotal(), 69000);
});

test('rouvrir le guichet après [Annuler] repart sur toutes les cases cochées', () => {
    const v = caisseView();
    v.balance = balanceWith3DueLines();

    v.openQuickPay();
    v.quickPay.checked = [true, false, false]; // l'utilisateur avait restreint la sélection
    v.closeQuickPay();
    assert.equal(v.quickPay.open, false);
    // L'élève reste sélectionné : le solde est toujours là.
    assert.ok(v.balance);

    v.openQuickPay(); // clic sur « ⚡ Encaisser les frais dus »
    assert.equal(v.quickPay.open, true);
    assert.deepEqual(v.quickPay.checked, [true, true, true]);
    assert.equal(v.quickPay.amount, 69000);
});

test('searchStudents accepte un matricule d\'un seul caractère sans bloquer la recherche', async () => {
    const calls = [];
    const ctx = loadScripts(['payment-cancel.js', 'caisse.js'], {
        preload: {
            auth: { role: 'Directeur' },
            pdfPreview: { state: () => ({}) },
            networkGuard: { newIdempotencyKey: () => 'key' },
            formatFCFA: (n) => String(n),
            api: {
                get: async (url) => {
                    calls.push(url);
                    return [{ id: 'student-1', matricule: 'A', fullName: 'Alpha' }];
                },
                toMessage: () => 'Erreur'
            }
        }
    });
    const view = ctx.initAlpine().get('caisseView')();
    view.studentSearch = 'A';
    await view.searchStudents();

    assert.deepEqual(calls, ['/finance/students/search?q=A']);
    assert.equal(view.students.length, 1);
    assert.equal(view.students[0].matricule, 'A');
});

test("un mois échu non réglé est proposé au guichet même quand l'engagement initial est soldé", () => {
    const v = caisseView();
    v.balance = {
        installments: [
            { id: 'i1', designation: 'Inscription', remainingDue: 0, isInitialScope: true, isDueNow: true },
            { id: 'i2', designation: 'Mensualité (Mois 1)', remainingDue: 0, isInitialScope: true, isDueNow: true },
            { id: 'i3', designation: 'Mensualité (Mois 2)', remainingDue: 22000, isInitialScope: false, isDueNow: true },
            { id: 'i4', designation: 'Mensualité (Mois 3)', remainingDue: 22000, isInitialScope: false, isDueNow: false }
        ],
        dueNowTotal: 22000
    };

    assert.deepEqual(v.quickPayRows().map((r) => r.id), ['i3']);
    assert.equal(v.dueNowTotal, 22000);
    v.openQuickPay();
    assert.equal(v.quickPay.open, true);
    assert.equal(v.quickPay.amount, 22000);
});

test('quickPayNote extrait « Mois 1 » d\'une désignation de mensualité, null sinon', () => {
    const v = caisseView();
    assert.equal(v.quickPayNote('Mensualité (Mois 1)'), 'Mois 1');
    assert.equal(v.quickPayNote('Inscription'), null);
});

test('dueNowTotal privilégie la valeur serveur, et retombe sur les échéances isInitialScope', () => {
    const v = caisseView();
    v.balance = { ...balanceWith3DueLines(), dueNowTotal: 69000 };
    assert.equal(v.dueNowTotal, 69000);

    const noServerValue = balanceWith3DueLines();
    v.balance = noServerValue; // pas de champ dueNowTotal
    assert.equal(v.dueNowTotal, 69000);
});
````


#### `src/SamaEcole.Web/tests/js/payroll-deductions.test.mjs`

````javascript
/**
 * Paie sans retenues (wwwroot/js/payroll.js) — le Directeur d'un Daara choisit, contrat par contrat, de ne retenir ni IPRES, ni CSS,
 * ni VRS/CFCE, ni BRS. Le régime avec retenues reste le défaut : les deux coexistent dans une même école.
 *
 * Règles verrouillées ici : « avec retenues » par défaut à la création ; la case décochée envoie explicitement `false` ; à la
 * modification, un contrat renvoyé sans le champ (ancienne réponse) reste « avec retenues » — jamais l'inverse ; la valeur
 * du contrat est renvoyée telle quelle au serveur.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, flush } from './harness.mjs';

function payrollView() {
    const calls = [];
    const api = {
        get: async () => [],
        post: async (url, body) => { calls.push({ method: 'POST', url, body }); return {}; },
        patch: async (url, body) => { calls.push({ method: 'PATCH', url, body }); return {}; },
        toMessage: (err, fallback) => fallback
    };
    const toast = { success() {}, error() {} };
    const ctx = loadScripts(['payroll.js'], {
        preload: { auth: { role: 'Directeur', schoolId: 'school-1' }, api, pdfPreview: { state: () => ({}) } }
    });
    // payroll.js appelle `api` et `toast` comme des globales nues : on les pose sur le bac à sable.
    ctx.sandbox.api = api;
    ctx.sandbox.toast = toast;
    const view = ctx.component('payrollView');
    return { view, calls };
}

test('un nouveau contrat est « avec retenues » par défaut : le calcul historique ne change pas', () => {
    const { view } = payrollView();
    view.openContractModal();

    assert.equal(view.contractForm.applyStatutoryDeductions, true);
});

test('un contrat créé avec la case cochée envoie applyStatutoryDeductions = true', async () => {
    const { view, calls } = payrollView();
    view.openContractModal();
    Object.assign(view.contractForm, { employeeKind: 'external', externalFullName: 'Oustaz Moussa', type: 'Permanent', baseSalary: '40000' });

    await view.submitContract();
    await flush();

    const post = calls.find((c) => c.url === '/finance/employee-contracts');
    assert.ok(post, 'le contrat doit être envoyé');
    assert.equal(post.body.applyStatutoryDeductions, true);
});

test('un contrat créé SANS retenues envoie explicitement applyStatutoryDeductions = false', async () => {
    const { view, calls } = payrollView();
    view.openContractModal();
    Object.assign(view.contractForm, {
        employeeKind: 'external', externalFullName: 'Oustaz Moussa', type: 'Permanent', baseSalary: '40000',
        applyStatutoryDeductions: false
    });

    await view.submitContract();
    await flush();

    const post = calls.find((c) => c.url === '/finance/employee-contracts');
    assert.equal(post.body.applyStatutoryDeductions, false);
    assert.equal(post.body.baseSalary, 40000, 'le salaire est envoyé tel quel');
});

test('modifier un contrat « sans retenues » préremplit la case décochée', () => {
    const { view } = payrollView();
    view.openEditContractModal({
        id: 'c-1', type: 'Permanent', baseSalary: 40000, hourlyRate: 0, transportAllowance: 0,
        payoutMethod: 'Cash', payoutAccountReference: null, rowVersion: 7, applyStatutoryDeductions: false
    });

    assert.equal(view.editContractForm.applyStatutoryDeductions, false);
});

test('un contrat renvoyé sans le champ reste « avec retenues » à la modification, jamais l\'inverse', () => {
    const { view } = payrollView();
    view.openEditContractModal({
        id: 'c-2', type: 'Permanent', baseSalary: 250000, hourlyRate: 0, transportAllowance: 0,
        payoutMethod: 'Cash', payoutAccountReference: null, rowVersion: 3
    });

    assert.equal(view.editContractForm.applyStatutoryDeductions, true);
});

test('la modification renvoie le régime choisi avec le motif et le jeton de version', async () => {
    const { view, calls } = payrollView();
    view.openEditContractModal({
        id: 'c-3', type: 'Permanent', baseSalary: 40000, hourlyRate: 0, transportAllowance: 0,
        payoutMethod: 'Cash', payoutAccountReference: null, rowVersion: 9, applyStatutoryDeductions: true
    });
    view.editContractForm.applyStatutoryDeductions = false;
    view.editContractForm.reason = 'Passage en paie sans retenues.';

    await view.submitEditContract();
    await flush();

    const patch = calls.find((c) => c.method === 'PATCH');
    assert.ok(patch, 'la modification doit être envoyée');
    assert.equal(patch.url, '/finance/employee-contracts/c-3');
    assert.equal(patch.body.applyStatutoryDeductions, false);
    assert.equal(patch.body.rowVersion, 9);
    assert.equal(patch.body.reason, 'Passage en paie sans retenues.');
});
````


#### `src/SamaEcole.Web/tests/js/fees-disbursements.test.mjs`

````javascript
/**
 * Décaissements : distinction visuelle de l'écriture d'origine et de sa contre-écriture (wwwroot/js/fees.js).
 *
 * Un décaissement est une écriture comptable : il n'est jamais supprimé, « annuler » ajoute une ligne aux montants
 * opposés qui référence l'original (API : reversalOfId, isReversed). L'écran doit donc (1) ne proposer l'annulation
 * que sur une écriture normale — une double annulation ou l'annulation d'une annulation sont refusées en 409 par
 * l'API —, et (2) montrer clairement quelle ligne annule laquelle.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts } from './harness.mjs';

function bootFees() {
    const ctx = loadScripts(['guided-delete.js', 'fees.js'], {
        preload: {
            auth: { role: 'Directeur', accessToken: 'jwt', isAuthenticated: () => false, isAccessTokenStale: () => false, redirectToLogin() {} },
            toast: { success() {}, error() {} }
        }
    });
    return ctx.initAlpine().get('feesView')();
}

const original = { id: 'o1', beneficiary: 'Papeterie Sow', date: '2026-10-01', amount: 10000, reason: 'Craies', isReversed: true, reversalOfId: null };
const reversal = { id: 'r1', beneficiary: 'Papeterie Sow', date: '2026-10-04', amount: -10000, reason: 'Annulation — Craies', isReversed: false, reversalOfId: 'o1' };
const normal = { id: 'n1', beneficiary: 'Électricité', date: '2026-10-02', amount: 25000, reason: 'Facture', isReversed: false, reversalOfId: null };

test("trois états : écriture normale, original annulé, contre-écriture", () => {
    const fees = bootFees();

    assert.equal(fees.disbursementState(normal), 'active');
    assert.equal(fees.disbursementState(original), 'reversed');
    assert.equal(fees.disbursementState(reversal), 'reversal');
});

test("seule une écriture normale propose l'annulation (pas de double annulation ni d'annulation d'annulation)", () => {
    const fees = bootFees();

    assert.equal(fees.canReverseDisbursement(normal), true);
    assert.equal(fees.canReverseDisbursement(original), false, 'déjà annulé → 409 DISBURSEMENT_ALREADY_REVERSED côté API');
    assert.equal(fees.canReverseDisbursement(reversal), false, 'une annulation ne s\'annule pas → 409 DISBURSEMENT_IS_REVERSAL');
});

test("une contre-écriture ne tient pas pour un original, même sans champ isReversed", () => {
    const fees = bootFees();

    assert.equal(fees.disbursementState({ id: 'x', reversalOfId: 'o1' }), 'reversal');
    assert.equal(fees.disbursementState({ id: 'y' }), 'active', 'réponse d\'un ancien serveur sans les nouveaux champs');
});

test("chaque état a un style de ligne distinct", () => {
    const fees = bootFees();
    const classes = [normal, original, reversal].map((d) => fees.disbursementRowClass(d));

    assert.equal(new Set(classes).size, 3);
    assert.match(fees.disbursementRowClass(reversal), /rose/);
});

test("la contre-écriture renvoie à l'écriture d'origine quand elle est dans la liste", () => {
    const fees = bootFees();
    fees.disbursements = [reversal, original, normal];

    const note = fees.reversalNote(reversal);

    assert.match(note, /Annule le décaissement du/);
    assert.match(note, /Papeterie Sow/);
    assert.match(note, /01\/10\/2026/);
});

test("sans l'original dans la liste (filtre de dates), la note reste explicite", () => {
    const fees = bootFees();
    fees.disbursements = [reversal];

    assert.equal(fees.reversalNote(reversal), 'Annule un décaissement antérieur.');
});
````


#### `src/SamaEcole.Web/tests/js/enrollment-optional-fees.test.mjs`

````javascript
/**
 * Frais optionnels, Tâche 2 — cases à cocher « Uniforme », « Tenue de sport »… du formulaire d'inscription
 * ET de réinscription (un seul composant `enrollmentsView` sert les deux modes).
 *
 * Ce que ces tests verrouillent :
 *   1. les frais optionnels sont PRÉ-COCHÉS à l'affichage ; les obligatoires n'ont pas de case ;
 *   2. décocher un frais le sort du total affiché, sans le faire disparaître de la liste (on doit pouvoir
 *      le recocher) ;
 *   3. le payload transmet la liste EXPLICITE des frais optionnels cochés (vide = aucun), dans les deux modes ;
 *   4. l'aperçu se recompose comme le serveur (OptionalFeeSelection) — le serveur reste l'autorité.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, plain } from './harness.mjs';

const CLASS = 'c-6e';
const INSCRIPTION = 'cat-inscription';
const MENSUALITE = 'cat-mensualite';
const UNIFORME = 'cat-uniforme';
const TENUE = 'cat-tenue';

function enrollments({ apiGet } = {}) {
    const posts = [];
    const ctx = loadScripts(['enrollments.js'], {
        preload: {
            auth: { role: 'Secretariat' },
            pdfPreview: { state: () => ({}) },
            api: {
                get: apiGet || (async () => []),
                post: async (endpoint, body) => { posts.push({ endpoint, body: plain(body) }); return {}; },
                toMessage: (_e, fallback) => fallback,
                toFieldErrors: (_e, fallback) => ({ global: fallback })
            },
            location: { href: 'https://localhost/inscriptions', search: '' }
        }
    });
    const view = ctx.initAlpine().get('enrollmentsView')();
    return { view, posts };
}

/** Barème de la 6e : deux frais obligatoires, deux optionnels. */
function withFees(view) {
    view.tuitionMonths = 10;
    view.form.classroomId = CLASS;
    view.feesByClassroom = {
        [CLASS]: [
            { feeCategoryId: INSCRIPTION, designation: 'Inscription', isRecurring: false, isOptional: false, unitAmount: 10000 },
            { feeCategoryId: MENSUALITE, designation: 'Mensualité', isRecurring: true, isOptional: false, unitAmount: 15000 },
            { feeCategoryId: UNIFORME, designation: 'Uniforme', isRecurring: false, isOptional: true, unitAmount: 25000 },
            { feeCategoryId: TENUE, designation: 'Tenue de sport', isRecurring: false, isOptional: true, unitAmount: 8000 }
        ]
    };
}

const MANDATORY_TOTAL = 10000 + 15000 * 10;

test('les frais optionnels sont pré-cochés à l\'affichage du formulaire', () => {
    const { view } = enrollments();
    withFees(view);

    assert.equal(view.isFeeChecked(UNIFORME), true);
    assert.equal(view.isFeeChecked(TENUE), true);
    assert.equal(view.previewTotal(), MANDATORY_TOTAL + 25000 + 8000);
});

test('feeChoices liste tous les frais de la classe et signale les optionnels', () => {
    const { view } = enrollments();
    withFees(view);

    const choices = plain(view.feeChoices());
    assert.equal(choices.length, 4);
    assert.deepEqual(
        choices.filter((c) => c.isOptional).map((c) => c.feeCategoryId).sort(),
        [TENUE, UNIFORME].sort());
    assert.ok(choices.every((c) => c.checked), 'tout est coché par défaut');
});

test('décocher un frais optionnel le sort du total mais le garde dans la liste', () => {
    const { view } = enrollments();
    withFees(view);

    view.toggleFee(UNIFORME);

    assert.equal(view.isFeeChecked(UNIFORME), false);
    assert.equal(view.previewTotal(), MANDATORY_TOTAL + 8000);
    assert.ok(!view.previewLines().some((l) => l.feeCategoryId === UNIFORME));
    const uniforme = plain(view.feeChoices()).find((c) => c.feeCategoryId === UNIFORME);
    assert.equal(uniforme.checked, false, 'la ligne reste affichée pour pouvoir être recochée');
});

test('recocher un frais le remet dans le total', () => {
    const { view } = enrollments();
    withFees(view);

    view.toggleFee(UNIFORME);
    view.toggleFee(UNIFORME);

    assert.equal(view.isFeeChecked(UNIFORME), true);
    assert.equal(view.previewTotal(), MANDATORY_TOTAL + 25000 + 8000);
});

test('un frais obligatoire ne se décoche pas', () => {
    const { view } = enrollments();
    withFees(view);

    view.toggleFee(MENSUALITE);
    view.toggleFee(INSCRIPTION);

    assert.equal(view.isFeeChecked(MENSUALITE), true);
    assert.equal(view.isFeeChecked(INSCRIPTION), true);
    assert.equal(view.previewTotal(), MANDATORY_TOTAL + 25000 + 8000);
});

test('le simulateur d\'aide au calcul ne compte pas un frais décoché', () => {
    const { view } = enrollments();
    withFees(view);
    view.simMonths = 2;

    view.toggleFee(TENUE);

    // ponctuels facturés (inscription + uniforme) + 2 mensualités
    assert.equal(view.simSubtotal(), 10000 + 25000 + 15000 * 2);
});

test('le payload transmet les frais optionnels cochés', () => {
    const { view } = enrollments();
    withFees(view);

    assert.deepEqual(plain(view.optionalFeePayload()).optionalFeeCategoryIds.sort(), [TENUE, UNIFORME].sort());

    view.toggleFee(UNIFORME);
    assert.deepEqual(plain(view.optionalFeePayload()), { optionalFeeCategoryIds: [TENUE] });
});

test('tout décocher transmet une liste VIDE (aucun frais optionnel), jamais un champ absent', () => {
    const { view } = enrollments();
    withFees(view);

    view.toggleFee(UNIFORME);
    view.toggleFee(TENUE);

    assert.deepEqual(plain(view.optionalFeePayload()), { optionalFeeCategoryIds: [] });
});

test('une classe sans frais optionnel ne transmet rien', () => {
    const { view } = enrollments();
    view.form.classroomId = 'c-sans-options';
    view.feesByClassroom = {
        'c-sans-options': [
            { feeCategoryId: INSCRIPTION, designation: 'Inscription', isRecurring: false, isOptional: false, unitAmount: 10000 }
        ]
    };

    assert.deepEqual(plain(view.optionalFeePayload()), {});
});

test('changer de classe recoche les frais optionnels', () => {
    const { view } = enrollments();
    withFees(view);
    view.toggleFee(UNIFORME);

    view.resetOptionalFees();

    assert.equal(view.isFeeChecked(UNIFORME), true);
});

test('réinscription : le payload envoyé au serveur porte les frais cochés', async () => {
    const { view, posts } = enrollments();
    withFees(view);
    view.mode = 'ReEnrollment';
    view.activeYear = { id: 'y1', isActive: true };
    view.form.studentId = 'stu-1';
    view.toggleFee(TENUE);

    await view.submit();

    assert.equal(posts.length, 1);
    assert.equal(posts[0].endpoint, '/enrollments');
    assert.equal(posts[0].body.type, 'ReEnrollment');
    assert.deepEqual(posts[0].body.optionalFeeCategoryIds, [UNIFORME]);
});

test('nouvelle inscription : le payload envoyé au serveur porte les frais cochés', async () => {
    const { view, posts } = enrollments();
    withFees(view);
    view.mode = 'NewEnrollment';
    view.activeYear = { id: 'y1', isActive: true };
    view.form.fullName = 'Awa Ndiaye';

    await view.submit();

    assert.equal(posts.length, 1);
    assert.equal(posts[0].body.type, 'NewEnrollment');
    assert.deepEqual(posts[0].body.optionalFeeCategoryIds.slice().sort(), [TENUE, UNIFORME].sort());
});

test('le chargement des références marque chaque frais optionnel d\'après sa catégorie', async () => {
    const apiGet = async (endpoint) => {
        if (endpoint === '/classrooms') return [{ id: CLASS, name: '6e', level: 'Collège' }];
        if (endpoint === '/finance/fee-categories') {
            return [
                { id: INSCRIPTION, name: 'Inscription', isRecurring: false, isOptional: false },
                { id: UNIFORME, name: 'Uniforme', isRecurring: false, isOptional: true }
            ];
        }
        if (endpoint === '/finance/fees') {
            return [
                { classroomId: CLASS, feeCategoryId: INSCRIPTION, feeCategoryName: 'Inscription', amount: 10000 },
                { classroomId: CLASS, feeCategoryId: UNIFORME, feeCategoryName: 'Uniforme', amount: 25000 }
            ];
        }
        if (endpoint === '/school-years') return [{ id: 'y1', isActive: true, label: '2026-2027' }];
        if (endpoint === '/schools/current/settings') return { tuitionMonthsPerYear: 10 };
        return [];
    };
    const { view } = enrollments({ apiGet });

    await view.loadReferenceData();
    view.form.classroomId = CLASS;

    const byId = Object.fromEntries(plain(view.feeChoices()).map((c) => [c.feeCategoryId, c.isOptional]));
    assert.deepEqual(byId, { [INSCRIPTION]: false, [UNIFORME]: true });
});
````

