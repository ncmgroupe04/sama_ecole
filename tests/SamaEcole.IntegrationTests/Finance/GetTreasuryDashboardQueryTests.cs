using FluentAssertions;
using SamaEcole.Application.Finance.Queries.GetTreasuryDashboard;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Finance;

/// <summary>
/// Module Comptabilité & Fiscalité (JGK) — le tableau de bord Trésorerie agrège les encaissements
/// (<c>Payments</c>) et décaissements (<c>Disbursements</c>) déjà existants, sans nouveau registre.
///
/// Deux garanties critiques :
/// - Le total encaissé compte TOUT paiement non annulé, y compris les versements PARTIELS (Status =
///   Partial reflète le solde de l'INSCRIPTION, pas si l'argent de CE versement a été reçu — un bug
///   de la première version filtrait sur Status == Paid et excluait donc tout paiement échelonné).
/// - L'isolation multi-tenant tient : les mouvements de l'École B ne remontent jamais dans le tableau
///   de l'École A (RLS + filtre EF, AGENTS.md règle #2), exercé ici contre un vrai PostgreSQL sous le
///   rôle applicatif.
/// </summary>
[Trait("Category", "MultiTenant")]
public class GetTreasuryDashboardQueryTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Classe = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid Annee = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");
    private static readonly Guid Eleve = Guid.Parse("eeeeeeee-0000-0000-0000-00000000000e");
    private static readonly Guid Inscription = Guid.Parse("11111111-0000-0000-0000-0000000000f1");
    private static readonly Guid Caissier = Guid.Parse("dddddddd-0000-0000-0000-00000000000d");

    // École B : fiches propres (une inscription liée à l'École A ne satisferait pas la FK composite
    // FK_payments_enrollments_SchoolId_EnrollmentId — impossible d'y accrocher un paiement d'École B).
    private static readonly Guid ClasseB = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000b");
    private static readonly Guid AnneeB = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000c");
    private static readonly Guid EleveB = Guid.Parse("eeeeeeee-0000-0000-0000-00000000000f");
    private static readonly Guid InscriptionB = Guid.Parse("11111111-0000-0000-0000-0000000000f2");

    private static readonly DateOnly PeriodStart = new(2026, 3, 1);
    private static readonly DateOnly PeriodEnd = new(2026, 3, 31);

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();

        owner.Schools.AddRange(
            new School { Id = EcoleA, Name = "École A" },
            new School { Id = EcoleB, Name = "École B" });

        owner.Classrooms.Add(new Classroom { Id = Classe, SchoolId = EcoleA, Name = "CM2", Level = "Primaire", Capacity = 40 });
        owner.SchoolYears.Add(new SchoolYear
        {
            Id = Annee, SchoolId = EcoleA, Label = "2025-2026",
            StartDate = new DateOnly(2025, 10, 1), EndDate = new DateOnly(2026, 6, 30), IsActive = true
        });
        owner.Students.Add(new Student
        {
            Id = Eleve, SchoolId = EcoleA, Matricule = "ELEV-2025-0001",
            FullName = "Awa Fall", BirthDate = new DateOnly(2015, 5, 20), BirthPlace = "Dakar", Gender = "F", ClassroomId = Classe
        });
        owner.Enrollments.Add(new Enrollment
        {
            Id = Inscription, SchoolId = EcoleA, StudentId = Eleve, SchoolYearId = Annee, ClassroomId = Classe,
            Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed,
            TotalDue = 100_000m, AmountPaid = 40_000m, ReceiptNumber = "REC-2025-0001",
            EnrolledAt = DateTimeOffset.UtcNow
        });

        // École A — dans la période : un versement PARTIEL (doit compter comme encaissement malgré
        // Status = Partial) et un décaissement.
        owner.Payments.Add(new Payment
        {
            SchoolId = EcoleA, EnrollmentId = Inscription, Amount = 40_000m, Method = PaymentMethod.Cash,
            Status = PaymentStatus.Partial, BalanceAfter = 60_000m, ReceiptNumber = "REC-2025-0002",
            ReceivedByUserId = Caissier, PaidAt = new DateTimeOffset(2026, 3, 15, 9, 0, 0, TimeSpan.Zero)
        });
        owner.Disbursements.Add(new Disbursement
        {
            SchoolId = EcoleA, Reason = "Fournitures", Category = DisbursementCategory.Fournitures,
            Amount = 10_000m, PaymentMethod = PaymentMethod.Cash, Date = new DateOnly(2026, 3, 10), Beneficiary = "Papeterie du Marché"
        });

        // École A — hors période (mars) : un paiement de février et un paiement ANNULÉ en mars. Ni l'un
        // ni l'autre ne doivent entrer dans le total de la période testée.
        owner.Payments.Add(new Payment
        {
            SchoolId = EcoleA, EnrollmentId = Inscription, Amount = 25_000m, Method = PaymentMethod.Cash,
            Status = PaymentStatus.Partial, BalanceAfter = 75_000m, ReceiptNumber = "REC-2025-0003",
            ReceivedByUserId = Caissier, PaidAt = new DateTimeOffset(2026, 2, 20, 9, 0, 0, TimeSpan.Zero)
        });
        owner.Payments.Add(new Payment
        {
            SchoolId = EcoleA, EnrollmentId = Inscription, Amount = 99_999m, Method = PaymentMethod.Cash,
            Status = PaymentStatus.Cancelled, BalanceAfter = 100_000m, ReceiptNumber = "REC-2025-0004",
            ReceivedByUserId = Caissier, PaidAt = new DateTimeOffset(2026, 3, 12, 9, 0, 0, TimeSpan.Zero)
        });

        // École B — même période : ne doit JAMAIS apparaître dans le tableau de l'École A.
        owner.Classrooms.Add(new Classroom { Id = ClasseB, SchoolId = EcoleB, Name = "6e", Level = "Collège", Capacity = 45 });
        owner.SchoolYears.Add(new SchoolYear
        {
            Id = AnneeB, SchoolId = EcoleB, Label = "2025-2026",
            StartDate = new DateOnly(2025, 10, 1), EndDate = new DateOnly(2026, 6, 30), IsActive = true
        });
        owner.Students.Add(new Student
        {
            Id = EleveB, SchoolId = EcoleB, Matricule = "ELEV-B-0001",
            FullName = "Moussa Diop", BirthDate = new DateOnly(2013, 2, 10), BirthPlace = "Thiès", Gender = "M", ClassroomId = ClasseB
        });
        owner.Enrollments.Add(new Enrollment
        {
            Id = InscriptionB, SchoolId = EcoleB, StudentId = EleveB, SchoolYearId = AnneeB, ClassroomId = ClasseB,
            Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed,
            TotalDue = 500_000m, AmountPaid = 500_000m, ReceiptNumber = "REC-B-0000",
            EnrolledAt = DateTimeOffset.UtcNow
        });
        owner.Payments.Add(new Payment
        {
            SchoolId = EcoleB, EnrollmentId = InscriptionB, Amount = 500_000m, Method = PaymentMethod.MobileMoney,
            Status = PaymentStatus.Paid, BalanceAfter = 0m, ReceiptNumber = "REC-B-0001",
            ReceivedByUserId = Caissier, PaidAt = new DateTimeOffset(2026, 3, 15, 9, 0, 0, TimeSpan.Zero)
        });
        owner.Disbursements.Add(new Disbursement
        {
            SchoolId = EcoleB, Reason = "Salaires École B", Category = DisbursementCategory.Salaires,
            Amount = 300_000m, PaymentMethod = PaymentMethod.Transfer, Date = new DateOnly(2026, 3, 5), Beneficiary = "Personnel"
        });

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task Total_Collected_Includes_Partial_Payments_Not_Only_Fully_Paid_Enrollments()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new GetTreasuryDashboardQueryHandler(ctx);

        var result = await handler.Handle(new GetTreasuryDashboardQuery(PeriodStart, PeriodEnd), CancellationToken.None);

        result.TotalCollected.Should().Be(40_000m,
            "le versement partiel de mars est un encaissement réel, Status=Partial ne veut pas dire « argent non reçu »");
    }

    [Fact]
    public async Task Cancelled_And_Out_Of_Period_Payments_Are_Excluded()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new GetTreasuryDashboardQueryHandler(ctx);

        var result = await handler.Handle(new GetTreasuryDashboardQuery(PeriodStart, PeriodEnd), CancellationToken.None);

        result.TotalCollected.Should().NotBe(40_000m + 25_000m + 99_999m,
            "le paiement de février et le paiement annulé ne doivent pas être comptés");
        result.RecentTransactions.Should().NotContain(t => t.Amount == 99_999m, "un paiement annulé n'est pas une transaction de trésorerie");
    }

    [Fact]
    public async Task Net_Position_Is_Collected_Minus_Disbursed()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new GetTreasuryDashboardQueryHandler(ctx);

        var result = await handler.Handle(new GetTreasuryDashboardQuery(PeriodStart, PeriodEnd), CancellationToken.None);

        result.TotalDisbursed.Should().Be(10_000m);
        result.NetPosition.Should().Be(30_000m, "40 000 encaissés − 10 000 décaissés");
    }

    [Fact]
    public async Task Outstanding_Debt_Reflects_The_Current_Enrollment_Balance()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new GetTreasuryDashboardQueryHandler(ctx);

        var result = await handler.Handle(new GetTreasuryDashboardQuery(PeriodStart, PeriodEnd), CancellationToken.None);

        result.TotalOutstandingDebt.Should().Be(60_000m, "TotalDue 100 000 − AmountPaid 40 000, indépendant du filtre de période");
    }

    [Fact]
    public async Task Other_School_Movements_Never_Leak_Into_The_Dashboard()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new GetTreasuryDashboardQueryHandler(ctx);

        var result = await handler.Handle(new GetTreasuryDashboardQuery(PeriodStart, PeriodEnd), CancellationToken.None);

        result.TotalCollected.Should().NotBe(40_000m + 500_000m, "le paiement de l'École B ne doit jamais compter dans le total de l'École A");
        result.TotalDisbursed.Should().NotBe(10_000m + 300_000m, "le décaissement de l'École B ne doit jamais compter dans le total de l'École A");
    }

    [Fact]
    public async Task Default_Period_Falls_Back_To_The_Current_Month_When_No_Dates_Are_Given()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new GetTreasuryDashboardQueryHandler(ctx);

        var result = await handler.Handle(new GetTreasuryDashboardQuery(), CancellationToken.None);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        result.StartDate.Should().Be(new DateOnly(today.Year, today.Month, 1));
        result.EndDate.Should().Be(today);
    }
}
