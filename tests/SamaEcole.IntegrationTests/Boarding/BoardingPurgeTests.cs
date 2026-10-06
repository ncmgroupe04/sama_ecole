using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Boarding;

/// <summary>
/// Les fonctions de purge <c>reset_school_data</c> et <c>delete_school_year</c> connaissent-elles les tables Internat ?
/// Sans le patch (migration AddBoardingToPurges), elles échouent en 23503 dès qu'un pensionnaire existe, car
/// <c>boarding_enrollments</c> référence <c>enrollments</c> et <c>students</c> en RESTRICT.
/// </summary>
[Trait("Category", "MultiTenant")]
public class BoardingPurgeTests : IAsyncLifetime
{
    private const string PreviousMigration = "20261006224058_AddBoardingDormitoryModel";

    private readonly RlsTestDatabase _db = new();
    private BoardingSqlSeed _sql = null!;

    private static readonly Guid EcoleA = Guid.Parse("e1111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("e2222222-2222-2222-2222-222222222222");
    private static readonly Guid AnneeActive = Guid.Parse("e1111111-0000-0000-0000-000000000001");
    private static readonly Guid AnneePassee = Guid.Parse("e1111111-0000-0000-0000-000000000002");
    private static readonly Guid AnneeB = Guid.Parse("e2222222-0000-0000-0000-000000000001");
    private static readonly Guid ClasseA = Guid.Parse("e1111111-0000-0000-0000-0000000000c1");
    private static readonly Guid ClasseB = Guid.Parse("e2222222-0000-0000-0000-0000000000c1");
    private static readonly Guid EleveA = Guid.Parse("e1111111-0000-0000-0000-0000000000e1");
    private static readonly Guid EleveB = Guid.Parse("e2222222-0000-0000-0000-0000000000e1");
    private static readonly Guid InscriptionActive = Guid.Parse("e1111111-0000-0000-0000-0000000000f1");
    private static readonly Guid InscriptionPassee = Guid.Parse("e1111111-0000-0000-0000-0000000000f2");
    private static readonly Guid InscriptionB = Guid.Parse("e2222222-0000-0000-0000-0000000000f1");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        _sql = new BoardingSqlSeed(_db);

        await using (var owner = _db.NewOwnerContext())
        {
            owner.Schools.AddRange(new School { Id = EcoleA, Name = "École A" }, new School { Id = EcoleB, Name = "École B" });
            owner.SchoolYears.AddRange(
                new SchoolYear { Id = AnneeActive, SchoolId = EcoleA, Label = "2026-2027", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true },
                new SchoolYear { Id = AnneePassee, SchoolId = EcoleA, Label = "2025-2026", StartDate = new DateOnly(2025, 9, 1), EndDate = new DateOnly(2026, 6, 30) },
                new SchoolYear { Id = AnneeB, SchoolId = EcoleB, Label = "2026-2027", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true });
            owner.Classrooms.AddRange(
                new Classroom { Id = ClasseA, SchoolId = EcoleA, Name = "4ème A", Level = "Collège", Cycle = CycleType.College, Capacity = 40 },
                new Classroom { Id = ClasseB, SchoolId = EcoleB, Name = "4ème A", Level = "Collège", Cycle = CycleType.College, Capacity = 40 });
            owner.Students.AddRange(
                new Student { Id = EleveA, SchoolId = EcoleA, Matricule = "ELEV-A1", FullName = "Awa A", BirthDate = new DateOnly(2011, 1, 1), BirthPlace = "Dakar", Gender = "F", ClassroomId = ClasseA },
                new Student { Id = EleveB, SchoolId = EcoleB, Matricule = "ELEV-B1", FullName = "Awa B", BirthDate = new DateOnly(2011, 1, 1), BirthPlace = "Dakar", Gender = "F", ClassroomId = ClasseB });
            owner.Enrollments.AddRange(
                NewEnrollment(InscriptionActive, EcoleA, EleveA, AnneeActive, ClasseA, "REC-A-1"),
                NewEnrollment(InscriptionPassee, EcoleA, EleveA, AnneePassee, ClasseA, "REC-A-0"),
                NewEnrollment(InscriptionB, EcoleB, EleveB, AnneeB, ClasseB, "REC-B-1"));
            await owner.SaveChangesAsync();
        }

        // École A : un séjour ACTIF (avec lit) pour l'année active, un séjour CLOS pour l'année passée, chacun avec
        // une sortie et un pointage. École B : un séjour actif.
        var dormitoryA = await _sql.InsertDormitoryAsync(EcoleA, EcoleA);
        var roomA = await _sql.InsertRoomAsync(EcoleA, EcoleA, dormitoryA);
        var bedA = await _sql.InsertBedAsync(EcoleA, EcoleA, roomA);
        var active = await _sql.InsertBoarderAsync(EcoleA, EcoleA, EleveA, InscriptionActive, bed: bedA);
        var past = await _sql.InsertBoarderAsync(EcoleA, EcoleA, EleveA, InscriptionPassee, active: false);
        foreach (var boarder in new[] { active, past })
        {
            await _sql.InsertLeaveAsync(EcoleA, EcoleA, boarder);
            await _sql.InsertAttendanceAsync(EcoleA, EcoleA, boarder);
        }

        var dormitoryB = await _sql.InsertDormitoryAsync(EcoleB, EcoleB);
        var roomB = await _sql.InsertRoomAsync(EcoleB, EcoleB, dormitoryB);
        var bedB = await _sql.InsertBedAsync(EcoleB, EcoleB, roomB);
        var boarderB = await _sql.InsertBoarderAsync(EcoleB, EcoleB, EleveB, InscriptionB, bed: bedB);
        await _sql.InsertLeaveAsync(EcoleB, EcoleB, boarderB);
        await _sql.InsertAttendanceAsync(EcoleB, EcoleB, boarderB);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task Resetting_The_School_Data_Removes_The_Boarding_Rows_Of_That_School_Only()
    {
        await RunPurgeAsync("SELECT count(*) FROM reset_school_data(@school);", EcoleA, ("school", EcoleA));

        foreach (var table in BoardingTables)
        {
            (await _sql.CountAsync(EcoleA, table)).Should().Be(0, table);
        }

        (await _sql.CountAsync(EcoleB, "dormitories")).Should().Be(1, "l'école B est intacte");
        (await _sql.CountAsync(EcoleB, "boarding_enrollments")).Should().Be(1);
        (await _sql.CountAsync(EcoleB, "boarding_leaves")).Should().Be(1);
        (await _sql.CountAsync(EcoleB, "boarding_attendances")).Should().Be(1);
    }

    [Fact]
    public async Task Deleting_A_School_Year_Removes_Its_Stays_Leaves_And_Attendances_And_Keeps_The_Rest()
    {
        await RunPurgeAsync("SELECT count(*) FROM delete_school_year(@school, @year);", EcoleA, ("school", EcoleA), ("year", AnneePassee));

        // Le séjour de l'année passée (avec sa sortie et son pointage) a disparu ; celui de l'année active reste.
        (await _sql.CountAsync(EcoleA, "boarding_enrollments")).Should().Be(1);
        (await _sql.CountAsync(EcoleA, "boarding_leaves")).Should().Be(1);
        (await _sql.CountAsync(EcoleA, "boarding_attendances")).Should().Be(1);

        // Pavillons, chambres et lits ne sont pas annuels : ils survivent.
        (await _sql.CountAsync(EcoleA, "dormitories")).Should().Be(1);
        (await _sql.CountAsync(EcoleA, "dormitory_rooms")).Should().Be(1);
        (await _sql.CountAsync(EcoleA, "beds")).Should().Be(1);

        (await _sql.CountAsync(EcoleB, "boarding_enrollments")).Should().Be(1, "l'école B est intacte");
    }

    [Fact]
    public async Task The_Purge_Patch_Is_Reversible_And_Leaves_The_Functions_Unpatched_After_Down()
    {
        var before = await FunctionDefinitionsAsync();
        before.Should().Contain("boarding_attendances", "le patch est appliqué");

        await using (var owner = _db.NewOwnerContext())
        {
            // Retour à la migration qui précède le patch : Down() doit retirer exactement ce qu'Up() a inséré.
            await owner.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        }

        (await FunctionDefinitionsAsync()).Should().NotContain("boarding_attendances");

        await using (var owner = _db.NewOwnerContext())
        {
            await owner.GetService<IMigrator>().MigrateAsync();
        }

        (await FunctionDefinitionsAsync()).Should().Be(before, "rejouer Up après Down redonne la même définition");
    }

    private static readonly string[] BoardingTables =
    [
        "boarding_attendances", "boarding_leaves", "boarding_enrollments", "beds", "dormitory_rooms", "dormitories"
    ];

    private static Enrollment NewEnrollment(Guid id, Guid school, Guid student, Guid year, Guid classroom, string receipt) => new()
    {
        Id = id, SchoolId = school, StudentId = student, SchoolYearId = year, ClassroomId = classroom,
        Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed, ReceiptNumber = receipt,
        EnrolledAt = DateTimeOffset.UtcNow
    };

    private async Task RunPurgeAsync(string sql, Guid session, params (string Name, object Value)[] parameters)
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(session);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteScalarAsync();
    }

    private async Task<string> FunctionDefinitionsAsync()
    {
        await using var owner = _db.NewOwnerContext();
        var connection = owner.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT pg_get_functiondef('reset_school_data(uuid)'::regprocedure)
                   || pg_get_functiondef('delete_school_year(uuid, uuid)'::regprocedure)
            """;
        return (string)(await command.ExecuteScalarAsync())!;
    }
}
