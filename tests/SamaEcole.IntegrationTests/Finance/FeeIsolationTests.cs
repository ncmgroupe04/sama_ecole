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
