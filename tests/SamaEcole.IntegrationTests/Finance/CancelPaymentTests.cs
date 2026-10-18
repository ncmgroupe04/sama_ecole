using FluentAssertions;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Commands.CancelPayment;
using SamaEcole.Application.Finance.Commands.RecordPayment;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace SamaEcole.IntegrationTests.Finance;

/// <summary>
/// Annulation d'un paiement saisi par erreur (spec 2026-10-17-deletion-dependency-guard-design.md §2.4).
/// Prérequis de la suppression d'un élève : un paiement annulé ne bloque plus, mais il n'est JAMAIS effacé
/// (AGENTS.md règle #6) et son numéro de reçu reste consommé (règle #3, aucun trou).
/// </summary>
[Trait("Category", "MultiTenant")]
public class CancelPaymentTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("11111111-6666-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("22222222-6666-1111-1111-111111111111");
    private static readonly Guid Classe = Guid.Parse("aaaaaaaa-6666-0000-0000-00000000000a");
    private static readonly Guid Annee = Guid.Parse("bbbbbbbb-6666-0000-0000-00000000000b");
    private static readonly Guid Eleve = Guid.Parse("eeeeeeee-6666-0000-0000-00000000000e");
    private static readonly Guid Inscription = Guid.Parse("11111111-6666-0000-0000-0000000000f1");
    private static readonly Guid Caissier = Guid.Parse("dddddddd-6666-0000-0000-00000000000d");
    private static readonly Guid Directeur = Guid.Parse("dddddddd-6666-0000-0000-0000000000d2");
    private static readonly Guid SessionCaisse = Guid.Parse("cccccccc-6666-0000-0000-00000000000c");

    private const decimal TotalDue = 100_000m;

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();

        owner.Schools.Add(new School { Id = EcoleA, Name = "École A" });
        owner.Schools.Add(new School { Id = EcoleB, Name = "École B" });
        owner.Classrooms.Add(new Classroom { Id = Classe, SchoolId = EcoleA, Name = "CM2", Level = "Primaire", Capacity = 40 });
        owner.SchoolYears.Add(new SchoolYear
        {
            Id = Annee, SchoolId = EcoleA, Label = "2026-2027",
            StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true
        });
        owner.Students.Add(new Student
        {
            Id = Eleve, SchoolId = EcoleA, Matricule = "ELEV-2026-0001",
            FullName = "Awa Fall", BirthDate = new DateOnly(2015, 5, 20), BirthPlace = "Dakar", Gender = "F", ClassroomId = Classe
        });
        owner.Enrollments.Add(new Enrollment
        {
            Id = Inscription, SchoolId = EcoleA, StudentId = Eleve, SchoolYearId = Annee, ClassroomId = Classe,
            Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed,
            TotalDue = TotalDue, AmountPaid = 0m, ReceiptNumber = "REC-2026-0001",
            EnrolledAt = DateTimeOffset.UtcNow
        });
        owner.Users.Add(new User
        {
            Id = Caissier, SchoolId = EcoleA, Email = "caissier.a@ecole-a.sn", PasswordHash = "hash",
            FullName = "Caissier A", Role = Role.Finance
        });
        owner.Users.Add(new User
        {
            Id = Directeur, SchoolId = EcoleA, Email = "directeur.a@ecole-a.sn", PasswordHash = "hash",
            FullName = "Directeur A", Role = Role.Directeur
        });
        owner.CashierSessions.Add(new CashierSession
        {
            Id = SessionCaisse, SchoolId = EcoleA, CashierId = Caissier,
            Status = CashierSessionStatus.Open, OpenedAt = DateTimeOffset.UtcNow, OpeningBalance = 0m
        });

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private async Task<RecordPaymentResult> RecordAsync(decimal amount)
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new RecordPaymentCommandHandler(
            ctx, new FixedTenant(EcoleA), new FixedUser(Caissier),
            new SamaEcole.Persistence.MatriculeGenerator((SamaEcole.Persistence.ApplicationDbContext)ctx, TimeProvider.System),
            new NoSms(), TimeProvider.System, new NoKpiCache());

        return await handler.Handle(
            new RecordPaymentCommand(Inscription, amount, PaymentMethod.Cash), CancellationToken.None);
    }

    private static CancelPaymentCommandHandler NewHandler(IApplicationDbContext ctx, Guid actor) =>
        new(ctx, new FixedUser(actor), TimeProvider.System, new NoKpiCache());

    [Fact]
    public async Task Cancelling_A_Payment_Keeps_The_Row_Marks_It_Cancelled_And_Gives_The_Amount_Back_To_The_Balance()
    {
        var payment = await RecordAsync(30_000m);

        await using var ctx = _db.NewAppContext(EcoleA);
        var result = await NewHandler(ctx, Directeur).Handle(
            new CancelPaymentCommand(payment.PaymentId, "Saisi sur le mauvais élève"), CancellationToken.None);

        result.ReceiptNumber.Should().Be(payment.ReceiptNumber, "le numéro de reçu reste consommé, aucun trou dans le carnet");
        result.AmountPaid.Should().Be(0m);
        result.RemainingBalance.Should().Be(TotalDue);

        await using var check = _db.NewAppContext(EcoleA);
        var stored = await check.Payments.AsNoTracking().SingleAsync(p => p.Id == payment.PaymentId);
        stored.Status.Should().Be(PaymentStatus.Cancelled);
        stored.CancellationReason.Should().Be("Saisi sur le mauvais élève");
        stored.CancelledByUserId.Should().Be(Directeur);
        stored.CancelledAt.Should().NotBeNull();
        stored.Amount.Should().Be(30_000m, "le montant d'origine n'est jamais réécrit");
        stored.ReceiptNumber.Should().Be(payment.ReceiptNumber);

        (await check.Enrollments.Where(e => e.Id == Inscription).Select(e => e.AmountPaid).FirstAsync())
            .Should().Be(0m);
    }

    [Fact]
    public async Task The_Destination_Of_The_Money_And_The_Cancellers_Open_Session_Are_Recorded()
    {
        var payment = await RecordAsync(30_000m);

        // Le caissier (qui a une session ouverte) annule : son annulation est comptée dans SA session.
        await using var ctx = _db.NewAppContext(EcoleA);
        await NewHandler(ctx, Caissier).Handle(
            new CancelPaymentCommand(payment.PaymentId, "Parent venu reprendre son argent", PaymentCancellationOutcome.RefundedToParent),
            CancellationToken.None);

        await using var check = _db.NewAppContext(EcoleA);
        var stored = await check.Payments.AsNoTracking().SingleAsync(p => p.Id == payment.PaymentId);
        stored.CancellationOutcome.Should().Be(PaymentCancellationOutcome.RefundedToParent);
        stored.CancellationSessionId.Should().Be(SessionCaisse);
    }

    [Fact]
    public async Task The_Default_Destination_Is_A_Simple_Correction_And_Without_An_Open_Session_None_Is_Recorded()
    {
        var payment = await RecordAsync(30_000m);

        // Le Directeur n'a pas de session de caisse ouverte : l'annulation figure dans les états par date seulement.
        await using var ctx = _db.NewAppContext(EcoleA);
        await NewHandler(ctx, Directeur).Handle(
            new CancelPaymentCommand(payment.PaymentId, "Erreur de saisie"), CancellationToken.None);

        await using var check = _db.NewAppContext(EcoleA);
        var stored = await check.Payments.AsNoTracking().SingleAsync(p => p.Id == payment.PaymentId);
        stored.CancellationOutcome.Should().Be(PaymentCancellationOutcome.KeptBySchool);
        stored.CancellationSessionId.Should().BeNull();
    }

    [Fact]
    public async Task Only_The_Cancelled_Payment_Is_Removed_From_The_Cumulative_Amount()
    {
        var first = await RecordAsync(30_000m);
        await RecordAsync(20_000m);

        await using var ctx = _db.NewAppContext(EcoleA);
        var result = await NewHandler(ctx, Directeur).Handle(
            new CancelPaymentCommand(first.PaymentId, "Doublon"), CancellationToken.None);

        result.AmountPaid.Should().Be(20_000m);
        result.RemainingBalance.Should().Be(80_000m);
    }

    [Fact]
    public async Task The_Amount_Can_Be_Collected_Again_After_A_Cancellation()
    {
        var payment = await RecordAsync(100_000m);

        await using (var ctx = _db.NewAppContext(EcoleA))
        {
            await NewHandler(ctx, Directeur).Handle(
                new CancelPaymentCommand(payment.PaymentId, "Erreur de montant"), CancellationToken.None);
        }

        var act = async () => await RecordAsync(100_000m);

        await act.Should().NotThrowAsync("l'inscription n'est plus soldée une fois le paiement annulé");
    }

    [Fact]
    public async Task Cancelling_Twice_Is_Refused()
    {
        var payment = await RecordAsync(30_000m);

        await using (var ctx = _db.NewAppContext(EcoleA))
        {
            await NewHandler(ctx, Directeur).Handle(
                new CancelPaymentCommand(payment.PaymentId, "Erreur"), CancellationToken.None);
        }

        await using var again = _db.NewAppContext(EcoleA);
        var act = async () => await NewHandler(again, Directeur).Handle(
            new CancelPaymentCommand(payment.PaymentId, "Encore"), CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleException>();

        await using var check = _db.NewAppContext(EcoleA);
        (await check.Enrollments.Where(e => e.Id == Inscription).Select(e => e.AmountPaid).FirstAsync())
            .Should().Be(0m, "un second appel ne doit pas retirer le montant une deuxième fois");
    }

    // Le contexte propriétaire n'a pas de tenant : sans IgnoreQueryFilters, le filtre global (SchoolId)
    // exclurait la ligne et la mise à jour toucherait zéro ligne sans un bruit.
    private async Task CloseCashierSessionAsync()
    {
        await using var owner = _db.NewOwnerContext();
        var updated = await owner.CashierSessions.IgnoreQueryFilters().Where(s => s.Id == SessionCaisse)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, CashierSessionStatus.Closed));
        updated.Should().Be(1, "la session de caisse doit réellement être clôturée avant l'appel");
    }

    private async Task MovePaidAtBackAsync(Guid paymentId, TimeSpan age)
    {
        await using var owner = _db.NewOwnerContext();
        var updated = await owner.Payments.IgnoreQueryFilters().Where(p => p.Id == paymentId)
            .ExecuteUpdateAsync(p => p.SetProperty(x => x.PaidAt, DateTimeOffset.UtcNow - age));
        updated.Should().Be(1);
    }

    [Fact]
    public async Task A_Payment_Of_A_Closed_Cashier_Session_Can_Still_Be_Cancelled_Within_The_Grace_Period()
    {
        var payment = await RecordAsync(30_000m);
        await CloseCashierSessionAsync();
        // Erreur remarquée le lendemain : 30 h après l'encaissement, caisse déjà clôturée.
        await MovePaidAtBackAsync(payment.PaymentId, TimeSpan.FromHours(30));

        await using var ctx = _db.NewAppContext(EcoleA);
        var result = await NewHandler(ctx, Directeur).Handle(
            new CancelPaymentCommand(payment.PaymentId, "Découvert le lendemain"), CancellationToken.None);

        result.AmountPaid.Should().Be(0m);

        await using var check = _db.NewAppContext(EcoleA);
        (await check.Payments.AsNoTracking().SingleAsync(p => p.Id == payment.PaymentId)).Status
            .Should().Be(PaymentStatus.Cancelled);
    }

    [Fact]
    public async Task A_Payment_Older_Than_The_Grace_Period_Can_No_Longer_Be_Cancelled()
    {
        var payment = await RecordAsync(30_000m);
        await MovePaidAtBackAsync(payment.PaymentId, TimeSpan.FromHours(49));

        await using var ctx = _db.NewAppContext(EcoleA);
        var act = async () => await NewHandler(ctx, Directeur).Handle(
            new CancelPaymentCommand(payment.PaymentId, "Trop tard"), CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("*délai*");

        await using var check = _db.NewAppContext(EcoleA);
        (await check.Payments.AsNoTracking().SingleAsync(p => p.Id == payment.PaymentId)).Status
            .Should().NotBe(PaymentStatus.Cancelled);
        (await check.Enrollments.Where(e => e.Id == Inscription).Select(e => e.AmountPaid).FirstAsync())
            .Should().Be(30_000m, "un refus ne doit rien retirer du solde");
    }

    [Fact]
    public async Task A_Payment_Of_Another_School_Is_Not_Found()
    {
        var payment = await RecordAsync(30_000m);

        await using var ctx = _db.NewAppContext(EcoleB);
        var act = async () => await NewHandler(ctx, Guid.NewGuid()).Handle(
            new CancelPaymentCommand(payment.PaymentId, "Tentative inter-école"), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    private sealed class FixedTenant(Guid schoolId) : ITenantProvider
    {
        public Guid? CurrentSchoolId => schoolId;
    }

    private sealed class FixedUser(Guid userId) : ICurrentUserService
    {
        public Guid? UserId => userId;
        public Role? Role => SamaEcole.Domain.Enums.Role.Directeur;
        public string? IpAddress => null;
    }

    private sealed class NoSms : ISmsDispatcher
    {
        public Task<SmsDispatchOutcome> DispatchAsync(SmsDispatchRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(SmsDispatchOutcome.Skipped("Désactivé dans ce test."));
    }

    private sealed class NoKpiCache : IKpiCacheService
    {
        public Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken) =>
            factory(cancellationToken);

        public void Invalidate(string key) { }
    }
}
