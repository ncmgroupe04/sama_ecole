using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Absences.Commands.CreateLateArrival;
using SamaEcole.Application.Attendance;
using SamaEcole.Application.Attendance.EntryTickets;
using SamaEcole.Application.Attendance.Commands.SubmitAttendanceSheet;
using SamaEcole.Application.Attendance.Events;
using SamaEcole.Application.Attendance.Queries.InitializeAttendanceSheet;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Reports;
using SamaEcole.Application.Schools;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using SamaEcole.Persistence;
using Xunit;
using ValidationException = SamaEcole.Application.Common.Exceptions.ValidationException;

namespace SamaEcole.IntegrationTests.VieScolaire;

file sealed class NoOpKpiCache : IKpiCacheService
{
    public Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken) =>
        factory(cancellationToken);

    public void Invalidate(string key) { }
}

/// <summary>Enregistre les notifications publiées : on prouve qu'un billet notifie la famille UNE fois, ou jamais.</summary>
file sealed class RecordingPublisher : IPublisher
{
    public List<object> Published { get; } = [];

    public Task Publish(object notification, CancellationToken cancellationToken = default)
    {
        Published.Add(notification);
        return Task.CompletedTask;
    }

    public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        Published.Add(notification!);
        return Task.CompletedTask;
    }
}

file sealed class StubTenant(Guid? schoolId) : ITenantProvider
{
    public Guid? CurrentSchoolId => schoolId;
}

