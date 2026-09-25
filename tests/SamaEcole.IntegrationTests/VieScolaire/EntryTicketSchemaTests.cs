using FluentAssertions;
using Npgsql;
using SamaEcole.Domain.Entities;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.VieScolaire;

/// <summary>
/// Évolution N°5 — le circuit du billet d'entrée et le lien fiche d'appel ↔ créneau tiennent-ils DANS LA
/// BASE ? SQL brut sous le rôle applicatif, sans EF Core : seuls les index, les FK et la RLS font foi.
/// </summary>
[Trait("Category", "MultiTenant")]
public class EntryTicketSchemaTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("b1111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("b2222222-2222-2222-2222-222222222222");

    private static readonly Guid ClasseA = Guid.Parse("baaaaaaa-0000-0000-0000-0000000000a1");
    private static readonly Guid ClasseB = Guid.Parse("baaaaaaa-0000-0000-0000-0000000000b1");
    private static readonly Guid MatiereA = Guid.Parse("bccccccc-0000-0000-0000-0000000000c1");
    private static readonly Guid MatiereB = Guid.Parse("bccccccc-0000-0000-0000-0000000000c2");
    private static readonly Guid EleveA = Guid.Parse("beeeeeee-0000-0000-0000-0000000000e1");
    private static readonly Guid EleveB = Guid.Parse("beeeeeee-0000-0000-0000-0000000000e2");
    private static readonly Guid FicheA = Guid.Parse("bfffffff-0000-0000-0000-0000000000f1");
    private static readonly Guid FicheB = Guid.Parse("bfffffff-0000-0000-0000-0000000000f2");
    private static readonly Guid CreneauA = Guid.Parse("b5555555-0000-0000-0000-000000000051");
    private static readonly Guid CreneauA2 = Guid.Parse("b5555555-0000-0000-0000-000000000052");
    private static readonly Guid CreneauB = Guid.Parse("b5555555-0000-0000-0000-000000000053");
    private static readonly Guid AnneeA = Guid.Parse("b1111111-0000-0000-0000-000000000001");

    private static readonly DateOnly Samedi = new(2026, 9, 26);

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
        owner.Subjects.AddRange(
            new Subject { Id = MatiereA, SchoolId = EcoleA, Name = "Maths", Level = "Primaire", Coefficient = 1 },
            new Subject { Id = MatiereB, SchoolId = EcoleB, Name = "Maths", Level = "Primaire", Coefficient = 1 });
        owner.Students.AddRange(
            new Student { Id = EleveA, SchoolId = EcoleA, Matricule = "ELEV-0001", FullName = "Awa", BirthDate = new DateOnly(2015, 1, 1), BirthPlace = "Dakar", Gender = "F", ClassroomId = ClasseA },
            new Student { Id = EleveB, SchoolId = EcoleB, Matricule = "ELEV-0001", FullName = "Modou", BirthDate = new DateOnly(2015, 1, 1), BirthPlace = "Dakar", Gender = "M", ClassroomId = ClasseB });
        owner.SchoolYears.Add(new SchoolYear
        {
            Id = AnneeA, SchoolId = EcoleA, Label = "2026-2027", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true
        });
        var teacherA = new Teacher { SchoolId = EcoleA, Matricule = "ENS-001", FullName = "Prof A", Email = "a@a.sn", BirthDate = new DateOnly(1985, 1, 1) };
        var teacherB = new Teacher { SchoolId = EcoleB, Matricule = "ENS-001", FullName = "Prof B", Email = "b@b.sn", BirthDate = new DateOnly(1985, 1, 1) };
        owner.Teachers.AddRange(teacherA, teacherB);
        owner.ScheduleSlots.AddRange(
            new ScheduleSlot { Id = CreneauA, SchoolId = EcoleA, TeacherId = teacherA.Id, ClassroomId = ClasseA, SubjectId = MatiereA, DayOfWeek = DayOfWeek.Saturday, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(10, 0) },
            new ScheduleSlot { Id = CreneauA2, SchoolId = EcoleA, TeacherId = teacherA.Id, ClassroomId = ClasseA, SubjectId = MatiereA, DayOfWeek = DayOfWeek.Saturday, StartTime = new TimeOnly(10, 0), EndTime = new TimeOnly(12, 0) },
            new ScheduleSlot { Id = CreneauB, SchoolId = EcoleB, TeacherId = teacherB.Id, ClassroomId = ClasseB, SubjectId = MatiereB, DayOfWeek = DayOfWeek.Saturday, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(10, 0) });

        await owner.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    // Les billets existants (avant l'évolution) n'ont ni cours visé ni statut : NULL = « sans cours visé ».
    [Fact]
    public async Task A_Legacy_Late_Arrival_Keeps_A_Null_Status_And_No_Target()
    {
        await InsertTicketAsync(EcoleA, EleveA, slot: null, status: null);

        var (status, target) = await ReadFirstTicketAsync(EcoleA);

        status.Should().BeNull("aucune migration de données : un billet sans cours visé garde le comportement d'avant");
        target.Should().BeNull();
    }

    [Fact]
    public async Task Only_One_Active_Ticket_Exists_Per_Student_Slot_And_Day()
    {
        await InsertTicketAsync(EcoleA, EleveA, CreneauA, "Issued");

        var duplicate = async () => await InsertTicketAsync(EcoleA, EleveA, CreneauA, "Issued");
        await duplicate.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.UniqueViolation);

        var acceptedToo = async () => await InsertTicketAsync(EcoleA, EleveA, CreneauA, "Accepted");
        await acceptedToo.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.UniqueViolation);

        // Un AUTRE cours le même jour : aucun conflit.
        await InsertTicketAsync(EcoleA, EleveA, CreneauA2, "Issued");
    }

    [Fact]
    public async Task A_Cancelled_Ticket_Frees_The_Key_And_Tickets_Without_A_Slot_Never_Conflict()
    {
        await InsertTicketAsync(EcoleA, EleveA, CreneauA, "Cancelled");
        await InsertTicketAsync(EcoleA, EleveA, CreneauA, "Issued");   // réémis après annulation

        await InsertTicketAsync(EcoleA, EleveA, slot: null, status: null);
        await InsertTicketAsync(EcoleA, EleveA, slot: null, status: null); // deux billets sans cours visé : permis
    }

    [Fact]
    public async Task An_Attendance_Sheet_Can_Point_To_A_Schedule_Slot_Of_Its_School()
    {
        await InsertSheetAsync(EcoleA, FicheA, ClasseA, MatiereA, AnneeA, CreneauA);

        await using var connection = await _db.OpenRawAppConnectionAsync(EcoleA);
        await using var command = connection.CreateCommand();
        command.CommandText = """SELECT "ScheduleSlotId" FROM attendance_sheets WHERE "Id" = @id;""";
        command.Parameters.AddWithValue("id", FicheA);
        ((Guid)(await command.ExecuteScalarAsync())!).Should().Be(CreneauA);
    }

    [Fact]
    public async Task A_Sheet_Cannot_Point_To_A_Slot_That_Does_Not_Exist()
    {
        var act = async () => await InsertSheetAsync(EcoleA, FicheA, ClasseA, MatiereA, AnneeA, Guid.NewGuid());

        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.ForeignKeyViolation);
    }

    [Fact]
    public async Task One_School_Never_Reads_Another_Schools_Tickets()
    {
        await InsertTicketAsync(EcoleA, EleveA, CreneauA, "Issued");
        await InsertTicketAsync(EcoleB, EleveB, CreneauB, "Issued");

        (await CountTicketsAsync(EcoleA)).Should().Be(1);
        (await CountTicketsAsync(EcoleB)).Should().Be(1);
        (await CountTicketsAsync(schoolId: null)).Should().Be(0, "sans tenant, la RLS masque tout");
    }

    private async Task InsertTicketAsync(Guid schoolId, Guid studentId, Guid? slot, string? status)
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(schoolId);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO "LateArrivals"
                ("Id", "SchoolId", "StudentId", "Date", "Minutes", "Reason", "CreatedAt", "IsDeleted", "TargetScheduleSlotId", "Status")
            VALUES (gen_random_uuid(), @schoolId, @studentId, @date, 10, 'Transport', NOW(), FALSE, @slot, @status);
            """;
        command.Parameters.AddWithValue("schoolId", schoolId);
        command.Parameters.AddWithValue("studentId", studentId);
        command.Parameters.AddWithValue("date", Samedi.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("slot", (object?)slot ?? DBNull.Value);
        command.Parameters.AddWithValue("status", (object?)status ?? DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }

    private async Task InsertSheetAsync(Guid schoolId, Guid id, Guid classroomId, Guid subjectId, Guid yearId, Guid slot)
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(schoolId);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO attendance_sheets
                ("Id", "SchoolId", "ClassroomId", "SubjectId", "SchoolYearId", "Date", "Period", "TakenByUserId", "ScheduleSlotId", "CreatedAt", "IsDeleted")
            VALUES (@id, @schoolId, @classroomId, @subjectId, @yearId, DATE '2026-09-26', '08:00-10:00', gen_random_uuid(), @slot, NOW(), FALSE);
            """;
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("schoolId", schoolId);
        command.Parameters.AddWithValue("classroomId", classroomId);
        command.Parameters.AddWithValue("subjectId", subjectId);
        command.Parameters.AddWithValue("yearId", yearId);
        command.Parameters.AddWithValue("slot", slot);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<(string? Status, Guid? Target)> ReadFirstTicketAsync(Guid schoolId)
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(schoolId);
        await using var command = connection.CreateCommand();
        command.CommandText = """SELECT "Status", "TargetScheduleSlotId" FROM "LateArrivals" LIMIT 1;""";
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (reader.IsDBNull(0) ? null : reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetGuid(1));
    }

    private async Task<long> CountTicketsAsync(Guid? schoolId)
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(schoolId);
        await using var command = connection.CreateCommand();
        command.CommandText = """SELECT COUNT(*) FROM "LateArrivals";""";
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
