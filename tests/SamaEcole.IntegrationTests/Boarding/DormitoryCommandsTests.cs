using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Boarding;
using SamaEcole.Application.Boarding.Dormitories.CreateDormitory;
using SamaEcole.Application.Boarding.Dormitories.DeleteDormitory;
using SamaEcole.Application.Boarding.Dormitories.RestoreDormitory;
using SamaEcole.Application.Boarding.Dormitories.UpdateDormitory;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.SoftDelete;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Boarding;

/// <summary>Commandes de gestion des pavillons (lot B, tâche 2) — contre un vrai PostgreSQL, RLS comprise.</summary>
[Trait("Category", "MultiTenant")]
public class DormitoryCommandsTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("f1111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("f2222222-2222-2222-2222-222222222222");
    private static readonly Guid DirecteurA = Guid.Parse("f1111111-0000-0000-0000-0000000000d1");
    private static readonly Guid SurveillantA = Guid.Parse("f1111111-0000-0000-0000-0000000000a1");
    private static readonly Guid SurveillantB = Guid.Parse("f2222222-0000-0000-0000-0000000000a1");
    private static readonly Guid AnneeA = Guid.Parse("f1111111-0000-0000-0000-000000000001");
    private static readonly Guid ClasseA = Guid.Parse("f1111111-0000-0000-0000-0000000000c1");
    private static readonly Guid EleveA = Guid.Parse("f1111111-0000-0000-0000-0000000000e1");
    private static readonly Guid InscriptionA = Guid.Parse("f1111111-0000-0000-0000-0000000000f1");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();
        owner.Schools.AddRange(new School { Id = EcoleA, Name = "École A" }, new School { Id = EcoleB, Name = "École B" });
        owner.Users.AddRange(
            NewUser(DirecteurA, EcoleA, Role.Directeur, "Directeur A"),
            NewUser(SurveillantA, EcoleA, Role.Surveillant, "Surv. A"),
            NewUser(SurveillantB, EcoleB, Role.Surveillant, "Surv. B"));
        owner.SchoolYears.Add(new SchoolYear { Id = AnneeA, SchoolId = EcoleA, Label = "2026-2027", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true });
        owner.Classrooms.Add(new Classroom { Id = ClasseA, SchoolId = EcoleA, Name = "4ème A", Level = "Collège", Cycle = CycleType.College, Capacity = 40 });
        owner.Students.Add(new Student { Id = EleveA, SchoolId = EcoleA, Matricule = "ELEV-A1", FullName = "Awa A", BirthDate = new DateOnly(2011, 1, 1), BirthPlace = "Dakar", Gender = "F", ClassroomId = ClasseA });
        owner.Enrollments.Add(new Enrollment { Id = InscriptionA, SchoolId = EcoleA, StudentId = EleveA, SchoolYearId = AnneeA, ClassroomId = ClasseA, Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed, ReceiptNumber = "REC-A-1", EnrolledAt = DateTimeOffset.UtcNow });
        await owner.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private static User NewUser(Guid id, Guid school, Role role, string name) => new()
    {
        Id = id, SchoolId = school, Role = role, Status = EntityStatus.Active,
        Email = $"{id}@test.sn", PasswordHash = "x", FullName = name
    };

    private async Task<DormitoryDto> CreateAsync(
        Guid school, string name, DormitoryGender gender = DormitoryGender.Garcons, Guid? supervisor = null, string? supervisorName = null)
    {
        await using var ctx = _db.NewAppContext(school);
        return await new CreateDormitoryCommandHandler(ctx, new BoardingTenant(school)).Handle(
            new CreateDormitoryCommand { Name = name, Gender = gender, SupervisorUserId = supervisor, SupervisorName = supervisorName },
            CancellationToken.None);
    }

    private async Task DeleteAsync(Guid school, DormitoryDto dormitory)
    {
        await using var ctx = _db.NewAppContext(school);
        await new DeleteDormitoryCommandHandler(ctx, new TestCurrentUser(DirecteurA))
            .Handle(new DeleteDormitoryCommand(dormitory.Id, dormitory.RowVersion), CancellationToken.None);
    }

    private async Task<Guid> SeedRoomAsync(Guid school, Guid dormitory, string name = "Ch. 1")
    {
        await using var owner = _db.NewOwnerContext();
        var room = new DormitoryRoom { SchoolId = school, DormitoryId = dormitory, Name = name };
        owner.DormitoryRooms.Add(room);
        await owner.SaveChangesAsync();
        return room.Id;
    }

    [Fact]
    public async Task Creating_A_Dormitory_Persists_It_For_The_Current_School_Only()
    {
        var created = await CreateAsync(EcoleA, "Pavillon Oustaz Ahmad");

        created.Name.Should().Be("Pavillon Oustaz Ahmad");
        created.Gender.Should().Be(DormitoryGender.Garcons);
        await using var owner = _db.NewOwnerContext();
        (await owner.Dormitories.IgnoreQueryFilters().CountAsync(d => d.SchoolId == EcoleA)).Should().Be(1);
        (await owner.Dormitories.IgnoreQueryFilters().CountAsync(d => d.SchoolId == EcoleB)).Should().Be(0);
    }

    [Fact]
    public async Task Creating_A_Name_That_Only_Exists_Deleted_Returns_ARCHIVED_ENTITY_EXISTS()
    {
        var first = await CreateAsync(EcoleA, "Pavillon A");
        await DeleteAsync(EcoleA, first);

        var act = async () => await CreateAsync(EcoleA, "Pavillon A");

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be(SoftDeleteLifecycle.ArchivedEntityExists);
    }

    [Fact]
    public async Task The_Same_Name_Can_Exist_In_Two_Schools()
    {
        await CreateAsync(EcoleA, "Pavillon A");

        var act = async () => await CreateAsync(EcoleB, "Pavillon A");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task A_Supervisor_Account_Must_Be_An_Active_Surveillant_Of_The_Same_School()
    {
        var director = async () => await CreateAsync(EcoleA, "P1", supervisor: DirecteurA);
        await director.Should().ThrowAsync<ValidationException>().Where(e => e.Errors.ContainsKey("SupervisorUserId"));

        var otherSchool = async () => await CreateAsync(EcoleA, "P2", supervisor: SurveillantB);
        await otherSchool.Should().ThrowAsync<ValidationException>().Where(e => e.Errors.ContainsKey("SupervisorUserId"));

        var ok = async () => await CreateAsync(EcoleA, "P3", supervisor: SurveillantA);
        await ok.Should().NotThrowAsync();
    }

    [Fact]
    public async Task When_A_Supervisor_Account_Is_Linked_The_Free_Text_Name_Is_Not_Stored()
    {
        var created = await CreateAsync(EcoleA, "Pavillon A", supervisor: SurveillantA, supervisorName: "Texte libre ignoré");

        created.SupervisorName.Should().Be("Surv. A", "le nom vient du compte lié");
        await using var owner = _db.NewOwnerContext();
        (await owner.Dormitories.IgnoreQueryFilters().SingleAsync(d => d.Id == created.Id)).SupervisorName.Should().BeNull();
    }

    [Fact]
    public async Task Updating_With_A_Stale_RowVersion_Is_Rejected_With_A_Concurrency_Conflict()
    {
        var created = await CreateAsync(EcoleA, "Pavillon A");

        await using (var ctx = _db.NewAppContext(EcoleA))
        {
            await new UpdateDormitoryCommandHandler(ctx).Handle(
                new UpdateDormitoryCommand(created.Id, "Pavillon A bis", DormitoryGender.Garcons, null, null, null, null, created.RowVersion),
                CancellationToken.None);
        }

        await using var stale = _db.NewAppContext(EcoleA);
        var act = async () => await new UpdateDormitoryCommandHandler(stale).Handle(
            new UpdateDormitoryCommand(created.Id, "Pavillon A ter", DormitoryGender.Garcons, null, null, null, null, created.RowVersion),
            CancellationToken.None);

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
    }

    [Fact]
    public async Task Changing_The_Gender_Of_A_Dormitory_That_Has_Active_Boarders_Is_Rejected()
    {
        var created = await CreateAsync(EcoleA, "Pavillon A");
        var room = await SeedRoomAsync(EcoleA, created.Id);
        await using (var owner = _db.NewOwnerContext())
        {
            var bed = new Bed { SchoolId = EcoleA, DormitoryRoomId = room, BedNumber = 1 };
            owner.Beds.Add(bed);
            owner.BoardingEnrollments.Add(new BoardingEnrollment
            {
                SchoolId = EcoleA, StudentId = EleveA, EnrollmentId = InscriptionA, Regime = BoardingRegime.Interne,
                BedId = bed.Id, StartDate = new DateOnly(2026, 9, 15), IsActive = true
            });
            await owner.SaveChangesAsync();
        }

        await using var ctx = _db.NewAppContext(EcoleA);
        var act = async () => await new UpdateDormitoryCommandHandler(ctx).Handle(
            new UpdateDormitoryCommand(created.Id, "Pavillon A", DormitoryGender.Filles, null, null, null, null, created.RowVersion),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>().Where(e => e.Errors.ContainsKey("Gender"));
    }

    [Fact]
    public async Task Deleting_A_Dormitory_With_Live_Rooms_Returns_RESOURCE_IN_USE()
    {
        var created = await CreateAsync(EcoleA, "Pavillon A");
        await SeedRoomAsync(EcoleA, created.Id);

        var act = async () => await DeleteAsync(EcoleA, created);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be(SoftDeleteLifecycle.ResourceInUse);
    }

    [Fact]
    public async Task Deleting_An_Empty_Dormitory_Then_Restoring_It_Makes_It_Visible_Again()
    {
        var created = await CreateAsync(EcoleA, "Pavillon A");
        await DeleteAsync(EcoleA, created);

        await using (var hidden = _db.NewAppContext(EcoleA))
        {
            (await hidden.Dormitories.AnyAsync(d => d.Id == created.Id)).Should().BeFalse("un pavillon supprimé disparaît des lectures");
        }

        await using (var ctx = _db.NewAppContext(EcoleA))
        {
            await new RestoreDormitoryCommandHandler(ctx, new BoardingTenant(EcoleA))
                .Handle(new RestoreDormitoryCommand(created.Id), CancellationToken.None);
        }

        await using var visible = _db.NewAppContext(EcoleA);
        (await visible.Dormitories.AnyAsync(d => d.Id == created.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task Restoring_Fails_With_ACTIVE_ENTITY_CONFLICT_When_The_Name_Was_Taken()
    {
        var first = await CreateAsync(EcoleA, "Pavillon A");
        await DeleteAsync(EcoleA, first);
        await using (var owner = _db.NewOwnerContext())
        {
            // Une ligne ACTIVE reprend l'identité du pavillon supprimé (ce que la création normale interdit).
            owner.Dormitories.Add(new Dormitory { SchoolId = EcoleA, Name = "Pavillon A", Gender = DormitoryGender.Filles });
            await owner.SaveChangesAsync();
        }

        await using var ctx = _db.NewAppContext(EcoleA);
        var act = async () => await new RestoreDormitoryCommandHandler(ctx, new BoardingTenant(EcoleA))
            .Handle(new RestoreDormitoryCommand(first.Id), CancellationToken.None);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be(SoftDeleteLifecycle.ActiveEntityConflict);
    }

    [Fact]
    public async Task A_Dormitory_Of_Another_School_Is_Not_Found()
    {
        var other = await CreateAsync(EcoleB, "Pavillon B");

        await using var ctx = _db.NewAppContext(EcoleA);
        var update = async () => await new UpdateDormitoryCommandHandler(ctx).Handle(
            new UpdateDormitoryCommand(other.Id, "x", DormitoryGender.Garcons, null, null, null, null, other.RowVersion), CancellationToken.None);
        await update.Should().ThrowAsync<KeyNotFoundException>();

        var delete = async () => await new DeleteDormitoryCommandHandler(ctx, new TestCurrentUser(DirecteurA))
            .Handle(new DeleteDormitoryCommand(other.Id, other.RowVersion), CancellationToken.None);
        await delete.Should().ThrowAsync<KeyNotFoundException>();

        var restore = async () => await new RestoreDormitoryCommandHandler(ctx, new BoardingTenant(EcoleA))
            .Handle(new RestoreDormitoryCommand(other.Id), CancellationToken.None);
        await restore.Should().ThrowAsync<KeyNotFoundException>();
    }
}
