using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Absences.Commands.CreateLateArrival;
using SamaEcole.Application.Absences.Queries.GetArrivalPreview;
using SamaEcole.Application.Attendance;
using SamaEcole.Application.Attendance.Commands.SubmitAttendanceSheet;
using SamaEcole.Application.Attendance.EntryTickets;
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

/// <summary>Enregistre les notifications publiées : le billet prévient la famille UNE fois (cours en cours), jamais pour un cours manqué.</summary>
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

file sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

/// <summary>
/// Complément N°5 bis, Tâche 13 — le billet par HEURE D'ARRIVÉE : le serveur déduit les cours manqués (passés en
/// « Absent (justifié) »), le retard sur le cours en cours et la durée totale, et l'annulation restaure chaque ligne.
/// École A : repos jeudi/vendredi ; le samedi 2026-09-26, trois cours de la classe A :
/// 08:00-10:00 Maths (1), 10:00-12:00 Français (2), 14:00-16:00 Anglais (3).
/// </summary>
public class ArrivalTimeTicketTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("f1111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("f2222222-2222-2222-2222-222222222222");
    private static readonly Guid ClasseA = Guid.Parse("faaaaaaa-0000-0000-0000-0000000000a1");
    private static readonly Guid ClasseB = Guid.Parse("faaaaaaa-0000-0000-0000-0000000000b1");
    private static readonly Guid Maths = Guid.Parse("fccccccc-0000-0000-0000-0000000000c1");
    private static readonly Guid Francais = Guid.Parse("fccccccc-0000-0000-0000-0000000000c2");
    private static readonly Guid Anglais = Guid.Parse("fccccccc-0000-0000-0000-0000000000c3");
    private static readonly Guid MathsB = Guid.Parse("fccccccc-0000-0000-0000-0000000000c4");
    private static readonly Guid AnneeA = Guid.Parse("f1111111-0000-0000-0000-000000000001");
    private static readonly Guid AnneeB = Guid.Parse("f2222222-0000-0000-0000-000000000001");
    private static readonly Guid Awa = Guid.Parse("feeeeeee-0000-0000-0000-0000000000e1");
    private static readonly Guid Modou = Guid.Parse("feeeeeee-0000-0000-0000-0000000000e2");
    private static readonly Guid EleveB = Guid.Parse("feeeeeee-0000-0000-0000-0000000000e3");
    private static readonly Guid FicheProf = Guid.Parse("fe000000-0000-0000-0000-0000000000e1");
    private static readonly Guid FicheProfB = Guid.Parse("fe000000-0000-0000-0000-0000000000e2");

    private static readonly Guid Cours1 = Guid.Parse("f5555555-0000-0000-0000-000000000001");
    private static readonly Guid Cours2 = Guid.Parse("f5555555-0000-0000-0000-000000000002");
    private static readonly Guid Cours3 = Guid.Parse("f5555555-0000-0000-0000-000000000003");
    private static readonly Guid CoursJeudi = Guid.Parse("f5555555-0000-0000-0000-000000000004");
    private static readonly Guid CoursB = Guid.Parse("f5555555-0000-0000-0000-000000000005");

    private static readonly DateOnly Samedi = new(2026, 9, 26);
    private static readonly DateOnly Jeudi = new(2026, 9, 24);

    private static readonly TestCurrentUser Directeur = new(Guid.Parse("fd000000-0000-0000-0000-000000000001"), Role.Directeur);
    private static readonly TestCurrentUser Surveillant = new(Guid.Parse("fd000000-0000-0000-0000-000000000002"), Role.Surveillant);

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();

        owner.Schools.AddRange(new School { Id = EcoleA, Name = "École A" }, new School { Id = EcoleB, Name = "École B" });
        owner.SchoolSettings.Add(new SchoolSettings { SchoolId = EcoleA, WorkingDays = "Monday,Tuesday,Wednesday,Saturday,Sunday" });
        owner.Classrooms.AddRange(
            new Classroom { Id = ClasseA, SchoolId = EcoleA, Name = "CM2 A", Level = "Primaire", Capacity = 40 },
            new Classroom { Id = ClasseB, SchoolId = EcoleB, Name = "6e B", Level = "Collège", Capacity = 40 });
        owner.Subjects.AddRange(
            new Subject { Id = Maths, SchoolId = EcoleA, Name = "Mathématiques", Level = "Primaire", Coefficient = 1 },
            new Subject { Id = Francais, SchoolId = EcoleA, Name = "Français", Level = "Primaire", Coefficient = 1 },
            new Subject { Id = Anglais, SchoolId = EcoleA, Name = "Anglais", Level = "Primaire", Coefficient = 1 },
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
            Slot(Cours1, EcoleA, FicheProf, ClasseA, Maths, DayOfWeek.Saturday, 8, 10),
            Slot(Cours2, EcoleA, FicheProf, ClasseA, Francais, DayOfWeek.Saturday, 10, 12),
            Slot(Cours3, EcoleA, FicheProf, ClasseA, Anglais, DayOfWeek.Saturday, 14, 16),
            Slot(CoursJeudi, EcoleA, FicheProf, ClasseA, Maths, DayOfWeek.Thursday, 8, 10),
            Slot(CoursB, EcoleB, FicheProfB, ClasseB, MathsB, DayOfWeek.Saturday, 8, 10));

        await owner.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    // 1 — cours 1 absent non justifié, cours 2 absent non justifié : arrivée 10h20.
    [Fact]
    public async Task Arriving_At_1020_Justifies_The_Missed_Course_And_Makes_The_Current_One_A_Twenty_Minute_Late()
    {
        await SubmitSheetAsync(Cours1, Maths, (Awa, AttendanceStatus.UnjustifiedAbsence, 0));
        await SubmitSheetAsync(Cours2, Francais, (Awa, AttendanceStatus.UnjustifiedAbsence, 0));
        var publisher = new RecordingPublisher();

        var ticketId = await IssueAsync(Awa, new TimeOnly(10, 20), publisher);

        await using var check = _db.NewAppContext(EcoleA);
        var ticket = await check.LateArrivals.SingleAsync(t => t.Id == ticketId);
        ticket.Status.Should().Be(EntryTicketStatus.Issued);
        ticket.ArrivalTime.Should().Be(new TimeOnly(10, 20));
        ticket.Minutes.Should().Be(20);
        ticket.TotalMinutes.Should().Be(140);
        ticket.TargetScheduleSlotId.Should().Be(Cours2);
        ticket.MissedScheduleSlotIds.Should().Equal(Cours1);

        var missed = await LineAsync(check, Cours1, Awa);
        missed.Status.Should().Be(AttendanceStatus.JustifiedAbsence);
        missed.EntryTicketId.Should().Be(ticketId);
        missed.PreviousStatus.Should().Be(AttendanceStatus.UnjustifiedAbsence);

        var current = await LineAsync(check, Cours2, Awa);
        current.Status.Should().Be(AttendanceStatus.Late);
        current.LateMinutes.Should().Be(20);
        current.EntryTicketId.Should().Be(ticketId);

        // C7 : UN seul message à la famille — le retard du cours en cours ; rien pour le cours manqué justifié.
        var notification = publisher.Published.Should().ContainSingle().Subject.Should().BeOfType<AttendanceRecordedEvent>().Subject;
        notification.Status.Should().Be(AttendanceStatus.Late);
        notification.LateMinutes.Should().Be(20);
    }

    // 2 — C5 : on ne réécrit jamais un « Présent ».
    [Fact]
    public async Task A_Missed_Course_That_Was_Marked_Present_Is_Left_Alone()
    {
        await SubmitSheetAsync(Cours1, Maths, (Awa, AttendanceStatus.Present, 0));

        await IssueAsync(Awa, new TimeOnly(10, 20), new RecordingPublisher());

        await using var check = _db.NewAppContext(EcoleA);
        var line = await LineAsync(check, Cours1, Awa);
        line.Status.Should().Be(AttendanceStatus.Present);
        line.EntryTicketId.Should().BeNull();
        line.PreviousStatus.Should().BeNull();
    }

    // 3 — fiches absentes : rien n'est écrit, les feuilles présélectionnent, la soumission rattache le billet.
    [Fact]
    public async Task Before_The_Roll_Is_Taken_The_Rosters_Preselect_And_The_Submission_Attaches_The_Ticket()
    {
        var ticketId = await IssueAsync(Awa, new TimeOnly(10, 20), new RecordingPublisher());

        await using (var none = _db.NewAppContext(EcoleA))
        {
            (await none.StudentAttendances.CountAsync()).Should().Be(0, "aucune fiche : aucune ligne à toucher");
        }

        var missed = (await RosterAsync(Cours1, Maths)).Students.Single(r => r.StudentId == Awa);
        missed.Status.Should().Be("JustifiedAbsence");
        missed.LateMinutes.Should().Be(0);
        missed.EntryTicketId.Should().Be(ticketId);

        var current = (await RosterAsync(Cours2, Francais)).Students.Single(r => r.StudentId == Awa);
        current.Status.Should().Be("Late");
        current.LateMinutes.Should().Be(20);
        current.EntryTicketId.Should().Be(ticketId);

        // Le cours suivant n'est pas concerné par le billet.
        (await RosterAsync(Cours3, Anglais)).Students.Single(r => r.StudentId == Awa).EntryTicketId.Should().BeNull();

        // La feuille du cours manqué, renvoyée telle quelle, est acceptée et rattachée au billet.
        await SubmitSheetAsync(Cours1, Maths, (Awa, AttendanceStatus.JustifiedAbsence, 0), (Modou, AttendanceStatus.Present, 0));
        await using var check = _db.NewAppContext(EcoleA);
        (await LineAsync(check, Cours1, Awa)).EntryTicketId.Should().Be(ticketId);
        (await LineAsync(check, Cours1, Modou)).EntryTicketId.Should().BeNull();
    }

    // 4 — annulation avant acceptation : chaque ligne retrouve SON statut d'avant.
    [Fact]
    public async Task Cancelling_Before_Acceptance_Restores_Every_Line_To_Its_Own_Previous_Status()
    {
        await SubmitSheetAsync(Cours1, Maths, (Awa, AttendanceStatus.UnjustifiedAbsence, 0));
        await SubmitSheetAsync(Cours2, Francais, (Awa, AttendanceStatus.Present, 0));
        var ticketId = await IssueAsync(Awa, new TimeOnly(10, 20), new RecordingPublisher());

        await CancelAsync(ticketId);

        await using var check = _db.NewAppContext(EcoleA);
        var first = await LineAsync(check, Cours1, Awa);
        first.Status.Should().Be(AttendanceStatus.UnjustifiedAbsence);
        first.EntryTicketId.Should().BeNull();
        first.PreviousStatus.Should().BeNull();

        var second = await LineAsync(check, Cours2, Awa);
        second.Status.Should().Be(AttendanceStatus.Present);
        second.LateMinutes.Should().Be(0);
        second.EntryTicketId.Should().BeNull();

        (await check.LateArrivals.SingleAsync(t => t.Id == ticketId)).Status.Should().Be(EntryTicketStatus.Cancelled);
    }

    // 5 — un billet accepté ne s'annule plus (B9).
    [Fact]
    public async Task An_Accepted_Ticket_Can_No_Longer_Be_Cancelled()
    {
        var ticketId = await IssueAsync(Awa, new TimeOnly(10, 20), new RecordingPublisher());
        await using (var db = _db.NewAppContext(EcoleA))
        {
            await new AcceptEntryTicketCommandHandler(
                    db, new StubTenant(EcoleA), Directeur, new RecordingPublisher(), new EntryTicketRegister(db),
                    new FixedTime(new DateTimeOffset(2026, 9, 26, 10, 30, 0, TimeSpan.Zero)))
                .Handle(new AcceptEntryTicketCommand(ticketId), default);
        }

        var act = async () => await CancelAsync(ticketId);

        await act.Should().ThrowAsync<ValidationException>();
    }

    // 6 — le client ne dicte rien : minutes et cours visé sont dérivés.
    [Fact]
    public async Task With_An_Arrival_Time_The_Clients_Minutes_And_Target_Are_Ignored()
    {
        await using var db = _db.NewAppContext(EcoleA);
        var ticketId = await NewHandler(db, new RecordingPublisher()).Handle(new CreateLateArrivalCommand
        {
            StudentId = Awa, Date = Samedi.ToDateTime(TimeOnly.MinValue), Reason = "Transport",
            ArrivalTime = new TimeOnly(10, 20), Minutes = 999, TargetScheduleSlotId = Cours3
        }, default);

        await using var check = _db.NewAppContext(EcoleA);
        var ticket = await check.LateArrivals.SingleAsync(t => t.Id == ticketId);
        ticket.Minutes.Should().Be(20);
        ticket.TargetScheduleSlotId.Should().Be(Cours2);
    }

    // 7 — arrivée avant le premier cours : refus lisible, rien d'écrit.
    [Fact]
    public async Task Arriving_Before_The_First_Course_Is_Refused_And_Nothing_Is_Written()
    {
        var act = async () => await IssueAsync(Awa, new TimeOnly(7, 30), new RecordingPublisher());

        (await act.Should().ThrowAsync<ValidationException>()).Which.Errors["ArrivalTime"].Single().Should().Contain("08:00");

        await using var check = _db.NewAppContext(EcoleA);
        (await check.LateArrivals.CountAsync()).Should().Be(0);
    }

    // 8 — jour de repos : refusé avec une heure d'arrivée, possible sans (D4 de l'Évolution N°3).
    [Fact]
    public async Task On_A_Rest_Day_An_Arrival_Time_Is_Refused_But_A_Free_Ticket_Is_Still_Possible()
    {
        await using (var db = _db.NewAppContext(EcoleA))
        {
            var act = async () => await NewHandler(db, new RecordingPublisher()).Handle(new CreateLateArrivalCommand
            {
                StudentId = Awa, Date = Jeudi.ToDateTime(TimeOnly.MinValue), Reason = "Transport", ArrivalTime = new TimeOnly(8, 30)
            }, default);
            await act.Should().ThrowAsync<ValidationException>();
        }

        await using var free = _db.NewAppContext(EcoleA);
        var ticketId = await NewHandler(free, new RecordingPublisher()).Handle(new CreateLateArrivalCommand
        {
            StudentId = Awa, Date = Jeudi.ToDateTime(TimeOnly.MinValue), Reason = "Transport", Minutes = 10
        }, default);

        await using var check = _db.NewAppContext(EcoleA);
        var ticket = await check.LateArrivals.SingleAsync(t => t.Id == ticketId);
        ticket.Status.Should().BeNull();
        ticket.ArrivalTime.Should().BeNull();
    }

    // 9 — un second billet le même jour ne re-justifie pas ce qu'un billet actif couvre déjà.
    [Fact]
    public async Task A_Second_Ticket_The_Same_Day_Skips_The_Courses_Already_Covered()
    {
        await SubmitSheetAsync(Cours1, Maths, (Awa, AttendanceStatus.UnjustifiedAbsence, 0));
        var first = await IssueAsync(Awa, new TimeOnly(10, 20), new RecordingPublisher());

        var second = await IssueAsync(Awa, new TimeOnly(15, 0), new RecordingPublisher());

        await using var check = _db.NewAppContext(EcoleA);
        var ticket = await check.LateArrivals.SingleAsync(t => t.Id == second);
        ticket.MissedScheduleSlotIds.Should().BeEmpty("les cours 1 et 2 sont déjà couverts par le premier billet");
        ticket.TargetScheduleSlotId.Should().Be(Cours3);
        ticket.Minutes.Should().Be(60);
        (await LineAsync(check, Cours1, Awa)).EntryTicketId.Should().Be(first, "la ligne reste rattachée au premier billet");
    }

    // 10 — arrivée pile à l'heure d'un cours : le cours précédent est manqué, aucun retard, le billet se rattache seulement.
    [Fact]
    public async Task Arriving_Exactly_When_A_Course_Starts_Justifies_The_Previous_One_Without_Any_Late()
    {
        await SubmitSheetAsync(Cours1, Maths, (Awa, AttendanceStatus.UnjustifiedAbsence, 0));
        await SubmitSheetAsync(Cours2, Francais, (Awa, AttendanceStatus.Present, 0));
        var publisher = new RecordingPublisher();

        var ticketId = await IssueAsync(Awa, new TimeOnly(10, 0), publisher);

        await using var check = _db.NewAppContext(EcoleA);
        var ticket = await check.LateArrivals.SingleAsync(t => t.Id == ticketId);
        ticket.Minutes.Should().Be(0);
        ticket.TotalMinutes.Should().Be(120);
        ticket.TargetScheduleSlotId.Should().Be(Cours2);

        (await LineAsync(check, Cours1, Awa)).Status.Should().Be(AttendanceStatus.JustifiedAbsence);
        var current = await LineAsync(check, Cours2, Awa);
        current.Status.Should().Be(AttendanceStatus.Present, "aucun retard à constater");
        current.EntryTicketId.Should().Be(ticketId, "mais le billet est rattaché pour que l'enseignant l'accepte");
        publisher.Published.Should().BeEmpty();
    }

    // 11 — le taux de présence : le cours manqué justifié reste une absence, le retard compte comme présent.
    [Fact]
    public async Task The_Attendance_Rate_Keeps_The_Justified_Course_As_An_Absence_And_Counts_The_Late_As_Present()
    {
        await SubmitSheetAsync(Cours1, Maths, (Awa, AttendanceStatus.UnjustifiedAbsence, 0));
        await SubmitSheetAsync(Cours2, Francais, (Awa, AttendanceStatus.UnjustifiedAbsence, 0));

        await using (var before = _db.NewAppContext(EcoleA))
        {
            (await new AttendanceReportAggregator(before).ComputeAsync(Samedi, Samedi, null, default))
                .AverageAttendanceRate.Should().Be(0m);
        }

        await IssueAsync(Awa, new TimeOnly(10, 20), new RecordingPublisher());

        await using var after = _db.NewAppContext(EcoleA);
        (await new AttendanceReportAggregator(after).ComputeAsync(Samedi, Samedi, null, default))
            .AverageAttendanceRate.Should().Be(0.5m, "1 cours en retard (présent) sur 2 lignes ; le cours manqué justifié reste une absence");
    }

    // 12 — l'aperçu de l'écran est le calcul de l'émission.
    [Fact]
    public async Task The_Preview_Returns_What_The_Issuance_Will_Do()
    {
        await using var db = _db.NewAppContext(EcoleA);
        var preview = await new GetArrivalPreviewQueryHandler(db, new ArrivalPlanner(db, new WorkingDayGuard(db)), TimeProvider.System)
            .Handle(new GetArrivalPreviewQuery(Awa, Samedi, new TimeOnly(10, 20)), default);

        preview.MissedSlots.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new ArrivalPreviewSlotDto(Cours1, "08:00-10:00", "Mathématiques", 120));
        preview.InProgress.Should().BeEquivalentTo(new ArrivalPreviewLateDto(Cours2, "10:00-12:00", "Français", 20));
        preview.TargetSlotId.Should().Be(Cours2);
        preview.MissedMinutes.Should().Be(120);
        preview.LateMinutes.Should().Be(20);
        preview.TotalMinutes.Should().Be(140);

        await using var check = _db.NewAppContext(EcoleA);
        (await check.LateArrivals.CountAsync()).Should().Be(0, "un aperçu n'écrit rien");
    }

    // 13 — isolation.
    [Fact]
    [Trait("Category", "MultiTenant")]
    public async Task Another_School_Sees_Neither_These_Courses_Nor_These_Tickets()
    {
        await IssueAsync(Awa, new TimeOnly(10, 20), new RecordingPublisher());

        await using var autre = _db.NewAppContext(EcoleB);
        (await autre.LateArrivals.CountAsync()).Should().Be(0);

        // L'aperçu d'un élève de A depuis l'école B : introuvable (RLS + filtre global), jamais calculé sur les cours de A.
        var preview = async () => await new GetArrivalPreviewQueryHandler(autre, new ArrivalPlanner(autre, new WorkingDayGuard(autre)), TimeProvider.System)
            .Handle(new GetArrivalPreviewQuery(Awa, Samedi, new TimeOnly(10, 20)), default);
        await preview.Should().ThrowAsync<NotFoundException>();
    }

    // 14 — audit.
    [Fact]
    public void Issuing_A_Ticket_Is_An_Audited_Request()
        => typeof(IAuditableRequest).IsAssignableFrom(typeof(CreateLateArrivalCommand)).Should().BeTrue();

    // -------------------------------------------------------------------------------- helpers

    private static ScheduleSlot Slot(Guid id, Guid school, Guid teacher, Guid classroom, Guid subject, DayOfWeek day, int from, int to) => new()
    {
        Id = id, SchoolId = school, TeacherId = teacher, ClassroomId = classroom, SubjectId = subject,
        DayOfWeek = day, StartTime = new TimeOnly(from, 0), EndTime = new TimeOnly(to, 0)
    };

    private static CreateLateArrivalCommandHandler NewHandler(ApplicationDbContext db, IPublisher publisher)
        => new(db, new StubTenant(EcoleA), publisher, new EntryTicketRegister(db), new WorkingDayGuard(db),
            new ArrivalPlanner(db, new WorkingDayGuard(db)));

    private async Task<Guid> IssueAsync(Guid student, TimeOnly arrival, IPublisher publisher)
    {
        await using var db = _db.NewAppContext(EcoleA);
        return await NewHandler(db, publisher).Handle(new CreateLateArrivalCommand
        {
            StudentId = student, Date = Samedi.ToDateTime(TimeOnly.MinValue), Reason = "Transport", ArrivalTime = arrival
        }, default);
    }

    private async Task CancelAsync(Guid ticketId)
    {
        await using var db = _db.NewAppContext(EcoleA);
        await new CancelEntryTicketCommandHandler(db, Surveillant, new EntryTicketRegister(db), new FixedTime(new DateTimeOffset(2026, 9, 26, 10, 30, 0, TimeSpan.Zero)))
            .Handle(new CancelEntryTicketCommand(ticketId), default);
    }

    private async Task SubmitSheetAsync(Guid slot, Guid subject, params (Guid Student, AttendanceStatus Status, int Minutes)[] entries)
    {
        await using var db = _db.NewAppContext(EcoleA);
        await new SubmitAttendanceSheetCommandHandler(
                db, new StubTenant(EcoleA), Directeur, new AttendanceScopeAuthorizer(db, Directeur),
                new WorkingDayGuard(db), new RecordingPublisher(), new NoOpKpiCache())
            .Handle(new SubmitAttendanceSheetCommand
            {
                ClassroomId = ClasseA, SubjectId = subject, Date = Samedi, ScheduleSlotId = slot,
                Entries = entries.Select(e => new AttendanceEntry(e.Student, e.Status, e.Minutes)).ToList()
            }, default);
    }

    private async Task<AttendanceRosterDto> RosterAsync(Guid slot, Guid subject)
    {
        await using var db = _db.NewAppContext(EcoleA);
        return await new InitializeAttendanceSheetQueryHandler(
                db, new AttendanceScopeAuthorizer(db, Directeur), new WorkingDayGuard(db))
            .Handle(new InitializeAttendanceSheetQuery
            {
                ClassroomId = ClasseA, SubjectId = subject, Date = Samedi, ScheduleSlotId = slot
            }, default);
    }

    /// <summary>La ligne d'appel d'un élève sur la fiche d'un cours donné ce samedi-là.</summary>
    private static Task<StudentAttendance> LineAsync(ApplicationDbContext db, Guid slot, Guid student)
        => (from sa in db.StudentAttendances
            join sheet in db.AttendanceSheets on sa.AttendanceSheetId equals sheet.Id
            where sheet.ScheduleSlotId == slot && sheet.Date == Samedi && sa.StudentId == student
            select sa).SingleAsync();
}
