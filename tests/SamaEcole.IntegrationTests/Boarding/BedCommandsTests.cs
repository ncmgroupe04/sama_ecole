using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Boarding;
using SamaEcole.Application.Boarding.Beds.ChangeBedStatus;
using SamaEcole.Application.Boarding.Beds.CreateBed;
using SamaEcole.Application.Boarding.Beds.DeleteBed;
using SamaEcole.Application.Boarding.Beds.RestoreBed;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.SoftDelete;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Boarding;

/// <summary>Commandes de gestion des lits (lot B, tâche 4) — contre un vrai PostgreSQL, RLS comprise.</summary>
[Trait("Category", "MultiTenant")]
public class BedCommandsTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("f4111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("f4222222-2222-2222-2222-222222222222");
    private static readonly Guid DirecteurA = Guid.Parse("f4111111-0000-0000-0000-0000000000d1");
    private static readonly Guid AnneeA = Guid.Parse("f4111111-0000-0000-0000-000000000001");
    private static readonly Guid ClasseA = Guid.Parse("f4111111-0000-0000-0000-0000000000c1");
    private static readonly Guid EleveA = Guid.Parse("f4111111-0000-0000-0000-0000000000e1");
    private static readonly Guid InscriptionA = Guid.Parse("f4111111-0000-0000-0000-0000000000f1");
    private static readonly Guid PavillonA = Guid.Parse("f4111111-0000-0000-0000-00000000b001");
    private static readonly Guid PavillonB = Guid.Parse("f4222222-0000-0000-0000-00000000b001");
    private static readonly Guid ChambreA = Guid.Parse("f4111111-0000-0000-0000-00000000a001");
    private static readonly Guid ChambreB = Guid.Parse("f4222222-0000-0000-0000-00000000a001");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();
        owner.Schools.AddRange(new School { Id = EcoleA, Name = "École A" }, new School { Id = EcoleB, Name = "École B" });
        owner.SchoolYears.Add(new SchoolYear { Id = AnneeA, SchoolId = EcoleA, Label = "2026-2027", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true });
        owner.Classrooms.Add(new Classroom { Id = ClasseA, SchoolId = EcoleA, Name = "4ème A", Level = "Collège", Cycle = CycleType.College, Capacity = 40 });
        owner.Students.Add(new Student { Id = EleveA, SchoolId = EcoleA, Matricule = "ELEV-A1", FullName = "Awa A", BirthDate = new DateOnly(2011, 1, 1), BirthPlace = "Dakar", Gender = "F", ClassroomId = ClasseA });
        owner.Enrollments.Add(new Enrollment { Id = InscriptionA, SchoolId = EcoleA, StudentId = EleveA, SchoolYearId = AnneeA, ClassroomId = ClasseA, Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed, ReceiptNumber = "REC-A-1", EnrolledAt = DateTimeOffset.UtcNow });
        owner.Dormitories.AddRange(
            new Dormitory { Id = PavillonA, SchoolId = EcoleA, Name = "Pavillon A", Gender = DormitoryGender.Filles },
            new Dormitory { Id = PavillonB, SchoolId = EcoleB, Name = "Pavillon B", Gender = DormitoryGender.Garcons });
        owner.DormitoryRooms.AddRange(
            new DormitoryRoom { Id = ChambreA, SchoolId = EcoleA, DormitoryId = PavillonA, Name = "Chambre A" },
            new DormitoryRoom { Id = ChambreB, SchoolId = EcoleB, DormitoryId = PavillonB, Name = "Chambre B" });
        await owner.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private async Task<BedDto> CreateAsync(Guid school, Guid room, int? number = null)
    {
        await using var ctx = _db.NewAppContext(school);
        return await new CreateBedCommandHandler(ctx, new BoardingTenant(school))
            .Handle(new CreateBedCommand { DormitoryRoomId = room, BedNumber = number }, CancellationToken.None);
    }

    private async Task DeleteAsync(Guid school, BedDto bed)
    {
        await using var ctx = _db.NewAppContext(school);
        await new DeleteBedCommandHandler(ctx, new TestCurrentUser(DirecteurA))
            .Handle(new DeleteBedCommand(bed.Id, bed.RowVersion), CancellationToken.None);
    }

    /// <summary>Un séjour ACTIF de l'élève A sur ce lit (c'est ce qui rend le lit « occupé »).</summary>
    private async Task OccupyAsync(Guid bed)
    {
        await using var owner = _db.NewOwnerContext();
        owner.BoardingEnrollments.Add(new BoardingEnrollment
        {
            SchoolId = EcoleA, StudentId = EleveA, EnrollmentId = InscriptionA, Regime = BoardingRegime.Interne,
            BedId = bed, StartDate = new DateOnly(2026, 9, 15), IsActive = true
        });
        await owner.SaveChangesAsync();
    }

    [Fact]
    public async Task The_Automatic_Number_Follows_The_Highest_Number_Starting_At_1()
    {
        (await CreateAsync(EcoleA, ChambreA)).BedNumber.Should().Be(1);
        (await CreateAsync(EcoleA, ChambreA)).BedNumber.Should().Be(2);
        (await CreateAsync(EcoleA, ChambreA, number: 7)).BedNumber.Should().Be(7);
        (await CreateAsync(EcoleA, ChambreA)).BedNumber.Should().Be(8);
    }

    [Fact]
    public async Task The_Automatic_Number_Never_Falls_Back_On_A_Deleted_Bed()
    {
        var first = await CreateAsync(EcoleA, ChambreA);
        var second = await CreateAsync(EcoleA, ChambreA);
        await DeleteAsync(EcoleA, second);

        (await CreateAsync(EcoleA, ChambreA)).BedNumber.Should().Be(3, "le numéro 2 appartient à un lit archivé");
        first.Status.Should().Be(BedStatus.Available);
    }

    [Fact]
    public async Task An_Explicit_Number_Already_Used_Is_A_Conflict_And_One_Used_By_A_Deleted_Bed_Is_Archived()
    {
        var bed = await CreateAsync(EcoleA, ChambreA, number: 5);

        var duplicate = async () => await CreateAsync(EcoleA, ChambreA, number: 5);
        await duplicate.Should().ThrowAsync<ConcurrencyConflictException>();

        await DeleteAsync(EcoleA, bed);
        var archived = async () => await CreateAsync(EcoleA, ChambreA, number: 5);
        (await archived.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be(SoftDeleteLifecycle.ArchivedEntityExists);
    }

    [Fact]
    public async Task A_Room_Of_Another_School_Is_Refused()
    {
        var act = async () => await CreateAsync(EcoleA, ChambreB);

        await act.Should().ThrowAsync<ValidationException>().Where(e => e.Errors.ContainsKey("DormitoryRoomId"));
    }

    [Fact]
    public async Task A_Free_Bed_Goes_To_Maintenance_And_Back()
    {
        var bed = await CreateAsync(EcoleA, ChambreA);

        BedDto inMaintenance;
        await using (var ctx = _db.NewAppContext(EcoleA))
        {
            inMaintenance = await new ChangeBedStatusCommandHandler(ctx)
                .Handle(new ChangeBedStatusCommand(bed.Id, BedStatus.Maintenance, bed.RowVersion), CancellationToken.None);
        }

        inMaintenance.Status.Should().Be(BedStatus.Maintenance);

        await using var back = _db.NewAppContext(EcoleA);
        var available = await new ChangeBedStatusCommandHandler(back)
            .Handle(new ChangeBedStatusCommand(bed.Id, BedStatus.Available, inMaintenance.RowVersion), CancellationToken.None);
        available.Status.Should().Be(BedStatus.Available);
    }

    [Fact]
    public async Task An_Occupied_Bed_Reads_As_Occupied_And_Cannot_Go_To_Maintenance_Or_Be_Deleted()
    {
        var bed = await CreateAsync(EcoleA, ChambreA);
        await OccupyAsync(bed.Id);

        await using var ctx = _db.NewAppContext(EcoleA);
        var read = (await SamaEcole.Application.Boarding.Beds.BedReader.ListForRoomAsync(ctx, ChambreA, CancellationToken.None)).Single();
        read.Status.Should().Be(BedStatus.Occupied);
        read.OccupantName.Should().Be("Awa A");
        read.OccupantBoarderId.Should().NotBeNull();

        var maintenance = async () => await new ChangeBedStatusCommandHandler(ctx)
            .Handle(new ChangeBedStatusCommand(bed.Id, BedStatus.Maintenance, bed.RowVersion), CancellationToken.None);
        (await maintenance.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be(SoftDeleteLifecycle.ResourceInUse);

        var delete = async () => await new DeleteBedCommandHandler(ctx, new TestCurrentUser(DirecteurA))
            .Handle(new DeleteBedCommand(bed.Id, bed.RowVersion), CancellationToken.None);
        (await delete.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be(SoftDeleteLifecycle.ResourceInUse);
    }

    [Fact]
    public async Task A_Stale_RowVersion_Is_Rejected_On_Status_Change()
    {
        var bed = await CreateAsync(EcoleA, ChambreA);
        await using (var ctx = _db.NewAppContext(EcoleA))
        {
            await new ChangeBedStatusCommandHandler(ctx)
                .Handle(new ChangeBedStatusCommand(bed.Id, BedStatus.Maintenance, bed.RowVersion), CancellationToken.None);
        }

        await using var stale = _db.NewAppContext(EcoleA);
        var act = async () => await new ChangeBedStatusCommandHandler(stale)
            .Handle(new ChangeBedStatusCommand(bed.Id, BedStatus.Available, bed.RowVersion), CancellationToken.None);

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
    }

    [Fact]
    public async Task A_Free_Bed_Can_Be_Deleted_And_Restored()
    {
        var bed = await CreateAsync(EcoleA, ChambreA);
        await DeleteAsync(EcoleA, bed);

        await using (var ctx = _db.NewAppContext(EcoleA))
        {
            (await ctx.Beds.AnyAsync(b => b.Id == bed.Id)).Should().BeFalse();
            await new RestoreBedCommandHandler(ctx, new BoardingTenant(EcoleA))
                .Handle(new RestoreBedCommand(bed.Id), CancellationToken.None);
        }

        await using var visible = _db.NewAppContext(EcoleA);
        (await visible.Beds.AnyAsync(b => b.Id == bed.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task Restoring_Fails_With_ACTIVE_ENTITY_CONFLICT_When_The_Number_Was_Taken()
    {
        var bed = await CreateAsync(EcoleA, ChambreA, number: 4);
        await DeleteAsync(EcoleA, bed);
        await using (var owner = _db.NewOwnerContext())
        {
            owner.Beds.Add(new Bed { SchoolId = EcoleA, DormitoryRoomId = ChambreA, BedNumber = 4 });
            await owner.SaveChangesAsync();
        }

        await using var ctx = _db.NewAppContext(EcoleA);
        var act = async () => await new RestoreBedCommandHandler(ctx, new BoardingTenant(EcoleA))
            .Handle(new RestoreBedCommand(bed.Id), CancellationToken.None);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be(SoftDeleteLifecycle.ActiveEntityConflict);
    }

    [Fact]
    public async Task Restoring_Into_A_Deleted_Room_Returns_PARENT_ENTITY_ARCHIVED()
    {
        var bed = await CreateAsync(EcoleA, ChambreA);
        await DeleteAsync(EcoleA, bed);
        await using (var owner = _db.NewOwnerContext())
        {
            (await owner.DormitoryRooms.IgnoreQueryFilters().SingleAsync(r => r.Id == ChambreA)).SoftDelete("test");
            await owner.SaveChangesAsync();
        }

        await using var ctx = _db.NewAppContext(EcoleA);
        var act = async () => await new RestoreBedCommandHandler(ctx, new BoardingTenant(EcoleA))
            .Handle(new RestoreBedCommand(bed.Id), CancellationToken.None);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("PARENT_ENTITY_ARCHIVED");
    }

    [Fact]
    public async Task A_Bed_Of_Another_School_Is_Not_Found()
    {
        var other = await CreateAsync(EcoleB, ChambreB);

        await using var ctx = _db.NewAppContext(EcoleA);
        var status = async () => await new ChangeBedStatusCommandHandler(ctx)
            .Handle(new ChangeBedStatusCommand(other.Id, BedStatus.Maintenance, other.RowVersion), CancellationToken.None);
        await status.Should().ThrowAsync<KeyNotFoundException>();

        var delete = async () => await new DeleteBedCommandHandler(ctx, new TestCurrentUser(DirecteurA))
            .Handle(new DeleteBedCommand(other.Id, other.RowVersion), CancellationToken.None);
        await delete.Should().ThrowAsync<KeyNotFoundException>();
    }
}
