using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Absences.Commands.CancelAbsenceJustification;
using SamaEcole.Application.Absences.Commands.CreateAbsenceJustification;
using SamaEcole.Application.Absences.Queries.GetAbsenceJustifications;
using SamaEcole.Application.Absences.Queries.GetAbsenceTicket;
using SamaEcole.Application.Attendance;
using SamaEcole.Application.Attendance.Commands.SubmitAttendanceSheet;
using SamaEcole.Application.Attendance.Queries.InitializeAttendanceSheet;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Reports;
using SamaEcole.Application.Schools;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
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
    public Task Publish(object notification, CancellationToken cancellationToken = default) { Published.Add(notification); return Task.CompletedTask; }
    public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification { Published.Add(notification!); return Task.CompletedTask; }
}

file sealed class StubTenant(Guid? schoolId) : ITenantProvider
{
    public Guid? CurrentSchoolId => schoolId;
}

/// <summary>
/// Billet d'absence par plage : justifie plusieurs jours d'un coup, répercuté sur le registre d'appel (lignes déjà
/// saisies, feuille présélectionnée, soumission) et réversible à l'annulation.
/// </summary>
public class AbsenceJustificationRangeTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("f1111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("f2222222-2222-2222-2222-222222222222");
    private static readonly Guid ClasseA = Guid.Parse("faaaaaaa-0000-0000-0000-0000000000a1");
    private static readonly Guid Maths = Guid.Parse("fccccccc-0000-0000-0000-0000000000c1");
    private static readonly Guid AnneeA = Guid.Parse("f1111111-0000-0000-0000-000000000001");
    private static readonly Guid Awa = Guid.Parse("feeeeeee-0000-0000-0000-0000000000e1");
    private static readonly Guid Modou = Guid.Parse("feeeeeee-0000-0000-0000-0000000000e2");
    private static readonly Guid Prof = Guid.Parse("fe000000-0000-0000-0000-0000000000e1");
    private static readonly Guid Creneau = Guid.Parse("f5555555-0000-0000-0000-000000000001");

    // Samedi 26/09/2026 : jour d'ouverture de l'école A (repos jeudi/vendredi).
    private static readonly DateOnly Samedi = new(2026, 9, 26);
    private static readonly TestCurrentUser Directeur = new(Guid.Parse("fd000000-0000-0000-0000-000000000001"), Role.Directeur);

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();
        owner.Schools.AddRange(new School { Id = EcoleA, Name = "École A" }, new School { Id = EcoleB, Name = "École B" });
        owner.SchoolSettings.Add(new SchoolSettings { SchoolId = EcoleA, WorkingDays = "Monday,Tuesday,Wednesday,Saturday,Sunday" });
        owner.Classrooms.Add(new Classroom { Id = ClasseA, SchoolId = EcoleA, Name = "CM2 A", Level = "Primaire", Capacity = 40 });
        owner.Subjects.Add(new Subject { Id = Maths, SchoolId = EcoleA, Name = "Mathématiques", Level = "Primaire", Coefficient = 1 });
        owner.SchoolYears.Add(new SchoolYear { Id = AnneeA, SchoolId = EcoleA, Label = "2026-2027", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true });
        owner.Students.AddRange(
            new Student { Id = Awa, SchoolId = EcoleA, Matricule = "ELEV-0001", FullName = "Awa Fall", BirthDate = new DateOnly(2015, 1, 1), BirthPlace = "Dakar", Gender = "F", ClassroomId = ClasseA },
            new Student { Id = Modou, SchoolId = EcoleA, Matricule = "ELEV-0002", FullName = "Modou Diop", BirthDate = new DateOnly(2015, 1, 1), BirthPlace = "Dakar", Gender = "M", ClassroomId = ClasseA });
        owner.Teachers.Add(new Teacher { Id = Prof, SchoolId = EcoleA, Matricule = "ENS-001", FullName = "Prof A", Email = "a@a.sn", BirthDate = new DateOnly(1985, 1, 1) });
        owner.ScheduleSlots.Add(new ScheduleSlot
        {
            Id = Creneau, SchoolId = EcoleA, TeacherId = Prof, ClassroomId = ClasseA, SubjectId = Maths,
            DayOfWeek = DayOfWeek.Saturday, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(10, 0)
        });
        await owner.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private async Task<Guid> CreateAsync(Guid student, DateOnly start, DateOnly? end)
    {
        await using var db = _db.NewAppContext(EcoleA);
        return await new CreateAbsenceJustificationCommandHandler(db, new StubTenant(EcoleA))
            .Handle(new CreateAbsenceJustificationCommand { StudentId = student, StartDate = start, EndDate = end, Reason = "Paludisme" }, default);
    }

    private async Task SubmitAsync(params (Guid Student, AttendanceStatus Status)[] entries)
    {
        await using var db = _db.NewAppContext(EcoleA);
        await new SubmitAttendanceSheetCommandHandler(
                db, new StubTenant(EcoleA), Directeur, new AttendanceScopeAuthorizer(db, Directeur),
                new WorkingDayGuard(db), new RecordingPublisher(), new NoOpKpiCache())
            .Handle(new SubmitAttendanceSheetCommand
            {
                ClassroomId = ClasseA, SubjectId = Maths, Date = Samedi, ScheduleSlotId = Creneau,
                Entries = entries.Select(e => new AttendanceEntry(e.Student, e.Status, 0)).ToList()
            }, default);
    }

    [Fact]
    public async Task A_Ticket_Justifies_The_Unjustified_Absences_Already_Recorded_And_Spares_The_Presents()
    {
        await SubmitAsync((Awa, AttendanceStatus.UnjustifiedAbsence), (Modou, AttendanceStatus.Present));

        var id = await CreateAsync(Awa, Samedi.AddDays(-2), Samedi.AddDays(2));
        await CreateAsync(Modou, Samedi, null);

        await using var check = _db.NewAppContext(EcoleA);
        var awa = await check.StudentAttendances.SingleAsync(sa => sa.StudentId == Awa);
        awa.Status.Should().Be(AttendanceStatus.JustifiedAbsence);
        awa.PreviousStatus.Should().Be(AttendanceStatus.UnjustifiedAbsence);
        awa.AbsenceJustificationId.Should().Be(id);

        var modou = await check.StudentAttendances.SingleAsync(sa => sa.StudentId == Modou);
        modou.Status.Should().Be(AttendanceStatus.Present, "ce que l'enseignant a constaté en classe prime");
        modou.AbsenceJustificationId.Should().BeNull();
    }

    [Fact]
    public async Task Cancelling_The_Ticket_Restores_The_Lines_And_Soft_Deletes_It()
    {
        await SubmitAsync((Awa, AttendanceStatus.UnjustifiedAbsence), (Modou, AttendanceStatus.Present));
        var id = await CreateAsync(Awa, Samedi, Samedi);

        await using (var db = _db.NewAppContext(EcoleA))
        {
            var restored = await new CancelAbsenceJustificationCommandHandler(db, Directeur).Handle(new CancelAbsenceJustificationCommand(id), default);
            restored.Should().Be(1);
        }

        await using var check = _db.NewAppContext(EcoleA);
        var line = await check.StudentAttendances.SingleAsync(sa => sa.StudentId == Awa);
        line.Status.Should().Be(AttendanceStatus.UnjustifiedAbsence);
        line.PreviousStatus.Should().BeNull();
        line.AbsenceJustificationId.Should().BeNull();
        (await check.AbsenceJustifications.CountAsync()).Should().Be(0, "le billet annulé sort du filtre global (soft delete)");

        await using var owner = _db.NewOwnerContext();
        (await owner.AbsenceJustifications.IgnoreQueryFilters().SingleAsync(a => a.Id == id)).IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task Before_The_Roll_The_Roster_Preselects_Justified_And_Submission_Links_The_Ticket()
    {
        var id = await CreateAsync(Awa, Samedi.AddDays(-1), Samedi.AddDays(1));

        await using (var db = _db.NewAppContext(EcoleA))
        {
            var roster = await new InitializeAttendanceSheetQueryHandler(db, new AttendanceScopeAuthorizer(db, Directeur), new WorkingDayGuard(db))
                .Handle(new InitializeAttendanceSheetQuery { ClassroomId = ClasseA, SubjectId = Maths, Date = Samedi, ScheduleSlotId = Creneau }, default);
            roster.Students.Single(r => r.StudentId == Awa).Status.Should().Be("JustifiedAbsence");
            roster.Students.Single(r => r.StudentId == Modou).Status.Should().BeNull();
        }

        // Même si l'enseignant coche « Absent » (non justifié), l'absence est couverte par le billet.
        await SubmitAsync((Awa, AttendanceStatus.UnjustifiedAbsence), (Modou, AttendanceStatus.Present));

        await using var check = _db.NewAppContext(EcoleA);
        var line = await check.StudentAttendances.SingleAsync(sa => sa.StudentId == Awa);
        line.Status.Should().Be(AttendanceStatus.JustifiedAbsence);
        line.AbsenceJustificationId.Should().Be(id);
        line.PreviousStatus.Should().Be(AttendanceStatus.UnjustifiedAbsence);
    }

    [Fact]
    public async Task A_Student_Present_Despite_The_Ticket_Stays_Present()
    {
        await CreateAsync(Awa, Samedi, Samedi);
        await SubmitAsync((Awa, AttendanceStatus.Present), (Modou, AttendanceStatus.Present));

        await using var check = _db.NewAppContext(EcoleA);
        var line = await check.StudentAttendances.SingleAsync(sa => sa.StudentId == Awa);
        line.Status.Should().Be(AttendanceStatus.Present);
        line.AbsenceJustificationId.Should().BeNull();
    }

    [Fact]
    public async Task Two_Active_Tickets_Cannot_Overlap_For_The_Same_Student_But_A_Cancelled_One_Frees_The_Days()
    {
        var first = await CreateAsync(Awa, Samedi, Samedi.AddDays(3));

        var act = () => CreateAsync(Awa, Samedi.AddDays(2), Samedi.AddDays(5));
        await act.Should().ThrowAsync<ValidationException>();

        // Un autre élève, ou des jours disjoints, passent.
        await CreateAsync(Modou, Samedi, Samedi.AddDays(3));
        await CreateAsync(Awa, Samedi.AddDays(4), Samedi.AddDays(6));

        await using (var db = _db.NewAppContext(EcoleA))
        {
            await new CancelAbsenceJustificationCommandHandler(db, Directeur).Handle(new CancelAbsenceJustificationCommand(first), default);
        }

        await CreateAsync(Awa, Samedi.AddDays(2), Samedi.AddDays(3));
    }

    [Fact]
    public async Task The_List_Exposes_The_Range_And_Is_Isolated_Between_Schools()
    {
        await CreateAsync(Awa, Samedi, Samedi.AddDays(2));

        await using var db = _db.NewAppContext(EcoleA);
        var list = await new GetAbsenceJustificationsQueryHandler(db).Handle(new GetAbsenceJustificationsQuery(), default);
        var dto = list.Should().ContainSingle().Subject;
        dto.StartDate.Should().Be(Samedi);
        dto.EndDate.Should().Be(Samedi.AddDays(2));
        dto.Days.Should().Be(3);

        await using var other = _db.NewAppContext(EcoleB);
        (await other.AbsenceJustifications.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task The_Printable_Ticket_Carries_The_Range_The_Student_And_Is_Not_Readable_From_Another_School()
    {
        var id = await CreateAsync(Awa, Samedi, Samedi.AddDays(2));

        await using (var db = _db.NewAppContext(EcoleA))
        {
            var ticket = await new GetAbsenceTicketQueryHandler(db).Handle(new GetAbsenceTicketQuery(id), default);
            ticket.TicketNumber.Should().StartWith("ABSENCE-");
            ticket.StudentFullName.Should().Be("Awa Fall");
            ticket.StartDate.Should().Be(Samedi);
            ticket.EndDate.Should().Be(Samedi.AddDays(2));
            ticket.Days.Should().Be(3);
            ticket.ClassroomName.Should().Be("CM2 A");
        }

        await using var other = _db.NewAppContext(EcoleB);
        var act = () => new GetAbsenceTicketQueryHandler(other).Handle(new GetAbsenceTicketQuery(id), default);
        await act.Should().ThrowAsync<KeyNotFoundException>("la RLS masque le billet d'une autre école");
    }

    [Theory]
    [InlineData("2026-09-26", "2026-09-25", false)]
    [InlineData("2026-09-26", "2026-09-26", true)]
    [InlineData("2026-09-26", "2026-12-26", false)]
    public void The_Validator_Bounds_The_Range(string start, string end, bool valid)
    {
        var result = new CreateAbsenceJustificationCommandValidator().Validate(new CreateAbsenceJustificationCommand
        {
            StudentId = Guid.NewGuid(), StartDate = DateOnly.Parse(start), EndDate = DateOnly.Parse(end), Reason = "Deuil"
        });
        result.IsValid.Should().Be(valid);
    }
}
