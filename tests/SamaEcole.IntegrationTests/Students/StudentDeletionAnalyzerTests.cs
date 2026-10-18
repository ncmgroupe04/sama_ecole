using FluentAssertions;
using SamaEcole.Application.Common.Deletion;
using SamaEcole.Application.Students.Deletion;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Students;

[Trait("Category", "MultiTenant")]
public class StudentDeletionAnalyzerTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("11111111-9999-1111-1111-111111111111");
    private static readonly Guid AutreEcole = Guid.Parse("22222222-9999-1111-1111-111111111111");
    private static readonly Guid Classe = Guid.Parse("aaaaaaaa-9999-0000-0000-00000000000a");
    private static readonly Guid Annee = Guid.Parse("bbbbbbbb-9999-0000-0000-00000000000b");
    private static readonly Guid Eleve = Guid.Parse("eeeeeeee-9999-0000-0000-00000000000e");
    private static readonly Guid Inscription = Guid.Parse("11111111-9999-0000-0000-0000000000f1");
    private static readonly Guid Caissier = Guid.Parse("dddddddd-9999-0000-0000-00000000000d");

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
            TotalDue = 100_000m, AmountPaid = 0m, ReceiptNumber = "REC-2026-0001", EnrolledAt = DateTimeOffset.UtcNow
        });
        owner.Users.Add(new User
        {
            Id = Caissier, SchoolId = Ecole, Email = "c@ecole-a.sn", PasswordHash = "h", FullName = "Caissier", Role = Role.Finance
        });
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private async Task AddPaymentAsync(string receipt, PaymentStatus status)
    {
        await using var owner = _db.NewOwnerContext();
        var payment = new Payment
        {
            SchoolId = Ecole, EnrollmentId = Inscription, Amount = 30_000m, Method = PaymentMethod.Cash, Status = status,
            BalanceAfter = 70_000m, ReceiptNumber = receipt, ReceivedByUserId = Caissier, PaidAt = DateTimeOffset.UtcNow
        };
        if (status == PaymentStatus.Cancelled)
        {
            payment.CancelledAt = DateTimeOffset.UtcNow;
            payment.CancellationReason = "Erreur";
            payment.CancellationOutcome = PaymentCancellationOutcome.KeptBySchool;
        }
        owner.Payments.Add(payment);
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    private async Task<DeletionImpact> AnalyzeAsync(Guid school, Guid student)
    {
        await using var ctx = _db.NewAppContext(school);
        return await new StudentDeletionAnalyzer(ctx).AnalyzeAsync(student, CancellationToken.None);
    }

    [Fact]
    public async Task A_Student_With_An_Active_Payment_Is_Financially_Blocked_Without_Any_Link_To_Break()
    {
        await AddPaymentAsync("REC-1", PaymentStatus.Partial);

        var impact = await AnalyzeAsync(Ecole, Eleve);

        impact.CanDelete.Should().BeFalse();
        impact.FinancialBlock.Should().BeTrue();
        impact.Message.Should().Be(DeletionMessages.StudentFinancialBlock);
        impact.Links.Should().BeEmpty("aucune option de rupture n'est proposée quand le blocage est financier");
    }

    [Fact]
    public async Task A_Cancelled_Payment_Does_Not_Block()
    {
        await AddPaymentAsync("REC-1", PaymentStatus.Cancelled);

        var impact = await AnalyzeAsync(Ecole, Eleve);

        impact.FinancialBlock.Should().BeFalse();
        impact.Links.Should().ContainSingle(l => l.Kind == "Enrollment").Which.Count.Should().Be(1);
    }

    [Fact]
    public async Task Non_Financial_Links_Are_Counted_And_Described_Precisely()
    {
        await using (var owner = _db.NewOwnerContext())
        {
            owner.LateArrivals.Add(new LateArrival { SchoolId = Ecole, StudentId = Eleve, Date = DateTime.UtcNow.Date, Minutes = 10, Reason = "Transport" });
            owner.LateArrivals.Add(new LateArrival { SchoolId = Ecole, StudentId = Eleve, Date = DateTime.UtcNow.Date.AddDays(-1), Minutes = 5, Reason = "Pluie" });
            await owner.SaveChangesAsync(CancellationToken.None);
        }

        var impact = await AnalyzeAsync(Ecole, Eleve);

        impact.CanDelete.Should().BeFalse();
        impact.FinancialBlock.Should().BeFalse();
        impact.Links.Select(l => l.Label).Should().BeEquivalentTo(["1 inscription", "2 retards"]);
        impact.Links.Should().OnlyContain(l => l.Breakable);
        impact.Message.Should().Be(
            "Cet élève est actuellement rattaché à 1 inscription et 2 retards. "
            + "Vous devez rompre ces liens avant de pouvoir effectuer la suppression.");
    }

    [Fact]
    public async Task A_Student_Without_Any_Link_Can_Be_Deleted()
    {
        Guid free;
        await using (var owner = _db.NewOwnerContext())
        {
            var student = new Student
            {
                Id = Guid.NewGuid(), SchoolId = Ecole, Matricule = "ELEV-2026-0002", FullName = "Moussa Diop",
                BirthDate = new DateOnly(2014, 1, 2), BirthPlace = "Thiès", Gender = "M", ClassroomId = Classe
            };
            owner.Students.Add(student);
            await owner.SaveChangesAsync(CancellationToken.None);
            free = student.Id;
        }

        var impact = await AnalyzeAsync(Ecole, free);

        impact.CanDelete.Should().BeTrue();
        impact.Links.Should().BeEmpty();
    }

    [Fact]
    public async Task A_Student_Of_Another_School_Is_Not_Found()
    {
        var act = async () => await AnalyzeAsync(AutreEcole, Eleve);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
