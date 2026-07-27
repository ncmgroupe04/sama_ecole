using FluentAssertions;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using SamaEcole.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace SamaEcole.IntegrationTests.Auth;

/// <summary>
/// Ticket JGK-A04, côté base. Tout s'exécute avec le RÔLE APPLICATIF et SANS tenant : c'est la
/// situation exacte du login, où la table `users` est sous RLS mais doit rester interrogeable via
/// les fonctions SECURITY DEFINER. Un test qui tournerait en propriétaire ne prouverait rien.
/// </summary>
public class AuthStoreTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly Guid DirecteurA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid DirecteurB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();

        owner.Schools.AddRange(
            new School { Id = EcoleA, Name = "École A" },
            new School { Id = EcoleB, Name = "École B" });

        owner.Users.AddRange(
            new User
            {
                Id = DirecteurA, SchoolId = EcoleA, Email = "directeur.a@sama-ecole.sn",
                PasswordHash = "hash-a", FullName = "Directeur A", Role = Role.Directeur
            },
            new User
            {
                Id = DirecteurB, SchoolId = EcoleB, Email = "directeur.b@sama-ecole.sn",
                PasswordHash = "hash-b", FullName = "Directeur B", Role = Role.Directeur
            });

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task Login_Path_Should_Find_A_User_Even_Without_Tenant_Despite_Rls()
    {
        // Aucun tenant : c'est tout l'enjeu. Sans les fonctions SECURITY DEFINER, la RLS rendrait
        // la connexion impossible — personne ne pourrait plus se connecter à l'application.
        await using var db = _db.NewAppContext(schoolId: null);

        var user = await _db.NewAuthStore(db).FindUserByEmailAsync("directeur.a@sama-ecole.sn", CancellationToken.None);

        user.Should().NotBeNull();
        user!.Id.Should().Be(DirecteurA);
        user.SchoolId.Should().Be(EcoleA);
        user.Role.Should().Be(Role.Directeur);
    }

    [Fact]
    public async Task Email_Lookup_Should_Be_Case_Insensitive()
    {
        await using var db = _db.NewAppContext(schoolId: null);

        var user = await _db.NewAuthStore(db).FindUserByEmailAsync("Directeur.A@Sama-Ecole.SN", CancellationToken.None);

        user.Should().NotBeNull("un e-mail ne doit pas dépendre de la casse pour se connecter");
    }

    [Fact]
    public async Task Direct_Sql_On_Users_Must_Still_Be_Blocked_By_Rls_For_Another_School()
    {
        // Le contournement doit se limiter aux fonctions d'authentification. En SQL direct, la table
        // users reste cloisonnée comme n'importe quelle table tenant.
        await using var connection = await _db.OpenRawAppConnectionAsync(EcoleA);

        await using var command = connection.CreateCommand();
        command.CommandText = """SELECT "Email" FROM users ORDER BY "Email";""";

        var emails = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            emails.Add(reader.GetString(0));
        }

        emails.Should().ContainSingle().Which.Should().Be("directeur.a@sama-ecole.sn");
        emails.Should().NotContain("directeur.b@sama-ecole.sn", "la RLS doit masquer les comptes des autres écoles");
    }

    [Fact]
    public async Task Account_Should_Lock_After_Five_Consecutive_Failures()
    {
        await using var db = _db.NewAppContext(schoolId: null);
        var store = _db.NewAuthStore(db);

        // docs/Volume_7_Security.md §2 : verrouillage après 5 échecs consécutifs.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await store.RecordLoginAttemptAsync(
                DirecteurA, success: false, maxFailedAttempts: 5, lockoutMinutes: 15, CancellationToken.None);
        }

        var user = await store.FindUserByEmailAsync("directeur.a@sama-ecole.sn", CancellationToken.None);

        user!.AccessFailedCount.Should().Be(5);
        user.LockoutEndAt.Should().NotBeNull();
        user.LockoutEndAt!.Value.Should().BeAfter(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Fourth_Failure_Should_Not_Lock_Yet()
    {
        await using var db = _db.NewAppContext(schoolId: null);
        var store = _db.NewAuthStore(db);

        for (var attempt = 0; attempt < 4; attempt++)
        {
            await store.RecordLoginAttemptAsync(
                DirecteurA, success: false, maxFailedAttempts: 5, lockoutMinutes: 15, CancellationToken.None);
        }

        var user = await store.FindUserByEmailAsync("directeur.a@sama-ecole.sn", CancellationToken.None);

        user!.AccessFailedCount.Should().Be(4);
        user.LockoutEndAt.Should().BeNull("le verrouillage ne doit intervenir qu'au 5e échec");
    }

    [Fact]
    public async Task Successful_Login_Should_Clear_Previous_Failures()
    {
        await using var db = _db.NewAppContext(schoolId: null);
        var store = _db.NewAuthStore(db);

        await store.RecordLoginAttemptAsync(DirecteurA, false, 5, 15, CancellationToken.None);
        await store.RecordLoginAttemptAsync(DirecteurA, false, 5, 15, CancellationToken.None);
        await store.RecordLoginAttemptAsync(DirecteurA, true, 5, 15, CancellationToken.None);

        var user = await store.FindUserByEmailAsync("directeur.a@sama-ecole.sn", CancellationToken.None);

        user!.AccessFailedCount.Should().Be(0);
        user.LockoutEndAt.Should().BeNull();
    }

    [Fact]
    public async Task Refresh_Token_Should_Be_Storable_And_Revocable_Without_Tenant()
    {
        await using var db = _db.NewAppContext(schoolId: null);
        var store = _db.NewAuthStore(db);

        const string hash = "hash-du-refresh-token";
        await store.StoreRefreshTokenAsync(DirecteurA, hash, DateTimeOffset.UtcNow.AddDays(14), CancellationToken.None);

        var stored = await store.FindRefreshTokenAsync(hash, CancellationToken.None);
        stored.Should().NotBeNull();
        stored!.RevokedAt.Should().BeNull();

        await store.RevokeAllRefreshTokensAsync(DirecteurA, CancellationToken.None);

        var afterLogout = await store.FindRefreshTokenAsync(hash, CancellationToken.None);
        afterLogout!.RevokedAt.Should().NotBeNull("le logout doit révoquer les refresh tokens (critère JGK-A04)");
    }

    [Fact]
    public async Task Revoking_Refresh_Tokens_By_School_Should_Only_Affect_That_Schools_Users()
    {
        // Ticket JGK-B01 — suspension d'établissement. `refresh_tokens` n'a pas de SchoolId : la
        // fonction SECURITY DEFINER doit le retrouver via `users`, qui EST sous RLS. Sans elle, un
        // Super Admin sans tenant ne verrait aucune ligne d'aucune école.
        await using var db = _db.NewAppContext(schoolId: null);
        var store = _db.NewAuthStore(db);

        await store.StoreRefreshTokenAsync(DirecteurA, "hash-a", DateTimeOffset.UtcNow.AddDays(14), CancellationToken.None);
        await store.StoreRefreshTokenAsync(DirecteurB, "hash-b", DateTimeOffset.UtcNow.AddDays(14), CancellationToken.None);

        var revoked = await store.RevokeAllRefreshTokensForSchoolAsync(EcoleA, CancellationToken.None);
        revoked.Should().Be(1);

        var tokenA = await store.FindRefreshTokenAsync("hash-a", CancellationToken.None);
        var tokenB = await store.FindRefreshTokenAsync("hash-b", CancellationToken.None);

        tokenA!.RevokedAt.Should().NotBeNull("suspendre l'école A doit couper les sessions de ses utilisateurs");
        tokenB!.RevokedAt.Should().BeNull("l'école B n'est pas concernée par la suspension de l'école A");
    }

    [Fact]
    public async Task Application_Role_Must_Not_Read_Users_Through_Its_Own_Sql()
    {
        // Le rôle applicatif ne doit disposer QUE des trois fonctions d'authentification pour voir
        // hors tenant. Vérifie qu'il n'a pas, par ailleurs, obtenu BYPASSRLS au passage.
        await using var connection = await _db.OpenRawAppConnectionAsync(schoolId: null);

        await using var command = connection.CreateCommand();
        command.CommandText = """SELECT count(*) FROM users;""";

        var visible = (long)(await command.ExecuteScalarAsync())!;

        visible.Should().Be(0, "sans tenant, aucune ligne de users ne doit être visible en SQL direct");
    }
}
