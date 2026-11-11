using FluentAssertions;
using SamaEcole.Application.Finance.Queries.GetMonthlyStatus;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Finance;

/// <summary>
/// Situation des mensualités avec des lignes de versement qui DÉSIGNENT un mois (option « demander le mois réglé »),
/// et avec la pratique d'école « le premier mois versé à l'inscription est le dernier mois » — contre un vrai PostgreSQL.
/// Inscription 10 000 + 9 mensualités de 15 000 (octobre → juin).
/// </summary>
public class GetMonthlyStatusTaggedTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateOnly Start = new(2026, 10, 1);
    private static readonly DateTimeOffset D1 = new(2026, 10, 3, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset D2 = new(2026, 11, 5, 0, 0, 0, TimeSpan.Zero);

    private readonly Guid _classroom = Guid.NewGuid();
    private readonly Guid _yearId = Guid.NewGuid();
    private readonly Guid _feeInscription = Guid.NewGuid();
    private readonly Guid _feeMonthly = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();

        owner.Schools.Add(new School { Id = Ecole, Name = "École A" });
        owner.FeeCategories.AddRange(
            new FeeCategory { Id = _feeInscription, SchoolId = Ecole, Name = "Inscription", IsRecurring = false },
            new FeeCategory { Id = _feeMonthly, SchoolId = Ecole, Name = "Mensualité", IsRecurring = true });
        owner.Classrooms.Add(new Classroom { Id = _classroom, SchoolId = Ecole, Name = "CM2", Level = "Primaire", Capacity = 40 });
        owner.SchoolYears.Add(new SchoolYear
        {
            Id = _yearId, SchoolId = Ecole, Label = "2026-2027", StartDate = Start, EndDate = Start.AddYears(1), IsActive = true
        });

        // Awa — un versement : inscription (10 000, sans mois) + 15 000 qui désignent JUIN.
        var awa = AddStudent(owner, "T1", "Awa Fall", amountPaid: 25_000m);
        AddPayment(owner, awa, "T1-1", D1, 25_000m, (_feeInscription, 10_000m, null), (_feeMonthly, 15_000m, 6));

        // Baba — mêmes 25 000 versés, mais SANS mois choisi (ancien paiement ou caissier qui ne choisit pas).
        var baba = AddStudent(owner, "T2", "Baba Diop", amountPaid: 25_000m);
        AddPayment(owner, baba, "T2-1", D1, 25_000m, (_feeInscription, 10_000m, null), (_feeMonthly, 15_000m, null));

        // Cheikh — paiement sans aucune ligne de ventilation (cas historique le plus courant).
        var cheikh = AddStudent(owner, "T3", "Cheikh Ba", amountPaid: 25_000m);
        AddPayment(owner, cheikh, "T3-1", D1, 25_000m);

        // Diary — juin désigné PUIS octobre payé sans mois : 25 000 + 15 000.
        var diary = AddStudent(owner, "T4", "Diary Sow", amountPaid: 40_000m);
        AddPayment(owner, diary, "T4-1", D1, 25_000m, (_feeInscription, 10_000m, null), (_feeMonthly, 15_000m, 6));
        AddPayment(owner, diary, "T4-2", D2, 15_000m);

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private static string Names(IEnumerable<MonthlyStatusRow> rows) => string.Join(",", rows.Select(r => r.StudentFullName).OrderBy(n => n));

    [Fact]
    public async Task A_Month_Chosen_At_The_Cashier_Is_Honoured_And_Untagged_Payments_Behave_As_Before()
    {
        var october = await RunAsync(10);
        var june = await RunAsync(6);

        // Octobre : Baba et Cheikh (payés sans mois, dans l'ordre) et Diary (15 000 sans mois) sont à jour ; Awa a désigné juin.
        Names(october.Paid).Should().Be("Baba Diop,Cheikh Ba,Diary Sow");
        Names(october.Unpaid).Should().Be("Awa Fall");

        // Juin : seuls ceux qui l'ont désigné.
        Names(june.Paid).Should().Be("Awa Fall,Diary Sow");
        Names(june.Unpaid).Should().Be("Baba Diop,Cheikh Ba");

        june.Paid.Single(r => r.StudentFullName == "Awa Fall").LastPaymentAt.Should().Be(D1);
        october.Paid.Single(r => r.StudentFullName == "Diary Sow").LastPaymentAt.Should().Be(D2);
    }

    [Fact]
    public async Task The_Summary_Still_Matches_The_Lists_With_Chosen_Months()
    {
        var report = await RunAsync(6);
        var all = report.Paid.Concat(report.Partial).Concat(report.Unpaid).ToList();

        report.Summary.TotalStudents.Should().Be(all.Count);
        report.Summary.TotalExpected.Should().Be(all.Sum(r => r.ExpectedAmount));
        report.Summary.TotalCollected.Should().Be(all.Sum(r => r.PaidAmount));
        report.Summary.TotalRemaining.Should().Be(all.Sum(r => r.RemainingAmount));
    }

    [Fact]
    public async Task The_School_Practice_Puts_The_First_Untagged_Month_On_June_Without_Touching_Chosen_Months()
    {
        await using (var owner = _db.NewOwnerContext())
        {
            owner.SchoolSettings.Add(new SchoolSettings { SchoolId = Ecole, FirstMonthlyPaymentCoversLastMonth = true });
            await owner.SaveChangesAsync(CancellationToken.None);
        }

        var june = await RunAsync(6);
        var october = await RunAsync(10);

        // Baba / Cheikh : 25 000 sans mois → inscription + premier mois rangé en JUIN ; octobre non payé.
        Names(june.Paid).Should().Be("Awa Fall,Baba Diop,Cheikh Ba,Diary Sow");
        Names(october.Unpaid).Should().Be("Awa Fall,Baba Diop,Cheikh Ba");
        Names(october.Paid).Should().Be("Diary Sow");
    }

    private async Task<MonthlyStatusDto> RunAsync(int month)
    {
        await using var context = _db.NewAppContext(Ecole);
        return await new GetMonthlyStatusQueryHandler(context)
            .Handle(new GetMonthlyStatusQuery(_yearId, month), CancellationToken.None);
    }

    private Guid AddStudent(Persistence.ApplicationDbContext owner, string matricule, string name, decimal amountPaid)
    {
        var studentId = Guid.NewGuid();
        var enrollmentId = Guid.NewGuid();
        owner.Students.Add(new Student
        {
            Id = studentId, SchoolId = Ecole, Matricule = matricule, FullName = name,
            BirthDate = new DateOnly(2015, 1, 1), BirthPlace = "Dakar", Gender = "F", ClassroomId = _classroom
        });
        owner.Enrollments.Add(new Enrollment
        {
            Id = enrollmentId, SchoolId = Ecole, StudentId = studentId, SchoolYearId = _yearId, ClassroomId = _classroom,
            Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed,
            TotalDue = 145_000m, AmountPaid = amountPaid, ReceiptNumber = $"REC-{matricule}",
            EnrolledAt = new DateTimeOffset(2026, 9, 20, 0, 0, 0, TimeSpan.Zero)
        });
        owner.EnrollmentFeeLines.AddRange(
            new EnrollmentFeeLine
            {
                SchoolId = Ecole, EnrollmentId = enrollmentId, FeeCategoryId = _feeInscription, Designation = "Inscription",
                IsRecurring = false, UnitAmount = 10_000m, Months = 1, LineTotal = 10_000m
            },
            new EnrollmentFeeLine
            {
                SchoolId = Ecole, EnrollmentId = enrollmentId, FeeCategoryId = _feeMonthly, Designation = "Mensualité",
                IsRecurring = true, UnitAmount = 15_000m, Months = 9, LineTotal = 135_000m
            });
        return enrollmentId;
    }

    private static void AddPayment(
        Persistence.ApplicationDbContext owner, Guid enrollmentId, string receipt, DateTimeOffset paidAt, decimal amount,
        params (Guid FeeCategoryId, decimal Amount, int? Month)[] lines)
    {
        var payment = new Payment
        {
            SchoolId = Ecole, EnrollmentId = enrollmentId, Amount = amount, Status = PaymentStatus.Partial,
            Method = PaymentMethod.Cash, ReceiptNumber = receipt, PaidAt = paidAt, ReceivedByUserId = Guid.NewGuid()
        };
        foreach (var l in lines)
        {
            payment.Breakdowns.Add(new PaymentBreakdown
            {
                SchoolId = Ecole, FeeCategoryId = l.FeeCategoryId, AmountAllocated = l.Amount, Month = l.Month
            });
        }

        owner.Payments.Add(payment);
    }
}
