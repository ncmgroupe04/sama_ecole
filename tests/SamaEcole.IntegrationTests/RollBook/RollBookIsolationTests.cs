using FluentAssertions;
using SamaEcole.Domain.Entities;
using SamaEcole.IntegrationTests.Common;
using Npgsql;
using Xunit;

namespace SamaEcole.IntegrationTests.RollBook;

/// <summary>
/// Cahier de roulement (Franco-Arabe Bloc 5b) — l'isolation de <c>roll_book_entries</c> tient-elle dans la BASE ?
/// Tout est en SQL BRUT, avec le rôle applicatif et sans EF Core : le Global Query Filter n'entre pas en jeu, seule la
/// policy RLS fait foi. S'y ajoutent les garde-fous propres à la table : une seule saisie VIVANTE par case, minutes de
/// retard cohérentes avec le statut, suppression physique refusée.
/// </summary>
[Trait("Category", "MultiTenant")]
public class RollBookIsolationTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("e1111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("e2222222-2222-2222-2222-222222222222");
    private static readonly Guid AnneeA = Guid.Parse("e1111111-0000-0000-0000-000000000001");
    private static readonly Guid AnneeB = Guid.Parse("e2222222-0000-0000-0000-000000000001");
    private static readonly Guid ClasseA = Guid.Parse("ea000000-0000-0000-0000-0000000000a1");
    private static readonly Guid ClasseB = Guid.Parse("ea000000-0000-0000-0000-0000000000b1");
    private static readonly Guid EleveA = Guid.Parse("ee000000-0000-0000-0000-0000000000a1");
    private static readonly Guid EleveB = Guid.Parse("ee000000-0000-0000-0000-0000000000b1");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();

        owner.Schools.AddRange(new School { Id = EcoleA, Name = "École A" }, new School { Id = EcoleB, Name = "École B" });
        owner.SchoolYears.AddRange(
            new SchoolYear { Id = AnneeA, SchoolId = EcoleA, Label = "2026-2027", StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2027, 7, 31), IsActive = true },
            new SchoolYear { Id = AnneeB, SchoolId = EcoleB, Label = "2026-2027", StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2027, 7, 31), IsActive = true });
        owner.Classrooms.AddRange(
            new Classroom { Id = ClasseA, SchoolId = EcoleA, Name = "CM2 A", Level = "Primaire", Capacity = 40 },
            new Classroom { Id = ClasseB, SchoolId = EcoleB, Name = "CM2 A", Level = "Primaire", Capacity = 40 });
        owner.Students.AddRange(
            new Student { Id = EleveA, SchoolId = EcoleA, Matricule = "ELEV-A", FullName = "Awa Fall", BirthDate = new DateOnly(2015, 1, 1), BirthPlace = "Dakar", Gender = "F", ClassroomId = ClasseA },
            new Student { Id = EleveB, SchoolId = EcoleB, Matricule = "ELEV-B", FullName = "Modou Diop", BirthDate = new DateOnly(2015, 1, 1), BirthPlace = "Dakar", Gender = "M", ClassroomId = ClasseB });

        await owner.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private async Task InsertAsync(
        Guid school, Guid year, Guid classroom, Guid student, string status = "Present", int minutes = 0,
        string half = "Morning", string date = "2026-11-02", Guid? sessionSchool = null)
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(sessionSchool ?? school);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO roll_book_entries
                ("Id", "SchoolId", "SchoolYearId", "ClassroomId", "StudentId", "Date", "HalfDay", "Status", "LateMinutes",
                 "RecordedByUserId", "CreatedAt", "IsDeleted")
            VALUES (gen_random_uuid(), @school, @year, @classroom, @student, @date::date, @half, @status, @minutes,
                    gen_random_uuid(), NOW(), FALSE);
            """;
        command.Parameters.AddWithValue("school", school);
        command.Parameters.AddWithValue("year", year);
        command.Parameters.AddWithValue("classroom", classroom);
        command.Parameters.AddWithValue("student", student);
        command.Parameters.AddWithValue("date", date);
        command.Parameters.AddWithValue("half", half);
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("minutes", minutes);

        await command.ExecuteNonQueryAsync();
    }

    private async Task<List<Guid>> ReadStudentsAsync(Guid? schoolId)
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(schoolId);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT \"StudentId\" FROM roll_book_entries";

        var ids = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            ids.Add(reader.GetGuid(0));
        }

        return ids;
    }

    [Fact]
    public async Task RawSqlQuery_Should_Never_Return_Other_School_Entries()
    {
        await InsertAsync(EcoleA, AnneeA, ClasseA, EleveA);
        await InsertAsync(EcoleB, AnneeB, ClasseB, EleveB);

        var students = await ReadStudentsAsync(EcoleA);

        students.Should().ContainSingle().Which.Should().Be(EleveA);
        students.Should().NotContain(EleveB, "la RLS doit masquer les saisies des autres écoles");
    }

    [Fact]
    public async Task Session_Without_Tenant_Should_See_No_Entry_At_All()
    {
        await InsertAsync(EcoleA, AnneeA, ClasseA, EleveA);

        (await ReadStudentsAsync(schoolId: null)).Should().BeEmpty();
    }

    [Fact]
    public async Task Writing_An_Entry_Into_Another_School_Should_Be_Rejected()
    {
        var act = async () => await InsertAsync(EcoleB, AnneeB, ClasseB, EleveB, sessionSchool: EcoleA);

        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task An_Entry_Cannot_Point_To_A_Student_Or_Class_Of_Another_School()
    {
        // FK composites (SchoolId, X) : l'élève B n'existe pas pour l'école A.
        var student = async () => await InsertAsync(EcoleA, AnneeA, ClasseA, EleveB);
        await student.Should().ThrowAsync<PostgresException>().Where(e => e.SqlState == PostgresErrorCodes.ForeignKeyViolation);

        var classroom = async () => await InsertAsync(EcoleA, AnneeA, ClasseB, EleveA);
        await classroom.Should().ThrowAsync<PostgresException>().Where(e => e.SqlState == PostgresErrorCodes.ForeignKeyViolation);

        var year = async () => await InsertAsync(EcoleA, AnneeB, ClasseA, EleveA);
        await year.Should().ThrowAsync<PostgresException>().Where(e => e.SqlState == PostgresErrorCodes.ForeignKeyViolation);
    }

    [Fact]
    public async Task A_Cell_Can_Hold_Only_One_Live_Entry()
    {
        await InsertAsync(EcoleA, AnneeA, ClasseA, EleveA, "Present");

        var act = async () => await InsertAsync(EcoleA, AnneeA, ClasseA, EleveA, "UnjustifiedAbsence");

        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task Morning_And_Afternoon_Are_Two_Cells()
    {
        await InsertAsync(EcoleA, AnneeA, ClasseA, EleveA, half: "Morning");
        await InsertAsync(EcoleA, AnneeA, ClasseA, EleveA, half: "Afternoon");

        (await ReadStudentsAsync(EcoleA)).Should().HaveCount(2);
    }

    [Fact]
    public async Task A_Soft_Deleted_Entry_Does_Not_Block_The_Next_One()
    {
        await InsertAsync(EcoleA, AnneeA, ClasseA, EleveA, "Present");

        await using (var connection = await _db.OpenRawAppConnectionAsync(EcoleA))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE roll_book_entries SET \"IsDeleted\" = TRUE, \"DeletedAt\" = NOW()";
            (await command.ExecuteNonQueryAsync()).Should().Be(1, "le rôle applicatif peut supprimer LOGIQUEMENT");
        }

        await InsertAsync(EcoleA, AnneeA, ClasseA, EleveA, "UnjustifiedAbsence");
    }

    [Fact]
    public async Task Late_Minutes_Are_Only_Allowed_On_A_Late_Entry_And_Stay_In_Range()
    {
        var onPresent = async () => await InsertAsync(EcoleA, AnneeA, ClasseA, EleveA, "Present", minutes: 10);
        await onPresent.Should().ThrowAsync<PostgresException>().Where(e => e.SqlState == PostgresErrorCodes.CheckViolation);

        var tooLong = async () => await InsertAsync(EcoleA, AnneeA, ClasseA, EleveA, "Late", minutes: 601);
        await tooLong.Should().ThrowAsync<PostgresException>().Where(e => e.SqlState == PostgresErrorCodes.CheckViolation);

        await InsertAsync(EcoleA, AnneeA, ClasseA, EleveA, "Late", minutes: 15);
    }

    [Fact]
    public async Task The_Application_Role_Must_Not_Be_Able_To_Physically_Delete_An_Entry()
    {
        await InsertAsync(EcoleA, AnneeA, ClasseA, EleveA);

        await using var connection = await _db.OpenRawAppConnectionAsync(EcoleA);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM roll_book_entries";

        var act = async () => await command.ExecuteNonQueryAsync();

        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.InsufficientPrivilege,
                "la suppression est logique (règle #6) : le rôle applicatif n'a pas DELETE");
    }
}
