using FluentAssertions;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Finance.Commands.GenerateTaxDeclaration;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Finance;

/// <summary>
/// Declaration fiscale - jusqu'ici sans aucune couverture, alors que TVA collectee/deductible etaient
/// cablees a 0 (voir GenerateTaxDeclarationCommand : le calcul est maintenant une vraie agregation de
/// Payment.VatAmount/Disbursement.VatAmount sur la periode). Ces tests couvrent : le sens du calcul
/// (encaisse - deductible), les exclusions (paiement annule, hors periode), et l'isolation multi-tenant
/// - exerces contre un vrai PostgreSQL (RLS active).
/// </summary>
[Trait("Category", "MultiTenant")]
public class GenerateTaxDeclarationCommandTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Classe = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid ClasseEcoleB = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000b");
    private static readonly Guid Annee = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");
    private static readonly Guid AnneeEcoleB = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000c");
    private static readonly Guid Eleve = Guid.Parse("eeeeeeee-0000-0000-0000-00000000000a");
    private static readonly Guid EleveEcoleB = Guid.Parse("eeeeeeee-0000-0000-0000-00000000000b");
    private static readonly Guid Inscription = Guid.Parse("11111111-0000-0000-0000-0000000000f1");
    private static readonly Guid InscriptionEcoleB = Guid.Parse("11111111-0000-0000-0000-0000000000f2");
    private static readonly Guid Caissier = Guid.Parse("dddddddd-0000-0000-0000-00000000000d");
    private static readonly Guid Enseignant = Guid.Parse("cccccccc-0000-0000-0000-00000000000c");
    private static readonly Guid Contrat = Guid.Parse("ffffffff-0000-0000-0000-00000000000f");

    private const int Mois = 3;
    private const int Annee2026 = 2026;

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();

        owner.Schools.AddRange(
            new School { Id = EcoleA, Name = "Ecole A" },
            new School { Id = EcoleB, Name = "Ecole B" });

        owner.Classrooms.AddRange(
            new Classroom { Id = Classe, SchoolId = EcoleA, Name = "CM2", Level = "Primaire", Capacity = 40 },
            new Classroom { Id = ClasseEcoleB, SchoolId = EcoleB, Name = "CM2", Level = "Primaire", Capacity = 40 });

        owner.SchoolYears.AddRange(
            new SchoolYear
            {
                Id = Annee, SchoolId = EcoleA, Label = "2025-2026",
                StartDate = new DateOnly(2025, 10, 1), EndDate = new DateOnly(2026, 6, 30), IsActive = true
            },
            new SchoolYear
            {
                Id = AnneeEcoleB, SchoolId = EcoleB, Label = "2025-2026",
                StartDate = new DateOnly(2025, 10, 1), EndDate = new DateOnly(2026, 6, 30), IsActive = true
            });

        owner.Students.AddRange(
            new Student { Id = Eleve, SchoolId = EcoleA, Matricule = "ELEV-2026-0001", FullName = "Awa Fall", BirthDate = new DateOnly(2015, 5, 20), BirthPlace = "Dakar", Gender = "F", ClassroomId = Classe },
            new Student { Id = EleveEcoleB, SchoolId = EcoleB, Matricule = "ELEV-B-0001", FullName = "Moussa Diop", BirthDate = new DateOnly(2013, 2, 10), BirthPlace = "Thies", Gender = "M", ClassroomId = ClasseEcoleB });

        owner.Enrollments.AddRange(
            new Enrollment
            {
                Id = Inscription, SchoolId = EcoleA, StudentId = Eleve, SchoolYearId = Annee, ClassroomId = Classe,
                Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed,
                TotalDue = 1_000_000m, AmountPaid = 0m, ReceiptNumber = "REC-2026-0001", EnrolledAt = DateTimeOffset.UtcNow
            },
            new Enrollment
            {
                Id = InscriptionEcoleB, SchoolId = EcoleB, StudentId = EleveEcoleB, SchoolYearId = AnneeEcoleB, ClassroomId = ClasseEcoleB,
                Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed,
                TotalDue = 1_000_000m, AmountPaid = 0m, ReceiptNumber = "REC-B-0001", EnrolledAt = DateTimeOffset.UtcNow
            });

        owner.Teachers.Add(new Teacher
        {
            Id = Enseignant, SchoolId = EcoleA, Matricule = "ENS-2026-0001", FullName = "Ndeye Sarr",
            Email = "ndeye@ecole-a.sn", BirthDate = new DateOnly(1985, 4, 12)
        });
        owner.EmployeeContracts.Add(new EmployeeContract
        {
            Id = Contrat, SchoolId = EcoleA, TeacherId = Enseignant, Type = ContractType.Permanent,
            BaseSalary = 200_000m, HourlyRate = 0m, TransportAllowance = 0m
        });
        owner.FichePaies.Add(Application.Finance.Services.PayrollCalculator.CalculateFichePaie(
            EcoleA, Contrat, Mois, Annee2026, baseSalary: 200_000m, hourlyRate: 0m, hoursWorked: 0m, transportAllowance: 0m));

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private static Payment NewPayment(Guid schoolId, Guid enrollmentId, decimal amount, decimal? vatRate, PaymentStatus status, DateTimeOffset paidAt, string receiptNumber) => new()
    {
        SchoolId = schoolId, EnrollmentId = enrollmentId, Amount = amount, Method = PaymentMethod.Cash,
        Status = status, BalanceAfter = 1_000_000m - amount, ReceiptNumber = receiptNumber,
        ReceivedByUserId = Caissier, PaidAt = paidAt,
        VatRate = vatRate, VatAmount = Application.Finance.Services.VatCalculator.ComputeVatAmount(amount, vatRate)
    };

    private static Disbursement NewDisbursement(Guid schoolId, decimal amount, decimal? vatRate, DateOnly date, string reason) => new()
    {
        SchoolId = schoolId, Reason = reason, Category = DisbursementCategory.Fournitures, Amount = amount,
        PaymentMethod = PaymentMethod.Cash, Date = date, Beneficiary = "Fournisseur Test",
        VatRate = vatRate, VatAmount = Application.Finance.Services.VatCalculator.ComputeVatAmount(amount, vatRate)
    };

    [Fact]
    public async Task Vat_Collected_And_Deductible_Are_Aggregated_From_The_Periods_Transactions()
    {
        await using var owner = _db.NewOwnerContext();
        owner.Payments.AddRange(
            NewPayment(EcoleA, Inscription, 118_000m, 0.18m, PaymentStatus.Partial, new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero), "REC-2026-0002"),
            NewPayment(EcoleA, Inscription, 50_000m, null, PaymentStatus.Partial, new DateTimeOffset(2026, 3, 15, 9, 0, 0, TimeSpan.Zero), "REC-2026-0003"));
        owner.Disbursements.Add(NewDisbursement(EcoleA, 59_000m, 0.18m, new DateOnly(2026, 3, 12), "Fournitures de bureau"));
        await owner.SaveChangesAsync(CancellationToken.None);

        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new GenerateTaxDeclarationCommandHandler(ctx, new FixedTenantProvider(EcoleA));

        var id = await handler.Handle(new GenerateTaxDeclarationCommand(Mois, Annee2026), CancellationToken.None);

        var declaration = await ctx.TaxeDeclarations.FindAsync([id], CancellationToken.None);
        declaration.Should().NotBeNull();
        declaration!.TvaCollected.Should().Be(18_000m, "118 000 TTC a 18% -> 18 000 de TVA, le versement exonere (50 000) n'apporte rien");
        declaration.TvaDeductible.Should().Be(9_000m, "59 000 TTC a 18% -> 9 000 de TVA recuperable");
        declaration.NetTva.Should().Be(9_000m, "18 000 collectee moins 9 000 deductible");
    }

    [Fact]
    public async Task A_Cancelled_Payment_Never_Contributes_To_Vat_Collected()
    {
        await using var owner = _db.NewOwnerContext();
        owner.Payments.Add(NewPayment(EcoleA, Inscription, 118_000m, 0.18m, PaymentStatus.Cancelled, new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero), "REC-2026-0004"));
        await owner.SaveChangesAsync(CancellationToken.None);

        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new GenerateTaxDeclarationCommandHandler(ctx, new FixedTenantProvider(EcoleA));

        var id = await handler.Handle(new GenerateTaxDeclarationCommand(Mois, Annee2026), CancellationToken.None);

        var declaration = await ctx.TaxeDeclarations.FindAsync([id], CancellationToken.None);
        declaration!.TvaCollected.Should().Be(0m, "un paiement annule n'a jamais represente un encaissement reel");
    }

    [Fact]
    public async Task Transactions_Outside_The_Declared_Month_Are_Excluded()
    {
        await using var owner = _db.NewOwnerContext();
        // Fevrier, alors que la declaration porte sur mars.
        owner.Payments.Add(NewPayment(EcoleA, Inscription, 118_000m, 0.18m, PaymentStatus.Partial, new DateTimeOffset(2026, 2, 28, 9, 0, 0, TimeSpan.Zero), "REC-2026-0005"));
        owner.Disbursements.Add(NewDisbursement(EcoleA, 59_000m, 0.18m, new DateOnly(2026, 4, 1), "Hors periode"));
        await owner.SaveChangesAsync(CancellationToken.None);

        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new GenerateTaxDeclarationCommandHandler(ctx, new FixedTenantProvider(EcoleA));

        var id = await handler.Handle(new GenerateTaxDeclarationCommand(Mois, Annee2026), CancellationToken.None);

        var declaration = await ctx.TaxeDeclarations.FindAsync([id], CancellationToken.None);
        declaration!.TvaCollected.Should().Be(0m);
        declaration.TvaDeductible.Should().Be(0m);
    }

    [Fact]
    public async Task Another_Schools_Vat_Never_Leaks_Into_The_Declaration()
    {
        await using var owner = _db.NewOwnerContext();
        owner.Payments.Add(NewPayment(EcoleB, InscriptionEcoleB, 500_000m, 0.18m, PaymentStatus.Partial, new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero), "REC-B-0002"));
        owner.Disbursements.Add(NewDisbursement(EcoleB, 200_000m, 0.18m, new DateOnly(2026, 3, 12), "Decaissement Ecole B"));
        await owner.SaveChangesAsync(CancellationToken.None);

        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new GenerateTaxDeclarationCommandHandler(ctx, new FixedTenantProvider(EcoleA));

        var id = await handler.Handle(new GenerateTaxDeclarationCommand(Mois, Annee2026), CancellationToken.None);

        var declaration = await ctx.TaxeDeclarations.FindAsync([id], CancellationToken.None);
        declaration!.TvaCollected.Should().Be(0m, "le paiement de l'Ecole B ne doit jamais compter dans la declaration de l'Ecole A");
        declaration.TvaDeductible.Should().Be(0m);
    }

    [Fact]
    public async Task The_Payroll_Aggregation_Still_Works_Alongside_The_New_Vat_Computation()
    {
        // Non-regression : IPRES/CSS/VRS/BRS restent agreges depuis les FichePaies, inchanges par l'ajout de la TVA.
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new GenerateTaxDeclarationCommandHandler(ctx, new FixedTenantProvider(EcoleA));

        var id = await handler.Handle(new GenerateTaxDeclarationCommand(Mois, Annee2026), CancellationToken.None);

        var declaration = await ctx.TaxeDeclarations.FindAsync([id], CancellationToken.None);
        declaration!.TotalIpres.Should().BeGreaterThan(0m);
        declaration.TotalDueToState.Should().BeGreaterThan(0m, "VRS + BRS restent dus independamment de la TVA");
    }

    [Fact]
    public async Task A_Duplicate_Declaration_For_The_Same_Period_Is_Rejected()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new GenerateTaxDeclarationCommandHandler(ctx, new FixedTenantProvider(EcoleA));
        await handler.Handle(new GenerateTaxDeclarationCommand(Mois, Annee2026), CancellationToken.None);

        await using var ctx2 = _db.NewAppContext(EcoleA);
        var handler2 = new GenerateTaxDeclarationCommandHandler(ctx2, new FixedTenantProvider(EcoleA));

        var act = async () => await handler2.Handle(new GenerateTaxDeclarationCommand(Mois, Annee2026), CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    private sealed class FixedTenantProvider(Guid schoolId) : Application.Common.Interfaces.ITenantProvider
    {
        public Guid? CurrentSchoolId => schoolId;
    }
}
