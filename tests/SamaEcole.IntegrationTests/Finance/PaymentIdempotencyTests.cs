using FluentAssertions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Commands.RecordPayment;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace SamaEcole.IntegrationTests.Finance;

/// <summary>
/// Ticket JGK-L01 — résilience réseau côté caisse, dans le cadre strict de D-01 (Volume_0 §0.13) :
/// aucune base locale, aucune file de mutations, la seule protection contre un retry après coupure
/// est cette clé côté serveur. Ces tests prouvent qu'un retry avec la MÊME clé ne crée jamais un
/// second encaissement, et rejoue le résultat du premier (le caissier récupère son reçu).
/// </summary>
[Trait("Category", "MultiTenant")]
public class PaymentIdempotencyTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("11111111-5555-1111-1111-111111111111");
    private static readonly Guid Classe = Guid.Parse("aaaaaaaa-5555-0000-0000-00000000000a");
    private static readonly Guid Annee = Guid.Parse("bbbbbbbb-5555-0000-0000-00000000000b");
    private static readonly Guid Eleve = Guid.Parse("eeeeeeee-5555-0000-0000-00000000000e");
    private static readonly Guid Inscription = Guid.Parse("11111111-5555-0000-0000-0000000000f1");
    private static readonly Guid Caissier = Guid.Parse("dddddddd-5555-0000-0000-00000000000d");
    private static readonly Guid SessionCaisse = Guid.Parse("cccccccc-5555-0000-0000-00000000000c");

    private const decimal TotalDue = 100_000m;

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
            Id = Eleve, SchoolId = Ecole, Matricule = "ELEV-2026-0001",
            FullName = "Awa Fall", BirthDate = new DateOnly(2015, 5, 20), BirthPlace = "Dakar", Gender = "F", ClassroomId = Classe
        });
        owner.Enrollments.Add(new Enrollment
        {
            Id = Inscription, SchoolId = Ecole, StudentId = Eleve, SchoolYearId = Annee, ClassroomId = Classe,
            Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed,
            TotalDue = TotalDue, AmountPaid = 0m, ReceiptNumber = "REC-2026-0001",
            EnrolledAt = DateTimeOffset.UtcNow
        });
        owner.Users.Add(new User
        {
            Id = Caissier, SchoolId = Ecole, Email = "caissier.a@ecole-a.sn", PasswordHash = "hash",
            FullName = "Caissier A", Role = Role.Finance
        });
        owner.CashierSessions.Add(new CashierSession
        {
            Id = SessionCaisse, SchoolId = Ecole, CashierId = Caissier,
            Status = CashierSessionStatus.Open, OpenedAt = DateTimeOffset.UtcNow, OpeningBalance = 0m
        });

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private RecordPaymentCommandHandler NewHandler(IApplicationDbContext ctx) => new(
        ctx, new FixedTenantProvider(Ecole), new FixedCurrentUser(Caissier),
        new SamaEcole.Persistence.MatriculeGenerator((SamaEcole.Persistence.ApplicationDbContext)ctx, TimeProvider.System),
        new FakeSmsDispatcher(), TimeProvider.System, new FakeKpiCacheService());

    [Fact]
    public async Task Retrying_With_The_Same_Idempotency_Key_Does_Not_Create_A_Duplicate()
    {
        var idempotencyKey = Guid.NewGuid();

        await using var firstCtx = _db.NewAppContext(Ecole);
        var firstResult = await NewHandler(firstCtx).Handle(
            new RecordPaymentCommand(Inscription, 30_000m, PaymentMethod.Cash, IdempotencyKey: idempotencyKey),
            CancellationToken.None);

        // Simule un retry après coupure réseau : même clé, nouvelle requête, nouveau contexte (comme
        // le ferait un second appel HTTP réel).
        await using var retryCtx = _db.NewAppContext(Ecole);
        var retryResult = await NewHandler(retryCtx).Handle(
            new RecordPaymentCommand(Inscription, 30_000m, PaymentMethod.Cash, IdempotencyKey: idempotencyKey),
            CancellationToken.None);

        retryResult.PaymentId.Should().Be(firstResult.PaymentId, "le retry doit rejouer le MÊME paiement, pas en créer un second");
        retryResult.ReceiptNumber.Should().Be(firstResult.ReceiptNumber, "le caissier doit récupérer le reçu déjà émis");

        await using var check = _db.NewAppContext(Ecole);
        (await check.Payments.CountAsync(p => p.EnrollmentId == Inscription)).Should().Be(1, "un seul encaissement, malgré les deux appels");
        (await check.Enrollments.Where(e => e.Id == Inscription).Select(e => e.AmountPaid).FirstAsync())
            .Should().Be(30_000m, "le solde ne doit pas être débité deux fois par le même paiement");
    }

    [Fact]
    public async Task Two_Payments_With_Different_Idempotency_Keys_Are_Both_Recorded()
    {
        await using var ctx1 = _db.NewAppContext(Ecole);
        await NewHandler(ctx1).Handle(
            new RecordPaymentCommand(Inscription, 20_000m, PaymentMethod.Cash, IdempotencyKey: Guid.NewGuid()),
            CancellationToken.None);

        await using var ctx2 = _db.NewAppContext(Ecole);
        await NewHandler(ctx2).Handle(
            new RecordPaymentCommand(Inscription, 15_000m, PaymentMethod.Cash, IdempotencyKey: Guid.NewGuid()),
            CancellationToken.None);

        await using var check = _db.NewAppContext(Ecole);
        (await check.Payments.CountAsync(p => p.EnrollmentId == Inscription)).Should().Be(2, "deux clés distinctes, deux versements réels");
        (await check.Enrollments.Where(e => e.Id == Inscription).Select(e => e.AmountPaid).FirstAsync())
            .Should().Be(35_000m);
    }

    [Fact]
    public async Task A_Payment_Without_An_Idempotency_Key_Still_Works()
    {
        // Rétrocompatibilité : la clé est optionnelle, un appel qui n'en fournit pas (client non
        // encore mis à jour) doit continuer à fonctionner exactement comme avant ce ticket.
        await using var ctx = _db.NewAppContext(Ecole);

        var act = async () => await NewHandler(ctx).Handle(
            new RecordPaymentCommand(Inscription, 10_000m, PaymentMethod.Cash), CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}

file sealed class FixedTenantProvider(Guid schoolId) : ITenantProvider
{
    public Guid? CurrentSchoolId => schoolId;
}

file sealed class FixedCurrentUser(Guid userId) : ICurrentUserService
{
    public Guid? UserId => userId;
    public Role? Role => SamaEcole.Domain.Enums.Role.Finance;
    public string? IpAddress => null;
}

file sealed class FakeSmsDispatcher : ISmsDispatcher
{
    public Task<SmsDispatchOutcome> DispatchAsync(SmsDispatchRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(SmsDispatchOutcome.Skipped("Désactivé dans ce test."));
}

file sealed class FakeKpiCacheService : IKpiCacheService
{
    public Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken) =>
        factory(cancellationToken);

    public void Invalidate(string key) { }
}
