using FluentAssertions;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Npgsql;
using Xunit;

namespace SamaEcole.IntegrationTests.Users;

/// <summary>
/// Ticket JGK-A05, côté base : le journal des statuts doit être cloisonné par école ET inaltérable.
/// Tout passe par le rôle applicatif — c'est le seul sur lequel les policies et les GRANT mordent.
/// </summary>
public class UserStatusHistoryTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly Guid UserA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid UserB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

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
                Id = UserA, SchoolId = EcoleA, Email = "secretaire.a@sama-ecole.sn",
                PasswordHash = "hash", FullName = "Secrétaire A", Role = Role.Secretariat
            },
            new User
            {
                Id = UserB, SchoolId = EcoleB, Email = "secretaire.b@sama-ecole.sn",
                PasswordHash = "hash", FullName = "Secrétaire B", Role = Role.Secretariat
            });

        // Une entrée d'audit dans chaque école.
        owner.UserStatusHistory.AddRange(
            NewEntry(EcoleA, UserA, "Absences répétées non justifiées"),
            NewEntry(EcoleB, UserB, "Départ de l'établissement"));

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private static UserStatusHistory NewEntry(Guid schoolId, Guid userId, string reason) => new()
    {
        SchoolId = schoolId,
        UserId = userId,
        PreviousStatus = EntityStatus.Active,
        NewStatus = EntityStatus.Suspended,
        Reason = reason,
        ChangedByUserId = Guid.NewGuid(),
        ChangedAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task History_Of_Another_School_Must_Never_Be_Readable()
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(EcoleA);

        await using var command = connection.CreateCommand();
        command.CommandText = """SELECT "Reason" FROM user_status_history;""";

        var reasons = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            reasons.Add(reader.GetString(0));
        }

        reasons.Should().ContainSingle().Which.Should().Be("Absences répétées non justifiées");
        reasons.Should().NotContain("Départ de l'établissement");
    }

    [Fact]
    public async Task Rewriting_A_Past_Audit_Entry_Must_Be_Impossible()
    {
        // Volume_7_Security.md §7 : le journal est « consultable mais jamais modifiable, y compris par
        // un administrateur ». Ce n'est pas une convention de code — le rôle applicatif n'a PAS le
        // droit UPDATE. Sans ce test, une future migration pourrait le rendre réécrivable sans bruit.
        await using var connection = await _db.OpenRawAppConnectionAsync(EcoleA);

        await using var command = connection.CreateCommand();
        command.CommandText = """UPDATE user_status_history SET "Reason" = 'motif réécrit';""";

        var act = async () => await command.ExecuteNonQueryAsync();

        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task Deleting_A_Past_Audit_Entry_Must_Be_Impossible()
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(EcoleA);

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM user_status_history;";

        var act = async () => await command.ExecuteNonQueryAsync();

        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task Appending_A_New_Entry_Must_Remain_Possible()
    {
        // L'append-only ne doit pas se transformer en read-only : l'application DOIT pouvoir écrire.
        await using var db = _db.NewAppContext(EcoleA);

        db.UserStatusHistory.Add(NewEntry(EcoleA, UserA, "Réactivation après régularisation"));
        await db.SaveChangesAsync(CancellationToken.None);

        var count = db.UserStatusHistory.Count();
        count.Should().Be(2);
    }
}
