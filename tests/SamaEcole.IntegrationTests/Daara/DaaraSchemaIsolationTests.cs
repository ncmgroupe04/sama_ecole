using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Daara;

/// <summary>
/// Socle Internat/Daara — <c>instructors</c>, <c>students.InstructorId</c> et <c>student_hizb_statuses</c> tiennent-ils
/// leur isolation, leurs bornes et leur purge DANS LA BASE ? SQL BRUT sous le rôle applicatif, sans EF Core : seuls la
/// policy RLS, les contraintes et les fonctions de purge font foi — même patron que QuranModuleIsolationTests.
/// </summary>
[Trait("Category", "MultiTenant")]
public class DaaraSchemaIsolationTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("a1111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("a2222222-2222-2222-2222-222222222222");
    private static readonly Guid ClasseA = Guid.Parse("a1111111-0000-0000-0000-0000000000c1");
    private static readonly Guid ClasseB = Guid.Parse("a2222222-0000-0000-0000-0000000000c1");
    private static readonly Guid EleveA = Guid.Parse("a1111111-0000-0000-0000-0000000000e1");
    private static readonly Guid EleveB = Guid.Parse("a2222222-0000-0000-0000-0000000000e1");
    private static readonly Guid OustazA = Guid.Parse("a1111111-0000-0000-0000-0000000000f1");
    private static readonly Guid OustazB = Guid.Parse("a2222222-0000-0000-0000-0000000000f1");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();

        owner.Schools.AddRange(new School { Id = EcoleA, Name = "Daara A" }, new School { Id = EcoleB, Name = "Daara B" });
        owner.Classrooms.AddRange(
            new Classroom { Id = ClasseA, SchoolId = EcoleA, Name = "Halqa A", Level = "Daara", Cycle = CycleType.Primaire, Capacity = 40 },
            new Classroom { Id = ClasseB, SchoolId = EcoleB, Name = "Halqa B", Level = "Daara", Cycle = CycleType.Primaire, Capacity = 40 });
        owner.Students.AddRange(
            new Student { Id = EleveA, SchoolId = EcoleA, Matricule = "ELEV-A1", FullName = "Moussa A", BirthDate = new DateOnly(2012, 1, 1), BirthPlace = "Touba", Gender = "M", ClassroomId = ClasseA },
            new Student { Id = EleveB, SchoolId = EcoleB, Matricule = "ELEV-B1", FullName = "Moussa B", BirthDate = new DateOnly(2012, 1, 1), BirthPlace = "Touba", Gender = "M", ClassroomId = ClasseB });
        await owner.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    // ------------------------------------------------------------------ RLS

    [Fact]
    public async Task Raw_Query_On_Instructors_Should_Never_Return_Other_School_Rows()
    {
        await InsertInstructorAsync(EcoleA, EcoleA, OustazA);
        await InsertInstructorAsync(EcoleB, EcoleB, OustazB);

        (await ScalarAsync(EcoleA, "SELECT count(*) FROM instructors")).Should().Be(1);
        (await ScalarAsync(EcoleB, "SELECT count(*) FROM instructors")).Should().Be(1);
        (await ScalarAsync(null, "SELECT count(*) FROM instructors")).Should().Be(0, "sans tenant dans la session, rien n'est visible");
    }

    [Fact]
    public async Task Raw_Query_On_Hizb_Statuses_Should_Never_Return_Other_School_Rows()
    {
        await InsertHizbAsync(EcoleA, EcoleA, EleveA, hizb: 1);
        await InsertHizbAsync(EcoleB, EcoleB, EleveB, hizb: 1);

        (await ScalarAsync(EcoleA, "SELECT count(*) FROM student_hizb_statuses")).Should().Be(1);
        (await ScalarAsync(EcoleB, "SELECT count(*) FROM student_hizb_statuses")).Should().Be(1);
        (await ScalarAsync(null, "SELECT count(*) FROM student_hizb_statuses")).Should().Be(0);
    }

    [Fact]
    public async Task Writing_An_Instructor_Or_A_Hizb_Status_Into_Another_School_Should_Be_Rejected()
    {
        var instructor = async () => await InsertInstructorAsync(EcoleA, EcoleB, OustazB);
        await instructor.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.InsufficientPrivilege);

        var hizb = async () => await InsertHizbAsync(EcoleA, EcoleB, EleveB, hizb: 1);
        await hizb.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task The_Application_Role_Cannot_Physically_Delete_Instructors_Or_Hizb_Statuses()
    {
        await InsertInstructorAsync(EcoleA, EcoleA, OustazA);
        await InsertHizbAsync(EcoleA, EcoleA, EleveA, hizb: 1);

        foreach (var sql in new[] { "DELETE FROM instructors", "DELETE FROM student_hizb_statuses" })
        {
            var act = async () => await ExecuteAsync(EcoleA, sql);
            await act.Should().ThrowAsync<PostgresException>()
                .Where(e => e.SqlState == PostgresErrorCodes.InsufficientPrivilege, "aucune suppression physique (règle #6)");
        }
    }

    [Fact]
    public async Task The_Ef_Global_Query_Filter_Also_Hides_Other_School_Rows()
    {
        await InsertInstructorAsync(EcoleA, EcoleA, OustazA);
        await InsertInstructorAsync(EcoleB, EcoleB, OustazB);
        await InsertHizbAsync(EcoleA, EcoleA, EleveA, hizb: 1);
        await InsertHizbAsync(EcoleB, EcoleB, EleveB, hizb: 1);

        // Contexte propriétaire (hors RLS) : seul le Global Query Filter EF peut alors isoler — la deuxième
        // des deux protections exigées par l'AGENTS.md règle #2.
        await using var owner = _db.NewOwnerContext();
        await using var scoped = _db.NewAppContext(EcoleA);

        (await owner.Instructors.IgnoreQueryFilters().CountAsync()).Should().Be(2);
        (await scoped.Instructors.AsNoTracking().Select(i => i.Id).ToListAsync()).Should().Equal(OustazA);
        (await scoped.StudentHizbStatuses.AsNoTracking().Select(h => h.StudentId).ToListAsync()).Should().Equal(EleveA);
    }

    // ------------------------------------------------------------------ students.InstructorId

    [Fact]
    public async Task A_Student_Can_Have_No_Instructor_And_Keeps_Its_Classroom()
    {
        // L'élève du setup n'a pas d'Oustaz : la colonne est NULL, et ClassroomId (axe caisse) est intact.
        (await ScalarAsync(EcoleA, """SELECT count(*) FROM students WHERE "InstructorId" IS NULL AND "ClassroomId" = @c""",
            ("c", ClasseA))).Should().Be(1);
    }

    [Fact]
    public async Task A_Student_Can_Be_Attached_To_An_Instructor_Of_The_Same_School()
    {
        await InsertInstructorAsync(EcoleA, EcoleA, OustazA);

        await ExecuteAsync(EcoleA, """UPDATE students SET "InstructorId" = @i WHERE "Id" = @s""", ("i", OustazA), ("s", EleveA));

        (await ScalarAsync(EcoleA, """SELECT count(*) FROM students WHERE "InstructorId" = @i""", ("i", OustazA))).Should().Be(1);
    }

    [Fact]
    public async Task A_Student_Cannot_Be_Attached_To_An_Instructor_Of_Another_School()
    {
        // L'Oustaz B existe bel et bien (inséré en session B) : la composite (SchoolId, InstructorId) le refuse
        // pour l'élève A, même si la RLS laisse l'UPDATE passer sur la ligne de A.
        await InsertInstructorAsync(EcoleB, EcoleB, OustazB);

        var act = async () => await ExecuteAsync(EcoleA, """UPDATE students SET "InstructorId" = @i WHERE "Id" = @s""", ("i", OustazB), ("s", EleveA));

        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.ForeignKeyViolation);
    }

    // ------------------------------------------------------------------ student_hizb_statuses

    [Fact]
    public async Task A_Hizb_Status_Cannot_Point_To_A_Student_Of_Another_School()
    {
        // Session ET SchoolId de la ligne = A (la RLS laisse passer) mais l'élève est celui de B : seule la FK composite l'arrête.
        var act = async () => await InsertHizbAsync(EcoleA, EcoleA, EleveB, hizb: 1);

        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.ForeignKeyViolation);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(61)]
    public async Task The_Hizb_Number_Is_Bounded_To_1_Through_60_By_The_Database(int hizb)
    {
        var act = async () => await InsertHizbAsync(EcoleA, EcoleA, EleveA, hizb);

        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.CheckViolation && e.ConstraintName == "CK_student_hizb_statuses_hizb_range");
    }

    [Fact]
    public async Task The_Hizb_Bounds_Are_Inclusive()
    {
        await InsertHizbAsync(EcoleA, EcoleA, EleveA, hizb: 1);
        await InsertHizbAsync(EcoleA, EcoleA, EleveA, hizb: 60);

        (await ScalarAsync(EcoleA, "SELECT count(*) FROM student_hizb_statuses")).Should().Be(2);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public async Task The_Rating_Is_Bounded_To_1_Through_5_When_Present(int rating)
    {
        var act = async () => await InsertHizbAsync(EcoleA, EcoleA, EleveA, hizb: 1, state: "InProgress", quarters: 2, rating: rating);

        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.CheckViolation && e.ConstraintName == "CK_student_hizb_statuses_rating_range");
    }

    [Fact]
    public async Task A_Rating_And_An_Evaluation_Date_Are_Optional_For_A_Hizb_Not_Started()
    {
        await InsertHizbAsync(EcoleA, EcoleA, EleveA, hizb: 7, state: "NotStarted", quarters: 0, rating: null);
        await InsertHizbAsync(EcoleA, EcoleA, EleveA, hizb: 8, state: "Completed", quarters: 4, rating: 5);

        (await ScalarAsync(EcoleA, """SELECT count(*) FROM student_hizb_statuses WHERE "Rating" IS NULL AND "LastEvaluatedAt" IS NULL""")).Should().Be(1);
    }

    [Theory]
    [InlineData("NotStarted", 1)]
    [InlineData("InProgress", 0)]
    [InlineData("InProgress", 4)]
    [InlineData("Completed", 3)]
    public async Task The_State_Must_Match_The_Completed_Quarters(string state, int quarters)
    {
        var act = async () => await InsertHizbAsync(EcoleA, EcoleA, EleveA, hizb: 1, state: state, quarters: quarters);

        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.CheckViolation && e.ConstraintName == "CK_student_hizb_statuses_state_matches_quarters");
    }

    [Theory]
    [InlineData("NotStarted", 0)]
    [InlineData("InProgress", 1)]
    [InlineData("InProgress", 3)]
    [InlineData("Completed", 4)]
    public async Task Every_Consistent_State_And_Quarters_Pair_Is_Accepted(string state, int quarters)
    {
        await InsertHizbAsync(EcoleA, EcoleA, EleveA, hizb: 1, state: state, quarters: quarters);

        (await ScalarAsync(EcoleA, "SELECT count(*) FROM student_hizb_statuses")).Should().Be(1);
    }

    [Fact]
    public async Task There_Is_One_Live_Status_Per_Student_And_Hizb_Until_It_Is_Soft_Deleted()
    {
        var first = await InsertHizbAsync(EcoleA, EcoleA, EleveA, hizb: 12);

        var duplicate = async () => await InsertHizbAsync(EcoleA, EcoleA, EleveA, hizb: 12);
        await duplicate.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.UniqueViolation);

        await InsertHizbAsync(EcoleA, EcoleA, EleveA, hizb: 13);   // autre Hizb : aucun conflit
        await InsertHizbAsync(EcoleB, EcoleB, EleveB, hizb: 12);   // autre école : aucun conflit

        await ExecuteAsync(EcoleA, """UPDATE student_hizb_statuses SET "IsDeleted" = TRUE, "DeletedAt" = NOW() WHERE "Id" = @id""", ("id", first));
        await InsertHizbAsync(EcoleA, EcoleA, EleveA, hizb: 12);   // recréation après suppression logique
    }

    // ------------------------------------------------------------------ purge

    [Fact]
    public async Task Resetting_The_School_Data_Purges_Instructors_And_Hizb_Statuses_Without_A_Foreign_Key_Error()
    {
        await InsertInstructorAsync(EcoleA, EcoleA, OustazA);
        await ExecuteAsync(EcoleA, """UPDATE students SET "InstructorId" = @i WHERE "Id" = @s""", ("i", OustazA), ("s", EleveA));
        await InsertHizbAsync(EcoleA, EcoleA, EleveA, hizb: 1);
        await InsertInstructorAsync(EcoleB, EcoleB, OustazB);
        await InsertHizbAsync(EcoleB, EcoleB, EleveB, hizb: 1);

        // students → instructors : si l'ordre de purge était inversé, ce DELETE échouerait en 23503.
        await ScalarAsync(EcoleA, "SELECT count(*) FROM reset_school_data(@school)", ("school", EcoleA));

        (await ScalarAsync(EcoleA, "SELECT count(*) FROM instructors")).Should().Be(0);
        (await ScalarAsync(EcoleA, "SELECT count(*) FROM student_hizb_statuses")).Should().Be(0);
        (await ScalarAsync(EcoleB, "SELECT count(*) FROM instructors")).Should().Be(1, "la purge ne touche jamais une autre école");
        (await ScalarAsync(EcoleB, "SELECT count(*) FROM student_hizb_statuses")).Should().Be(1);
    }

    // ------------------------------------------------------------------ SQL brut

    private async Task InsertInstructorAsync(Guid sessionSchool, Guid schoolId, Guid id) =>
        await ExecuteAsync(sessionSchool,
            """
            INSERT INTO instructors ("Id", "SchoolId", "FullName", "Status", "CreatedAt", "IsDeleted")
            VALUES (@id, @schoolId, 'Serigne Oustaz', 'Active', NOW(), FALSE)
            """,
            ("id", id), ("schoolId", schoolId));

    private async Task<Guid> InsertHizbAsync(
        Guid sessionSchool, Guid schoolId, Guid studentId, int hizb,
        string state = "NotStarted", int quarters = 0, int? rating = null)
    {
        var id = Guid.NewGuid();
        await ExecuteAsync(sessionSchool,
            """
            INSERT INTO student_hizb_statuses
                ("Id", "SchoolId", "StudentId", "HizbNumber", "CompletedQuarters", "State", "Rating", "CreatedAt", "IsDeleted")
            VALUES (@id, @schoolId, @studentId, @hizb, @quarters, @state, @rating, NOW(), FALSE)
            """,
            ("id", id), ("schoolId", schoolId), ("studentId", studentId), ("hizb", hizb),
            ("quarters", quarters), ("state", state), ("rating", (object?)rating ?? DBNull.Value));
        return id;
    }

    private async Task ExecuteAsync(Guid? sessionSchool, string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(sessionSchool);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync();
    }

    private async Task<long> ScalarAsync(Guid? sessionSchool, string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(sessionSchool);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }
}
