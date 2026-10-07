using FluentAssertions;
using SamaEcole.Application.Boarding.Assignments;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Enrollments.Commands.CreateEnrollment;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using SamaEcole.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace SamaEcole.IntegrationTests.Enrollments;

/// <summary>Cache KPI désactivé : ces tests exercent le Handler directement, hors DI (voir la même
/// justification dans EnrollmentTests.cs).</summary>
file sealed class NoOpKpiCacheService : IKpiCacheService
{
    public Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken) =>
        factory(cancellationToken);

    public void Invalidate(string key) { }
}

/// <summary>
/// Inscription avec hébergement (modèle Pavillon/Lit, lot C) : régime, chambre et ligne de pension. Le SÉJOUR est créé dans
/// la même transaction que l'inscription, par la règle d'affectation unique ; les colonnes héritées de l'inscription ne
/// sont plus écrites. Exerce le vrai Handler contre un PostgreSQL réel sous le rôle applicatif (RLS active).
/// </summary>
[Trait("Category", "MultiTenant")]
public class EnrollmentBoardingTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("33333333-1111-1111-1111-111111111111");
    private static readonly Guid ClasseA = Guid.Parse("33333333-0000-0000-0000-00000000000a");
    private static readonly Guid AnneeA = Guid.Parse("33333333-0000-0000-0000-00000000000b");
    private static readonly Guid Chambre = Guid.Parse("33333333-0000-0000-0000-00000000000c");
    private static readonly Guid CatPension = Guid.Parse("33333333-0000-0000-0000-00000000000d");
    private static readonly Guid Pavillon = Guid.Parse("33333333-0000-0000-0000-00000000000e");
    private static readonly Guid PavillonGarcons = Guid.Parse("33333333-0000-0000-0000-0000000000a1");
    private static readonly Guid ChambreGarcons = Guid.Parse("33333333-0000-0000-0000-0000000000a2");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();

        owner.Schools.Add(new School { Id = EcoleA, Name = "École A", Phone = "77 123 45 67" });
        owner.TenantSubscriptions.Add(RlsTestDatabase.UnlimitedSubscription(EcoleA));
        owner.Classrooms.Add(new Classroom { Id = ClasseA, SchoolId = EcoleA, Name = "CM2", Level = "Primaire", Capacity = 40 });
        owner.SchoolYears.Add(new SchoolYear
        {
            Id = AnneeA, SchoolId = EcoleA, Label = "2026-2027",
            StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true
        });

        // Un pavillon mixte avec UNE chambre d'UN lit (le cas limite « chambre pleine »), un pavillon de garçons.
        owner.Dormitories.AddRange(
            new Dormitory { Id = Pavillon, SchoolId = EcoleA, Name = "Pavillon A", Gender = DormitoryGender.Mixte },
            new Dormitory { Id = PavillonGarcons, SchoolId = EcoleA, Name = "Pavillon Garçons", Gender = DormitoryGender.Garcons });
        owner.DormitoryRooms.AddRange(
            new DormitoryRoom { Id = Chambre, SchoolId = EcoleA, DormitoryId = Pavillon, Name = "Chambre 1" },
            new DormitoryRoom { Id = ChambreGarcons, SchoolId = EcoleA, DormitoryId = PavillonGarcons, Name = "Chambre G" });
        owner.Beds.AddRange(
            new Bed { SchoolId = EcoleA, DormitoryRoomId = Chambre, BedNumber = 1 },
            new Bed { SchoolId = EcoleA, DormitoryRoomId = ChambreGarcons, BedNumber = 1 });

        owner.FeeCategories.Add(new FeeCategory { Id = CatPension, SchoolId = EcoleA, Name = "Pension", IsRecurring = true, IsBoardingFee = true });
        owner.ClassFees.Add(new ClassFee { SchoolId = EcoleA, FeeCategoryId = CatPension, ClassroomId = ClasseA, Amount = 20_000m });
        owner.SchoolSettings.Add(new SchoolSettings { SchoolId = EcoleA, IsInternatEnabled = true });

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private CreateEnrollmentCommandHandler NewHandler(ApplicationDbContext db) =>
        new(db, new StubTenantProvider(EcoleA), _db.NewGenerator(db), TimeProvider.System, new NoOpKpiCacheService(),
            new TestCurrentUser(), _db.NewQuotaGuard(db, EcoleA), new BoardingAssignmentService(db, TimeProvider.System));

    private static CreateEnrollmentCommand NewCommand(
        BoardingStatus status, Guid? roomId, bool includeFee, string gender = "F", string name = "Fatou Ndiaye") => new()
    {
        Type = EnrollmentType.NewEnrollment,
        ClassroomId = ClasseA,
        FullName = name,
        BirthDate = new DateOnly(2015, 3, 1),
        BirthPlace = "Dakar",
        Gender = gender,
        BoardingStatus = status,
        RoomId = roomId,
        IncludeBoardingFee = includeFee
    };

    [Fact]
    public async Task Interne_With_IncludeBoardingFee_Adds_The_Pension_Line()
    {
        await using var db = _db.NewAppContext(EcoleA);
        var receipt = await NewHandler(db).Handle(NewCommand(BoardingStatus.Interne, Chambre, includeFee: true), CancellationToken.None);

        receipt.Lines.Should().Contain(l => l.Designation == "Pension" && l.LineTotal == 20_000m * 9);
    }

    [Fact]
    public async Task Interne_Without_IncludeBoardingFee_Does_Not_Add_The_Pension_Line()
    {
        await using var db = _db.NewAppContext(EcoleA);
        var receipt = await NewHandler(db).Handle(NewCommand(BoardingStatus.Interne, Chambre, includeFee: false), CancellationToken.None);

        receipt.Lines.Should().NotContain(l => l.Designation == "Pension");
    }

    [Fact]
    public async Task Externe_Student_Never_Sees_The_Pension_Line_Even_If_Requested()
    {
        await using var db = _db.NewAppContext(EcoleA);
        var receipt = await NewHandler(db).Handle(NewCommand(BoardingStatus.Externe, roomId: null, includeFee: true), CancellationToken.None);

        receipt.Lines.Should().NotContain(l => l.Designation == "Pension");
        await using var check = _db.NewAppContext(EcoleA);
        (await check.BoardingEnrollments.AsNoTracking().CountAsync()).Should().Be(0, "un externe n'a pas de séjour");
    }

    [Fact]
    public async Task Interne_Creates_An_Active_Stay_On_The_Free_Bed_In_The_Same_Transaction()
    {
        await using var db = _db.NewAppContext(EcoleA);
        await NewHandler(db).Handle(NewCommand(BoardingStatus.Interne, Chambre, includeFee: false), CancellationToken.None);

        await using var check = _db.NewAppContext(EcoleA);
        var stay = await check.BoardingEnrollments.AsNoTracking().SingleAsync();
        (stay.Regime, stay.IsActive, stay.EndDate).Should().Be((BoardingRegime.Interne, true, (DateOnly?)null));
        stay.StartDate.Should().Be(DateOnly.FromDateTime(DateTime.UtcNow));
        (await check.Beds.AsNoTracking().SingleAsync(b => b.Id == stay.BedId)).DormitoryRoomId.Should().Be(Chambre);

        // Les colonnes héritées de l'inscription ne sont plus écrites.
        var enrollment = await check.Enrollments.AsNoTracking().SingleAsync(e => e.Id == stay.EnrollmentId);
#pragma warning disable CS0618
        (enrollment.BoardingStatus, enrollment.RoomId).Should().Be((BoardingStatus.Externe, (Guid?)null));
#pragma warning restore CS0618
    }

    [Fact]
    public async Task A_Half_Boarder_Gets_A_Stay_Without_A_Bed_Even_If_A_Room_Is_Sent()
    {
        await using var db = _db.NewAppContext(EcoleA);
        await NewHandler(db).Handle(NewCommand(BoardingStatus.DemiPensionnaire, Chambre, includeFee: false), CancellationToken.None);

        await using var check = _db.NewAppContext(EcoleA);
        var stay = await check.BoardingEnrollments.AsNoTracking().SingleAsync();
        (stay.Regime, stay.BedId).Should().Be((BoardingRegime.DemiPensionnaire, (Guid?)null));
    }

    [Fact]
    public async Task Room_At_Capacity_Is_Rejected_With_422_And_Nothing_Is_Created_For_The_Second_Student()
    {
        await using var db1 = _db.NewAppContext(EcoleA);
        await NewHandler(db1).Handle(NewCommand(BoardingStatus.Interne, Chambre, includeFee: false), CancellationToken.None);

        await using var db2 = _db.NewAppContext(EcoleA);
        var act = () => NewHandler(db2).Handle(
            NewCommand(BoardingStatus.Interne, Chambre, includeFee: false, name: "Binta Sow"), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>().Where(e => e.Errors.ContainsKey("RoomId"));

        // Atomicité : l'inscription refusée n'a laissé ni élève, ni inscription, ni séjour.
        await using var check = _db.NewAppContext(EcoleA);
        (await check.BoardingEnrollments.AsNoTracking().CountAsync()).Should().Be(1);
        (await check.Enrollments.AsNoTracking().CountAsync()).Should().Be(1);
        (await check.Students.AsNoTracking().AnyAsync(s => s.FullName == "Binta Sow")).Should().BeFalse();
    }

    [Fact]
    public async Task A_Girl_Cannot_Be_Enrolled_Into_A_Boys_Dormitory_And_A_New_Boy_Can()
    {
        await using var db1 = _db.NewAppContext(EcoleA);
        var girl = () => NewHandler(db1).Handle(
            NewCommand(BoardingStatus.Interne, ChambreGarcons, includeFee: false, gender: "F"), CancellationToken.None);
        await girl.Should().ThrowAsync<ValidationException>().Where(e => e.Errors.ContainsKey("BedId"));

        // Le genre d'un élève NEUF (pas encore enregistré) est lu sur l'entité suivie : le garçon passe.
        await using var db2 = _db.NewAppContext(EcoleA);
        var boy = () => NewHandler(db2).Handle(
            NewCommand(BoardingStatus.Interne, ChambreGarcons, includeFee: false, gender: "M", name: "Modou Ba"), CancellationToken.None);
        await boy.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Boarding_Status_Rejected_With_422_When_Internat_Module_Disabled()
    {
        await using var setup = _db.NewOwnerContext();
        // IgnoreQueryFilters : NewOwnerContext() n'a pas de tenant (StubTenantProvider(null)), le Global
        // Query Filter (SchoolId == CurrentSchoolId) ne matcherait donc jamais une entité tenant, même
        // avec un prédicat explicite sur SchoolId (même raisonnement que GetReportCardPdfTests.cs).
        var settings = await setup.SchoolSettings.IgnoreQueryFilters().SingleAsync(s => s.SchoolId == EcoleA);
        settings.IsInternatEnabled = false;
        await setup.SaveChangesAsync(CancellationToken.None);

        await using var db = _db.NewAppContext(EcoleA);
        var act = () => NewHandler(db).Handle(NewCommand(BoardingStatus.Interne, Chambre, includeFee: false), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>()
            .Where(e => e.Errors.ContainsKey("BoardingStatus"));
    }

    private sealed class StubTenantProvider(Guid? schoolId) : ITenantProvider
    {
        public Guid? CurrentSchoolId => schoolId;
    }
}
