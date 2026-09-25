using FluentAssertions;
using SamaEcole.Application.Absences.Queries.GetTodaySlotsForStudent;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Schools;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.VieScolaire;

/// <summary>Horloge figée : « en cours » et « prochain » dépendent de l'heure, un test ne peut pas dépendre de la sienne.</summary>
file sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

/// <summary>
/// Évolution N°5, arbitrage B12 — les cours du jour de la classe d'un élève, avec le cours EN COURS (à défaut le
/// PROCHAIN) à présélectionner. École A : repos jeudi/vendredi ; le samedi 2026-09-26 porte deux cours
/// (08:00-10:00 et 10:00-12:00).
/// </summary>
public class TodaySlotsForStudentTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("f1111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("f2222222-2222-2222-2222-222222222222");
    private static readonly Guid Classe = Guid.Parse("faaaaaaa-0000-0000-0000-0000000000a1");
    private static readonly Guid ClasseB = Guid.Parse("faaaaaaa-0000-0000-0000-0000000000b1");
    private static readonly Guid Maths = Guid.Parse("fccccccc-0000-0000-0000-0000000000c1");
    private static readonly Guid Francais = Guid.Parse("fccccccc-0000-0000-0000-0000000000c2");
    private static readonly Guid Awa = Guid.Parse("feeeeeee-0000-0000-0000-0000000000e1");
    private static readonly Guid EleveB = Guid.Parse("feeeeeee-0000-0000-0000-0000000000e2");
    private static readonly Guid Prof = Guid.Parse("ff000000-0000-0000-0000-0000000000e1");
    private static readonly Guid ProfB = Guid.Parse("ff000000-0000-0000-0000-0000000000e2");
    private static readonly Guid Creneau1 = Guid.Parse("f5555555-0000-0000-0000-000000000001");
    private static readonly Guid Creneau2 = Guid.Parse("f5555555-0000-0000-0000-000000000002");
    private static readonly Guid CreneauJeudi = Guid.Parse("f5555555-0000-0000-0000-000000000003");

    private static readonly DateOnly Samedi = new(2026, 9, 26);
    private static readonly DateOnly Jeudi = new(2026, 9, 24);

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();

        owner.Schools.AddRange(new School { Id = EcoleA, Name = "École A" }, new School { Id = EcoleB, Name = "École B" });
        owner.SchoolSettings.Add(new SchoolSettings { SchoolId = EcoleA, WorkingDays = "Monday,Tuesday,Wednesday,Saturday,Sunday" });
        owner.Classrooms.AddRange(
            new Classroom { Id = Classe, SchoolId = EcoleA, Name = "CM2 A", Level = "Primaire", Capacity = 40 },
            new Classroom { Id = ClasseB, SchoolId = EcoleB, Name = "6e B", Level = "Collège", Capacity = 40 });
        owner.Subjects.AddRange(
            new Subject { Id = Maths, SchoolId = EcoleA, Name = "Mathématiques", Level = "Primaire", Coefficient = 1 },
            new Subject { Id = Francais, SchoolId = EcoleA, Name = "Français", Level = "Primaire", Coefficient = 1 });
        owner.Students.AddRange(
            new Student { Id = Awa, SchoolId = EcoleA, Matricule = "ELEV-0001", FullName = "Awa Fall", BirthDate = new DateOnly(2015, 1, 1), BirthPlace = "Dakar", Gender = "F", ClassroomId = Classe },
            new Student { Id = EleveB, SchoolId = EcoleB, Matricule = "ELEV-0001", FullName = "Ibra", BirthDate = new DateOnly(2014, 1, 1), BirthPlace = "Dakar", Gender = "M", ClassroomId = ClasseB });
        owner.Teachers.AddRange(
            new Teacher { Id = Prof, SchoolId = EcoleA, Matricule = "ENS-001", FullName = "Awa Sow", Email = "a@a.sn", BirthDate = new DateOnly(1985, 1, 1) },
            new Teacher { Id = ProfB, SchoolId = EcoleB, Matricule = "ENS-001", FullName = "Prof B", Email = "b@b.sn", BirthDate = new DateOnly(1985, 1, 1) });
        owner.ScheduleSlots.AddRange(
            Slot(Creneau1, Maths, DayOfWeek.Saturday, 8, 10),
            Slot(Creneau2, Francais, DayOfWeek.Saturday, 10, 12),
            Slot(CreneauJeudi, Maths, DayOfWeek.Thursday, 8, 10));

        await owner.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Theory]
    [InlineData(9, 0, "Creneau1", null)]      // pendant le premier cours
    [InlineData(10, 30, "Creneau2", null)]    // pendant le second
    [InlineData(10, 0, "Creneau2", null)]     // à l'heure pile de fin/début : le second commence
    [InlineData(7, 0, null, "Creneau1")]      // avant le premier : il est le prochain
    [InlineData(12, 0, null, null)]           // après le dernier : ni en cours ni prochain
    public async Task The_Current_Slot_Or_Else_The_Next_One_Is_Flagged(int hour, int minute, string? current, string? next)
    {
        await using var db = _db.NewAppContext(EcoleA);

        var slots = await HandlerAsync(db, hour, minute).Handle(new GetTodaySlotsForStudentQuery(Awa), default);

        slots.Should().HaveCount(2);
        slots.Where(s => s.IsCurrent).Select(s => s.SlotId).Should().Equal(Map(current));
        slots.Where(s => s.IsNext).Select(s => s.SlotId).Should().Equal(Map(next));
        slots.Select(s => s.Label).Should().Equal("08:00-10:00", "10:00-12:00");
    }

    [Fact]
    public async Task Another_Date_Has_Neither_A_Current_Nor_A_Next_Slot()
    {
        await using var db = _db.NewAppContext(EcoleA);

        // Le samedi suivant, à l'heure d'un cours d'aujourd'hui : « en cours » n'a de sens que pour aujourd'hui.
        var slots = await HandlerAsync(db, 9, 0).Handle(new GetTodaySlotsForStudentQuery(Awa, Samedi.AddDays(7)), default);

        slots.Should().HaveCount(2);
        slots.Should().OnlyContain(s => !s.IsCurrent && !s.IsNext);
    }

    [Fact]
    public async Task A_Rest_Day_Returns_No_Slot()
    {
        await using var db = _db.NewAppContext(EcoleA);

        (await HandlerAsync(db, 9, 0).Handle(new GetTodaySlotsForStudentQuery(Awa, Jeudi), default))
            .Should().BeEmpty("jeudi est un jour de repos : le billet reste possible sans cours visé");
    }

    [Fact]
    public async Task A_Slot_Shows_The_Status_Of_An_Active_Ticket_Already_Issued()
    {
        await using (var owner = _db.NewOwnerContext())
        {
            owner.LateArrivals.Add(new LateArrival
            {
                SchoolId = EcoleA, StudentId = Awa, Date = Samedi.ToDateTime(TimeOnly.MinValue), Minutes = 10,
                Reason = "Transport", TargetScheduleSlotId = Creneau1, Status = EntryTicketStatus.Issued
            });
            owner.LateArrivals.Add(new LateArrival
            {
                SchoolId = EcoleA, StudentId = Awa, Date = Samedi.ToDateTime(TimeOnly.MinValue), Minutes = 10,
                Reason = "Transport", TargetScheduleSlotId = Creneau2, Status = EntryTicketStatus.Cancelled
            });
            await owner.SaveChangesAsync();
        }

        await using var db = _db.NewAppContext(EcoleA);
        var slots = await HandlerAsync(db, 9, 0).Handle(new GetTodaySlotsForStudentQuery(Awa), default);

        slots.Single(s => s.SlotId == Creneau1).TicketStatus.Should().Be(EntryTicketStatus.Issued);
        slots.Single(s => s.SlotId == Creneau2).TicketStatus.Should().BeNull("un billet annulé n'est plus actif");
    }

    [Fact]
    [Trait("Category", "MultiTenant")]
    public async Task An_Unknown_Or_Foreign_Student_Is_Not_Found()
    {
        await using var db = _db.NewAppContext(EcoleA);
        var handler = HandlerAsync(db, 9, 0);

        var unknown = async () => await handler.Handle(new GetTodaySlotsForStudentQuery(Guid.NewGuid()), default);
        await unknown.Should().ThrowAsync<NotFoundException>();

        var foreign = async () => await handler.Handle(new GetTodaySlotsForStudentQuery(EleveB), default);
        await foreign.Should().ThrowAsync<NotFoundException>();
    }

    private static Guid[] Map(string? name) => name switch
    {
        "Creneau1" => [Creneau1],
        "Creneau2" => [Creneau2],
        _ => []
    };

    private static GetTodaySlotsForStudentQueryHandler HandlerAsync(SamaEcole.Persistence.ApplicationDbContext db, int hour, int minute)
        => new(db, new WorkingDayGuard(db), new FixedTimeProvider(new DateTimeOffset(2026, 9, 26, hour, minute, 0, TimeSpan.Zero)));

    private static ScheduleSlot Slot(Guid id, Guid subject, DayOfWeek day, int from, int to) => new()
    {
        Id = id, SchoolId = EcoleA, TeacherId = Prof, ClassroomId = Classe, SubjectId = subject,
        DayOfWeek = day, StartTime = new TimeOnly(from, 0), EndTime = new TimeOnly(to, 0)
    };
}
