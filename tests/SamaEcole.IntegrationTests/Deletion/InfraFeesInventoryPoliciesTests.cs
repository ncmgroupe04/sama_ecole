using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Buildings.Deletion;
using SamaEcole.Application.Common.Deletion;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Finance.Deletion;
using SamaEcole.Application.Inventory.Deletion;
using SamaEcole.Application.Rooms.Deletion;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Deletion;

/// <summary>
/// Suppression guidée des bâtiments, salles, catégories de frais, catégories et biens d'inventaire (spec 2026-10-17, §6) :
/// blocage financier, liens énumérés, rupture explicite (archivage ou détachement), liens non rompables, isolation.
/// </summary>
[Trait("Category", "MultiTenant")]
public class InfraFeesInventoryPoliciesTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("11111111-eeee-1111-1111-111111111111");
    private static readonly Guid AutreEcole = Guid.Parse("22222222-eeee-1111-1111-111111111111");
    private static readonly Guid Classe = Guid.Parse("aaaaaaaa-eeee-0000-0000-00000000000a");
    private static readonly Guid Annee = Guid.Parse("bbbbbbbb-eeee-0000-0000-00000000000b");
    private static readonly Guid Eleve = Guid.Parse("eeeeeeee-eeee-0000-0000-00000000000e");
    private static readonly Guid Inscription = Guid.Parse("11111111-eeee-0000-0000-0000000000f1");
    private static readonly Guid Caissier = Guid.Parse("dddddddd-eeee-0000-0000-00000000000d");
    private static readonly Guid Batiment = Guid.Parse("bb000000-eeee-0000-0000-000000000001");
    private static readonly Guid BatimentLibre = Guid.Parse("bb000000-eeee-0000-0000-000000000002");
    private static readonly Guid Salle = Guid.Parse("ff000000-eeee-0000-0000-000000000001");
    private static readonly Guid SalleLibre = Guid.Parse("ff000000-eeee-0000-0000-000000000002");
    private static readonly Guid Frais = Guid.Parse("fc000000-eeee-0000-0000-000000000001");
    private static readonly Guid FraisLibre = Guid.Parse("fc000000-eeee-0000-0000-000000000002");
    private static readonly Guid CategorieStock = Guid.Parse("cc000000-eeee-0000-0000-000000000001");
    private static readonly Guid CategorieStockLibre = Guid.Parse("cc000000-eeee-0000-0000-000000000002");
    private static readonly Guid Bien = Guid.Parse("1b000000-eeee-0000-0000-000000000001");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();
        owner.Schools.Add(new School { Id = Ecole, Name = "École A" });
        owner.Schools.Add(new School { Id = AutreEcole, Name = "École B" });
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
        owner.Buildings.Add(new Building { Id = Batiment, SchoolId = Ecole, Name = "Bâtiment A" });
        owner.Buildings.Add(new Building { Id = BatimentLibre, SchoolId = Ecole, Name = "Bâtiment B" });
        owner.Rooms.Add(new Room { Id = Salle, SchoolId = Ecole, Name = "Salle 1", BuildingId = Batiment });
        owner.Rooms.Add(new Room { Id = SalleLibre, SchoolId = Ecole, Name = "Salle 2", BuildingId = BatimentLibre });
        owner.FeeCategories.Add(new FeeCategory { Id = Frais, SchoolId = Ecole, Name = "Scolarité" });
        owner.FeeCategories.Add(new FeeCategory { Id = FraisLibre, SchoolId = Ecole, Name = "Cantine" });
        owner.InventoryCategories.Add(new InventoryCategory { Id = CategorieStock, SchoolId = Ecole, Name = "Manuels" });
        owner.InventoryCategories.Add(new InventoryCategory { Id = CategorieStockLibre, SchoolId = Ecole, Name = "Mobilier" });
        owner.InventoryItems.Add(new InventoryItem
        {
            Id = Bien, SchoolId = Ecole, Name = "Manuel de maths", CategoryId = CategorieStock, QuantityTotal = 10, QuantityAvailable = 9
        });
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private async Task<DeletionImpact> AnalyzeAsync(EntityDeletionPolicy policy, Guid id, Guid? school = null)
    {
        await using var ctx = _db.NewAppContext(school ?? Ecole);
        return await EntityDeletionAnalyzer.AnalyzeAsync(ctx, policy, id, CancellationToken.None);
    }

    private async Task<DeletionImpact> UnlinkAsync(EntityDeletionPolicy policy, Guid id)
    {
        await using var ctx = _db.NewAppContext(Ecole);
        return await EntityDeletionAnalyzer.UnlinkAsync(ctx, policy, id, "directeur", CancellationToken.None);
    }

    // ------------------------------------------------------------------------------------------ Bâtiments

    [Fact]
    public async Task A_Building_With_Rooms_Lists_Them_As_Not_Breakable()
    {
        var impact = await AnalyzeAsync(BuildingDeletionPolicy.Policy, Batiment);

        impact.CanDelete.Should().BeFalse();
        impact.Links.Should().ContainSingle(l => l.Kind == "Room" && !l.Breakable).Which.Label.Should().Be("1 salle");
        impact.Message.Should().StartWith("Ce bâtiment est actuellement rattaché à 1 salle");

        var act = async () => await UnlinkAsync(BuildingDeletionPolicy.Policy, Batiment);
        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be(DeletionErrorCodes.DependenciesNotBreakable);
    }

    // ------------------------------------------------------------------------------------------ Salles

    [Fact]
    public async Task Unlinking_A_Room_Detaches_Its_Inventory_Items_Without_Archiving_Them()
    {
        await using (var owner = _db.NewOwnerContext())
        {
            var item = await owner.InventoryItems.IgnoreQueryFilters().SingleAsync(i => i.Id == Bien);
            item.RoomId = Salle;
            await owner.SaveChangesAsync(CancellationToken.None);
        }

        var before = await AnalyzeAsync(RoomDeletionPolicy.Policy, Salle);
        before.Links.Should().ContainSingle(l => l.Kind == "InventoryItem" && l.Breakable);

        var after = await UnlinkAsync(RoomDeletionPolicy.Policy, Salle);
        after.CanDelete.Should().BeTrue();

        await using var check = _db.NewAppContext(Ecole);
        var item2 = await check.InventoryItems.SingleAsync(i => i.Id == Bien);
        item2.IsDeleted.Should().BeFalse("le bien n'est ni archivé ni supprimé");
        item2.RoomId.Should().BeNull("seul son emplacement est vidé");
    }

    [Fact]
    public async Task A_Room_And_A_Building_Without_Links_Can_Be_Deleted()
    {
        (await AnalyzeAsync(RoomDeletionPolicy.Policy, SalleLibre)).CanDelete.Should().BeTrue();
        // Bâtiment B porte la salle libre : on la retire pour tester un bâtiment vide.
        await using (var owner = _db.NewOwnerContext())
        {
            var room = await owner.Rooms.IgnoreQueryFilters().SingleAsync(r => r.Id == SalleLibre);
            room.SoftDelete("directeur");
            await owner.SaveChangesAsync(CancellationToken.None);
        }
        (await AnalyzeAsync(BuildingDeletionPolicy.Policy, BatimentLibre)).CanDelete.Should().BeTrue();
    }

    // ------------------------------------------------------------------------------------------ Catégories de frais

    private async Task AddEnrollmentFeeLineAsync()
    {
        await using var owner = _db.NewOwnerContext();
        owner.EnrollmentFeeLines.Add(new EnrollmentFeeLine
        {
            SchoolId = Ecole, EnrollmentId = Inscription, FeeCategoryId = Frais, Designation = "Scolarité",
            UnitAmount = 100_000m, Months = 1, LineTotal = 100_000m
        });
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    private async Task AddBreakdownAsync(PaymentStatus status)
    {
        await using var owner = _db.NewOwnerContext();
        var payment = new Payment
        {
            Id = Guid.NewGuid(), SchoolId = Ecole, EnrollmentId = Inscription, Amount = 30_000m, Method = PaymentMethod.Cash,
            Status = status, BalanceAfter = 70_000m, ReceiptNumber = "REC-" + Guid.NewGuid().ToString()[..6],
            ReceivedByUserId = Caissier, PaidAt = DateTimeOffset.UtcNow
        };
        if (status == PaymentStatus.Cancelled)
        {
            payment.CancelledAt = DateTimeOffset.UtcNow;
            payment.CancellationOutcome = PaymentCancellationOutcome.KeptBySchool;
        }
        owner.Payments.Add(payment);
        owner.PaymentBreakdowns.Add(new PaymentBreakdown
        {
            SchoolId = Ecole, PaymentId = payment.Id, FeeCategoryId = Frais, AmountAllocated = 30_000m
        });
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    [Fact]
    public async Task A_Fee_Category_Billed_To_An_Enrollment_Is_Financially_Blocked()
    {
        await AddEnrollmentFeeLineAsync();

        var impact = await AnalyzeAsync(FeeCategoryDeletionPolicy.Policy, Frais);

        impact.FinancialBlock.Should().BeTrue();
        impact.Message.Should().Be(FeeCategoryDeletionPolicy.FinancialBlock);
        impact.Links.Should().BeEmpty();
    }

    [Fact]
    public async Task A_Fee_Category_Used_By_An_Active_Payment_Is_Blocked_But_Not_By_A_Cancelled_One()
    {
        await AddBreakdownAsync(PaymentStatus.Cancelled);
        (await AnalyzeAsync(FeeCategoryDeletionPolicy.Policy, Frais)).FinancialBlock.Should().BeFalse("un paiement annulé ne bloque plus");

        await AddBreakdownAsync(PaymentStatus.Partial);
        (await AnalyzeAsync(FeeCategoryDeletionPolicy.Policy, Frais)).FinancialBlock.Should().BeTrue();
    }

    [Fact]
    public async Task Unlinking_A_Fee_Category_Archives_Its_Fee_Schedule_Lines_Explicitly()
    {
        await using (var owner = _db.NewOwnerContext())
        {
            owner.ClassFees.Add(new ClassFee { SchoolId = Ecole, FeeCategoryId = FraisLibre, ClassroomId = Classe, Amount = 25_000m });
            await owner.SaveChangesAsync(CancellationToken.None);
        }

        var before = await AnalyzeAsync(FeeCategoryDeletionPolicy.Policy, FraisLibre);
        before.CanDelete.Should().BeFalse("l'ancienne cascade silencieuse sur le barème n'existe plus");
        before.Links.Should().ContainSingle(l => l.Kind == "ClassFee" && l.Breakable).Which.Label.Should().Be("1 ligne du barème");

        (await UnlinkAsync(FeeCategoryDeletionPolicy.Policy, FraisLibre)).CanDelete.Should().BeTrue();

        await using var owner2 = _db.NewOwnerContext();
        (await owner2.ClassFees.IgnoreQueryFilters().SingleAsync(f => f.FeeCategoryId == FraisLibre)).IsDeleted.Should().BeTrue("archivé, pas effacé");
    }

    // ------------------------------------------------------------------------------------------ Inventaire

    [Fact]
    public async Task An_Inventory_Category_With_Items_Lists_Them_As_Not_Breakable()
    {
        var impact = await AnalyzeAsync(InventoryCategoryDeletionPolicy.Policy, CategorieStock);

        impact.Links.Should().ContainSingle(l => l.Kind == "InventoryItem" && !l.Breakable).Which.Label.Should().Be("1 bien");
        (await AnalyzeAsync(InventoryCategoryDeletionPolicy.Policy, CategorieStockLibre)).CanDelete.Should().BeTrue();
    }

    private async Task AddLoanAsync(AssignmentStatus status)
    {
        await using var owner = _db.NewOwnerContext();
        owner.ItemAssignments.Add(new ItemAssignment
        {
            SchoolId = Ecole, ItemId = Bien, Quantity = 1, BeneficiaryType = AssignmentBeneficiaryType.Eleve,
            StudentId = Eleve, BeneficiaryLabel = "Awa Fall", AssignedOn = new DateOnly(2026, 10, 12), Status = status
        });
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    [Fact]
    public async Task An_Open_Loan_Blocks_An_Inventory_Item_And_Cannot_Be_Unlinked()
    {
        await AddLoanAsync(AssignmentStatus.EnCours);

        var impact = await AnalyzeAsync(InventoryItemDeletionPolicy.Policy, Bien);

        impact.CanDelete.Should().BeFalse();
        impact.Links.Should().ContainSingle(l => l.Kind == "OpenLoan" && !l.Breakable);

        var act = async () => await UnlinkAsync(InventoryItemDeletionPolicy.Policy, Bien);
        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be(DeletionErrorCodes.DependenciesNotBreakable);
    }

    [Fact]
    public async Task Closed_Loans_Are_Intrinsic_And_Archived_With_The_Item_While_The_Stock_Journal_Stays()
    {
        await AddLoanAsync(AssignmentStatus.Restitue);

        var impact = await AnalyzeAsync(InventoryItemDeletionPolicy.Policy, Bien);
        impact.CanDelete.Should().BeTrue("l'historique des prêts clos part avec le bien, sans rupture manuelle");

        await using (var ctx = _db.NewAppContext(Ecole))
        {
            await EntityDeletionAnalyzer.ArchiveIntrinsicAsync(ctx, InventoryItemDeletionPolicy.Policy, Bien, "directeur", CancellationToken.None);
            await ctx.SaveChangesAsync(CancellationToken.None);
        }

        await using var owner = _db.NewOwnerContext();
        (await owner.ItemAssignments.IgnoreQueryFilters().SingleAsync(a => a.ItemId == Bien)).IsDeleted.Should().BeTrue();
    }

    // ------------------------------------------------------------------------------------------ Isolation

    [Fact]
    public async Task Another_School_Cannot_Analyze_These_Elements()
    {
        foreach (var (policy, id) in new[]
                 {
                     (BuildingDeletionPolicy.Policy, Batiment), (RoomDeletionPolicy.Policy, Salle),
                     (FeeCategoryDeletionPolicy.Policy, Frais), (InventoryCategoryDeletionPolicy.Policy, CategorieStock),
                     (InventoryItemDeletionPolicy.Policy, Bien)
                 })
        {
            var analyze = async () => await AnalyzeAsync(policy, id, AutreEcole);
            await analyze.Should().ThrowAsync<KeyNotFoundException>(policy.Resource);
        }
    }
}
