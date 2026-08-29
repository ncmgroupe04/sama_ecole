using FluentAssertions;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Commands.CloseCashierSession;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Xunit;
using ValidationException = SamaEcole.Application.Common.Exceptions.ValidationException;

namespace SamaEcole.IntegrationTests.Finance;

/// <summary>
/// Ticket JGK-F09 — contrôle du comptage physique et écarts de caisse à la clôture. L'écart se
/// compare aux ESPÈCES attendues (fonds initial + encaissements EN ESPÈCES uniquement de la session),
/// jamais au total encaissé toutes méthodes confondues — un virement ou un versement mobile money ne
/// transite jamais par le tiroir-caisse physique, il ne peut donc jamais participer à un manquant.
/// </summary>
public class CashierSessionClosingTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("11111111-7777-1111-1111-111111111111");
    private static readonly Guid Classe = Guid.Parse("aaaaaaaa-7777-0000-0000-00000000000a");
    private static readonly Guid Annee = Guid.Parse("bbbbbbbb-7777-0000-0000-00000000000b");
    private static readonly Guid Eleve = Guid.Parse("eeeeeeee-7777-0000-0000-00000000000e");
    private static readonly Guid Inscription = Guid.Parse("11111111-7777-0000-0000-0000000000f1");
    private static readonly Guid Caissier = Guid.Parse("dddddddd-7777-0000-0000-00000000000d");

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
            TotalDue = 200_000m, AmountPaid = 0m, ReceiptNumber = "REC-2026-0001",
            EnrolledAt = DateTimeOffset.UtcNow
        });
        owner.Users.Add(new User { Id = Caissier, SchoolId = Ecole, Email = "caissier.a@ecole-a.sn", PasswordHash = "hash", FullName = "Caissier A", Role = Role.Finance });

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private static CloseCashierSessionCommandHandler NewHandler(IApplicationDbContext ctx) =>
        new(ctx, new FixedTenantProvider(Ecole), TimeProvider.System);

    private async Task<Guid> SeedOpenSessionAsync(decimal openingBalance, params (decimal amount, PaymentMethod method)[] payments)
    {
        var sessionId = Guid.NewGuid();
        await using var ctx = _db.NewAppContext(Ecole);

        ctx.CashierSessions.Add(new CashierSession
        {
            Id = sessionId, SchoolId = Ecole, CashierId = Caissier,
            Status = CashierSessionStatus.Open, OpenedAt = DateTimeOffset.UtcNow, OpeningBalance = openingBalance
        });
        await ctx.SaveChangesAsync(CancellationToken.None);

        foreach (var (amount, method) in payments)
        {
            ctx.Payments.Add(new Payment
            {
                Id = Guid.NewGuid(), SchoolId = Ecole, EnrollmentId = Inscription, CashierSessionId = sessionId,
                Amount = amount, Method = method, Status = PaymentStatus.Paid,
                ReceiptNumber = $"REC-{Guid.NewGuid():N}"[..12], BalanceAfter = 0m,
                PaidAt = DateTimeOffset.UtcNow, ReceivedByUserId = Caissier
            });
        }
        await ctx.SaveChangesAsync(CancellationToken.None);

        return sessionId;
    }

    [Fact]
    public async Task Closing_With_The_Exact_Expected_Cash_Requires_No_Reason()
    {
        var sessionId = await SeedOpenSessionAsync(5_000m, (10_000m, PaymentMethod.Cash));

        await using var ctx = _db.NewAppContext(Ecole);
        var result = await NewHandler(ctx).Handle(
            new CloseCashierSessionCommand(sessionId, 15_000m), CancellationToken.None);

        result.DiscrepancyAmount.Should().Be(0);
        result.DiscrepancyReason.Should().BeNull();
        result.ExpectedCashAmount.Should().Be(15_000m);

        await using var check = _db.NewAppContext(Ecole);
        var session = await check.CashierSessions.AsNoTracking().FirstAsync(s => s.Id == sessionId);
        session.Status.Should().Be(CashierSessionStatus.Closed);
        session.ActualCashAmount.Should().Be(15_000m);
        session.DiscrepancyAmount.Should().Be(0);
        session.DiscrepancyReason.Should().BeNull();
    }

    [Fact]
    public async Task Closing_With_A_Discrepancy_And_No_Reason_Is_Rejected()
    {
        var sessionId = await SeedOpenSessionAsync(5_000m, (10_000m, PaymentMethod.Cash));

        await using var ctx = _db.NewAppContext(Ecole);
        var act = async () => await NewHandler(ctx).Handle(
            new CloseCashierSessionCommand(sessionId, 14_000m), CancellationToken.None);

        (await act.Should().ThrowAsync<ValidationException>())
            .Which.Message.Should().NotBeNullOrEmpty();

        // La session doit rester OUVERTE : un écart non justifié ne clôture jamais silencieusement.
        await using var check = _db.NewAppContext(Ecole);
        (await check.CashierSessions.AsNoTracking().FirstAsync(s => s.Id == sessionId)).Status
            .Should().Be(CashierSessionStatus.Open);
    }

    [Fact]
    public async Task Closing_With_A_Discrepancy_And_A_Reason_Records_The_Adjustment()
    {
        var sessionId = await SeedOpenSessionAsync(5_000m, (10_000m, PaymentMethod.Cash));

        await using var ctx = _db.NewAppContext(Ecole);
        var result = await NewHandler(ctx).Handle(
            new CloseCashierSessionCommand(sessionId, 14_000m, "Erreur de rendu monnaie sur un encaissement."),
            CancellationToken.None);

        result.DiscrepancyAmount.Should().Be(-1_000m, "1 000 FCFA manquent par rapport aux espèces attendues");
        result.DiscrepancyReason.Should().Be("Erreur de rendu monnaie sur un encaissement.");

        await using var check = _db.NewAppContext(Ecole);
        var session = await check.CashierSessions.AsNoTracking().FirstAsync(s => s.Id == sessionId);
        session.DiscrepancyAmount.Should().Be(-1_000m);
        session.DiscrepancyReason.Should().Be("Erreur de rendu monnaie sur un encaissement.");
    }

    [Fact]
    public async Task A_Surplus_Also_Requires_A_Reason()
    {
        var sessionId = await SeedOpenSessionAsync(5_000m, (10_000m, PaymentMethod.Cash));

        await using var ctx = _db.NewAppContext(Ecole);
        var act = async () => await NewHandler(ctx).Handle(
            new CloseCashierSessionCommand(sessionId, 16_000m), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>("un surplus est un écart comme un autre — pas seulement un manquant");
    }

    [Fact]
    public async Task Non_Cash_Payments_Never_Enter_The_Expected_Cash_Amount()
    {
        // 10 000 en espèces + 50 000 par virement : le tiroir-caisse ne doit jamais s'attendre à
        // contenir les 60 000 — seul le virement n'y a jamais transité.
        var sessionId = await SeedOpenSessionAsync(0m,
            (10_000m, PaymentMethod.Cash),
            (50_000m, PaymentMethod.Transfer));

        await using var ctx = _db.NewAppContext(Ecole);
        var result = await NewHandler(ctx).Handle(
            new CloseCashierSessionCommand(sessionId, 10_000m), CancellationToken.None);

        result.ExpectedCashAmount.Should().Be(10_000m, "le virement n'entre jamais dans les espèces attendues");
        result.TotalCollected.Should().Be(60_000m, "le total toutes méthodes, lui, reste inchangé");
        result.DiscrepancyAmount.Should().Be(0);
        result.DiscrepancyReason.Should().BeNull();
    }

    [Fact]
    public async Task An_Already_Closed_Session_Cannot_Be_Closed_Again()
    {
        var sessionId = await SeedOpenSessionAsync(1_000m);

        await using (var ctx = _db.NewAppContext(Ecole))
        {
            await NewHandler(ctx).Handle(new CloseCashierSessionCommand(sessionId, 1_000m), CancellationToken.None);
        }

        await using var ctx2 = _db.NewAppContext(Ecole);
        var act = async () => await NewHandler(ctx2).Handle(
            new CloseCashierSessionCommand(sessionId, 1_000m), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }
}

file sealed class FixedTenantProvider(Guid schoolId) : ITenantProvider
{
    public Guid? CurrentSchoolId => schoolId;
}