/// <summary>
/// Évolution N°5, arbitrage B6 — un billet d'entrée qui vise un COURS met à jour le registre d'appel de ce
/// cours : la ligne de l'élève passe à « Retard » (statut précédent conservé), ou la feuille de l'enseignant
/// présélectionne le retard si l'appel n'est pas encore fait. École A : repos jeudi/vendredi.
/// </summary>
public class EntryTicketRegisterTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("e1111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("e2222222-2222-2222-2222-222222222222");
    private static readonly Guid ClasseA = Guid.Parse("eaaaaaaa-0000-0000-0000-0000000000a1");
    private static readonly Guid ClasseA2 = Guid.Parse("eaaaaaaa-0000-0000-0000-0000000000a2");
    private static readonly Guid ClasseB = Guid.Parse("eaaaaaaa-0000-0000-0000-0000000000b1");
    private static readonly Guid Maths = Guid.Parse("eccccccc-0000-0000-0000-0000000000c1");
    private static readonly Guid MathsB = Guid.Parse("eccccccc-0000-0000-0000-0000000000c3");
    private static readonly Guid AnneeA = Guid.Parse("e1111111-0000-0000-0000-000000000001");
    private static readonly Guid AnneeB = Guid.Parse("e2222222-0000-0000-0000-000000000001");
    private static readonly Guid Awa = Guid.Parse("eeeeeeee-0000-0000-0000-0000000000e1");
    private static readonly Guid Modou = Guid.Parse("eeeeeeee-0000-0000-0000-0000000000e2");
    private static readonly Guid EleveB = Guid.Parse("eeeeeeee-0000-0000-0000-0000000000e3");
    private static readonly Guid FicheProf = Guid.Parse("ee000000-0000-0000-0000-0000000000e1");
    private static readonly Guid FicheProfB = Guid.Parse("ee000000-0000-0000-0000-0000000000e2");

    private static readonly Guid CreneauSamedi = Guid.Parse("e5555555-0000-0000-0000-000000000001");
    private static readonly Guid CreneauJeudi = Guid.Parse("e5555555-0000-0000-0000-000000000002");
    private static readonly Guid CreneauAutreClasse = Guid.Parse("e5555555-0000-0000-0000-000000000003");
    private static readonly Guid CreneauB = Guid.Parse("e5555555-0000-0000-0000-000000000004");

    private static readonly DateOnly Samedi = new(2026, 9, 26);
    private static readonly DateOnly Jeudi = new(2026, 9, 24);

    private static readonly TestCurrentUser Directeur = new(Guid.Parse("ed000000-0000-0000-0000-000000000001"), Role.Directeur);

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();

        owner.Schools.AddRange(new School { Id = EcoleA, Name = "École A" }, new School { Id = EcoleB, Name = "École B" });
        owner.SchoolSettings.Add(new SchoolSettings { SchoolId = EcoleA, WorkingDays = "Monday,Tuesday,Wednesday,Saturday,Sunday" });
        owner.Classrooms.AddRange(
            new Classroom { Id = ClasseA, SchoolId = EcoleA, Name = "CM2 A", Level = "Primaire", Capacity = 40 },
            new Classroom { Id = ClasseA2, SchoolId = EcoleA, Name = "CM2 B", Level = "Primaire", Capacity = 40 },
            new Classroom { Id = ClasseB, SchoolId = EcoleB, Name = "6e B", Level = "Collège", Capacity = 40 });
        owner.Subjects.AddRange(
            new Subject { Id = Maths, SchoolId = EcoleA, Name = "Mathématiques", Level = "Primaire", Coefficient = 1 },
            new Subject { Id = MathsB, SchoolId = EcoleB, Name = "Mathématiques", Level = "Collège", Coefficient = 1 });
        owner.SchoolYears.AddRange(
            new SchoolYear { Id = AnneeA, SchoolId = EcoleA, Label = "2026-2027", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true },
            new SchoolYear { Id = AnneeB, SchoolId = EcoleB, Label = "2026-2027", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true });
        owner.Students.AddRange(
            new Student { Id = Awa, SchoolId = EcoleA, Matricule = "ELEV-0001", FullName = "Awa Fall", BirthDate = new DateOnly(2015, 1, 1), BirthPlace = "Dakar", Gender = "F", ClassroomId = ClasseA },
            new Student { Id = Modou, SchoolId = EcoleA, Matricule = "ELEV-0002", FullName = "Modou Diop", BirthDate = new DateOnly(2015, 1, 1), BirthPlace = "Dakar", Gender = "M", ClassroomId = ClasseA },
            new Student { Id = EleveB, SchoolId = EcoleB, Matricule = "ELEV-0001", FullName = "Ibra", BirthDate = new DateOnly(2014, 1, 1), BirthPlace = "Dakar", Gender = "M", ClassroomId = ClasseB });
        owner.Teachers.AddRange(
            new Teacher { Id = FicheProf, SchoolId = EcoleA, Matricule = "ENS-001", FullName = "Prof A", Email = "a@a.sn", BirthDate = new DateOnly(1985, 1, 1) },
            new Teacher { Id = FicheProfB, SchoolId = EcoleB, Matricule = "ENS-001", FullName = "Prof B", Email = "b@b.sn", BirthDate = new DateOnly(1985, 1, 1) });
        owner.ScheduleSlots.AddRange(
            Slot(CreneauSamedi, EcoleA, FicheProf, ClasseA, Maths, DayOfWeek.Saturday),
            Slot(CreneauJeudi, EcoleA, FicheProf, ClasseA, Maths, DayOfWeek.Thursday),
            Slot(CreneauAutreClasse, EcoleA, FicheProf, ClasseA2, Maths, DayOfWeek.Saturday),
            Slot(CreneauB, EcoleB, FicheProfB, ClasseB, MathsB, DayOfWeek.Saturday));

        await owner.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    // 1 — la fiche existe, l'élève est noté absent : le billet le passe en retard, et la famille est prévenue UNE fois.
    [Fact]
    public async Task A_Ticket_Turns_A_Recorded_Absence_Into_A_Late_Keeps_The_Previous_Status_And_Notifies_Once()
    {
        await SubmitSheetAsync(Samedi, (Awa, AttendanceStatus.UnjustifiedAbsence, 0), (Modou, AttendanceStatus.Present, 0));
        var publisher = new RecordingPublisher();

        await using var db = _db.NewAppContext(EcoleA);
        var ticketId = await IssueAsync(db, publisher, Awa, Samedi, minutes: 12, slot: CreneauSamedi);

        await using var check = _db.NewAppContext(EcoleA);
        var ticket = await check.LateArrivals.SingleAsync(t => t.Id == ticketId);
        ticket.Status.Should().Be(EntryTicketStatus.Issued);
        ticket.TargetScheduleSlotId.Should().Be(CreneauSamedi);
        ticket.PreviousStatus.Should().Be(AttendanceStatus.UnjustifiedAbsence);
        ticket.PreviousLateMinutes.Should().Be(0);

        var line = await check.StudentAttendances.SingleAsync(sa => sa.StudentId == Awa);
        line.Status.Should().Be(AttendanceStatus.Late);
        line.LateMinutes.Should().Be(12);
        line.EntryTicketId.Should().Be(ticketId);

        var notification = publisher.Published.Should().ContainSingle().Which.Should().BeOfType<AttendanceRecordedEvent>().Subject;
        notification.Status.Should().Be(AttendanceStatus.Late);
        notification.LateMinutes.Should().Be(12);
        notification.StudentId.Should().Be(Awa);
        notification.Period.Should().Be("08:00-10:00");
    }

    // 2 — la ligne était « Présent » : elle passe en retard, mais aucune « rectification » n'est envoyée.
    [Fact]
    public async Task A_Ticket_On_A_Present_Line_Makes_It_Late_Without_Any_Notification()
    {
        await SubmitSheetAsync(Samedi, (Awa, AttendanceStatus.Present, 0), (Modou, AttendanceStatus.Present, 0));
        var publisher = new RecordingPublisher();

        await using var db = _db.NewAppContext(EcoleA);
        await IssueAsync(db, publisher, Awa, Samedi, minutes: 8, slot: CreneauSamedi);

        await using var check = _db.NewAppContext(EcoleA);
        (await check.StudentAttendances.SingleAsync(sa => sa.StudentId == Awa)).Status.Should().Be(AttendanceStatus.Late);
        publisher.Published.Should().BeEmpty("il n'y avait aucune absence à rectifier auprès de la famille");
    }

    // 3 — l'appel n'est pas encore fait : rien n'est créé, la feuille présélectionne le retard ; à la soumission le billet est rattaché.
    [Fact]
    public async Task Before_The_Roll_Is_Taken_The_Roster_Preselects_The_Late_And_The_Sheet_Links_The_Ticket()
    {
        var publisher = new RecordingPublisher();
        await using var db = _db.NewAppContext(EcoleA);
        var ticketId = await IssueAsync(db, publisher, Awa, Samedi, minutes: 15, slot: CreneauSamedi);

        await using var check = _db.NewAppContext(EcoleA);
        (await check.AttendanceSheets.CountAsync()).Should().Be(0, "aucune fiche n'est créée par un billet");
        publisher.Published.Should().BeEmpty("la fiche n'existe pas : le retard sera notifié à la soumission");

        var roster = await RosterAsync(Samedi);
        var row = roster.Students.Single(r => r.StudentId == Awa);
        row.Status.Should().Be("Late");
        row.LateMinutes.Should().Be(15);
        row.EntryTicketId.Should().Be(ticketId);
        row.EntryTicketStatus.Should().Be("Issued");
        row.EntryTicketNumber.Should().StartWith("BILLET-");
        roster.Students.Single(r => r.StudentId == Modou).EntryTicketId.Should().BeNull();

        // L'enseignant valide la grille telle qu'elle est présélectionnée.
        await SubmitSheetAsync(Samedi, (Awa, AttendanceStatus.Late, 15), (Modou, AttendanceStatus.Present, 0));

        await using var after = _db.NewAppContext(EcoleA);
        (await after.StudentAttendances.SingleAsync(sa => sa.StudentId == Awa)).EntryTicketId.Should().Be(ticketId);
        (await after.StudentAttendances.SingleAsync(sa => sa.StudentId == Modou)).EntryTicketId.Should().BeNull();
    }

    // 4 — l'enseignant a marqué « Absent » un élève qui a un billet : sa saisie est respectée, le billet reste en attente.
    [Fact]
    public async Task A_Teacher_Who_Marks_The_Ticket_Holder_Absent_Is_Respected_And_The_Ticket_Stays_Pending()
    {
        await using var db = _db.NewAppContext(EcoleA);
        var ticketId = await IssueAsync(db, new RecordingPublisher(), Awa, Samedi, minutes: 15, slot: CreneauSamedi);

        await SubmitSheetAsync(Samedi, (Awa, AttendanceStatus.UnjustifiedAbsence, 0), (Modou, AttendanceStatus.Present, 0));

        await using var check = _db.NewAppContext(EcoleA);
        var line = await check.StudentAttendances.SingleAsync(sa => sa.StudentId == Awa);
        line.Status.Should().Be(AttendanceStatus.UnjustifiedAbsence, "l'enseignant décide de ce qu'il constate en classe");
        line.EntryTicketId.Should().Be(ticketId);
        (await check.LateArrivals.SingleAsync(t => t.Id == ticketId)).Status.Should().Be(EntryTicketStatus.Issued);

        var roster = await RosterAsync(Samedi);
        var row = roster.Students.Single(r => r.StudentId == Awa);
        row.Status.Should().Be("UnjustifiedAbsence");
        row.EntryTicketStatus.Should().Be("Issued", "la feuille signale qu'un billet est en attente d'acceptation");
    }

    // 5 — un cours qui ne convient pas est refusé, rien n'est écrit.
    [Fact]
    public async Task A_Slot_Of_Another_Class_Or_Another_Weekday_Is_Refused_And_Nothing_Is_Written()
    {
        await using var db = _db.NewAppContext(EcoleA);

        var otherClass = async () => await IssueAsync(db, new RecordingPublisher(), Awa, Samedi, 10, CreneauAutreClasse);
        (await otherClass.Should().ThrowAsync<ValidationException>()).Which.Errors
            .Should().ContainKey(nameof(CreateLateArrivalCommand.TargetScheduleSlotId));

        var otherWeekday = async () => await IssueAsync(db, new RecordingPublisher(), Awa, Samedi.AddDays(1), 10, CreneauSamedi);
        await otherWeekday.Should().ThrowAsync<ValidationException>();

        var unknown = async () => await IssueAsync(db, new RecordingPublisher(), Awa, Samedi, 10, Guid.NewGuid());
        await unknown.Should().ThrowAsync<ValidationException>();

        await using var check = _db.NewAppContext(EcoleA);
        (await check.LateArrivals.CountAsync()).Should().Be(0);
    }

    // 6 — au plus un billet ACTIF par élève, cours et jour.
    [Fact]
    public async Task A_Second_Active_Ticket_For_The_Same_Student_Slot_And_Day_Is_A_Conflict()
    {
        await using var db = _db.NewAppContext(EcoleA);
        await IssueAsync(db, new RecordingPublisher(), Awa, Samedi, 10, CreneauSamedi);

        await using var again = _db.NewAppContext(EcoleA);
        var act = async () => await IssueAsync(again, new RecordingPublisher(), Awa, Samedi, 10, CreneauSamedi);

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
    }

    // 7 — sans cours visé : le comportement d'avant, y compris un jour de repos (D4 de l'Évolution N°3).
    [Fact]
    public async Task A_Ticket_Without_A_Target_Behaves_As_Before_Even_On_A_Rest_Day()
    {
        await SubmitSheetAsync(Samedi, (Awa, AttendanceStatus.UnjustifiedAbsence, 0), (Modou, AttendanceStatus.Present, 0));
        var publisher = new RecordingPublisher();

        await using var db = _db.NewAppContext(EcoleA);
        var ticketId = await IssueAsync(db, publisher, Awa, Jeudi, minutes: 10, slot: null); // jeudi : repos

        await using var check = _db.NewAppContext(EcoleA);
        var ticket = await check.LateArrivals.SingleAsync(t => t.Id == ticketId);
        ticket.Status.Should().BeNull();
        ticket.TargetScheduleSlotId.Should().BeNull();
        (await check.StudentAttendances.SingleAsync(sa => sa.StudentId == Awa)).Status
            .Should().Be(AttendanceStatus.UnjustifiedAbsence, "aucun cours visé : le registre n'est pas touché");
        publisher.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task A_Ticket_Targeting_A_Course_On_A_Rest_Day_Is_Refused()
    {
        await using var db = _db.NewAppContext(EcoleA);

        var act = async () => await IssueAsync(db, new RecordingPublisher(), Awa, Jeudi, 10, CreneauJeudi);

        (await act.Should().ThrowAsync<ValidationException>()).Which.Errors.Values.SelectMany(v => v)
            .Should().Contain(m => m.Contains("jeudi"));
    }

    // 8 — le taux de présence reflète le passage Absent → Retard (un retard compte comme présent).
    [Fact]
    public async Task The_Attendance_Rate_Counts_The_Ticket_Holder_As_Present_Once_He_Is_Late()
    {
        await SubmitSheetAsync(Samedi, (Awa, AttendanceStatus.UnjustifiedAbsence, 0), (Modou, AttendanceStatus.Present, 0));

        await using var before = _db.NewAppContext(EcoleA);
        (await new AttendanceReportAggregator(before).ComputeAsync(Samedi, Samedi, null, default))
            .AverageAttendanceRate.Should().Be(0.5m);

        await using var db = _db.NewAppContext(EcoleA);
        await IssueAsync(db, new RecordingPublisher(), Awa, Samedi, 10, CreneauSamedi);

        await using var after = _db.NewAppContext(EcoleA);
        (await new AttendanceReportAggregator(after).ComputeAsync(Samedi, Samedi, null, default))
            .AverageAttendanceRate.Should().Be(1m);
    }

    // 9 — isolation.
    [Fact]
    [Trait("Category", "MultiTenant")]
    public async Task Another_School_Can_Neither_Target_These_Slots_Nor_See_These_Tickets()
    {
        await using var db = _db.NewAppContext(EcoleA);
        await IssueAsync(db, new RecordingPublisher(), Awa, Samedi, 10, CreneauSamedi);

        await using var autre = _db.NewAppContext(EcoleB);
        (await autre.LateArrivals.CountAsync()).Should().Be(0);

        // L'élève de B vise le créneau de A : introuvable dans B, jamais accepté.
        var act = async () => await new CreateLateArrivalCommandHandler(
                autre, new StubTenant(EcoleB), new RecordingPublisher(), new EntryTicketRegister(autre), new WorkingDayGuard(autre))
            .Handle(new CreateLateArrivalCommand
            {
                StudentId = EleveB, Date = Samedi.ToDateTime(TimeOnly.MinValue), Minutes = 10, Reason = "Transport",
                TargetScheduleSlotId = CreneauSamedi
            }, default);
        await act.Should().ThrowAsync<ValidationException>();
    }

    private static ScheduleSlot Slot(Guid id, Guid school, Guid teacher, Guid classroom, Guid subject, DayOfWeek day) => new()
    {
        Id = id, SchoolId = school, TeacherId = teacher, ClassroomId = classroom, SubjectId = subject,
        DayOfWeek = day, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(10, 0)
    };

    private static Task<Guid> IssueAsync(
        ApplicationDbContext db, IPublisher publisher, Guid student, DateOnly date, int minutes, Guid? slot)
        => new CreateLateArrivalCommandHandler(
                db, new StubTenant(EcoleA), publisher, new EntryTicketRegister(db), new WorkingDayGuard(db))
            .Handle(new CreateLateArrivalCommand
            {
                StudentId = student,
                Date = date.ToDateTime(TimeOnly.MinValue),
                Minutes = minutes,
                Reason = "Transport",
                TargetScheduleSlotId = slot
            }, default);

    private async Task SubmitSheetAsync(DateOnly date, params (Guid Student, AttendanceStatus Status, int Minutes)[] entries)
    {
        await using var db = _db.NewAppContext(EcoleA);
        var handler = new SubmitAttendanceSheetCommandHandler(
            db, new StubTenant(EcoleA), Directeur, new AttendanceScopeAuthorizer(db, Directeur),
            new WorkingDayGuard(db), new RecordingPublisher(), new NoOpKpiCache());

        await handler.Handle(new SubmitAttendanceSheetCommand
        {
            ClassroomId = ClasseA,
            SubjectId = Maths,
            Date = date,
            ScheduleSlotId = CreneauSamedi,
            Entries = entries.Select(e => new AttendanceEntry(e.Student, e.Status, e.Minutes)).ToList()
        }, default);
    }

    private async Task<AttendanceRosterDto> RosterAsync(DateOnly date)
    {
        await using var db = _db.NewAppContext(EcoleA);
        return await new InitializeAttendanceSheetQueryHandler(
                db, new AttendanceScopeAuthorizer(db, Directeur), new WorkingDayGuard(db))
            .Handle(new InitializeAttendanceSheetQuery
            {
                ClassroomId = ClasseA, SubjectId = Maths, Date = date, ScheduleSlotId = CreneauSamedi
            }, default);
    }
}
