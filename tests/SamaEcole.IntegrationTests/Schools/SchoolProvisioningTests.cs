using FluentAssertions;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Npgsql;
using Xunit;

namespace SamaEcole.IntegrationTests.Schools;

/// <summary>
/// Ticket JGK-B01, côté base. Tout s'exécute avec le RÔLE APPLICATIF et SANS tenant : c'est la
/// situation exacte d'un Super Admin, qui n'a aucun schoolId. Un test qui tournerait en propriétaire
/// contournerait la RLS et ne prouverait strictement rien.
/// </summary>
public class SchoolProvisioningTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleExistante = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid DirecteurExistant = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();

        owner.Schools.Add(new School { Id = EcoleExistante, Name = "École déjà provisionnée" });
        owner.Users.Add(new User
        {
            Id = DirecteurExistant,
            SchoolId = EcoleExistante,
            Email = "directeur.existant@sama-ecole.sn",
            PasswordHash = "hash",
            FullName = "Directeur existant",
            Role = Role.Directeur
        });

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task Direct_Insert_Into_Users_Must_Be_Refused_By_Rls_For_A_Tenantless_Session()
    {
        // Le point de départ du ticket : sans porte dédiée, un Super Admin ne PEUT PAS créer de
        // compte. Si un jour ce test cesse d'échouer, c'est que la RLS de `users` a sauté.
        await using var connection = await _db.OpenRawAppConnectionAsync(schoolId: null);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO users ("Id", "SchoolId", "Email", "PasswordHash", "FullName", "Role",
                               "Status", "AccessFailedCount", "CreatedAt", "IsDeleted")
            VALUES (gen_random_uuid(), @schoolId, 'force@sama-ecole.sn', 'hash', 'Forcé', 'Directeur',
                    'Active', 0, NOW(), FALSE);
            """;
        command.Parameters.AddWithValue("schoolId", Guid.NewGuid());

        var act = async () => await command.ExecuteNonQueryAsync();

        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task Provisioning_Should_Create_The_Director_Of_A_Brand_New_School()
    {
        var nouvelleEcole = Guid.NewGuid();

        await using var owner = _db.NewOwnerContext();
        owner.Schools.Add(new School { Id = nouvelleEcole, Name = "École neuve" });
        await owner.SaveChangesAsync(CancellationToken.None);

        // Session SANS tenant, rôle applicatif : exactement le contexte d'un Super Admin.
        await using var db = _db.NewAppContext(schoolId: null);

        var directorId = await _db.NewProvisioningStore(db).CreateInitialDirectorAsync(
            nouvelleEcole, "directrice@neuve.sn", "hash", "Directrice", Role.Directeur, CancellationToken.None);

        directorId.Should().NotBeNull("la fonction SECURITY DEFINER doit pouvoir amorcer une école vierge");
    }

    [Fact]
    public async Task Provisioning_Must_Refuse_A_School_That_Already_Has_A_User()
    {
        // LA garde anti-escalade. Sans elle, cette fonction serait une porte ouverte : n'importe quel
        // appel avec le SchoolId d'un concurrent y créerait un Directeur — donc un accès complet à ses
        // données. Ici, elle refuse et renvoie null.
        await using var db = _db.NewAppContext(schoolId: null);

        var directorId = await _db.NewProvisioningStore(db).CreateInitialDirectorAsync(
            EcoleExistante, "intrus@sama-ecole.sn", "hash", "Intrus", Role.Directeur, CancellationToken.None);

        directorId.Should().BeNull("on ne s'injecte pas dans une école déjà provisionnée");

        // Et rien n'a été écrit.
        var exists = await _db.NewProvisioningStore(db)
            .EmailExistsAsync("intrus@sama-ecole.sn", CancellationToken.None);

        exists.Should().BeFalse();
    }

    [Fact]
    public async Task Email_Uniqueness_Should_Be_Checked_Across_All_Schools()
    {
        // L'unicité de l'e-mail est PLATEFORME, pas par école : la vérification doit voir les comptes
        // de toutes les écoles, ce qu'une requête EF sous RLS ne pourrait pas faire.
        await using var db = _db.NewAppContext(schoolId: null);
        var store = _db.NewProvisioningStore(db);

        (await store.EmailExistsAsync("directeur.existant@sama-ecole.sn", CancellationToken.None))
            .Should().BeTrue();

        (await store.EmailExistsAsync("personne@sama-ecole.sn", CancellationToken.None))
            .Should().BeFalse();
    }

    [Fact]
    public async Task Provisioning_Two_Schools_With_The_Same_Director_Email_Must_Refuse_The_Second()
    {
        // Audit sécurité : deux établissements ne peuvent PAS naître avec le même e-mail de Directeur.
        // La garde « école vierge » ne s'y oppose pas (les deux écoles sont vierges) — c'est l'unicité
        // globale de users.Email qui doit trancher, traduite en refus lisible (pas un 23505 brut).
        var ecoleA = Guid.NewGuid();
        var ecoleB = Guid.NewGuid();

        await using (var owner = _db.NewOwnerContext())
        {
            owner.Schools.AddRange(
                new School { Id = ecoleA, Name = "École A" },
                new School { Id = ecoleB, Name = "École B" });
            await owner.SaveChangesAsync(CancellationToken.None);
        }

        await using var db = _db.NewAppContext(schoolId: null);
        var store = _db.NewProvisioningStore(db);

        var first = await store.CreateInitialDirectorAsync(
            ecoleA, "directeur@groupe.sn", "hash", "Directeur A", Role.Directeur, CancellationToken.None);
        first.Should().NotBeNull();

        var act = async () => await store.CreateInitialDirectorAsync(
            ecoleB, "directeur@groupe.sn", "hash", "Directeur B", Role.Directeur, CancellationToken.None);

        await act.Should().ThrowAsync<DuplicateRecordException>(
            "un e-mail n'identifie qu'un seul compte sur toute la plateforme");
    }

    [Fact]
    public async Task Provisioning_Must_Refuse_A_Director_Email_That_Differs_Only_By_Case()
    {
        // Le cœur de la faille : « Directeur@X » et « directeur@X » passaient tous deux, car l'index
        // unique était un btree varchar sensible à la casse. Colonne citext -> ils se confondent.
        var ecoleA = Guid.NewGuid();
        var ecoleB = Guid.NewGuid();

        await using (var owner = _db.NewOwnerContext())
        {
            owner.Schools.AddRange(
                new School { Id = ecoleA, Name = "École Casse A" },
                new School { Id = ecoleB, Name = "École Casse B" });
            await owner.SaveChangesAsync(CancellationToken.None);
        }

        await using var db = _db.NewAppContext(schoolId: null);
        var store = _db.NewProvisioningStore(db);

        await store.CreateInitialDirectorAsync(
            ecoleA, "directeur@casse.sn", "hash", "Directeur A", Role.Directeur, CancellationToken.None);

        var act = async () => await store.CreateInitialDirectorAsync(
            ecoleB, "DIRECTEUR@CASSE.SN", "hash", "Directeur B", Role.Directeur, CancellationToken.None);

        await act.Should().ThrowAsync<DuplicateRecordException>(
            "l'unicité de l'e-mail doit ignorer la casse (colonne citext)");

        // Et l'index le confirme du côté « lecture » : la variante de casse est bien vue comme prise.
        (await store.EmailExistsAsync("Directeur@Casse.SN", CancellationToken.None)).Should().BeTrue();
    }

    [Fact]
    public async Task A_Soft_Deleted_Director_Frees_The_Email_For_A_New_School()
    {
        // L'index unique est filtré « WHERE IsDeleted = false » : un compte supprimé ne réserve plus
        // l'adresse — cohérent avec auth_find_user_by_email, qui ignore déjà les comptes supprimés.
        var ecoleA = Guid.NewGuid();
        var ecoleB = Guid.NewGuid();
        var directeurA = Guid.NewGuid();

        await using (var owner = _db.NewOwnerContext())
        {
            owner.Schools.AddRange(
                new School { Id = ecoleA, Name = "École Reprise A" },
                new School { Id = ecoleB, Name = "École Reprise B" });
            var supprime = new User
            {
                Id = directeurA,
                SchoolId = ecoleA,
                Email = "reprise@ecole.sn",
                PasswordHash = "hash",
                FullName = "Directeur A",
                Role = Role.Directeur
            };
            supprime.SoftDelete("test");
            owner.Users.Add(supprime);
            await owner.SaveChangesAsync(CancellationToken.None);
        }

        await using var db = _db.NewAppContext(schoolId: null);
        var store = _db.NewProvisioningStore(db);

        var directorId = await store.CreateInitialDirectorAsync(
            ecoleB, "reprise@ecole.sn", "hash", "Directeur B", Role.Directeur, CancellationToken.None);

        directorId.Should().NotBeNull("l'adresse d'un compte soft-deleted redevient disponible");
    }
}
