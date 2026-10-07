#pragma warning disable CS0618 // Ancien modèle (Enrollment.BoardingStatus/RoomId) volontairement utilisé : c'est ce que la réconciliation lit.

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using SamaEcole.Persistence;
using Xunit;

namespace SamaEcole.IntegrationTests.Boarding;

/// <summary>
/// Réconciliation à usage unique des séjours avec l'ancien modèle (lot C, tâche 1). Le script SQL testé est EXACTEMENT
/// celui que la migration exécute (<see cref="BoardingReconciliationSql.Script"/>). Règle : l'ancien modèle prévaut pour
/// les séjours, la structure est additive.
/// </summary>
[Trait("Category", "MultiTenant")]
public class BoardingReconciliationTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("d7111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("d7222222-2222-2222-2222-222222222222");
    private static readonly Guid AnneeActive = Guid.Parse("d7111111-0000-0000-0000-000000000001");
    private static readonly Guid AnneePassee = Guid.Parse("d7111111-0000-0000-0000-000000000002");
    private static readonly Guid AnneeB = Guid.Parse("d7222222-0000-0000-0000-000000000001");
    private static readonly Guid ClasseA = Guid.Parse("d7111111-0000-0000-0000-0000000000c1");
    private static readonly Guid ClasseB = Guid.Parse("d7222222-0000-0000-0000-0000000000c1");
    private static readonly Guid PavGarcons = Guid.Parse("d7111111-0000-0000-0000-00000000b001");
    private static readonly Guid PavFilles = Guid.Parse("d7111111-0000-0000-0000-00000000b002");
    private static readonly Guid BlocClasses = Guid.Parse("d7111111-0000-0000-0000-00000000b003");
    private static readonly Guid PavB = Guid.Parse("d7222222-0000-0000-0000-00000000b001");
    private static readonly Guid Ch101 = Guid.Parse("d7111111-0000-0000-0000-00000000a101");
    private static readonly Guid Ch102 = Guid.Parse("d7111111-0000-0000-0000-00000000a102");
    private static readonly Guid Ch201 = Guid.Parse("d7111111-0000-0000-0000-00000000a201");
    private static readonly Guid Salle1 = Guid.Parse("d7111111-0000-0000-0000-00000000a901");
    private static readonly Guid ChB = Guid.Parse("d7222222-0000-0000-0000-00000000a101");

    private readonly Dictionary<string, Guid> _enrollments = new();
    private readonly Dictionary<string, Guid> _students = new();
    private int _day;

    public Task InitializeAsync() => _db.InitializeAsync();

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    // ------------------------------------------------------------------ Scénarios

    [Fact]
    public async Task A_First_Run_Creates_Structure_And_Seats_Every_Boarder()
    {
        await SeedLegacyAsync();

        await RunReconciliationAsync();

        var dormitories = await RowsAsync(
            """SELECT "Id", "Gender" FROM dormitories WHERE "SchoolId" = @s ORDER BY "Name" """, ("s", EcoleA));
        dormitories.Select(r => ((Guid)r[0], (string)r[1])).Should().BeEquivalentTo(new[]
        {
            (PavFilles, "Filles"), (PavGarcons, "Garcons")
        });

        (await GuidsAsync("""SELECT "Id" FROM dormitory_rooms WHERE "SchoolId" = @s""", ("s", EcoleA)))
            .Should().BeEquivalentTo(new[] { Ch101, Ch102, Ch201 });

        (await BedCountAsync(Ch101)).Should().Be(2);
        (await BedCountAsync(Ch102)).Should().Be(1);
        (await BedCountAsync(Ch201)).Should().Be(2);

        (await StayAsync("g1")).Single().BedNumber.Should().Be(1);
        (await StayAsync("g2")).Single().BedNumber.Should().Be(2);
        (await StayAsync("g3")).Single().RoomId.Should().Be(Ch102);
        (await StayAsync("f1")).Single().RoomId.Should().Be(Ch201);
        (await StayAsync("f2")).Single().Should().Match<Stay>(s => s.Regime == "DemiPensionnaire" && s.BedId == null);
        (await StayAsync("x1")).Single().BedId.Should().BeNull("interne sans chambre : en attente");
        (await StayAsync("x2")).Should().BeEmpty("un externe n'a pas de séjour");

        (await GuidsAsync("""SELECT "Id" FROM dormitories WHERE "SchoolId" = @s""", ("s", EcoleB)))
            .Should().ContainSingle().Which.Should().Be(PavB);

        await AssertInvariantsAsync();
    }

    [Fact]
    public async Task Drift_After_The_Backfill_Is_Reconciled_And_The_Legacy_Model_Wins()
    {
        await SeedLegacyAsync();
        await RunReconciliationAsync();

        // --- Écritures de l'ancien écran APRÈS la reprise ---
        await ExecAsync("""UPDATE enrollments SET "RoomId" = @r WHERE "Id" = @id""", ("r", Ch102), ("id", _enrollments["g1"]));
        await ExecAsync("""UPDATE enrollments SET "BoardingStatus" = 'Externe', "RoomId" = NULL WHERE "Id" = @id""", ("id", _enrollments["f1"]));
        await ExecAsync("""UPDATE enrollments SET "Status" = 'Cancelled' WHERE "Id" = @id""", ("id", _enrollments["g3"]));
        await ExecAsync("""UPDATE enrollments SET "BoardingStatus" = 'Interne' WHERE "Id" = @id""", ("id", _enrollments["f2"]));
        await ExecAsync("""UPDATE enrollments SET "RoomId" = @r WHERE "Id" = @id""", ("r", Ch101), ("id", _enrollments["x1"]));
        var ch103 = Guid.Parse("d7111111-0000-0000-0000-00000000a103");
        var pavNeuf = Guid.Parse("d7111111-0000-0000-0000-00000000b004");
        var ch301 = Guid.Parse("d7111111-0000-0000-0000-00000000a301");
        await using (var owner = _db.NewOwnerContext())
        {
            owner.Buildings.Add(new Building { Id = pavNeuf, SchoolId = EcoleA, Name = "Pavillon neuf" });
            owner.Rooms.AddRange(
                new Room { Id = ch103, SchoolId = EcoleA, BuildingId = PavGarcons, Name = "Dortoir 103", Capacity = 2, Type = RoomType.Dortoir },
                new Room { Id = ch301, SchoolId = EcoleA, BuildingId = pavNeuf, Name = "Dortoir 301", Capacity = 1, Type = RoomType.Dortoir });
            AddBoarder(owner, "n1", EcoleA, AnneeActive, ClasseA, "M", BoardingStatus.Interne, Ch101);
            AddBoarder(owner, "n2", EcoleA, AnneeActive, ClasseA, "M", BoardingStatus.Interne, ch103);
            AddBoarder(owner, "n3", EcoleA, AnneeActive, ClasseA, "M", BoardingStatus.Interne, ch301);
            await owner.SaveChangesAsync();
        }

        await RunReconciliationAsync();

        (await StayAsync("g1")).Single().RoomId.Should().Be(Ch102, "g1 a changé de chambre dans l'ancien écran");
        var f1 = (await StayAsync("f1")).Single();
        (f1.IsActive, f1.BedId, f1.EndDate).Should().Be((false, (Guid?)null, f1.EndDate));
        f1.EndDate.Should().NotBeNull();
        (await StayAsync("g3")).Single().IsActive.Should().BeFalse("inscription annulée");
        (await StayAsync("f2")).Single().Should().Match<Stay>(s => s.Regime == "Interne" && s.RoomId == Ch201);
        (await StayAsync("x1")).Single().RoomId.Should().Be(Ch101);
        (await StayAsync("n1")).Single().RoomId.Should().Be(Ch101);

        (await BedCountAsync(Ch101)).Should().Be(3, "g2, x1 et n1 sont internes de la chambre 101 : un lit de complément est créé");
        (await GuidsAsync("""SELECT "Id" FROM dormitory_rooms WHERE "Id" = @r""", ("r", ch103))).Should().ContainSingle();
        (await StayAsync("n2")).Single().RoomId.Should().Be(ch103);
        (await GuidsAsync("""SELECT "Id" FROM dormitories WHERE "Id" = @r""", ("r", pavNeuf))).Should().ContainSingle();
        (await StayAsync("n3")).Single().RoomId.Should().Be(ch301);
        (await ScalarAsync<string>("""SELECT "Gender" FROM dormitories WHERE "Id" = @r""", ("r", pavNeuf))).Should().Be("Garcons");

        await AssertInvariantsAsync();
    }

    [Fact]
    public async Task Running_It_Twice_Changes_Nothing()
    {
        await SeedLegacyAsync();
        await RunReconciliationAsync();
        await ExecAsync("""UPDATE enrollments SET "RoomId" = @r WHERE "Id" = @id""", ("r", Ch102), ("id", _enrollments["g1"]));
        await RunReconciliationAsync();
        var before = await SnapshotAsync();

        await RunReconciliationAsync();

        (await SnapshotAsync()).Should().Equal(before);
    }

    [Fact]
    public async Task The_Structure_Created_By_The_New_Api_Is_Never_Touched()
    {
        await SeedLegacyAsync();
        await RunReconciliationAsync();

        var apiDormitory = Guid.NewGuid();
        var apiRoom = Guid.NewGuid();
        var apiBed = Guid.NewGuid();
        await ExecAsync("""
            INSERT INTO dormitories ("Id","SchoolId","Name","Gender","CreatedAt","IsDeleted") VALUES (@d,@s,'Pavillon API','Filles',NOW(),FALSE);
            INSERT INTO dormitory_rooms ("Id","SchoolId","DormitoryId","Name","CreatedAt","IsDeleted") VALUES (@r,@s,@d,'Chambre API',NOW(),FALSE);
            INSERT INTO beds ("Id","SchoolId","DormitoryRoomId","BedNumber","Status","CreatedAt","IsDeleted") VALUES (@b,@s,@r,1,'Maintenance',NOW(),FALSE);
            """, ("d", apiDormitory), ("r", apiRoom), ("b", apiBed), ("s", EcoleA));

        // x1 (Interne, sans chambre dans l'ancien modèle) est « posé » sur le lit de la nouvelle API : le legacy prévaut.
        await ExecAsync("""
            UPDATE beds SET "Status" = 'Available' WHERE "Id" = @b;
            UPDATE boarding_enrollments SET "BedId" = @b WHERE "EnrollmentId" = @e AND "IsActive";
            UPDATE beds SET "Status" = 'Maintenance' WHERE "Id" = @b;
            """, ("b", apiBed), ("e", _enrollments["x1"]));

        await RunReconciliationAsync();

        (await StayAsync("x1")).Single().BedId.Should().BeNull("l'ancien modèle dit « sans chambre »");
        (await ScalarAsync<string>("""SELECT "Name" FROM dormitories WHERE "Id" = @d""", ("d", apiDormitory))).Should().Be("Pavillon API");
        (await ScalarAsync<string>("""SELECT "Status" FROM beds WHERE "Id" = @b""", ("b", apiBed))).Should().Be("Maintenance");
        (await ScalarAsync<bool>("""SELECT "IsDeleted" FROM dormitory_rooms WHERE "Id" = @r""", ("r", apiRoom))).Should().BeFalse();
    }

    [Fact]
    public async Task A_Name_Conflict_Does_Not_Abort_The_Run()
    {
        await SeedLegacyAsync();
        await ExecAsync("""
            INSERT INTO dormitories ("Id","SchoolId","Name","Gender","CreatedAt","IsDeleted")
            VALUES (gen_random_uuid(), @s, 'Pavillon Garçons', 'Garcons', NOW(), FALSE)
            """, ("s", EcoleA));

        var act = async () => await RunReconciliationAsync();

        await act.Should().NotThrowAsync();
        (await GuidsAsync("""SELECT "Id" FROM dormitories WHERE "Id" = @d""", ("d", PavGarcons))).Should().BeEmpty("le nom est déjà pris");
        (await StayAsync("g1")).Single().BedId.Should().BeNull("occupant en attente : sa chambre n'a pas de pavillon");
        (await StayAsync("f1")).Single().BedId.Should().NotBeNull("les autres pavillons sont traités normalement");
    }

    [Fact]
    public async Task A_Room_Deleted_By_A_Director_Is_Not_Resurrected()
    {
        await SeedLegacyAsync();
        await RunReconciliationAsync();
        await ExecAsync("""UPDATE dormitory_rooms SET "IsDeleted" = TRUE WHERE "Id" = @r""", ("r", Ch101));
        await using (var owner = _db.NewOwnerContext())
        {
            AddBoarder(owner, "n1", EcoleA, AnneeActive, ClasseA, "M", BoardingStatus.Interne, Ch101);
            await owner.SaveChangesAsync();
        }

        await RunReconciliationAsync();

        (await ScalarAsync<bool>("""SELECT "IsDeleted" FROM dormitory_rooms WHERE "Id" = @r""", ("r", Ch101))).Should().BeTrue();
        (await StayAsync("n1")).Single().BedId.Should().BeNull("sa chambre est supprimée : il reste en attente");
    }

    [Fact]
    public async Task A_Year_Rollover_Closes_The_Stays_Of_The_Old_Year_And_Opens_The_New_One()
    {
        await SeedLegacyAsync();
        await RunReconciliationAsync();

        var anneeSuivante = Guid.Parse("d7111111-0000-0000-0000-000000000003");
        await ExecAsync("""UPDATE school_years SET "IsActive" = FALSE WHERE "Id" = @y""", ("y", AnneeActive));
        await using (var owner = _db.NewOwnerContext())
        {
            owner.SchoolYears.Add(new SchoolYear
            {
                Id = anneeSuivante, SchoolId = EcoleA, Label = "2027-2028",
                StartDate = new DateOnly(2027, 9, 1), EndDate = new DateOnly(2028, 6, 30), IsActive = true
            });
            await owner.SaveChangesAsync();
            owner.Enrollments.Add(new Enrollment
            {
                SchoolId = EcoleA, StudentId = _students["g1"], SchoolYearId = anneeSuivante, ClassroomId = ClasseA,
                Type = EnrollmentType.ReEnrollment, Status = EnrollmentStatus.Confirmed, ReceiptNumber = "REC-G1-NEW",
                EnrolledAt = new DateTimeOffset(2027, 9, 2, 8, 0, 0, TimeSpan.Zero),
                BoardingStatus = BoardingStatus.Interne, RoomId = Ch101
            });
            await owner.SaveChangesAsync();
        }

        await RunReconciliationAsync();

        var old = (await StayAsync("g1")).Single();
        old.IsActive.Should().BeFalse();
        old.EndDate.Should().Be(new DateOnly(2027, 6, 30), "fin de l'année scolaire désactivée");
        old.BedId.Should().BeNull();
        (await ScalarAsync<long>("""
            SELECT count(*) FROM boarding_enrollments be JOIN enrollments e ON e."Id" = be."EnrollmentId"
            WHERE e."StudentId" = @st AND e."SchoolYearId" = @y AND be."IsActive" AND be."BedId" IS NOT NULL
            """, ("st", _students["g1"]), ("y", anneeSuivante))).Should().Be(1, "séjour actif de la nouvelle année, assis sur un lit libéré");

        await AssertInvariantsAsync();
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
            new Building { Id = BlocClasses, SchoolId = EcoleA, Name = "Bloc classes" },
            new Building { Id = PavB, SchoolId = EcoleB, Name = "Pavillon B" });
        owner.Rooms.AddRange(
            new Room { Id = Ch101, SchoolId = EcoleA, BuildingId = PavGarcons, Name = "Dortoir 101", Capacity = 2, Type = RoomType.Dortoir },
            new Room { Id = Ch102, SchoolId = EcoleA, BuildingId = PavGarcons, Name = "Dortoir 102", Capacity = 1, Type = RoomType.Dortoir },
            new Room { Id = Ch201, SchoolId = EcoleA, BuildingId = PavFilles, Name = "Dortoir 201", Capacity = 2, Type = RoomType.Dortoir },
            new Room { Id = Salle1, SchoolId = EcoleA, BuildingId = BlocClasses, Name = "Salle 1", Capacity = 30, Type = RoomType.SalleDeClasse },
            new Room { Id = ChB, SchoolId = EcoleB, BuildingId = PavB, Name = "Dortoir B1", Capacity = 2, Type = RoomType.Dortoir });

        AddBoarder(owner, "g1", EcoleA, AnneeActive, ClasseA, "M", BoardingStatus.Interne, Ch101);
        AddBoarder(owner, "g2", EcoleA, AnneeActive, ClasseA, "M", BoardingStatus.Interne, Ch101);
        AddBoarder(owner, "g3", EcoleA, AnneeActive, ClasseA, "M", BoardingStatus.Interne, Ch102);
        AddBoarder(owner, "f1", EcoleA, AnneeActive, ClasseA, "F", BoardingStatus.Interne, Ch201);
        AddBoarder(owner, "f2", EcoleA, AnneeActive, ClasseA, "F", BoardingStatus.DemiPensionnaire, Ch201);
        AddBoarder(owner, "x1", EcoleA, AnneeActive, ClasseA, "M", BoardingStatus.Interne, null);
        AddBoarder(owner, "x2", EcoleA, AnneeActive, ClasseA, "M", BoardingStatus.Externe, null);
        AddBoarder(owner, "b1", EcoleB, AnneeB, ClasseB, "M", BoardingStatus.Interne, ChB);
        await owner.SaveChangesAsync();
    }

    private void AddBoarder(
        ApplicationDbContext owner, string key, Guid school, Guid year, Guid classroom, string gender,
        BoardingStatus status, Guid? room)
    {
        var student = new Student
        {
            Id = Guid.NewGuid(), SchoolId = school, Matricule = $"M-{key}", FullName = key,
            BirthDate = new DateOnly(2011, 1, 1), BirthPlace = "Dakar", Gender = gender, ClassroomId = classroom
        };
        var enrollment = new Enrollment
        {
            Id = Guid.NewGuid(), SchoolId = school, StudentId = student.Id, SchoolYearId = year, ClassroomId = classroom,
            Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed, ReceiptNumber = $"REC-{key}",
            EnrolledAt = new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero).AddDays(_day++),
            BoardingStatus = status, RoomId = room
        };
        _students[key] = student.Id;
        _enrollments[key] = enrollment.Id;
        owner.Students.Add(student);
        owner.Enrollments.Add(enrollment);
    }

    // ------------------------------------------------------------------ Exécution et lectures (rôle propriétaire)

    private async Task RunReconciliationAsync()
    {
        await using var connection = new NpgsqlConnection(_db.OwnerConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(BoardingReconciliationSql.Script, connection);
        await command.ExecuteNonQueryAsync();
    }

    private sealed record Stay(bool IsActive, string Regime, Guid? BedId, Guid? RoomId, int? BedNumber, DateOnly? EndDate);

    private async Task<List<Stay>> StayAsync(string key)
    {
        var rows = await RowsAsync("""
            SELECT be."IsActive", be."Regime", be."BedId", b."DormitoryRoomId", b."BedNumber", be."EndDate"
            FROM boarding_enrollments be LEFT JOIN beds b ON b."Id" = be."BedId"
            WHERE be."EnrollmentId" = @e ORDER BY be."IsActive" DESC, be."StartDate" DESC
            """, ("e", _enrollments[key]));

        return rows.Select(r => new Stay(
            (bool)r[0], (string)r[1],
            r[2] as Guid?, r[3] as Guid?, r[4] as int?,
            r[5] switch { DateOnly d => d, DateTime dt => DateOnly.FromDateTime(dt), _ => null })).ToList();
    }

    private Task<long> BedCountAsync(Guid room) =>
        ScalarAsync<long>("""SELECT count(*) FROM beds WHERE "DormitoryRoomId" = @r AND NOT "IsDeleted" """, ("r", room));

    private async Task<List<string>> SnapshotAsync() =>
        (await RowsAsync("""
            SELECT 'stay|' || be."Id" || '|' || be."EnrollmentId" || '|' || be."Regime" || '|' || COALESCE(be."BedId"::text, '-') || '|' || be."IsActive"
            FROM boarding_enrollments be
            UNION ALL SELECT 'bed|' || "Id" || '|' || "DormitoryRoomId" || '|' || "BedNumber" || '|' || "Status" FROM beds
            UNION ALL SELECT 'room|' || "Id" FROM dormitory_rooms
            UNION ALL SELECT 'dorm|' || "Id" FROM dormitories
            ORDER BY 1
            """)).Select(r => (string)r[0]).ToList();

    private async Task AssertInvariantsAsync()
    {
        // (a) autant de séjours actifs que d'inscriptions pensionnaires actives de l'année active
        var counts = (await RowsAsync("""
            SELECT (SELECT count(*) FROM boarding_enrollments be JOIN enrollments e ON e."Id" = be."EnrollmentId"
                    JOIN school_years y ON y."Id" = e."SchoolYearId" AND y."IsActive" WHERE be."IsActive" AND NOT be."IsDeleted"),
                   (SELECT count(*) FROM enrollments e JOIN school_years y ON y."Id" = e."SchoolYearId" AND y."IsActive"
                    WHERE e."BoardingStatus" <> 'Externe' AND e."Status" <> 'Cancelled' AND NOT e."IsDeleted")
            """)).Single();
        ((long)counts[0]).Should().Be((long)counts[1], "un séjour actif par inscription pensionnaire de l'année active");

        // (b) aucun séjour actif que le legacy ne justifie pas
        (await ScalarAsync<long>("""
            SELECT count(*) FROM boarding_enrollments be JOIN enrollments e ON e."Id" = be."EnrollmentId"
            JOIN school_years y ON y."Id" = e."SchoolYearId"
            WHERE be."IsActive" AND NOT be."IsDeleted"
              AND (e."BoardingStatus" = 'Externe' OR e."Status" = 'Cancelled' OR e."IsDeleted" OR NOT y."IsActive")
            """)).Should().Be(0);

        // (c) aucun lit partagé
        (await ScalarAsync<long>("""
            SELECT count(*) FROM (SELECT "BedId" FROM boarding_enrollments WHERE "IsActive" AND NOT "IsDeleted" AND "BedId" IS NOT NULL
                                  GROUP BY "BedId" HAVING count(*) > 1) t
            """)).Should().Be(0);

        // (d) un demi-pensionnaire n'a pas de lit
        (await ScalarAsync<long>("""
            SELECT count(*) FROM boarding_enrollments WHERE "IsActive" AND "Regime" = 'DemiPensionnaire' AND "BedId" IS NOT NULL
            """)).Should().Be(0);

        // (e) un interne assis est dans la chambre que lui assigne l'ancien modèle
        (await ScalarAsync<long>("""
            SELECT count(*) FROM boarding_enrollments be JOIN enrollments e ON e."Id" = be."EnrollmentId"
            JOIN beds b ON b."Id" = be."BedId"
            WHERE be."IsActive" AND NOT be."IsDeleted" AND be."Regime" = 'Interne' AND b."DormitoryRoomId" <> e."RoomId"
            """)).Should().Be(0);
    }

    private async Task ExecAsync(string sql, params (string Name, object? Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(_db.OwnerConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        await command.ExecuteNonQueryAsync();
    }

    private async Task<List<object[]>> RowsAsync(string sql, params (string Name, object? Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(_db.OwnerConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        var rows = new List<object[]>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var row = new object[reader.FieldCount];
            for (var i = 0; i < row.Length; i++)
            {
                row[i] = reader.GetValue(i);   // DBNull pour NULL : `as Guid?` / `as int?` donnent alors null
            }

            rows.Add(row);
        }

        return rows;
    }

    private async Task<List<Guid>> GuidsAsync(string sql, params (string Name, object? Value)[] parameters) =>
        (await RowsAsync(sql, parameters)).Select(r => (Guid)r[0]).ToList();

    private async Task<T> ScalarAsync<T>(string sql, params (string Name, object? Value)[] parameters) =>
        (T)(await RowsAsync(sql, parameters)).Single()[0];
}
