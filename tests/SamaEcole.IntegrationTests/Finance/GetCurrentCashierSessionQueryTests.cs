using FluentAssertions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Queries.GetCurrentCashierSession;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace SamaEcole.IntegrationTests.Finance;

/// <summary>
/// GET /finance/sessions/current (câblage écran /caisse, 27/08/2026) : c'est ce que l'écran interroge
/// AVANT toute tentative d'encaissement pour savoir s'il doit proposer d'ouvrir une session ou afficher
/// le statut de celle déjà ouverte — <see cref="Application.Finance.Commands.RecordPayment.RecordPaymentCommandHandler"/>
/// refusait déjà (422) tout encaissement hors session ouverte, mais rien ne permettait à l'écran de le
/// savoir sans essayer et échouer.
/// </summary>
public class GetCurrentCashierSessionQueryTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("11111111-6666-1111-1111-111111111111");
    private static readonly Guid Classe = Guid.Parse("aaaaaaaa-6666-0000-0000-00000000000a");
    private static readonly Guid Annee = Guid.Parse("bbbbbbbb-6666-0000-0000-00000000000b");
    private static readonly Guid Eleve = Guid.Parse("eeeeeeee-6666-0000-0000-00000000000e");
    private static readonly Guid Inscription = Guid.Parse("11111111-6666-0000-0000-0000000000f1");
    private static readonly Guid Caissier = Guid.Parse("dddddddd-6666-0000-0000-00000000000d");
    private static readonly Guid AutreCaissier = Guid.Parse("dddddddd-6666-0000-0000-00000000000e");

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
            TotalDue = 100_000m, AmountPaid = 0m, ReceiptNumber = "REC-2026-0001",
            EnrolledAt = DateTimeOffset.UtcNow
        });
        owner.Users.AddRange(
            new User { Id = Caissier, SchoolId = Ecole, Email = "caissier.a@ecole-a.sn", PasswordHash = "hash", FullName = "Caissier A", Role = Role.Finance },
            new User { Id = AutreCaissier, SchoolId = Ecole, Email = "caissier.b@ecole-a.sn", PasswordHash = "hash", FullName = "Caissier B", Role = Role.Finance });

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private static GetCurrentCashierSessionQueryHandler NewHandler(IApplicationDbContext ctx, Guid userId) =>
        new(ctx, new FixedCurrentUser(userId));

    [Fact]
    public async Task Returns_Null_When_The_User_Has_No_Open_Session()
    {
        await using var ctx = _db.NewAppContext(Ecole);

        var result = await NewHandler(ctx, Caissier).Handle(new GetCurrentCashierSessionQuery(), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Returns_The_Open_Session_With_Its_Running_Total()
    {
        var sessionId = Guid.NewGuid();
        await using (var seedCtx = _db.NewAppContext(Ecole))
        {
            seedCtx.CashierSessions.Add(new CashierSession
            {
                Id = sessionId, SchoolId = Ecole, CashierId = Caissier,
                Status = CashierSessionStatus.Open, OpenedAt = DateTimeOffset.UtcNow, OpeningBalance = 5_000m
            });
            await seedCtx.SaveChangesAsync(CancellationToken.None);

            seedCtx.Payments.AddRange(
                new Payment
                {
                    Id = Guid.NewGuid(), SchoolId = Ecole, EnrollmentId = Inscription, CashierSessionId = sessionId,
                    Amount = 10_000m, Method = PaymentMethod.Cash, Status = PaymentStatus.Paid,
                    ReceiptNumber = "REC-A", BalanceAfter = 90_000m, PaidAt = DateTimeOffset.UtcNow, ReceivedByUserId = Caissier
                },
                new Payment
                {
                    Id = Guid.NewGuid(), SchoolId = Ecole, EnrollmentId = Inscription, CashierSessionId = sessionId,
                    Amount = 7_000m, Method = PaymentMethod.Cash, Status = PaymentStatus.Paid,
                    ReceiptNumber = "REC-B", BalanceAfter = 83_000m, PaidAt = DateTimeOffset.UtcNow, ReceivedByUserId = Caissier
                },
                // Annulé : ne doit JAMAIS entrer dans le total affiché — même exclusion que
                // CloseCashierSessionCommandHandler (Volume 1 §15.2), sans quoi le chiffre montré
                // pendant la journée contredirait celui du rapport de clôture.
                new Payment
                {
                    Id = Guid.NewGuid(), SchoolId = Ecole, EnrollmentId = Inscription, CashierSessionId = sessionId,
                    Amount = 99_999m, Method = PaymentMethod.Cash, Status = PaymentStatus.Cancelled,
                    ReceiptNumber = "REC-C", BalanceAfter = 0m, PaidAt = DateTimeOffset.UtcNow, ReceivedByUserId = Caissier
                });
            await seedCtx.SaveChangesAsync(CancellationToken.None);
        }

        await using var ctx = _db.NewAppContext(Ecole);
        var result = await NewHandler(ctx, Caissier).Handle(new GetCurrentCashierSessionQuery(), CancellationToken.None);

        result.Should().NotBeNull();
        result!.SessionId.Should().Be(sessionId);
        result.OpeningBalance.Should().Be(5_000m);
        result.TotalCollected.Should().Be(17_000m, "les deux versements complétés, jamais le paiement annulé");
        result.PaymentsCount.Should().Be(2);
    }

    [Fact]
    public async Task Never_Returns_Another_Cashiers_Open_Session()
    {
        await using (var seedCtx = _db.NewAppContext(Ecole))
        {
            seedCtx.CashierSessions.Add(new CashierSession
            {
                Id = Guid.NewGuid(), SchoolId = Ecole, CashierId = AutreCaissier,
                Status = CashierSessionStatus.Open, OpenedAt = DateTimeOffset.UtcNow, OpeningBalance = 1_000m
            });
            await seedCtx.SaveChangesAsync(CancellationToken.None);
        }

        await using var ctx = _db.NewAppContext(Ecole);
        var result = await NewHandler(ctx, Caissier).Handle(new GetCurrentCashierSessionQuery(), CancellationToken.None);

        result.Should().BeNull("la session ouverte par un autre caissier n'est pas la sienne");
    }

    [Fact]
    public async Task Never_Returns_A_Closed_Session()
    {
        await using (var seedCtx = _db.NewAppContext(Ecole))
        {
            seedCtx.CashierSessions.Add(new CashierSession
            {
                Id = Guid.NewGuid(), SchoolId = Ecole, CashierId = Caissier,
                Status = CashierSessionStatus.Closed, OpenedAt = DateTimeOffset.UtcNow.AddHours(-8),
                ClosedAt = DateTimeOffset.UtcNow, OpeningBalance = 2_000m, ClosingBalance = 2_000m
            });
            await seedCtx.SaveChangesAsync(CancellationToken.None);
        }

        await using var ctx = _db.NewAppContext(Ecole);
        var result = await NewHandler(ctx, Caissier).Handle(new GetCurrentCashierSessionQuery(), CancellationToken.None);

        result.Should().BeNull("la journée d'hier est clôturée : elle ne doit pas rouvrir le formulaire d'encaissement aujourd'hui");
    }
}

file sealed class FixedCurrentUser(Guid userId) : ICurrentUserService
{
    public Guid? UserId => userId;
    public Role? Role => Domain.Enums.Role.Finance;
    public string? IpAddress => "127.0.0.1";
}
