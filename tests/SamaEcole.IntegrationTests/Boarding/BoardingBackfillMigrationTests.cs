#pragma warning disable CS0618 // Ancien modèle (Enrollment.BoardingStatus/RoomId) volontairement utilisé : c'est ce que la reprise lit.

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Boarding;

/// <summary>
/// Reprise des données Internat héritées (spec 2026-10-06 §4.1, plan lot A tâche 4). Les dortoirs/chambres/inscriptions
/// sont semés dans le schéma COURANT (les colonnes héritées <c>enrollments.BoardingStatus/RoomId</c> existent toujours),
/// puis on RECULE avant la migration et on la rejoue sur ces données — exactement ce qui arrive en production.
/// </summary>
[Trait("Category", "MultiTenant")]
public class BoardingBackfillMigrationTests : IAsyncLifetime
{
    private const string PreviousMigration = "20261006145320_AddManagedCyclesToSchoolSettings";

    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("d1111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("d2222222-2222-2222-2222-222222222222");
    private static readonly Guid AnneeActive = Guid.Parse("d1111111-0000-0000-0000-000000000001");
    private static readonly Guid AnneePassee = Guid.Parse("d1111111-0000-0000-0000-000000000002");
    private static readonly Guid AnneeB = Guid.Parse("d2222222-0000-0000-0000-000000000001");
    private static readonly Guid ClasseA = Guid.Parse("d1111111-0000-0000-0000-0000000000c1");
    private static readonly Guid ClasseB = Guid.Parse("d2222222-0000-0000-0000-0000000000c1");

    // Bâtiments et chambres.
    private static readonly Guid PavGarcons = Guid.Parse("d1111111-0000-0000-0000-00000000b001");
    private static readonly Guid PavFilles = Guid.Parse("d1111111-0000-0000-0000-00000000b002");
    private static readonly Guid PavMixte = Guid.Parse("d1111111-0000-0000-0000-00000000b003");
    private static readonly Guid BlocClasses = Guid.Parse("d1111111-0000-0000-0000-00000000b004");
    private static readonly Guid PavB = Guid.Parse("d2222222-0000-0000-0000-00000000b001");
    private static readonly Guid Ch101 = Guid.Parse("d1111111-0000-0000-0000-00000000a101");
    private static readonly Guid Ch102 = Guid.Parse("d1111111-0000-0000-0000-00000000a102");
    private static readonly Guid Ch201 = Guid.Parse("d1111111-0000-0000-0000-00000000a201");
    private static readonly Guid Ch301 = Guid.Parse("d1111111-0000-0000-0000-00000000a301");
    private static readonly Guid Salle1 = Guid.Parse("d1111111-0000-0000-0000-00000000a901");
    private static readonly Guid ChB = Guid.Parse("d2222222-0000-0000-0000-00000000a101");

    private readonly Dictionary<string, Guid> _enrollments = new();

    public Task InitializeAsync() => _db.InitializeAsync();

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task Migration_Backfills_Dormitories_Rooms_Beds_And_Stays_From_The_Legacy_Model()
    {
        await SeedLegacyAsync();

        await MigrateToAsync(PreviousMigration);
        await MigrateToAsync(null);

        // --- Pavillons : un par bâtiment ayant une chambre Dortoir, Id = Building.Id, genre déduit.
        var dormitories = await QueryAsync("""SELECT "Id", "Gender" FROM dormitories WHERE "SchoolId" = @school""", EcoleA);
        dormitories.Should().BeEquivalentTo(new[]
        {
            new Row(PavGarcons, "Garcons"), new Row(PavFilles, "Filles"), new Row(PavMixte, "Mixte")
        }, "pas de pavillon pour le bloc de classes ; le pavillon d'élèves des deux genres devient Mixte");

        // --- Chambres : Id = Room.Id.
        var rooms = await ScalarListAsync("""SELECT "Id" FROM dormitory_rooms WHERE "SchoolId" = @school""", EcoleA);
        rooms.Should().BeEquivalentTo(new[] { Ch101, Ch102, Ch201, Ch301 });

        // --- Lits : max(capacité, internes de l'année active), numérotés 1..N.
        (await BedCountAsync(Ch101)).Should().Be(2);
        (await BedCountAsync(Ch102)).Should().Be(2, "2 internes pour une capacité héritée de 1 : on ne perd personne");
        (await BedCountAsync(Ch201)).Should().Be(2);
        (await BedCountAsync(Ch301)).Should().Be(3);

        // --- Séjours : 9 actifs (année active) + 1 clos (année passée) ; externe et annulé ignorés.
        var stays = await StaysAsync(EcoleA);
        stays.Count(s => s.IsActive).Should().Be(9);
        stays.Count(s => !s.IsActive).Should().Be(1);
        stays.Should().NotContain(s => s.EnrollmentId == _enrollments["x2"] || s.EnrollmentId == _enrollments["x3"]);

        var closed = stays.Single(s => !s.IsActive);
        closed.EnrollmentId.Should().Be(_enrollments["g1_old"]);
        closed.BedId.Should().BeNull("un séjour clos ne tient aucun lit");
        closed.EndDate.Should().Be(new DateOnly(2026, 6, 30), "fin de l'année passée");

        // --- Affectation des lits dans l'ordre d'inscription ; demi-pension et « en attente » sans lit.
        var bedsOf101 = await BedNumbersAsync(_enrollments["g1"], _enrollments["g2"]);
        bedsOf101.Should().Equal(1, 2);
        (await BedNumbersAsync(_enrollments["f1"])).Should().Equal(1);
        (await BedNumbersAsync(_enrollments["g3"], _enrollments["g4"])).Should().Equal(1, 2);
        stays.Single(s => s.EnrollmentId == _enrollments["f2"]).BedId.Should().BeNull("un demi-pensionnaire n'a pas de lit");
        stays.Single(s => s.EnrollmentId == _enrollments["x1"]).BedId.Should().BeNull("interne sans chambre : en attente");
        stays.Where(s => s.BedId is not null).Select(s => s.BedId).Should().OnlyHaveUniqueItems();
        stays.Single(s => s.EnrollmentId == _enrollments["f2"]).Regime.Should().Be("DemiPensionnaire");

        // --- Isolation entre écoles : la reprise de l'école B est la sienne.
        (await QueryAsync("""SELECT "Id", "Gender" FROM dormitories WHERE "SchoolId" = @school""", EcoleB))
            .Should().ContainSingle().Which.Id.Should().Be(PavB);
        (await StaysAsync(EcoleB)).Should().ContainSingle();

        // --- Données héritées intactes (la reprise ne fait que LIRE).
        (await ScalarAsync<long>("""SELECT count(*) FROM enrollments WHERE "RoomId" IS NOT NULL AND "SchoolId" = @school""", EcoleA))
            .Should().Be(8 + 1 /* g1_old */ + 1 /* x3 annulé */);

        // --- Contrôle d'invariants (spec §4.1.5) : autant de séjours actifs que d'inscriptions pensionnaires actives.
        var expectedActive = await ScalarAsync<long>("""
            SELECT count(*) FROM enrollments e
            JOIN school_years y ON y."SchoolId" = e."SchoolId" AND y."Id" = e."SchoolYearId" AND y."IsActive"
            WHERE e."SchoolId" = @school AND e."BoardingStatus" <> 'Externe' AND e."Status" <> 'Cancelled' AND NOT e."IsDeleted"
            """, EcoleA);
        expectedActive.Should().Be(stays.Count(s => s.IsActive));
    }

    [Fact]
    public async Task Migration_Is_Reversible_And_Can_Be_Replayed_With_The_Same_Result()
    {
        await SeedLegacyAsync();

        await MigrateToAsync(PreviousMigration);
        (await TableExistsAsync()).Should().BeFalse("Down supprime les tables");

        await MigrateToAsync(null);
        var first = await ScalarAsync<long>("SELECT count(*) FROM boarding_enrollments", null);

        await MigrateToAsync(PreviousMigration);
        await MigrateToAsync(null);
        var second = await ScalarAsync<long>("SELECT count(*) FROM boarding_enrollments", null);

        first.Should().Be(11, "10 séjours de l'école A + 1 de l'école B");
        second.Should().Be(first);
        (await ScalarAsync<long>("""SELECT count(*) FROM enrollments WHERE "BoardingStatus" <> 'Externe'""", null))
            .Should().BeGreaterThan(0, "les colonnes héritées survivent à Down/Up");
    }

    // ------------------------------------------------------------------ Données héritées

    private async Task SeedLegacyAsync()
    {
        await using var owner = _db.NewOwnerContext();

        owner.Schools.AddRange(new School { Id = EcoleA, Name = "École A" }, new School { Id = EcoleB, Name = "École B" });
        owner.SchoolYears.AddRange(
            new SchoolYear { Id = AnneeActive, SchoolId = EcoleA, Label = "2026-2027", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true },
            new SchoolYear { Id = AnneePassee, SchoolId = EcoleA, Label = "2025-2026", StartDate = new DateOnly(2025, 9, 1), EndDate = new DateOnly(2026, 6, 30) },
            new SchoolYear { Id = AnneeB, SchoolId = EcoleB, Label = "2026-2027", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true });
        owner.Classrooms.AddRange(
            new Classroom { Id = ClasseA, SchoolId = EcoleA, Name = "4ème A", Level = "Collège", Cycle = CycleType.College, Capacity = 60 },
            new Classroom { Id = ClasseB, SchoolId = EcoleB, Name = "4ème A", Level = "Collège", Cycle = CycleType.College, Capacity = 60 });

        owner.Buildings.AddRange(
            new Building { Id = PavGarcons, SchoolId = EcoleA, Name = "Pavillon Garçons" },
            new Building { Id = PavFilles, SchoolId = EcoleA, Name = "Pavillon Filles" },
            new Building { Id = PavMixte, SchoolId = EcoleA, Name = "Pavillon Mixte" },
            new Building { Id = BlocClasses, SchoolId = EcoleA, Name = "Bloc classes" },
            new Building { Id = PavB, SchoolId = EcoleB, Name = "Pavillon B" });
        owner.Rooms.AddRange(
            new Room { Id = Ch101, SchoolId = EcoleA, BuildingId = PavGarcons, Name = "Dortoir 101", Capacity = 2, Type = RoomType.Dortoir },
            new Room { Id = Ch102, SchoolId = EcoleA, BuildingId = PavGarcons, Name = "Dortoir 102", Capacity = 1, Type = RoomType.Dortoir },
            new Room { Id = Ch201, SchoolId = EcoleA, BuildingId = PavFilles, Name = "Dortoir 201", Capacity = 2, Type = RoomType.Dortoir },
            new Room { Id = Ch301, SchoolId = EcoleA, BuildingId = PavMixte, Name = "Dortoir 301", Capacity = 3, Type = RoomType.Dortoir },
            new Room { Id = Salle1, SchoolId = EcoleA, BuildingId = BlocClasses, Name = "Salle 1", Capacity = 30, Type = RoomType.SalleDeClasse },
            new Room { Id = ChB, SchoolId = EcoleB, BuildingId = PavB, Name = "Dortoir B1", Capacity = 2, Type = RoomType.Dortoir });

        var day = 0;
        void Add(string key, Guid school, Guid year, Guid classroom, string gender, BoardingStatus status, Guid? room, EnrollmentStatus enrollmentStatus = EnrollmentStatus.Confirmed, DateTimeOffset? enrolledAt = null)
        {
            var student = new Student { Id = Guid.NewGuid(), SchoolId = school, Matricule = $"M-{key}", FullName = key, BirthDate = new DateOnly(2011, 1, 1), BirthPlace = "Dakar", Gender = gender, ClassroomId = classroom };
            var enrollment = new Enrollment
            {
                Id = Guid.NewGuid(), SchoolId = school, StudentId = student.Id, SchoolYearId = year, ClassroomId = classroom,
                Type = EnrollmentType.NewEnrollment, Status = enrollmentStatus, ReceiptNumber = $"REC-{key}",
                EnrolledAt = enrolledAt ?? new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero).AddDays(day++),
                BoardingStatus = status, RoomId = room
            };
            _enrollments[key] = enrollment.Id;
            owner.Students.Add(student);
            owner.Enrollments.Add(enrollment);
        }

        Add("g1", EcoleA, AnneeActive, ClasseA, "M", BoardingStatus.Interne, Ch101);
        Add("g2", EcoleA, AnneeActive, ClasseA, "M", BoardingStatus.Interne, Ch101);
        Add("g3", EcoleA, AnneeActive, ClasseA, "M", BoardingStatus.Interne, Ch102);
        Add("g4", EcoleA, AnneeActive, ClasseA, "M", BoardingStatus.Interne, Ch102);
        // f2 (demi-pensionnaire) est inscrite AVANT f1 : elle ne doit pas décaler le lit de f1.
        Add("f2", EcoleA, AnneeActive, ClasseA, "F", BoardingStatus.DemiPensionnaire, Ch201);
        Add("f1", EcoleA, AnneeActive, ClasseA, "F", BoardingStatus.Interne, Ch201);
        Add("m1", EcoleA, AnneeActive, ClasseA, "M", BoardingStatus.Interne, Ch301);
        Add("m2", EcoleA, AnneeActive, ClasseA, "F", BoardingStatus.Interne, Ch301);
        Add("x1", EcoleA, AnneeActive, ClasseA, "M", BoardingStatus.Interne, null);
        Add("x2", EcoleA, AnneeActive, ClasseA, "M", BoardingStatus.Externe, null);
        Add("x3", EcoleA, AnneeActive, ClasseA, "M", BoardingStatus.Interne, Ch101, EnrollmentStatus.Cancelled);
        Add("g1_old", EcoleA, AnneePassee, ClasseA, "M", BoardingStatus.Interne, Ch101,
            enrolledAt: new DateTimeOffset(2025, 9, 5, 8, 0, 0, TimeSpan.Zero));
        Add("b1", EcoleB, AnneeB, ClasseB, "M", BoardingStatus.Interne, ChB);

        await owner.SaveChangesAsync();
    }

