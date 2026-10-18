using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance;
using SamaEcole.Application.Finance.Queries.GetDailyCashRegisterPdf;
using SamaEcole.Application.Finance.Queries.GetDailyClosingReportPdf;
using SamaEcole.Application.Finance.Queries.GetPaymentReceipt;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Finance;

/// <summary>
/// Un élève supprimé laisse derrière lui des paiements ANNULÉS, conservés en base (preuve comptable). Les journaux de
/// caisse et le reçu joignent l'inscription et l'élève, tous deux filtrés une fois archivés : sans correction, le
/// rapport de clôture plante (inscription nulle) et le reçu annulé devient introuvable. Ils doivent rester lisibles,
/// sans jamais ouvrir l'accès à une autre école.
/// </summary>
[Trait("Category", "MultiTenant")]
public class DeletedStudentFinancialHistoryTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("11111111-cccc-1111-1111-111111111111");
    private static readonly Guid AutreEcole = Guid.Parse("22222222-cccc-1111-1111-111111111111");
    private static readonly Guid Classe = Guid.Parse("aaaaaaaa-cccc-0000-0000-00000000000a");
    private static readonly Guid Annee = Guid.Parse("bbbbbbbb-cccc-0000-0000-00000000000b");
    private static readonly Guid Eleve = Guid.Parse("eeeeeeee-cccc-0000-0000-00000000000e");
    private static readonly Guid Inscription = Guid.Parse("11111111-cccc-0000-0000-0000000000f1");
    private static readonly Guid Caissier = Guid.Parse("dddddddd-cccc-0000-0000-00000000000d");
    private static readonly Guid SessionLundi = Guid.Parse("cccccccc-cccc-0000-0000-0000000000c1");
    private static readonly Guid SessionMardi = Guid.Parse("cccccccc-cccc-0000-0000-0000000000c2");
    private static readonly Guid PaiementLundi = Guid.Parse("99999999-cccc-0000-0000-0000000000a1");

    private static readonly DateTimeOffset Lundi = new(2026, 10, 12, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Mardi = Lundi.AddDays(1);

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
        owner.CashierSessions.Add(new CashierSession
        {
            Id = SessionMardi, SchoolId = Ecole, CashierId = Caissier, Status = CashierSessionStatus.Open,
            OpenedAt = Mardi.AddHours(8), OpeningBalance = 0m
        });
        // Lundi : 50 000 en espèces, annulé mardi — puis l'élève est supprimé.
        owner.Payments.Add(new Payment
        {
            Id = PaiementLundi, SchoolId = Ecole, EnrollmentId = Inscription, CashierSessionId = SessionLundi,
            Amount = 50_000m, Method = PaymentMethod.Cash, Status = PaymentStatus.Cancelled, BalanceAfter = 0m,
            ReceiptNumber = "REC-LUNDI", ReceivedByUserId = Caissier, PaidAt = Lundi.AddHours(10),
            CancelledAt = Mardi.AddHours(9), CancelledByUserId = Caissier, CancellationReason = "Mauvais élève",
            CancellationOutcome = PaymentCancellationOutcome.KeptBySchool, CancellationSessionId = SessionMardi
        });
        await owner.SaveChangesAsync(CancellationToken.None);

        var enrollment = await owner.Enrollments.IgnoreQueryFilters().SingleAsync(e => e.Id == Inscription);
        var student = await owner.Students.IgnoreQueryFilters().SingleAsync(s => s.Id == Eleve);
        enrollment.SoftDelete("directeur");
        student.SoftDelete("directeur");
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task The_Closing_Report_Still_Names_The_Deleted_Student()
    {
        await using var ctx = _db.NewAppContext(Ecole);
        var generator = new CapturingClosingGenerator();
        var handler = new GetDailyClosingReportPdfQueryHandler(ctx, new FixedTenant(Ecole), new NoLogo(), generator);

        await handler.Handle(new GetDailyClosingReportPdfQuery(SessionLundi), CancellationToken.None);

        var row = generator.Captured!.Transactions.Should().ContainSingle().Subject;
        row.StudentName.Should().Contain("Awa Fall").And.Contain("(élève supprimé)");
        row.CancelledNote.Should().Contain("Mauvais élève");
    }

    [Fact]
    public async Task The_Closing_Report_Of_The_Cancelling_Session_Still_Lists_The_Reversal()
    {
        await using var ctx = _db.NewAppContext(Ecole);
        var generator = new CapturingClosingGenerator();
        var handler = new GetDailyClosingReportPdfQueryHandler(ctx, new FixedTenant(Ecole), new NoLogo(), generator);

        await handler.Handle(new GetDailyClosingReportPdfQuery(SessionMardi), CancellationToken.None);

        var reversal = generator.Captured!.Reversals.Should().ContainSingle().Subject;
        reversal.StudentName.Should().Contain("Awa Fall").And.Contain("(élève supprimé)");
        reversal.Amount.Should().Be(-50_000m);
    }

    [Fact]
    public async Task The_Daily_Cash_Register_Still_Names_The_Deleted_Student()
    {
        await using var ctx = _db.NewAppContext(Ecole);
        var generator = new CapturingCashRegisterGenerator();
        var handler = new GetDailyCashRegisterPdfQueryHandler(ctx, new FixedTenant(Ecole), generator, new NoLogo());

        await handler.Handle(new GetDailyCashRegisterPdfQuery(DateOnly.FromDateTime(Lundi.UtcDateTime)), CancellationToken.None);

        var line = generator.Captured!.Payments.Should().ContainSingle().Subject;
        line.StudentFullName.Should().Contain("Awa Fall").And.Contain("(élève supprimé)");
        line.Matricule.Should().Be("ELEV-2026-0001");
    }

    [Fact]
    public async Task The_Cancelled_Receipt_Of_A_Deleted_Student_Is_Still_Readable()
    {
        await using var ctx = _db.NewAppContext(Ecole);
        var handler = new GetPaymentReceiptQueryHandler(ctx, new FixedTenant(Ecole));

        var receipt = await handler.Handle(new GetPaymentReceiptQuery(PaiementLundi), CancellationToken.None);

        receipt.StudentFullName.Should().Contain("Awa Fall");
        receipt.CancellationNotice.Should().NotBeNull().And.Contain("Mauvais élève");
    }

    [Fact]
    public async Task Another_School_Cannot_Read_The_Deleted_Students_Receipt()
    {
        await using var ctx = _db.NewAppContext(AutreEcole);
        var handler = new GetPaymentReceiptQueryHandler(ctx, new FixedTenant(AutreEcole));

        var act = async () => await handler.Handle(new GetPaymentReceiptQuery(PaiementLundi), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>(
            "IgnoreQueryFilters retire aussi le filtre de tenant : il est rétabli à la main, et la RLS protège en dernier recours");
    }

    private sealed class FixedTenant(Guid schoolId) : ITenantProvider
    {
        public Guid? CurrentSchoolId => schoolId;
    }

    private sealed class NoLogo : ISchoolLogoProvider
    {
        public Task<byte[]?> TryFetchAsync(string? logoUrl, CancellationToken cancellationToken) => Task.FromResult<byte[]?>(null);
    }

    private sealed class CapturingClosingGenerator : IDailyClosingReportPdfGenerator
    {
        public DailyClosingReportDto? Captured { get; private set; }

        public byte[] Generate(DailyClosingReportDto report, byte[]? schoolLogo)
        {
            Captured = report;
            return [1];
        }
    }

    private sealed class CapturingCashRegisterGenerator : IDailyCashRegisterPdfGenerator
    {
        public DailyCashRegisterDto? Captured { get; private set; }

        public byte[] Generate(DailyCashRegisterDto data, byte[]? schoolLogo)
        {
            Captured = data;
            return [1];
        }
    }
}
