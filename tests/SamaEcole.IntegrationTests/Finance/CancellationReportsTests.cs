using FluentAssertions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Commands.CloseCashierSession;
using SamaEcole.Application.Finance.Commands.GenerateTaxDeclaration;
using SamaEcole.Application.Finance.Queries.GetDailyClosingReportPdf;
using SamaEcole.Application.Finance.Queries.GetTreasuryDashboard;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Finance;

/// <summary>
/// Remèdes « caisse clôturée » (spec 2026-10-17, §2.4) : un versement encaissé le lundi, annulé le mardi.
///   • le journal de LUNDI garde le versement, signalé « annulé le … » — les chiffres signés ne bougent pas ;
///   • la session de MARDI porte une écriture négative ; seuls les espèces d'un REMBOURSEMENT quittent le tiroir ;
///   • la trésorerie et la TVA suivent le même registre daté.
/// </summary>
[Trait("Category", "MultiTenant")]
public class CancellationReportsTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("11111111-8888-1111-1111-111111111111");
    private static readonly Guid Classe = Guid.Parse("aaaaaaaa-8888-0000-0000-00000000000a");
    private static readonly Guid Annee = Guid.Parse("bbbbbbbb-8888-0000-0000-00000000000b");
    private static readonly Guid Eleve = Guid.Parse("eeeeeeee-8888-0000-0000-00000000000e");
    private static readonly Guid Inscription = Guid.Parse("11111111-8888-0000-0000-0000000000f1");
    private static readonly Guid Caissier = Guid.Parse("dddddddd-8888-0000-0000-00000000000d");
    private static readonly Guid SessionLundi = Guid.Parse("cccccccc-8888-0000-0000-0000000000c1");
    private static readonly Guid SessionMardi = Guid.Parse("cccccccc-8888-0000-0000-0000000000c2");

    // Octobre 2026 : lundi 12 (encaissement), mardi 13 (annulation).
    private static readonly DateTimeOffset Lundi = new(2026, 10, 12, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Mardi = Lundi.AddDays(1);

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
            TotalDue = 500_000m, AmountPaid = 0m, ReceiptNumber = "REC-2026-0001", EnrolledAt = Lundi
        });
        owner.Users.Add(new User
        {
            Id = Caissier, SchoolId = Ecole, Email = "c@ecole-a.sn", PasswordHash = "h", FullName = "Caissier", Role = Role.Finance
        });
        owner.CashierSessions.Add(new CashierSession
        {
            Id = SessionLundi, SchoolId = Ecole, CashierId = Caissier, Status = CashierSessionStatus.Closed,
            OpenedAt = Lundi.AddHours(8), ClosedAt = Lundi.AddHours(18), OpeningBalance = 0m
        });
        // Mardi : fonds de caisse de 100 000, session ouverte — c'est là que l'on annule.
        owner.CashierSessions.Add(new CashierSession
        {
            Id = SessionMardi, SchoolId = Ecole, CashierId = Caissier, Status = CashierSessionStatus.Open,
            OpenedAt = Mardi.AddHours(8), OpeningBalance = 100_000m
        });
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    /// <summary>Lundi : 50 000 en espèces (TVA 9 000), annulé mardi dans la session de mardi.</summary>
    private async Task SeedMondayPaymentCancelledOnTuesdayAsync(PaymentCancellationOutcome outcome)
    {
        await using var owner = _db.NewOwnerContext();
        owner.Payments.Add(new Payment
        {
            SchoolId = Ecole, EnrollmentId = Inscription, CashierSessionId = SessionLundi, Amount = 50_000m,
            VatAmount = 9_000m, Method = PaymentMethod.Cash, Status = PaymentStatus.Cancelled, BalanceAfter = 0m,
            ReceiptNumber = "REC-LUNDI", ReceivedByUserId = Caissier, PaidAt = Lundi.AddHours(10),
            CancelledAt = Mardi.AddHours(9), CancelledByUserId = Caissier, CancellationReason = "Mauvais élève",
            CancellationOutcome = outcome, CancellationSessionId = SessionMardi
        });
        // Mardi : un vrai encaissement de 20 000 en espèces.
        owner.Payments.Add(new Payment
        {
            SchoolId = Ecole, EnrollmentId = Inscription, CashierSessionId = SessionMardi, Amount = 20_000m,
            Method = PaymentMethod.Cash, Status = PaymentStatus.Partial, BalanceAfter = 0m,
            ReceiptNumber = "REC-MARDI", ReceivedByUserId = Caissier, PaidAt = Mardi.AddHours(10)
        });
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    private async Task<CloseCashierSessionResult> CloseTuesdayAsync(decimal actualCash)
    {
        await using var ctx = _db.NewAppContext(Ecole);
        var handler = new CloseCashierSessionCommandHandler(ctx, new FixedTenant(Ecole), TimeProvider.System);
        return await handler.Handle(new CloseCashierSessionCommand(SessionMardi, actualCash), CancellationToken.None);
    }

    [Fact]
    public async Task A_Simple_Correction_Lowers_The_Net_Total_But_Not_The_Expected_Cash()
    {
        await SeedMondayPaymentCancelledOnTuesdayAsync(PaymentCancellationOutcome.KeptBySchool);

        // Fonds 100 000 + encaissement de mardi 20 000 : le tiroir contient 120 000. L'annulation d'une erreur de
        // saisie de lundi n'a déplacé aucune espèce aujourd'hui.
        var result = await CloseTuesdayAsync(actualCash: 120_000m);

        result.TotalCollected.Should().Be(20_000m - 50_000m, "le total encaissé est net de l'annulation (écriture négative)");
        result.ExpectedCashAmount.Should().Be(120_000m, "aucune espèce ne quitte le tiroir pour une simple correction");
        result.DiscrepancyAmount.Should().Be(0m);
    }

    [Fact]
    public async Task A_Refund_To_The_Parent_Takes_The_Cash_Out_Of_The_Drawer()
    {
        await SeedMondayPaymentCancelledOnTuesdayAsync(PaymentCancellationOutcome.RefundedToParent);

        // 100 000 (fonds) + 20 000 (mardi) − 50 000 (remboursé au parent) = 70 000 attendus.
        var result = await CloseTuesdayAsync(actualCash: 70_000m);

        result.ExpectedCashAmount.Should().Be(70_000m);
        result.DiscrepancyAmount.Should().Be(0m);
    }

    [Fact]
    public async Task The_Monday_Closing_Report_Still_Shows_The_Payment_Flagged_As_Cancelled()
    {
        await SeedMondayPaymentCancelledOnTuesdayAsync(PaymentCancellationOutcome.KeptBySchool);

        var report = await BuildReportAsync(SessionLundi);

        report.TotalCollected.Should().Be(50_000m, "les chiffres signés lundi ne bougent pas");
        var row = report.Transactions.Should().ContainSingle().Subject;
        row.ReceiptNumber.Should().Be("REC-LUNDI");
        row.CancelledNote.Should().Contain("Annulé le 13/10/2026").And.Contain("Mauvais élève");
        report.Reversals.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task The_Tuesday_Closing_Report_Lists_The_Cancellation_As_A_Negative_Line()
    {
        await SeedMondayPaymentCancelledOnTuesdayAsync(PaymentCancellationOutcome.ReusedForEnrollment);

        var report = await BuildReportAsync(SessionMardi);

        report.TotalCollected.Should().Be(-30_000m);
        report.TotalReversed.Should().Be(50_000m);
        var reversal = report.Reversals.Should().ContainSingle().Subject;
        reversal.ReceiptNumber.Should().Be("REC-LUNDI");
        reversal.Amount.Should().Be(-50_000m);
        reversal.Note.Should().Contain("12/10/2026").And.Contain("réutilisé pour l'inscription");
        report.Transactions.Should().ContainSingle(t => t.ReceiptNumber == "REC-MARDI");
    }

    [Fact]
    public async Task The_Treasury_Of_October_Is_Net_And_Lists_The_Cancellation()
    {
        await SeedMondayPaymentCancelledOnTuesdayAsync(PaymentCancellationOutcome.KeptBySchool);

        await using var ctx = _db.NewAppContext(Ecole);
        var handler = new GetTreasuryDashboardQueryHandler(ctx);

        var october = await handler.Handle(
            new GetTreasuryDashboardQuery(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31)), CancellationToken.None);
        october.TotalCollected.Should().Be(20_000m, "50 000 encaissés puis annulés dans le même mois : il ne reste que mardi");

        // Lundi seul : le versement annulé le lendemain y reste compté, la trésorerie de lundi ne bouge pas.
        var monday = await handler.Handle(
            new GetTreasuryDashboardQuery(new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 12)), CancellationToken.None);
        monday.TotalCollected.Should().Be(50_000m);

        var tuesday = await handler.Handle(
            new GetTreasuryDashboardQuery(new DateOnly(2026, 10, 13), new DateOnly(2026, 10, 13)), CancellationToken.None);
        tuesday.TotalCollected.Should().Be(20_000m - 50_000m);
        tuesday.RecentTransactions.Should().Contain(t => t.Kind == "Annulation" && t.Amount == -50_000m);
    }

    [Fact]
    public async Task The_Vat_Collected_Takes_The_Cancellation_Back_In_The_Month_It_Is_Made()
    {
        // Annulé le mois SUIVANT : la TVA d'octobre (déjà déposée) ne change pas, novembre régularise en négatif.
        await using (var owner = _db.NewOwnerContext())
        {
            owner.Payments.Add(new Payment
            {
                SchoolId = Ecole, EnrollmentId = Inscription, CashierSessionId = SessionLundi, Amount = 50_000m,
                VatAmount = 9_000m, Method = PaymentMethod.Cash, Status = PaymentStatus.Cancelled, BalanceAfter = 0m,
                ReceiptNumber = "REC-OCT", ReceivedByUserId = Caissier, PaidAt = Lundi.AddHours(10),
                CancelledAt = new DateTimeOffset(2026, 11, 3, 9, 0, 0, TimeSpan.Zero), CancelledByUserId = Caissier,
                CancellationReason = "Erreur", CancellationOutcome = PaymentCancellationOutcome.KeptBySchool
            });
            await owner.SaveChangesAsync(CancellationToken.None);
        }

        decimal VatOf(Guid declarationId, IReadOnlyList<TaxeDeclaration> all) => all.Single(d => d.Id == declarationId).TvaCollected;

        await using var ctx = _db.NewAppContext(Ecole);
        var handler = new GenerateTaxDeclarationCommandHandler(ctx, new FixedTenant(Ecole));

        var october = await handler.Handle(new GenerateTaxDeclarationCommand(10, 2026), CancellationToken.None);
        var november = await handler.Handle(new GenerateTaxDeclarationCommand(11, 2026), CancellationToken.None);

        var declarations = ctx.TaxeDeclarations.ToList();
        VatOf(october, declarations).Should().Be(9_000m, "la déclaration d'octobre garde la TVA du versement annulé en novembre");
        VatOf(november, declarations).Should().Be(-9_000m, "novembre régularise : la TVA est reprise en négatif");
    }

    private async Task<DailyClosingReportDto> BuildReportAsync(Guid sessionId)
    {
        await using var ctx = _db.NewAppContext(Ecole);
        var generator = new CapturingGenerator();
        var handler = new GetDailyClosingReportPdfQueryHandler(ctx, new FixedTenant(Ecole), new NoLogo(), generator);

        await handler.Handle(new GetDailyClosingReportPdfQuery(sessionId), CancellationToken.None);

        return generator.Captured!;
    }

    private sealed class FixedTenant(Guid schoolId) : ITenantProvider
    {
        public Guid? CurrentSchoolId => schoolId;
    }

    private sealed class NoLogo : ISchoolLogoProvider
    {
        public Task<byte[]?> TryFetchAsync(string? logoUrl, CancellationToken cancellationToken) => Task.FromResult<byte[]?>(null);
    }

    private sealed class CapturingGenerator : IDailyClosingReportPdfGenerator
    {
        public DailyClosingReportDto? Captured { get; private set; }

        public byte[] Generate(DailyClosingReportDto report, byte[]? schoolLogo)
        {
            Captured = report;
            return [1];
        }
    }
}