    // ------------------------------------------------------------------ Lectures (rôle propriétaire)

    private sealed record Row(Guid Id, string Gender);

    private sealed record Stay(Guid EnrollmentId, string Regime, Guid? BedId, bool IsActive, DateOnly? EndDate);

    private async Task<List<Row>> QueryAsync(string sql, Guid school)
    {
        await using var connection = new NpgsqlConnection(_db.OwnerConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("school", school);
        var rows = new List<Row>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new Row(reader.GetGuid(0), reader.GetString(1)));
        }

        return rows;
    }

    private async Task<List<Guid>> ScalarListAsync(string sql, Guid? school)
    {
        await using var connection = new NpgsqlConnection(_db.OwnerConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("school", (object?)school ?? DBNull.Value);
        var values = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetGuid(0));
        }

        return values;
    }

    private async Task<T> ScalarAsync<T>(string sql, Guid? school)
    {
        await using var connection = new NpgsqlConnection(_db.OwnerConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        if (school is not null)
        {
            command.Parameters.AddWithValue("school", school.Value);
        }

        return (T)(await command.ExecuteScalarAsync())!;
    }

    private Task<long> BedCountAsync(Guid room) =>
        ScalarAsync<long>($"""SELECT count(*) FROM beds WHERE "DormitoryRoomId" = '{room}'""", null);

    private async Task<List<Stay>> StaysAsync(Guid school)
    {
        await using var connection = new NpgsqlConnection(_db.OwnerConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT "EnrollmentId", "Regime", "BedId", "IsActive", "EndDate" FROM boarding_enrollments WHERE "SchoolId" = @school
            """, connection);
        command.Parameters.AddWithValue("school", school);
        var stays = new List<Stay>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            stays.Add(new Stay(
                reader.GetGuid(0), reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetGuid(2),
                reader.GetBoolean(3),
                reader.IsDBNull(4) ? null : DateOnly.FromDateTime(reader.GetDateTime(4))));
        }

        return stays;
    }

    private async Task<List<int>> BedNumbersAsync(params Guid[] enrollmentsInOrder)
    {
        var numbers = new List<int>();
        foreach (var enrollment in enrollmentsInOrder)
        {
            numbers.Add(await ScalarAsync<int>($"""
                SELECT b."BedNumber" FROM boarding_enrollments be JOIN beds b ON b."Id" = be."BedId"
                WHERE be."EnrollmentId" = '{enrollment}'
                """, null));
        }

        return numbers;
    }

    private async Task MigrateToAsync(string? targetMigration)
    {
        await using var owner = _db.NewOwnerContext();
        await owner.GetService<IMigrator>().MigrateAsync(targetMigration);
    }

    private async Task<bool> TableExistsAsync()
    {
        await using var owner = _db.NewOwnerContext();
        var connection = owner.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT to_regclass('public.boarding_enrollments') IS NOT NULL";
        return (bool)(await command.ExecuteScalarAsync())!;
    }
}
