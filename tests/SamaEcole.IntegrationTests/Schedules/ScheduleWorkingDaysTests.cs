using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Features.Schedules;
using SamaEcole.Application.Schools;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Schedules;

/// <summary>
/// Évolution N°3 — les créneaux d'emploi du temps ne se placent que sur les jours ouvrés de
/// l'établissement (repos jeudi/vendredi ici). Arbitrage D3 : un créneau HÉRITÉ d'un jour devenu repos
/// reste lisible et supprimable, mais pas modifiable.
/// </summary>
public class ScheduleWorkingDaysTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("51111111-1111-1111-1111-111111111111");
    private static readonly Guid Classe = Guid.Parse("5aaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid Matiere = Guid.Parse("5ccccccc-0000-0000-0000-00000000000c");
    private static readonly Guid Fiche = Guid.Parse("5eeeeeee-0000-0000-0000-0000000000e1");

    private static readonly TestCurrentUser Directeur = new(Guid.Parse("5d000000-0000-0000-0000-000000000001"), Role.Directeur);

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();

        owner.Schools.Add(new School { Id = Ecole, Name = "École A" });
        // Repos jeudi et vendredi.
        owner.SchoolSettings.Add(new SchoolSettings { SchoolId = Ecole, WorkingDays = "Monday,Tuesday,Wednesday,Saturday,Sunday" });
        owner.Classrooms.Add(new Classroom { Id = Classe, SchoolId = Ecole, Name = "CM2 A", Level = "Primaire", Capacity = 40 });
        owner.Subjects.Add(new Subject { Id = Matiere, SchoolId = Ecole, Name = "Mathématiques", Level = "Primaire", Coefficient = 4 });
        owner.Teachers.Add(new Teacher
        {
            Id = Fiche, SchoolId = Ecole, Matricule = "ENS-2026-001", FullName = "Awa Fall",
            Email = "awa@ecole-a.sn", BirthDate = new DateOnly(1990, 4, 3)
        });

        await owner.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    // 7 — création : jeudi refusé (rien d'écrit), samedi accepté.
    [Fact]
    public async Task Creating_A_Slot_On_A_Rest_Day_Is_Refused_And_On_A_Working_Day_Succeeds()
    {
        await using var context = _db.NewAppContext(Ecole);

        var onRestDay = async () => await CreateAsync(context, DayOfWeek.Thursday);
        var error = (await onRestDay.Should().ThrowAsync<ValidationException>()).Which;
        error.Errors[nameof(CreateScheduleSlotCommand.DayOfWeek)].Single().Should().Contain("jeudi");

        await using var relecture = _db.NewAppContext(Ecole);
        (await relecture.ScheduleSlots.CountAsync()).Should().Be(0);

        var id = await CreateAsync(context, DayOfWeek.Saturday);
        id.Should().NotBeEmpty();
    }

    // 8 — déplacement vers un jour de repos : refusé, le créneau garde son jour.
    [Fact]
    public async Task Moving_A_Slot_To_A_Rest_Day_Is_Refused_And_Keeps_Its_Day()
    {
        await using var context = _db.NewAppContext(Ecole);
        var id = await CreateAsync(context, DayOfWeek.Monday);

        var act = async () => await UpdateAsync(context, id, DayOfWeek.Friday, "Salle 1");
        await act.Should().ThrowAsync<ValidationException>();

        await using var relecture = _db.NewAppContext(Ecole);
        (await relecture.ScheduleSlots.SingleAsync(s => s.Id == id)).DayOfWeek.Should().Be(DayOfWeek.Monday);
    }

    // 9 — créneau hérité : lisible et supprimable, mais pas modifiable sans changer son jour (D3).
    [Fact]
    public async Task A_Legacy_Slot_On_A_Rest_Day_Stays_Readable_And_Deletable_But_Not_Editable()
    {
        // Créneau posé un jeudi AVANT le changement de réglage : inséré par le propriétaire.
        Guid legacyId;
        await using (var owner = _db.NewOwnerContext())
        {
            var legacy = new ScheduleSlot
            {
                SchoolId = Ecole, TeacherId = Fiche, ClassroomId = Classe, SubjectId = Matiere,
                DayOfWeek = DayOfWeek.Thursday, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(10, 0), RoomNumber = "Salle 1"
            };
            owner.ScheduleSlots.Add(legacy);
            await owner.SaveChangesAsync();
            legacyId = legacy.Id;
        }

        await using var context = _db.NewAppContext(Ecole);

        var visible = await new GetClassroomScheduleQueryHandler(context)
            .Handle(new GetClassroomScheduleQuery(Classe), default);
        visible.Should().ContainSingle().Which.DayOfWeek.Should().Be(DayOfWeek.Thursday);

        // Modifier seulement la salle, sans changer le jour : refusé, un jour de repos reste un jour de repos.
        var edit = async () => await UpdateAsync(context, legacyId, DayOfWeek.Thursday, "Salle 2");
        await edit.Should().ThrowAsync<ValidationException>();

        var delete = new DeleteScheduleSlotCommandHandler(context, Directeur, Authorizer(context));
        await delete.Handle(new DeleteScheduleSlotCommand(legacyId), default);

        await using var relecture = _db.NewAppContext(Ecole);
        (await relecture.ScheduleSlots.AnyAsync(s => s.Id == legacyId)).Should().BeFalse();
    }

    private static ScheduleOwnershipAuthorizer Authorizer(IApplicationDbContext context) => new(context, Directeur);

    private static Task<Guid> CreateAsync(SamaEcole.Persistence.ApplicationDbContext context, DayOfWeek day)
        => new CreateScheduleSlotCommandHandler(
                context, new StubTenant(Ecole), Directeur, Authorizer(context), new WorkingDayGuard(context))
            .Handle(
                new CreateScheduleSlotCommand(Fiche, Classe, Matiere, day, new TimeOnly(8, 0), new TimeOnly(10, 0), "Salle 1"),
                default);

    private static Task UpdateAsync(SamaEcole.Persistence.ApplicationDbContext context, Guid id, DayOfWeek day, string room)
        => new UpdateScheduleSlotCommandHandler(context, Directeur, Authorizer(context), new WorkingDayGuard(context))
            .Handle(
                new UpdateScheduleSlotCommand(id, Fiche, Classe, Matiere, day, new TimeOnly(8, 0), new TimeOnly(10, 0), room),
                default);

    private sealed class StubTenant(Guid? schoolId) : ITenantProvider
    {
        public Guid? CurrentSchoolId => schoolId;
    }
}
