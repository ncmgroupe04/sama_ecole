using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Deletion;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Students.Commands.UnlinkStudentDependencies;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Students;

/// <summary>
/// Rupture explicite des liens d'un élève (spec 2026-10-17, §2.1) : archive les liens non financiers, ne touche jamais
/// à un paiement, refuse les liens non rompables, et reste inoffensive si on la rejoue.
/// </summary>
[Trait("Category", "MultiTenant")]
public class UnlinkStudentDependenciesTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("11111111-aaaa-1111-1111-111111111111");
    private static readonly Guid Classe = Guid.Parse("aaaaaaaa-aaaa-0000-0000-00000000000a");
    private static readonly Guid Annee = Guid.Parse("bbbbbbbb-aaaa-0000-0000-00000000000b");
    private static readonly Guid Eleve = Guid.Parse("eeeeeeee-aaaa-0000-0000-00000000000e");
    private static readonly Guid Inscription = Guid.Parse("11111111-aaaa-0000-0000-0000000000f1");
    private static readonly Guid Caissier = Guid.Parse("dddddddd-aaaa-0000-0000-00000000000d");
    private static readonly Guid Directeur = Guid.Parse("dddddddd-aaaa-0000-0000-0000000000d2");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();
        owner.Schools.Add(new School { Id = Ecole, Name = "École A" });
        owner.Classrooms.Add(new Classroom { Id = Classe, SchoolId = Ecole, Name = "CM2", Level = "Primaire", Capacity = 40 });
        owner.SchoolYears.Add(new SchoolYear
        {
            Id = Annee, SchoolId = Ecole, Label = "2026-2027",
            StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true
        });
        owner.Students.Add(new Student
        {
            Id = Eleve, SchoolId = Ecole, Matricule = "ELEV-2026-0001", FullName = "Awa Fall",
            BirthDate = new DateOnly(2015, 5, 20), BirthPlace = "Dakar", Gender = "F", ClassroomId = Classe
        });
        owner.Enrollments.Add(new Enrollment
        {
            Id = Inscription, SchoolId = Ecole, StudentId = Eleve, SchoolYearId = Annee, ClassroomId = Classe,
            Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed,
            TotalDue = 100_000m, AmountPaid = 0m, ReceiptNumber = "REC-2026-0001", EnrolledAt = DateTimeOffset.UtcNow
        });
        owner.Users.Add(new User
        {
            Id = Caissier, SchoolId = Ecole, Email = "c@ecole-a.sn", PasswordHash = "h", FullName = "Caissier", Role = Role.Finance
        });
        owner.Users.Add(new User
        {
            Id = Directeur, SchoolId = Ecole, Email = "d@ecole-a.sn", PasswordHash = "h", FullName = "Directeur", Role = Role.Directeur
        });
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private static UnlinkStudentDependenciesCommandHandler NewHandler(IApplicationDbContext ctx) =>
        new(ctx, new FixedUser(Directeur), new NoKpiCache());

    private async Task AddActivePaymentAsync()
    {
        await using var owner = _db.NewOwnerContext();
        owner.Payments.Add(new Payment
        {
            SchoolId = Ecole, EnrollmentId = Inscription, Amount = 30_000m, Method = PaymentMethod.Cash,
            Status = PaymentStatus.Partial, BalanceAfter = 70_000m, ReceiptNumber = "REC-1",
            ReceivedByUserId = Caissier, PaidAt = DateTimeOffset.UtcNow
        });
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Unlinking_Archives_The_Links_And_Leaves_The_Student_Deletable()
    {
        await using (var owner = _db.NewOwnerContext())
        {
            owner.LateArrivals.Add(new LateArrival { SchoolId = Ecole, StudentId = Eleve, Date = DateTime.UtcNow.Date, Minutes = 10, Reason = "Transport" });
            await owner.SaveChangesAsync(CancellationToken.None);
        }

        await using var ctx = _db.NewAppContext(Ecole);
        var impact = await NewHandler(ctx).Handle(new UnlinkStudentDependenciesCommand(Eleve), CancellationToken.None);

        impact.CanDelete.Should().BeTrue();

        await using var check = _db.NewAppContext(Ecole);
        (await check.Enrollments.CountAsync(e => e.StudentId == Eleve)).Should().Be(0, "l'inscription est archivée, plus visible");
        (await check.LateArrivals.CountAsync(l => l.StudentId == Eleve)).Should().Be(0);

        // Rien n'est effacé : les lignes existent toujours, marquées supprimées avec l'auteur.
        await using var owner2 = _db.NewOwnerContext();
        var archived = await owner2.Enrollments.IgnoreQueryFilters().SingleAsync(e => e.Id == Inscription);
        archived.IsDeleted.Should().BeTrue();
        archived.DeletedBy.Should().Be(Directeur.ToString());
        archived.Status.Should().Be(EnrollmentStatus.Cancelled);
    }

    [Fact]
    public async Task Unlinking_Is_Refused_When_An_Active_Payment_Exists_And_Touches_Nothing()
    {
        await AddActivePaymentAsync();

        await using var ctx = _db.NewAppContext(Ecole);
        var act = async () => await NewHandler(ctx).Handle(new UnlinkStudentDependenciesCommand(Eleve), CancellationToken.None);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be(DeletionErrorCodes.FinancialRecordsExist);

        await using var check = _db.NewAppContext(Ecole);
        (await check.Enrollments.CountAsync(e => e.StudentId == Eleve)).Should().Be(1);
    }

    [Fact]
    public async Task Unlinking_Is_Refused_While_Loaned_Equipment_Is_Not_Returned()
    {
        await using (var owner = _db.NewOwnerContext())
        {
            var category = new InventoryCategory { Id = Guid.NewGuid(), SchoolId = Ecole, Name = "Manuels" };
            var item = new InventoryItem
            {
                Id = Guid.NewGuid(), SchoolId = Ecole, Name = "Manuel de maths", CategoryId = category.Id,
                QuantityTotal = 10, QuantityAvailable = 9
            };
            owner.InventoryCategories.Add(category);
            owner.InventoryItems.Add(item);
            owner.ItemAssignments.Add(new ItemAssignment
            {
                SchoolId = Ecole, ItemId = item.Id, Quantity = 1, BeneficiaryType = AssignmentBeneficiaryType.Eleve,
                StudentId = Eleve, BeneficiaryLabel = "Awa Fall", AssignedOn = new DateOnly(2026, 10, 12),
                Status = AssignmentStatus.EnCours
            });
            await owner.SaveChangesAsync(CancellationToken.None);
        }

        await using var ctx = _db.NewAppContext(Ecole);
        var act = async () => await NewHandler(ctx).Handle(new UnlinkStudentDependenciesCommand(Eleve), CancellationToken.None);

        var error = (await act.Should().ThrowAsync<BusinessRuleException>()).Which;
        error.Code.Should().Be(DeletionErrorCodes.DependenciesNotBreakable);
        error.Message.Should().Contain("matériel prêté non restitué");

        await using var check = _db.NewAppContext(Ecole);
        (await check.Enrollments.CountAsync(e => e.StudentId == Eleve)).Should().Be(1, "rien n'est rompu tant que le matériel n'est pas restitué");
    }

    [Fact]
    public async Task Unlinking_Twice_Is_Harmless()
    {
        await using (var ctx = _db.NewAppContext(Ecole))
        {
            await NewHandler(ctx).Handle(new UnlinkStudentDependenciesCommand(Eleve), CancellationToken.None);
        }

        await using var again = _db.NewAppContext(Ecole);
        var impact = await NewHandler(again).Handle(new UnlinkStudentDependenciesCommand(Eleve), CancellationToken.None);

        impact.CanDelete.Should().BeTrue();
    }

    private sealed class FixedUser(Guid userId) : ICurrentUserService
    {
        public Guid? UserId => userId;
        public Role? Role => SamaEcole.Domain.Enums.Role.Directeur;
        public string? IpAddress => null;
    }

    private sealed class NoKpiCache : IKpiCacheService
    {
        public Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken) =>
            factory(cancellationToken);

        public void Invalidate(string key) { }
    }
}
