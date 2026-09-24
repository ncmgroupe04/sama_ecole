using FluentAssertions;
using SamaEcole.Domain.Entities;
using SamaEcole.IntegrationTests.Common;
using Npgsql;
using Xunit;

namespace SamaEcole.IntegrationTests.Quran;

/// <summary>
/// Socle Franco-Arabe/Daara — l'isolation de quran_progress et quran_evaluations tient-elle dans
/// la BASE ? Tout en SQL BRUT avec le rôle applicatif, sans EF Core, pour que seule la policy RLS
/// fasse foi — même patron que ClassJournalIsolationTests.
/// </summary>
[Trait("Category", "MultiTenant")]
public class QuranModuleIsolationTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ClasseA = Guid.Parse("cccccccc-0000-0000-0000-00000000000c");
    private static readonly Guid ClasseB = Guid.Parse("dddddddd-0000-0000-0000-00000000000d");
    private static readonly Guid EleveA = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid EleveB = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();
        owner.Schools.AddRange(
            new School { Id = EcoleA, Name = "École A" },
            new School { Id = EcoleB, Name = "École B" });
        owner.Classrooms.AddRange(
            new Classroom { Id = ClasseA, SchoolId = EcoleA, Name = "CM2 A", Level = "Primaire", Capacity = 40 },
            new Classroom { Id = ClasseB, SchoolId = EcoleB, Name = "CM2 B", Level = "Primaire", Capacity = 40 });
        owner.Students.AddRange(
            new Student { Id = EleveA, SchoolId = EcoleA, Matricule = "ELEV-2026-0001", FullName = "Élève A", BirthDate = new DateOnly(2015, 1, 1), BirthPlace = "Dakar", Gender = "M", ClassroomId = ClasseA },
            new Student { Id = EleveB, SchoolId = EcoleB, Matricule = "ELEV-2026-0001", FullName = "Élève B", BirthDate = new DateOnly(2015, 1, 1), BirthPlace = "Thiès", Gender = "F", ClassroomId = ClasseB });
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task Raw_Query_On_Quran_Progress_Should_Never_Return_Other_School_Rows()
    {
        await InsertProgressAsync(EcoleA, EleveA);
        await InsertProgressAsync(EcoleB, EleveB);

        var rows = await ReadProgressStudentsAsync(EcoleA);

        rows.Should().ContainSingle().Which.Should().Be(EleveA);
    }

    [Fact]
    public async Task Raw_Query_On_Quran_Evaluations_Should_Never_Return_Other_School_Rows()
    {
        await InsertEvaluationAsync(EcoleA, EleveA);
        await InsertEvaluationAsync(EcoleB, EleveB);

        var rows = await ReadEvaluationStudentsAsync(EcoleA);

        rows.Should().ContainSingle().Which.Should().Be(EleveA);
    }

    [Fact]
    public async Task Writing_A_Progress_Entry_Into_Another_School_Should_Be_Rejected()
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(EcoleA);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO quran_progress
                ("Id", "SchoolId", "StudentId", "JuzNumber", "HizbNumber", "SurahNumber", "Status", "CreatedAt", "IsDeleted")
            VALUES (gen_random_uuid(), @schoolId, @studentId, 1, 1, 1, 'InProcess', NOW(), FALSE);
            """;
        command.Parameters.AddWithValue("schoolId", EcoleB);
        command.Parameters.AddWithValue("studentId", EleveB);

        var act = async () => await command.ExecuteNonQueryAsync();

        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task Deleting_A_Quran_Evaluation_Row_Is_Refused_By_Privilege_Not_Just_Policy()
    {
        var id = await InsertEvaluationAsync(EcoleA, EleveA);

        await using var connection = await _db.OpenRawAppConnectionAsync(EcoleA);
        await using var command = connection.CreateCommand();
        command.CommandText = """DELETE FROM quran_evaluations WHERE "Id" = @id;""";
        command.Parameters.AddWithValue("id", id);

        var act = async () => await command.ExecuteNonQueryAsync();

        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.InsufficientPrivilege);
    }

    private async Task<Guid> InsertProgressAsync(Guid schoolId, Guid studentId)
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(schoolId);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO quran_progress
                ("Id", "SchoolId", "StudentId", "JuzNumber", "HizbNumber", "SurahNumber", "Status", "CreatedAt", "IsDeleted")
            VALUES (@id, @schoolId, @studentId, 1, 1, 1, 'InProcess', NOW(), FALSE);
            """;
        var id = Guid.NewGuid();
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("schoolId", schoolId);
        command.Parameters.AddWithValue("studentId", studentId);
        await command.ExecuteNonQueryAsync();
        return id;
    }

    private async Task<Guid> InsertEvaluationAsync(Guid schoolId, Guid studentId)
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(schoolId);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO quran_evaluations
                ("Id", "SchoolId", "StudentId", "EvaluationDate", "MemoryMistakes", "TajwidMistakes", "Hesitations", "FinalScore", "CreatedAt", "IsDeleted")
            VALUES (@id, @schoolId, @studentId, DATE '2026-09-20', 0, 0, 0, 15, NOW(), FALSE);
            """;
        var id = Guid.NewGuid();
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("schoolId", schoolId);
        command.Parameters.AddWithValue("studentId", studentId);
        await command.ExecuteNonQueryAsync();
        return id;
    }

    private async Task<List<Guid>> ReadProgressStudentsAsync(Guid schoolId)
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(schoolId);
        await using var command = connection.CreateCommand();
        command.CommandText = """SELECT "StudentId" FROM quran_progress ORDER BY "StudentId";""";
        return await ReadGuidColumnAsync(command);
    }

    private async Task<List<Guid>> ReadEvaluationStudentsAsync(Guid schoolId)
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(schoolId);
        await using var command = connection.CreateCommand();
        command.CommandText = """SELECT "StudentId" FROM quran_evaluations ORDER BY "StudentId";""";
        return await ReadGuidColumnAsync(command);
    }

    private static async Task<List<Guid>> ReadGuidColumnAsync(NpgsqlCommand command)
    {
        var ids = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            ids.Add(reader.GetGuid(0));
        }
        return ids;
    }
}
