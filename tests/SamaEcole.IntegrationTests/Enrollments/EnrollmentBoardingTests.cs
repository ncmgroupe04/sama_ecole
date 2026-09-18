using FluentAssertions;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Enrollments.Commands.CreateEnrollment;
using SamaEcole.Domain.Common;
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
/// Ticket JGK-E03 (notification e-mail à l'inscription) : ces tests portent sur le module Internat,
/// pas sur la publication d'événements — couverte séparément par EnrollmentTests.cs (RecordingPublisher)
/// et NotifyAdminOnStudentEnrolledEventHandlerTests. Un no-op suffit ici pour satisfaire le constructeur.
/// </summary>
file sealed class NoOpPublisher : IPublisher
{
    public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification => Task.CompletedTask;
}

/// <summary>
/// Module Internat (Task 4) — étend CreateEnrollmentCommand : régime d'hébergement, chambre et ligne
/// de pension. Exerce le vrai Handler contre un PostgreSQL réel sous le rôle applicatif (RLS active).
/// </summary>
[Trait("Category", "MultiTenant")]
public class EnrollmentBoardingTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("33333333-1111-1111-1111-111111111111");
    private static readonly Guid ClasseA = Guid.Parse("33333333-0000-0000-0000-00000000000a");
    private static readonly Guid AnneeA = Guid.Parse("33333333-0000-0000-0000-00000000000b");
    private static readonly Guid Dortoir = Guid.Parse("33333333-0000-0000-0000-00000000000c");
    private static readonly Guid CatPension = Guid.Parse("33333333-0000-0000-0000-00000000000d");
    private static readonly Guid Batiment = Guid.Parse("33333333-0000-0000-0000-00000000000e");

    private readonly int _year = AcademicYear.ForDate(DateTimeOffset.UtcNow);

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();

        owner.Schools.Add(new School { Id = EcoleA, Name = "École A", Phone = "77 123 45 67" });
        owner.Classrooms.Add(new Classroom { Id = ClasseA, SchoolId = EcoleA, Name = "CM2", Level = "Primaire", Capacity = 40 });
        owner.SchoolYears.Add(new SchoolYear
        {
            Id = AnneeA, SchoolId = EcoleA, Label = $"{_year}-{_year + 1}",
            StartDate = new DateOnly(_year, 10, 1), EndDate = new DateOnly(_year + 1, 6, 30), IsActive = true
        });
        owner.Buildings.Add(new Building { Id = Batiment, SchoolId = EcoleA, Name = "Pavillon A" });
        owner.Rooms.Add(new Room { Id = Dortoir, SchoolId = EcoleA, BuildingId = Batiment, Name = "Chambre 1", Type = RoomType.Dortoir, Capacity = 1 });
        owner.FeeCategories.Add(new FeeCategory { Id = CatPension, SchoolId = EcoleA, Name = "Pension", IsRecurring = true, IsBoardingFee = true });
        owner.ClassFees.Add(new ClassFee { SchoolId = EcoleA, FeeCategoryId = CatPension, ClassroomId = ClasseA, Amount = 20_000m });
        owner.SchoolSettings.Add(new SchoolSettings { SchoolId = EcoleA, IsInternatEnabled = true });

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private CreateEnrollmentCommandHandler NewHandler(ApplicationDbContext db) =>
        new(db, new StubTenantProvider(EcoleA), _db.NewGenerator(db), TimeProvider.System, new NoOpKpiCacheService(), new NoOpPublisher());

    private static CreateEnrollmentCommand NewCommand(BoardingStatus status, Guid? roomId, bool includeFee) => new()
    {
        Type = EnrollmentType.NewEnrollment,
        ClassroomId = ClasseA,
        FullName = "Fatou Ndiaye",
        BirthDate = new DateOnly(2015, 3, 1),
        BirthPlace = "Dakar",
        Gender = "F",
        BoardingStatus = status,
        RoomId = roomId,
        IncludeBoardingFee = includeFee
    };

    [Fact]
    public async Task Interne_With_IncludeBoardingFee_Adds_The_Pension_Line()
    {
        await using var db = _db.NewAppContext(EcoleA);
        var receipt = await NewHandler(db).Handle(NewCommand(BoardingStatus.Interne, Dortoir, includeFee: true), CancellationToken.None);

        receipt.Lines.Should().Contain(l => l.Designation == "Pension" && l.LineTotal == 20_000m * 9);
    }

    [Fact]
    public async Task Interne_Without_IncludeBoardingFee_Does_Not_Add_The_Pension_Line()
    {
        await using var db = _db.NewAppContext(EcoleA);
        var receipt = await NewHandler(db).Handle(NewCommand(BoardingStatus.Interne, Dortoir, includeFee: false), CancellationToken.None);

        receipt.Lines.Should().NotContain(l => l.Designation == "Pension");
    }

    [Fact]
    public async Task Externe_Student_Never_Sees_The_Pension_Line_Even_If_Requested()
    {
        await using var db = _db.NewAppContext(EcoleA);
        var receipt = await NewHandler(db).Handle(NewCommand(BoardingStatus.Externe, roomId: null, includeFee: true), CancellationToken.None);

        receipt.Lines.Should().NotContain(l => l.Designation == "Pension");
    }

    [Fact]
    public async Task Room_At_Capacity_Is_Rejected_With_422()
    {
        await using var db1 = _db.NewAppContext(EcoleA);
        await NewHandler(db1).Handle(NewCommand(BoardingStatus.Interne, Dortoir, includeFee: false), CancellationToken.None);

        await using var db2 = _db.NewAppContext(EcoleA);
        var act = () => NewHandler(db2).Handle(NewCommand(BoardingStatus.Interne, Dortoir, includeFee: false), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>()
            .Where(e => e.Errors.ContainsKey("RoomId"));
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
        var act = () => NewHandler(db).Handle(NewCommand(BoardingStatus.Interne, Dortoir, includeFee: false), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>()
            .Where(e => e.Errors.ContainsKey("BoardingStatus"));
    }

    private sealed class StubTenantProvider(Guid? schoolId) : ITenantProvider
    {
        public Guid? CurrentSchoolId => schoolId;
    }
}
