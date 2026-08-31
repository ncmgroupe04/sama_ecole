using FluentAssertions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Commands.RecordPayment;
using SamaEcole.Application.Finance.Queries.GetCaisseLookup;
using SamaEcole.Domain.Common;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using SamaEcole.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace SamaEcole.IntegrationTests.Finance;

/// <summary>
/// Modèle hybride inscription/caisse (volet 2). On exerce, contre un PostgreSQL réel sous le rôle
/// applicatif (RLS active) :
///   * <see cref="GetCaisseLookupQueryHandler"/> — la Caisse repère-t-elle une inscription en attente
///     de règlement (statut PendingPayment ou solde non nul) et refuse-t-elle de voir celle d'une
///     autre école ?
///   * <see cref="RecordPaymentCommandHandler"/> — le PREMIER encaissement, même partiel, fait-il
///     passer l'inscription de PendingPayment à Confirmed en lui attribuant son vrai numéro de reçu
///     officiel (jusque-là un jeton « EN-ATTENTE-… ») ?
/// </summary>
[Trait("Category", "MultiTenant")]
public class PendingEnrollmentRecoveryTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("11111111-9999-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("22222222-9999-2222-2222-222222222222");
    private static readonly Guid Classe = Guid.Parse("aaaaaaaa-9999-0000-0000-00000000000a");
    private static readonly Guid Annee = Guid.Parse("bbbbbbbb-9999-0000-0000-00000000000b");
    private static readonly Guid Eleve = Guid.Parse("eeeeeeee-9999-0000-0000-00000000000e");
    private static readonly Guid EleveSansDette = Guid.Parse("eeeeeeee-9999-0000-0000-00000000000f");
    private static readonly Guid Inscription = Guid.Parse("11111111-9999-0000-0000-0000000000f1");
    private static readonly Guid Caissier = Guid.Parse("dddddddd-9999-0000-0000-00000000000d");
    private static readonly Guid SessionCaisse = Guid.Parse("cccccccc-9999-0000-0000-00000000000c");
    private static readonly Guid CatInscription = Guid.Parse("aaaaaaaa-9999-0000-0000-0000000000c1");
    private static readonly Guid CatMensualite = Guid.Parse("aaaaaaaa-9999-0000-0000-0000000000c2");

    private const string Matricule = "ELEV-2026-0042";
    private const decimal Inscr = 25_000m;
    private const decimal Mensualite = 20_000m;
    private const int Mois = 3;
    private static readonly decimal TotalDue = Inscr + Mensualite * Mois; // 85 000

    // Le millésime du numéro de reçu vient du générateur (date du jour), pas de l'année scolaire seed.
    private static readonly int ReceiptYear = AcademicYear.ForDate(DateTimeOffset.UtcNow);

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
            Id = Annee, SchoolId = EcoleA, Label = "2026-2027",
            StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true
        });
        owner.Students.AddRange(
            new Student
            {
                Id = Eleve, SchoolId = EcoleA, Matricule = Matricule,
                FullName = "Mamadou Diop", BirthDate = new DateOnly(2015, 5, 20),
                BirthPlace = "Dakar", Gender = "M", ClassroomId = Classe
            },
            new Student
            {
                Id = EleveSansDette, SchoolId = EcoleA, Matricule = "ELEV-2026-0043",
                FullName = "Awa Fall", BirthDate = new DateOnly(2015, 2, 2),
                BirthPlace = "Dakar", Gender = "F", ClassroomId = Classe
            });

        owner.FeeCategories.AddRange(
            new FeeCategory { Id = CatInscription, SchoolId = EcoleA, Name = "Droit d'inscription", IsRecurring = false },
            new FeeCategory { Id = CatMensualite, SchoolId = EcoleA, Name = "Mensualité", IsRecurring = true });

        // Inscription ENGAGÉE au secrétariat, jamais réglée : statut PendingPayment, aucun Payment,
        // jeton de reçu provisoire (comme le pose CreateEnrollmentCommandHandler).
        owner.Enrollments.Add(new Enrollment
        {
            Id = Inscription, SchoolId = EcoleA, StudentId = Eleve, SchoolYearId = Annee, ClassroomId = Classe,
            Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.PendingPayment,
            TotalDue = TotalDue, AmountPaid = 0m, ReceiptNumber = $"EN-ATTENTE-{Inscription:N}",
            EnrolledAt = DateTimeOffset.UtcNow
        });
        owner.EnrollmentFeeLines.AddRange(
            new EnrollmentFeeLine
            {
                SchoolId = EcoleA, EnrollmentId = Inscription, FeeCategoryId = CatInscription,
                Designation = "Droit d'inscription", IsRecurring = false,
                UnitAmount = Inscr, Months = 1, LineTotal = Inscr
            },
            new EnrollmentFeeLine
            {
                SchoolId = EcoleA, EnrollmentId = Inscription, FeeCategoryId = CatMensualite,
                Designation = "Mensualité", IsRecurring = true,
                UnitAmount = Mensualite, Months = Mois, LineTotal = Mensualite * Mois
            });

        owner.Users.Add(new User
        {
            Id = Caissier, SchoolId = EcoleA, Email = "caissier.a@ecole-a.sn", PasswordHash = "hash",
            FullName = "Caissier A", Role = Role.Finance
        });
        owner.CashierSessions.Add(new CashierSession
        {
            Id = SessionCaisse, SchoolId = EcoleA, CashierId = Caissier,
            Status = CashierSessionStatus.Open, OpenedAt = DateTimeOffset.UtcNow, OpeningBalance = 0m
        });

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private RecordPaymentCommandHandler NewPaymentHandler(ApplicationDbContext ctx) => new(
        ctx, new RecoveryTenantProvider(EcoleA), new RecoveryCurrentUser(Caissier),
        _db.NewGenerator(ctx),
        new RecoverySmsDispatcher(), TimeProvider.System, new RecoveryKpiCache());

    [Fact]
    public async Task The_Caisse_Lookup_Flags_A_Pending_Enrollment_By_Student_Id()
    {
        await using var ctx = _db.NewAppContext(EcoleA);

        var result = await new GetCaisseLookupQueryHandler(ctx)
            .Handle(new GetCaisseLookupQuery(Eleve.ToString()), CancellationToken.None);

        result.HasPendingEnrollment.Should().BeTrue();
        result.EnrollmentId.Should().Be(Inscription);
        result.Status.Should().Be(nameof(EnrollmentStatus.PendingPayment));
        result.TotalDue.Should().Be(TotalDue);
        result.AmountPaid.Should().Be(0m);
        result.BalanceRemaining.Should().Be(TotalDue);
        result.Breakdown.Should().HaveCount(2);
        result.Breakdown.Sum(l => l.Amount).Should().Be(TotalDue);
    }

    [Fact]
    public async Task The_Caisse_Lookup_Also_Resolves_By_Exact_Matricule()
    {
        await using var ctx = _db.NewAppContext(EcoleA);

        var result = await new GetCaisseLookupQueryHandler(ctx)
            .Handle(new GetCaisseLookupQuery(Matricule), CancellationToken.None);

        result.StudentId.Should().Be(Eleve);
        result.HasPendingEnrollment.Should().BeTrue();
    }

    [Fact]
    public async Task The_Caisse_Lookup_Reports_No_Debt_For_A_Student_Without_An_Active_Enrollment()
    {
        await using var ctx = _db.NewAppContext(EcoleA);

        var result = await new GetCaisseLookupQueryHandler(ctx)
            .Handle(new GetCaisseLookupQuery(EleveSansDette.ToString()), CancellationToken.None);

        result.StudentId.Should().Be(EleveSansDette);
        result.HasPendingEnrollment.Should().BeFalse("aucune inscription active — rien à recouvrer");
        result.EnrollmentId.Should().BeNull();
        result.Breakdown.Should().BeEmpty();
    }

    [Fact]
    public async Task The_Caisse_Lookup_Cannot_Reach_A_Pending_Enrollment_Of_Another_School()
    {
        // Session tenant = École B : l'élève d'École A est structurellement invisible (RLS + filtre).
        await using var ctx = _db.NewAppContext(EcoleB);

        var act = async () => await new GetCaisseLookupQueryHandler(ctx)
            .Handle(new GetCaisseLookupQuery(Eleve.ToString()), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task The_First_Payment_Confirms_The_Enrollment_And_Assigns_The_Official_Receipt_Number()
    {
        // Versement PARTIEL sur l'inscription PendingPayment.
        await using var ctx = _db.NewAppContext(EcoleA);
        var result = await NewPaymentHandler(ctx).Handle(
            new RecordPaymentCommand(Inscription, 30_000m, PaymentMethod.Cash, PaymentCategory.Enrollment),
            CancellationToken.None);

        result.ReceiptNumber.Should().Be($"REC-{ReceiptYear}-0001", "le premier encaissement consomme le premier numéro officiel");

        await using var check = _db.NewAppContext(EcoleA);
        var enrollment = await check.Enrollments.SingleAsync(e => e.Id == Inscription);

        enrollment.Status.Should().Be(EnrollmentStatus.Confirmed, "un versement, même partiel, confirme l'inscription");
        enrollment.AmountPaid.Should().Be(30_000m);
        enrollment.ReceiptNumber.Should().Be(result.ReceiptNumber,
            "le jeton provisoire cède la place au numéro officiel du premier versement — une seule pièce, un seul numéro");
        enrollment.ReceiptNumber.Should().NotStartWith("EN-ATTENTE-");

        var payment = await check.Payments.SingleAsync(p => p.EnrollmentId == Inscription);
        payment.ReceiptNumber.Should().Be(enrollment.ReceiptNumber);
        payment.Status.Should().Be(PaymentStatus.Partial);
    }

    [Fact]
    public async Task A_Second_Payment_Does_Not_Re_Rewrite_The_Enrollment_Receipt_Number()
    {
        await using (var ctx1 = _db.NewAppContext(EcoleA))
        {
            await NewPaymentHandler(ctx1).Handle(
                new RecordPaymentCommand(Inscription, 30_000m, PaymentMethod.Cash, PaymentCategory.Enrollment),
                CancellationToken.None);
        }

        string officialNumber;
        await using (var check1 = _db.NewAppContext(EcoleA))
        {
            officialNumber = await check1.Enrollments.Where(e => e.Id == Inscription)
                .Select(e => e.ReceiptNumber).SingleAsync();
        }

        await using (var ctx2 = _db.NewAppContext(EcoleA))
        {
            await NewPaymentHandler(ctx2).Handle(
                new RecordPaymentCommand(Inscription, 55_000m, PaymentMethod.Cash, PaymentCategory.Enrollment),
                CancellationToken.None);
        }

        await using var check2 = _db.NewAppContext(EcoleA);
        var enrollment = await check2.Enrollments.SingleAsync(e => e.Id == Inscription);

        enrollment.ReceiptNumber.Should().Be(officialNumber, "le numéro d'inscription est figé au premier versement");
        enrollment.AmountPaid.Should().Be(TotalDue);
        enrollment.Status.Should().Be(EnrollmentStatus.Confirmed);
        (await check2.Payments.CountAsync(p => p.EnrollmentId == Inscription)).Should().Be(2);
    }
}

file sealed class RecoveryTenantProvider(Guid schoolId) : ITenantProvider
{
    public Guid? CurrentSchoolId => schoolId;
}

file sealed class RecoveryCurrentUser(Guid userId) : ICurrentUserService
{
    public Guid? UserId => userId;
    public Role? Role => SamaEcole.Domain.Enums.Role.Finance;
    public string? IpAddress => null;
}

file sealed class RecoverySmsDispatcher : ISmsDispatcher
{
    public Task<SmsDispatchOutcome> DispatchAsync(SmsDispatchRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(SmsDispatchOutcome.Skipped("Désactivé dans ce test."));
}

file sealed class RecoveryKpiCache : IKpiCacheService
{
    public Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken) =>
        factory(cancellationToken);

    public void Invalidate(string key) { }
}
