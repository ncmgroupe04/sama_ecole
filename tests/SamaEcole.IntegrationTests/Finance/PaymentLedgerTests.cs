using FluentAssertions;
using SamaEcole.Application.Finance;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Finance;

/// <summary>
/// Registre daté des encaissements (spec 2026-10-17, §2.4) : une erreur de caisse se corrige sans réécrire le passé
/// ni faire disparaître la trace. Les versements sont insérés directement avec leurs dates (aucune horloge simulée).
/// </summary>
[Trait("Category", "MultiTenant")]
public class PaymentLedgerTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("11111111-7777-1111-1111-111111111111");
    private static readonly Guid Classe = Guid.Parse("aaaaaaaa-7777-0000-0000-00000000000a");
    private static readonly Guid Annee = Guid.Parse("bbbbbbbb-7777-0000-0000-00000000000b");
    private static readonly Guid Eleve = Guid.Parse("eeeeeeee-7777-0000-0000-00000000000e");
    private static readonly Guid Inscription = Guid.Parse("11111111-7777-0000-0000-0000000000f1");
    private static readonly Guid Caissier = Guid.Parse("dddddddd-7777-0000-0000-00000000000d");
    private static readonly Guid SessionLundi = Guid.Parse("cccccccc-7777-0000-0000-0000000000c1");
    private static readonly Guid SessionMardi = Guid.Parse("cccccccc-7777-0000-0000-0000000000c2");

    private static readonly DateTimeOffset Lundi = new(2026, 10, 12, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Mardi = Lundi.AddDays(1);
    private static readonly DateTimeOffset Mercredi = Lundi.AddDays(2);

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
        // Lundi : session clôturée en fin de journée. Mardi : session ouverte (celle où l'on annule).
        owner.CashierSessions.Add(new CashierSession
        {
            Id = SessionLundi, SchoolId = Ecole, CashierId = Caissier, Status = CashierSessionStatus.Closed,
            OpenedAt = Lundi.AddHours(8), ClosedAt = Lundi.AddHours(18), OpeningBalance = 0m
        });
        owner.CashierSessions.Add(new CashierSession
        {
            Id = SessionMardi, SchoolId = Ecole, CashierId = Caissier, Status = CashierSessionStatus.Open,
            OpenedAt = Mardi.AddHours(8), OpeningBalance = 0m
        });
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private async Task<Guid> AddPaymentAsync(
        string receipt, decimal amount, DateTimeOffset paidAt, Guid? sessionId,
        PaymentMethod method = PaymentMethod.Cash, decimal vat = 0m,
        DateTimeOffset? cancelledAt = null, Guid? cancellationSessionId = null,
        PaymentCancellationOutcome outcome = PaymentCancellationOutcome.KeptBySchool)
    {
        await using var owner = _db.NewOwnerContext();
        var payment = new Payment
        {
            SchoolId = Ecole, EnrollmentId = Inscription, CashierSessionId = sessionId, Amount = amount,
            VatAmount = vat, Method = method, Status = PaymentStatus.Partial, BalanceAfter = 0m,
            ReceiptNumber = receipt, ReceivedByUserId = Caissier, PaidAt = paidAt
        };
        if (cancelledAt is not null)
        {
            payment.Status = PaymentStatus.Cancelled;
            payment.CancelledAt = cancelledAt;
            payment.CancelledByUserId = Caissier;
            payment.CancellationReason = "Erreur de saisie";
            payment.CancellationOutcome = outcome;
            payment.CancellationSessionId = cancellationSessionId;
        }
        owner.Payments.Add(payment);
        await owner.SaveChangesAsync(CancellationToken.None);
        return payment.Id;
    }

    private async Task<List<PaymentLedgerEntry>> DayAsync(DateTimeOffset day)
    {
        await using var ctx = _db.NewAppContext(Ecole);
        return await PaymentLedger.EntriesAsync(ctx, day, day.AddDays(1), CancellationToken.None);
    }

    [Fact]
    public async Task A_Payment_Cancelled_The_Next_Day_Stays_In_Its_Original_Day_And_Is_Flagged()
    {
        var p = await AddPaymentAsync("REC-1", 50_000m, Lundi.AddHours(10), SessionLundi,
            cancelledAt: Mardi.AddHours(9), cancellationSessionId: SessionMardi);

        var lundi = await DayAsync(Lundi);

        var entry = lundi.Should().ContainSingle().Subject;
        entry.PaymentId.Should().Be(p);
        entry.Amount.Should().Be(50_000m, "les chiffres de lundi restent ceux de lundi");
        entry.IsReversal.Should().BeFalse();
        entry.CancelledLater.Should().BeTrue("le journal de lundi doit dire que ce paiement a été annulé depuis");
        entry.CancellationReason.Should().Be("Erreur de saisie");
    }

    [Fact]
    public async Task The_Cancellation_Is_A_Negative_Line_In_The_Day_It_Is_Made()
    {
        var p = await AddPaymentAsync("REC-1", 50_000m, Lundi.AddHours(10), SessionLundi,
            vat: 9_000m, cancelledAt: Mardi.AddHours(9), cancellationSessionId: SessionMardi);

        var mardi = await DayAsync(Mardi);

        var entry = mardi.Should().ContainSingle().Subject;
        entry.PaymentId.Should().Be(p);
        entry.IsReversal.Should().BeTrue();
        entry.Amount.Should().Be(-50_000m);
        entry.VatAmount.Should().Be(-9_000m, "la TVA collectée est reprise avec le versement annulé");
        entry.At.Should().Be(Mardi.AddHours(9));
    }

    [Fact]
    public async Task A_Payment_Cancelled_The_Same_Day_Disappears_Without_A_Reversal()
    {
        await AddPaymentAsync("REC-1", 50_000m, Lundi.AddHours(10), SessionLundi, cancelledAt: Lundi.AddHours(11));

        (await DayAsync(Lundi)).Should().BeEmpty("l'erreur et sa correction se compensent dans la même journée");
        (await DayAsync(Mardi)).Should().BeEmpty();
    }

    [Fact]
    public async Task Net_Total_Over_The_Two_Days_Is_Zero_Once_Cancelled_And_Each_Day_Is_Coherent()
    {
        await AddPaymentAsync("REC-1", 50_000m, Lundi.AddHours(10), SessionLundi,
            cancelledAt: Mardi.AddHours(9), cancellationSessionId: SessionMardi);

        await using var ctx = _db.NewAppContext(Ecole);
        var lundi = await PaymentLedger.NetAsync(ctx, Lundi, Mardi, p => p.Amount, CancellationToken.None);
        var mardi = await PaymentLedger.NetAsync(ctx, Mardi, Mercredi, p => p.Amount, CancellationToken.None);
        var deuxJours = await PaymentLedger.NetAsync(ctx, Lundi, Mercredi, p => p.Amount, CancellationToken.None);

        lundi.Should().Be(50_000m);
        mardi.Should().Be(-50_000m);
        deuxJours.Should().Be(0m, "sur toute la période, le paiement annulé ne compte plus — rien n'est compté deux fois");
    }

    [Fact]
    public async Task The_Closed_Monday_Session_Keeps_The_Payment_And_The_Open_Tuesday_Session_Carries_The_Reversal()
    {
        await AddPaymentAsync("REC-1", 50_000m, Lundi.AddHours(10), SessionLundi,
            cancelledAt: Mardi.AddHours(9), cancellationSessionId: SessionMardi);
        await AddPaymentAsync("REC-2", 20_000m, Mardi.AddHours(10), SessionMardi);

        await using var ctx = _db.NewAppContext(Ecole);
        var lundiSession = ctx.CashierSessions.Single(s => s.Id == SessionLundi);
        var mardiSession = ctx.CashierSessions.Single(s => s.Id == SessionMardi);

        var lundi = await PaymentLedger.SessionEntriesAsync(ctx, lundiSession, CancellationToken.None);
        lundi.Should().ContainSingle();
        lundi[0].Amount.Should().Be(50_000m);
        lundi[0].CancelledLater.Should().BeTrue();

        var mardi = await PaymentLedger.SessionEntriesAsync(ctx, mardiSession, CancellationToken.None);
        mardi.Should().HaveCount(2);
        mardi.Sum(e => e.Amount).Should().Be(20_000m - 50_000m);
        mardi.Single(e => e.IsReversal).ReceiptNumber.Should().Be("REC-1");
    }

    [Fact]
    public async Task Cancelling_A_Payment_Of_A_Session_Still_Open_Is_Not_Counted_Twice()
    {
        // Le versement de mardi, annulé mardi alors que SA session est encore ouverte : il sort de cette session,
        // et ne doit PAS réapparaître en négatif dans la session de l'auteur (qui est ici la même).
        await AddPaymentAsync("REC-2", 20_000m, Mardi.AddHours(10), SessionMardi,
            cancelledAt: Mardi.AddHours(11), cancellationSessionId: SessionMardi);

        await using var ctx = _db.NewAppContext(Ecole);
        var mardiSession = ctx.CashierSessions.Single(s => s.Id == SessionMardi);

        (await PaymentLedger.SessionEntriesAsync(ctx, mardiSession, CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_Cancellation_Without_An_Open_Session_Is_In_The_Day_Statements_But_In_No_Session()
    {
        await AddPaymentAsync("REC-1", 50_000m, Lundi.AddHours(10), SessionLundi,
            cancelledAt: Mardi.AddHours(9), cancellationSessionId: null);

        (await DayAsync(Mardi)).Should().ContainSingle(e => e.IsReversal);

        await using var ctx = _db.NewAppContext(Ecole);
        var mardiSession = ctx.CashierSessions.Single(s => s.Id == SessionMardi);
        (await PaymentLedger.SessionEntriesAsync(ctx, mardiSession, CancellationToken.None)).Should().BeEmpty();
    }
}
