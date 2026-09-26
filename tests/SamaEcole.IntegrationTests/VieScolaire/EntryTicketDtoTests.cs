using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Absences.Commands.CreateLateArrival;
using SamaEcole.Application.Absences.Queries.GetEntryTicket;
using SamaEcole.Application.Attendance.EntryTickets;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Schools;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.VieScolaire;

file sealed class NoOpPublisher : IPublisher
{
    public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification => Task.CompletedTask;
}

file sealed class StubTenant(Guid? schoolId) : ITenantProvider
{
    public Guid? CurrentSchoolId => schoolId;
}

/// <summary>
/// Évolution N°5 — le billet imprimé porte le cours visé (matière, horaires, enseignant) et son statut ; un billet
/// SANS cours visé se présente exactement comme avant, avec ces champs à null.
/// </summary>
public class EntryTicketDtoTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("b1b11111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("b2b22222-2222-2222-2222-222222222222");
    private static readonly Guid Classe = Guid.Parse("b1aaaaaa-0000-0000-0000-0000000000a1");
    private static readonly Guid Maths = Guid.Parse("b1cccccc-0000-0000-0000-0000000000c1");
    private static readonly Guid Awa = Guid.Parse("b1eeeeee-0000-0000-0000-0000000000e1");
    private static readonly Guid Prof = Guid.Parse("b1e00000-0000-0000-0000-0000000000e1");
    private static readonly Guid Creneau = Guid.Parse("b1555555-0000-0000-0000-000000000001");
    private static readonly DateOnly Samedi = new(2026, 9, 26);

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();
        owner.Schools.AddRange(new School { Id = EcoleA, Name = "École A" }, new School { Id = EcoleB, Name = "École B" });
        owner.Classrooms.Add(new Classroom { Id = Classe, SchoolId = EcoleA, Name = "CM2 A", Level = "Primaire", Capacity = 40 });
        owner.Subjects.Add(new Subject { Id = Maths, SchoolId = EcoleA, Name = "Mathématiques", Level = "Primaire", Coefficient = 1 });
        owner.Students.Add(new Student
        {
            Id = Awa, SchoolId = EcoleA, Matricule = "ELEV-0001", FullName = "Awa Fall", BirthDate = new DateOnly(2015, 1, 1),
            BirthPlace = "Dakar", Gender = "F", ClassroomId = Classe
        });
        owner.Teachers.Add(new Teacher { Id = Prof, SchoolId = EcoleA, Matricule = "ENS-001", FullName = "Awa Sow", Email = "a@a.sn", BirthDate = new DateOnly(1985, 1, 1) });
        owner.ScheduleSlots.Add(new ScheduleSlot
        {
            Id = Creneau, SchoolId = EcoleA, TeacherId = Prof, ClassroomId = Classe, SubjectId = Maths,
            DayOfWeek = DayOfWeek.Saturday, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(10, 0)
        });
        await owner.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task A_Ticket_With_A_Target_Carries_The_Course_And_Its_Status()
    {
        var id = await IssueAsync(Creneau);

        await using var db = _db.NewAppContext(EcoleA);
        var dto = await new GetEntryTicketQueryHandler(db).Handle(new GetEntryTicketQuery(id), default);

        dto.TargetSubjectName.Should().Be("Mathématiques");
        dto.TargetTimeRange.Should().Be("08:00-10:00");
        dto.TargetTeacherName.Should().Be("Awa Sow");
        dto.Status.Should().Be("Issued");
        dto.TicketNumber.Should().Be(EntryTicketNumber.For(id), "un seul calcul du numéro de billet");
    }

    // Complément N°5 bis — le billet par heure d'arrivée porte l'heure, la durée régularisée et les cours manqués.
    [Fact]
    public async Task A_Ticket_By_Arrival_Time_Carries_The_Arrival_The_Duration_And_The_Missed_Courses()
    {
        Guid id;
        await using (var issue = _db.NewAppContext(EcoleA))
        {
            id = await new CreateLateArrivalCommandHandler(
                    issue, new StubTenant(EcoleA), new NoOpPublisher(), new EntryTicketRegister(issue), new WorkingDayGuard(issue),
                    new ArrivalPlanner(issue, new WorkingDayGuard(issue)))
                .Handle(new CreateLateArrivalCommand
                {
                    StudentId = Awa, Date = Samedi.ToDateTime(TimeOnly.MinValue), Reason = "Transport", ArrivalTime = new TimeOnly(11, 0)
                }, default);
        }

        await using var db = _db.NewAppContext(EcoleA);
        var dto = await new GetEntryTicketQueryHandler(db).Handle(new GetEntryTicketQuery(id), default);

        dto.ArrivalTime.Should().Be(new TimeOnly(11, 0));
        dto.TotalMinutes.Should().Be(120);
        dto.Minutes.Should().Be(0, "aucun retard sur un cours en cours : le cours est entièrement manqué");
        dto.MissedSlots.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new EntryTicketMissedSlot("Mathématiques", "08:00-10:00", 120));
    }

    [Fact]
    public async Task The_Status_Follows_The_Ticket_Through_Cancellation()
    {
        var id = await IssueAsync(Creneau);
        await using (var owner = _db.NewOwnerContext())
        {
            var ticket = owner.LateArrivals.IgnoreQueryFilters().Single(l => l.Id == id);
            ticket.Status = EntryTicketStatus.Cancelled;
            await owner.SaveChangesAsync();
        }

        await using var db = _db.NewAppContext(EcoleA);
        (await new GetEntryTicketQueryHandler(db).Handle(new GetEntryTicketQuery(id), default)).Status.Should().Be("Cancelled");
    }

    [Fact]
    public async Task A_Ticket_Without_A_Target_Is_Presented_As_Before()
    {
        var id = await IssueAsync(slot: null);

        await using var db = _db.NewAppContext(EcoleA);
        var dto = await new GetEntryTicketQueryHandler(db).Handle(new GetEntryTicketQuery(id), default);

        dto.TargetSubjectName.Should().BeNull();
        dto.TargetTimeRange.Should().BeNull();
        dto.TargetTeacherName.Should().BeNull();
        dto.Status.Should().BeNull();
        dto.Minutes.Should().Be(10);
        dto.ArrivalTime.Should().BeNull();
        dto.TotalMinutes.Should().BeNull();
        dto.MissedSlots.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "MultiTenant")]
    public async Task A_Ticket_Of_Another_School_Is_Not_Found()
    {
        var id = await IssueAsync(Creneau);

        await using var autre = _db.NewAppContext(EcoleB);
        var act = async () => await new GetEntryTicketQueryHandler(autre).Handle(new GetEntryTicketQuery(id), default);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    private async Task<Guid> IssueAsync(Guid? slot)
    {
        await using var db = _db.NewAppContext(EcoleA);
        return await new CreateLateArrivalCommandHandler(
                db, new StubTenant(EcoleA), new NoOpPublisher(), new EntryTicketRegister(db), new WorkingDayGuard(db), new ArrivalPlanner(db, new WorkingDayGuard(db)))
            .Handle(new CreateLateArrivalCommand
            {
                StudentId = Awa, Date = Samedi.ToDateTime(TimeOnly.MinValue), Minutes = 10, Reason = "Transport",
                TargetScheduleSlotId = slot
            }, default);
    }
}
