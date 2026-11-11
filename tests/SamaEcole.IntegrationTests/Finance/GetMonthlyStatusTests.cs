using FluentAssertions;
using SamaEcole.Application.Finance.Queries.GetMonthlyStatus;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Finance;

/// <summary>
/// Situation des mensualités contre un vrai PostgreSQL sous le rôle applicatif : plusieurs élèves, deux classes,
/// paiements multiples/partiels/annulés, et cohérence résumé ↔ listes.
/// Année 2026-2027 (début 1er octobre) ; inscription 10 000 + 9 mensualités de 15 000.
/// </summary>
public class GetMonthlyStatusTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AutreEcole = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateOnly Start = new(2026, 10, 1);

    private readonly Guid _cm2 = Guid.NewGuid();
    private readonly Guid _sixieme = Guid.NewGuid();
    private readonly Guid _yearId = Guid.NewGuid();
    private readonly Guid _feeInscription = Guid.NewGuid();
    private readonly Guid _feeMonthly = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();

        owner.Schools.AddRange(new School { Id = Ecole, Name = "École A" }, new School { Id = AutreEcole, Name = "École B" });
        owner.FeeCategories.AddRange(
            new FeeCategory { Id = _feeInscription, SchoolId = Ecole, Name = "Inscription", IsRecurring = false },
            new FeeCategory { Id = _feeMonthly, SchoolId = Ecole, Name = "Mensualité", IsRecurring = true });
        owner.Classrooms.AddRange(
            new Classroom { Id = _cm2, SchoolId = Ecole, Name = "CM2", Level = "Primaire", Capacity = 40 },
            new Classroom { Id = _sixieme, SchoolId = Ecole, Name = "6e", Level = "Collège", Capacity = 40 });
        owner.SchoolYears.Add(new SchoolYear
        {
            Id = _yearId, SchoolId = Ecole, Label = "2026-2027",
            StartDate = Start, EndDate = Start.AddYears(1), IsActive = true
        });

        // Awa (CM2) — 25 000 en deux versements : inscription + octobre soldés.
        var awa = Add(owner, "A1", "Awa Fall", _cm2, amountPaid: 25_000m,
            (15_000m, new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero)),
            (10_000m, new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero)));
        // Baba (CM2) — 20 000 : inscription + 10 000 sur octobre → partiel.
        Add(owner, "A2", "Baba Diop", _cm2, amountPaid: 20_000m,
            (20_000m, new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.Zero)));
        // Cheikh (6e) — rien payé.
        Add(owner, "A3", "Cheikh Ba", _sixieme, amountPaid: 0m);
        // Diary (6e) — un versement ANNULÉ : AmountPaid redescendu à 0, le versement annulé ne compte pas.
        var diary = Add(owner, "A4", "Diary Sow", _sixieme, amountPaid: 0m);
        owner.Payments.Add(new Payment
        {
            SchoolId = Ecole, EnrollmentId = diary, Amount = 25_000m, Status = PaymentStatus.Cancelled,
            Method = PaymentMethod.Cash, ReceiptNumber = "REC-ANN", PaidAt = DateTimeOffset.UtcNow,
            ReceivedByUserId = Guid.NewGuid(), CancelledAt = DateTimeOffset.UtcNow
        });
        // Eve (6e) — inscrite en décembre : pas concernée par octobre.
        Add(owner, "A5", "Eve Ndiaye", _sixieme, amountPaid: 0m, enrolledAt: new DateTimeOffset(2026, 12, 5, 0, 0, 0, TimeSpan.Zero));

        await owner.SaveChangesAsync(CancellationToken.None);
        _ = awa;
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task October_Splits_Students_In_Three_Lists_And_The_Summary_Matches_The_Lists()
    {
        var report = await RunAsync(new GetMonthlyStatusQuery(_yearId, 10));

        report.Paid.Select(r => r.StudentFullName).Should().Equal("Awa Fall");
        report.Partial.Select(r => r.StudentFullName).Should().Equal("Baba Diop");
        report.Unpaid.Select(r => r.StudentFullName).Should().BeEquivalentTo("Cheikh Ba", "Diary Sow");

        report.Paid.Single().PaidAmount.Should().Be(15_000m);
        report.Paid.Single().LastPaymentAt.Should().Be(new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero));
        report.Partial.Single().PaidAmount.Should().Be(10_000m);
        report.Partial.Single().RemainingAmount.Should().Be(5_000m);

        var s = report.Summary;
        s.TotalStudents.Should().Be(4, "Eve, inscrite en décembre, n'est pas concernée");
        (s.PaidCount, s.PartialCount, s.UnpaidCount).Should().Be((1, 1, 2));
        s.TotalExpected.Should().Be(4 * 15_000m);
        s.TotalCollected.Should().Be(15_000m + 10_000m);
        s.TotalRemaining.Should().Be(s.TotalExpected - s.TotalCollected);

        var all = report.Paid.Concat(report.Partial).Concat(report.Unpaid).ToList();
        all.Sum(r => r.ExpectedAmount).Should().Be(s.TotalExpected);
        all.Sum(r => r.PaidAmount).Should().Be(s.TotalCollected);
        all.Sum(r => r.RemainingAmount).Should().Be(s.TotalRemaining);
    }

    [Fact]
    public async Task The_Classroom_Filter_Restricts_Every_List_And_The_Summary()
    {
        var report = await RunAsync(new GetMonthlyStatusQuery(_yearId, 10, _sixieme));

        report.Paid.Should().BeEmpty();
        report.Partial.Should().BeEmpty();
        report.Unpaid.Select(r => r.StudentFullName).Should().BeEquivalentTo("Cheikh Ba", "Diary Sow");
        report.Summary.TotalStudents.Should().Be(2);
    }

    [Fact]
    public async Task November_Is_Unpaid_For_Everyone_Who_Only_Covered_October_And_Eve_Appears_From_December()
    {
        var november = await RunAsync(new GetMonthlyStatusQuery(_yearId, 11));
        november.Paid.Should().BeEmpty();
        november.Partial.Should().BeEmpty();
        november.Summary.TotalStudents.Should().Be(4);

        var december = await RunAsync(new GetMonthlyStatusQuery(_yearId, 12));
        december.Summary.TotalStudents.Should().Be(5, "Eve est inscrite en décembre");
    }

    [Fact]
    public async Task Another_Tenants_Year_Is_Invisible()
    {
        await using var context = _db.NewAppContext(AutreEcole);
        var handler = new GetMonthlyStatusQueryHandler(context);

        var act = () => handler.Handle(new GetMonthlyStatusQuery(_yearId, 10), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    private async Task<MonthlyStatusDto> RunAsync(GetMonthlyStatusQuery query)
    {
        await using var context = _db.NewAppContext(Ecole);
        return await new GetMonthlyStatusQueryHandler(context).Handle(query, CancellationToken.None);
    }

    private Guid Add(
        Persistence.ApplicationDbContext owner, string matricule, string name, Guid classroomId, decimal amountPaid,
        params (decimal Amount, DateTimeOffset PaidAt)[] payments)
        => Add(owner, matricule, name, classroomId, amountPaid, null, payments);

    private Guid Add(
        Persistence.ApplicationDbContext owner, string matricule, string name, Guid classroomId, decimal amountPaid,
        DateTimeOffset? enrolledAt, params (decimal Amount, DateTimeOffset PaidAt)[] payments)
    {
        var studentId = Guid.NewGuid();
        var enrollmentId = Guid.NewGuid();

        owner.Students.Add(new Student
        {
            Id = studentId, SchoolId = Ecole, Matricule = matricule, FullName = name,
            BirthDate = new DateOnly(2015, 1, 1), BirthPlace = "Dakar", Gender = "F", ClassroomId = classroomId
        });
        owner.Enrollments.Add(new Enrollment
        {
            Id = enrollmentId, SchoolId = Ecole, StudentId = studentId, SchoolYearId = _yearId, ClassroomId = classroomId,
            Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed,
            TotalDue = 145_000m, AmountPaid = amountPaid, ReceiptNumber = $"REC-{matricule}",
            EnrolledAt = enrolledAt ?? new DateTimeOffset(2026, 9, 20, 0, 0, 0, TimeSpan.Zero)
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

        var n = 0;
        foreach (var (amount, paidAt) in payments)
        {
            owner.Payments.Add(new Payment
            {
                SchoolId = Ecole, EnrollmentId = enrollmentId, Amount = amount, Status = PaymentStatus.Partial,
                Method = PaymentMethod.Cash, ReceiptNumber = $"REC-{matricule}-{++n}", PaidAt = paidAt,
                ReceivedByUserId = Guid.NewGuid()
            });
        }

        return enrollmentId;
    }
}
