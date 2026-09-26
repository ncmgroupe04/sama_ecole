using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Absences.Commands.CreateLateArrival;
using SamaEcole.Application.Attendance;
using SamaEcole.Application.Attendance.Commands.SubmitAttendanceSheet;
using SamaEcole.Application.Attendance.Events;
using SamaEcole.Application.Attendance.EntryTickets;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
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

file sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

/// <summary>
/// Évolution N°5, arbitrages B6/B9 — l'enseignant du cours accepte le billet en classe ; la Vie Scolaire peut
/// l'annuler tant qu'il n'est pas accepté. Awa Sow est titulaire du cours du samedi 08:00-10:00 (Maths) ;
/// Modou Ba est un autre enseignant de l'école.
/// </summary>
public class EntryTicketAcceptanceTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("a1a11111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("a2a22222-2222-2222-2222-222222222222");
    private static readonly Guid Classe = Guid.Parse("a1aaaaaa-0000-0000-0000-0000000000a1");
    private static readonly Guid ClasseB = Guid.Parse("a1aaaaaa-0000-0000-0000-0000000000b1");
    private static readonly Guid Maths = Guid.Parse("a1cccccc-0000-0000-0000-0000000000c1");
    private static readonly Guid MathsB = Guid.Parse("a1cccccc-0000-0000-0000-0000000000c2");
    private static readonly Guid AnneeA = Guid.Parse("a1a11111-0000-0000-0000-000000000001");
    private static readonly Guid AnneeB = Guid.Parse("a2a22222-0000-0000-0000-000000000001");
    private static readonly Guid Awa = Guid.Parse("a1eeeeee-0000-0000-0000-0000000000e1");
    private static readonly Guid Modou = Guid.Parse("a1eeeeee-0000-0000-0000-0000000000e2");
    private static readonly Guid EleveB = Guid.Parse("a1eeeeee-0000-0000-0000-0000000000e3");

    private static readonly Guid CompteProf = Guid.Parse("a1d00000-0000-0000-0000-0000000000d1");
    private static readonly Guid CompteAutreProf = Guid.Parse("a1d00000-0000-0000-0000-0000000000d2");
    private static readonly Guid FicheProf = Guid.Parse("a1e00000-0000-0000-0000-0000000000e1");
    private static readonly Guid FicheAutreProf = Guid.Parse("a1e00000-0000-0000-0000-0000000000e2");
    private static readonly Guid FicheProfB = Guid.Parse("a1e00000-0000-0000-0000-0000000000e3");
    private static readonly Guid Creneau = Guid.Parse("a1555555-0000-0000-0000-000000000001");
    private static readonly Guid CreneauB = Guid.Parse("a1555555-0000-0000-0000-000000000002");

    private static readonly DateOnly Samedi = new(2026, 9, 26);
    private static readonly DateTimeOffset Maintenant = new(2026, 9, 26, 9, 15, 0, TimeSpan.Zero);

    private static readonly TestCurrentUser Directeur = new(Guid.Parse("a1d00000-0000-0000-0000-000000000009"), Role.Directeur);
    private static readonly TestCurrentUser Surveillant = new(Guid.Parse("a1d00000-0000-0000-0000-000000000008"), Role.Surveillant);
    private static readonly TestCurrentUser Secretaire = new(Guid.Parse("a1d00000-0000-0000-0000-000000000007"), Role.Secretariat);
    private static readonly TestCurrentUser Titulaire = new(CompteProf, Role.Enseignant);
    private static readonly TestCurrentUser AutreEnseignant = new(CompteAutreProf, Role.Enseignant);

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();

        owner.Schools.AddRange(new School { Id = EcoleA, Name = "École A" }, new School { Id = EcoleB, Name = "École B" });
        owner.Classrooms.AddRange(
            new Classroom { Id = Classe, SchoolId = EcoleA, Name = "CM2 A", Level = "Primaire", Capacity = 40 },
            new Classroom { Id = ClasseB, SchoolId = EcoleB, Name = "6e B", Level = "Collège", Capacity = 40 });
        owner.Subjects.AddRange(
            new Subject { Id = Maths, SchoolId = EcoleA, Name = "Mathématiques", Level = "Primaire", Coefficient = 1 },
            new Subject { Id = MathsB, SchoolId = EcoleB, Name = "Mathématiques", Level = "Collège", Coefficient = 1 });
        owner.SchoolYears.AddRange(
            new SchoolYear { Id = AnneeA, SchoolId = EcoleA, Label = "2026-2027", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true },
            new SchoolYear { Id = AnneeB, SchoolId = EcoleB, Label = "2026-2027", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true });
        owner.Students.AddRange(
            new Student { Id = Awa, SchoolId = EcoleA, Matricule = "ELEV-0001", FullName = "Awa Fall", BirthDate = new DateOnly(2015, 1, 1), BirthPlace = "Dakar", Gender = "F", ClassroomId = Classe },
            new Student { Id = Modou, SchoolId = EcoleA, Matricule = "ELEV-0002", FullName = "Modou Diop", BirthDate = new DateOnly(2015, 1, 1), BirthPlace = "Dakar", Gender = "M", ClassroomId = Classe },
            new Student { Id = EleveB, SchoolId = EcoleB, Matricule = "ELEV-0001", FullName = "Ibra", BirthDate = new DateOnly(2014, 1, 1), BirthPlace = "Dakar", Gender = "M", ClassroomId = ClasseB });
        owner.Users.AddRange(
            new User { Id = CompteProf, SchoolId = EcoleA, Email = "awa@a.sn", PasswordHash = "x", FullName = "Awa Sow", Role = Role.Enseignant },
            new User { Id = CompteAutreProf, SchoolId = EcoleA, Email = "modou@a.sn", PasswordHash = "x", FullName = "Modou Ba", Role = Role.Enseignant });
        owner.Teachers.AddRange(
            new Teacher { Id = FicheProf, SchoolId = EcoleA, Matricule = "ENS-001", FullName = "Awa Sow", Email = "awa@a.sn", BirthDate = new DateOnly(1985, 1, 1), UserId = CompteProf },
            new Teacher { Id = FicheAutreProf, SchoolId = EcoleA, Matricule = "ENS-002", FullName = "Modou Ba", Email = "modou@a.sn", BirthDate = new DateOnly(1985, 1, 1), UserId = CompteAutreProf },
            new Teacher { Id = FicheProfB, SchoolId = EcoleB, Matricule = "ENS-001", FullName = "Prof B", Email = "b@b.sn", BirthDate = new DateOnly(1985, 1, 1) });
        owner.ScheduleSlots.AddRange(
            new ScheduleSlot { Id = Creneau, SchoolId = EcoleA, TeacherId = FicheProf, ClassroomId = Classe, SubjectId = Maths, DayOfWeek = DayOfWeek.Saturday, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(10, 0) },
            new ScheduleSlot { Id = CreneauB, SchoolId = EcoleB, TeacherId = FicheProfB, ClassroomId = ClasseB, SubjectId = MathsB, DayOfWeek = DayOfWeek.Saturday, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(10, 0) });

        await owner.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    // 1 — le titulaire accepte : billet Accepted, ligne « Retard » ; qui et quand viennent du JWT et de l'horloge.
    [Fact]
    public async Task The_Slot_Teacher_Accepts_The_Ticket_And_The_Absence_Becomes_A_Confirmed_Late()
    {
        await SubmitSheetAsync((Awa, AttendanceStatus.UnjustifiedAbsence, 0), (Modou, AttendanceStatus.Present, 0));
        var ticketId = await IssueAsync(Awa, 12);
        var publisher = new RecordingPublisher();

        var result = await AcceptAsync(Titulaire, ticketId, publisher);

        result.Status.Should().Be(EntryTicketStatus.Accepted);
        result.AcceptedAt.Should().Be(Maintenant);

        await using var check = _db.NewAppContext(EcoleA);
        var ticket = await check.LateArrivals.SingleAsync(t => t.Id == ticketId);
        ticket.Status.Should().Be(EntryTicketStatus.Accepted);
        ticket.AcceptedByUserId.Should().Be(CompteProf);
        (await check.StudentAttendances.SingleAsync(sa => sa.StudentId == Awa)).Status.Should().Be(AttendanceStatus.Late);
        publisher.Published.Should().BeEmpty("la rectification a déjà été envoyée à l'émission : l'acceptation ne prévient pas deux fois");
    }

    // 2 — seuls l'enseignant du cours et le Directeur.
    [Fact]
    public async Task Only_The_Slot_Teacher_And_The_Director_May_Accept()
    {
        var ticketId = await IssueAsync(Awa, 12);

        var other = async () => await AcceptAsync(AutreEnseignant, ticketId);
        (await other.Should().ThrowAsync<ForbiddenException>()).Which.Message.Should().Contain("autre enseignant");

        var surveillant = async () => await AcceptAsync(Surveillant, ticketId);
        await surveillant.Should().ThrowAsync<ForbiddenException>();

        var secretariat = async () => await AcceptAsync(Secretaire, ticketId);
        await secretariat.Should().ThrowAsync<ForbiddenException>();

        (await AcceptAsync(Directeur, ticketId)).Status.Should().Be(EntryTicketStatus.Accepted);
    }

    // 3 — idempotent : rien ne bouge à la seconde acceptation.
    [Fact]
    public async Task Accepting_Twice_Changes_Nothing_The_Second_Time()
    {
        var ticketId = await IssueAsync(Awa, 12);
        var first = await AcceptAsync(Titulaire, ticketId, now: Maintenant);

        var second = await AcceptAsync(Directeur, ticketId, now: Maintenant.AddHours(2));

        second.Status.Should().Be(EntryTicketStatus.Accepted);
        second.AcceptedAt.Should().Be(first.AcceptedAt, "la date d'acceptation n'est pas réécrite");

        await using var check = _db.NewAppContext(EcoleA);
        (await check.LateArrivals.SingleAsync(t => t.Id == ticketId)).AcceptedByUserId
            .Should().Be(CompteProf, "l'auteur de la première acceptation est conservé");
    }

    // 4 — le billet a été émis AVANT l'appel ; l'enseignant a marqué l'élève absent ; à l'acceptation, la ligne est réparée.
    [Fact]
    public async Task Accepting_After_The_Teacher_Marked_The_Student_Absent_Repairs_The_Line_And_Notifies()
    {
        var ticketId = await IssueAsync(Awa, 12); // aucune fiche encore
        await SubmitSheetAsync((Awa, AttendanceStatus.UnjustifiedAbsence, 0), (Modou, AttendanceStatus.Present, 0));
        var publisher = new RecordingPublisher();

        await AcceptAsync(Titulaire, ticketId, publisher);

        await using var check = _db.NewAppContext(EcoleA);
        var line = await check.StudentAttendances.SingleAsync(sa => sa.StudentId == Awa);
        line.Status.Should().Be(AttendanceStatus.Late);
        line.LateMinutes.Should().Be(12);
        line.EntryTicketId.Should().Be(ticketId);
        (await check.LateArrivals.SingleAsync(t => t.Id == ticketId)).PreviousStatus
            .Should().Be(AttendanceStatus.UnjustifiedAbsence, "ce que l'enseignant avait saisi est conservé");

        var notification = publisher.Published.Should().ContainSingle().Which.Should().BeOfType<AttendanceRecordedEvent>().Subject;
        notification.Status.Should().Be(AttendanceStatus.Late);
    }

    // 5 — annulation d'un billet non accepté : la ligne retrouve son statut d'avant, sans notification.
    [Fact]
    public async Task Cancelling_A_Pending_Ticket_Restores_The_Previous_Status()
    {
        await SubmitSheetAsync((Awa, AttendanceStatus.UnjustifiedAbsence, 0), (Modou, AttendanceStatus.Present, 0));
        var ticketId = await IssueAsync(Awa, 12);

        var result = await CancelAsync(Surveillant, ticketId);

        result.Status.Should().Be(EntryTicketStatus.Cancelled);
        await using var check = _db.NewAppContext(EcoleA);
        var line = await check.StudentAttendances.SingleAsync(sa => sa.StudentId == Awa);
        line.Status.Should().Be(AttendanceStatus.UnjustifiedAbsence);
        line.LateMinutes.Should().Be(0);
        line.EntryTicketId.Should().BeNull();
        var ticket = await check.LateArrivals.SingleAsync(t => t.Id == ticketId);
        ticket.Status.Should().Be(EntryTicketStatus.Cancelled);
        ticket.CancelledByUserId.Should().Be(Surveillant.UserId);
    }

    [Fact]
    public async Task A_Cancelled_Ticket_Cannot_Be_Accepted_And_A_New_One_Can_Be_Issued()
    {
        var ticketId = await IssueAsync(Awa, 12);
        await CancelAsync(Directeur, ticketId);

        var act = async () => await AcceptAsync(Titulaire, ticketId);
        (await act.Should().ThrowAsync<ValidationException>()).Which.Errors["Ticket"].Single().Should().Contain("annulé");

        (await IssueAsync(Awa, 9)).Should().NotBe(ticketId, "la clé (élève, cours, jour) est libre après annulation");
    }

    // 6 — un billet accepté ne s'annule plus ; annuler deux fois ne fait pas échouer.
    [Fact]
    public async Task An_Accepted_Ticket_Cannot_Be_Cancelled_But_Cancelling_Twice_Is_Harmless()
    {
        var accepted = await IssueAsync(Awa, 12);
        await AcceptAsync(Titulaire, accepted);

        var act = async () => await CancelAsync(Surveillant, accepted);
        (await act.Should().ThrowAsync<ValidationException>()).Which.Errors["Ticket"].Single().Should().Contain("accepté");

        var another = await IssueAsync(Modou, 7);
        await CancelAsync(Surveillant, another);
        (await CancelAsync(Surveillant, another)).Status.Should().Be(EntryTicketStatus.Cancelled);
    }

    [Fact]
    public async Task Only_The_School_Life_And_The_Director_May_Cancel()
    {
        var ticketId = await IssueAsync(Awa, 12);

        var teacher = async () => await CancelAsync(Titulaire, ticketId);
        await teacher.Should().ThrowAsync<ForbiddenException>();

        var secretariat = async () => await CancelAsync(Secretaire, ticketId);
        await secretariat.Should().ThrowAsync<ForbiddenException>();
    }

    // 7 — un billet sans cours visé n'a rien à accepter ni à annuler.
    [Fact]
    public async Task A_Ticket_Without_A_Target_Has_Nothing_To_Accept_Or_Cancel()
    {
        Guid ticketId;
        await using (var db = _db.NewAppContext(EcoleA))
        {
            ticketId = await new CreateLateArrivalCommandHandler(
                    db, new StubTenant(EcoleA), new RecordingPublisher(), new EntryTicketRegister(db), new WorkingDayGuard(db), new ArrivalPlanner(db, new WorkingDayGuard(db)))
                .Handle(new CreateLateArrivalCommand
                {
                    StudentId = Awa, Date = Samedi.ToDateTime(TimeOnly.MinValue), Minutes = 10, Reason = "Transport"
                }, default);
        }

        var accept = async () => await AcceptAsync(Directeur, ticketId);
        await accept.Should().ThrowAsync<ValidationException>();

        var cancel = async () => await CancelAsync(Directeur, ticketId);
        await cancel.Should().ThrowAsync<ValidationException>();
    }

    // 8 — isolation.
    [Fact]
    [Trait("Category", "MultiTenant")]
    public async Task A_Ticket_Of_Another_School_Is_Not_Found()
    {
        var ticketId = await IssueAsync(Awa, 12);

        await using var autre = _db.NewAppContext(EcoleB);
        var accept = async () => await new AcceptEntryTicketCommandHandler(
                autre, new StubTenant(EcoleB), Directeur, new RecordingPublisher(), new EntryTicketRegister(autre), new FixedTimeProvider(Maintenant))
            .Handle(new AcceptEntryTicketCommand(ticketId), default);
        await accept.Should().ThrowAsync<NotFoundException>();

        var cancel = async () => await new CancelEntryTicketCommandHandler(
                autre, Directeur, new EntryTicketRegister(autre), new FixedTimeProvider(Maintenant))
            .Handle(new CancelEntryTicketCommand(ticketId), default);
        await cancel.Should().ThrowAsync<NotFoundException>();
    }

    // 9 — audit : chaque geste est historisé.
    [Fact]
    public void Accepting_And_Cancelling_Are_Audited_Requests()
    {
        typeof(IAuditableRequest).IsAssignableFrom(typeof(AcceptEntryTicketCommand)).Should().BeTrue();
        typeof(IAuditableRequest).IsAssignableFrom(typeof(CancelEntryTicketCommand)).Should().BeTrue();
    }

    private async Task<Guid> IssueAsync(Guid student, int minutes)
    {
        await using var db = _db.NewAppContext(EcoleA);
        return await new CreateLateArrivalCommandHandler(
                db, new StubTenant(EcoleA), new RecordingPublisher(), new EntryTicketRegister(db), new WorkingDayGuard(db), new ArrivalPlanner(db, new WorkingDayGuard(db)))
            .Handle(new CreateLateArrivalCommand
            {
                StudentId = student, Date = Samedi.ToDateTime(TimeOnly.MinValue), Minutes = minutes,
                Reason = "Transport", TargetScheduleSlotId = Creneau
            }, default);
    }

    private async Task<EntryTicketActionResult> AcceptAsync(
        ICurrentUserService user, Guid ticketId, IPublisher? publisher = null, DateTimeOffset? now = null)
    {
        await using var db = _db.NewAppContext(EcoleA);
        return await new AcceptEntryTicketCommandHandler(
                db, new StubTenant(EcoleA), user, publisher ?? new RecordingPublisher(), new EntryTicketRegister(db),
                new FixedTimeProvider(now ?? Maintenant))
            .Handle(new AcceptEntryTicketCommand(ticketId), default);
    }

    private async Task<EntryTicketActionResult> CancelAsync(ICurrentUserService user, Guid ticketId)
    {
        await using var db = _db.NewAppContext(EcoleA);
        return await new CancelEntryTicketCommandHandler(db, user, new EntryTicketRegister(db), new FixedTimeProvider(Maintenant))
            .Handle(new CancelEntryTicketCommand(ticketId), default);
    }

    private async Task SubmitSheetAsync(params (Guid Student, AttendanceStatus Status, int Minutes)[] entries)
    {
        await using var db = _db.NewAppContext(EcoleA);
        await new SubmitAttendanceSheetCommandHandler(
                db, new StubTenant(EcoleA), Directeur, new AttendanceScopeAuthorizer(db, Directeur),
                new WorkingDayGuard(db), new RecordingPublisher(), new NoOpKpiCache())
            .Handle(new SubmitAttendanceSheetCommand
            {
                ClassroomId = Classe, SubjectId = Maths, Date = Samedi, ScheduleSlotId = Creneau,
                Entries = entries.Select(e => new AttendanceEntry(e.Student, e.Status, e.Minutes)).ToList()
            }, default);
    }
}
