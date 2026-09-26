using FluentAssertions;
using SamaEcole.Application.Finance.Queries.GetDebtorAgingReport;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Finance;

/// <summary>
/// Rapport d'ancienneté des débiteurs (Volume 1 §7.5), contre un vrai PostgreSQL sous le rôle
/// applicatif. Plusieurs débiteurs dans la même école : le handler charge lignes de frais et
/// échéanciers EN LOT pour tous les débiteurs (plus une requête par débiteur) — ces tests prouvent
/// que chaque inscription retrouve bien SES lignes, dans le bon ordre, et SON échéancier actif.
/// </summary>
public class GetDebtorAgingReportTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateOnly Today = new(2027, 1, 15);
    private static readonly DateOnly SchoolYearStart = new(2026, 10, 1);

    private readonly Guid _classroomId = Guid.NewGuid();
    private readonly Guid _yearId = Guid.NewGuid();
    private readonly Guid _inscriptionCategoryId = Guid.NewGuid();
    private readonly Guid _monthlyCategoryId = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();

        owner.Schools.Add(new School { Id = Ecole, Name = "École A" });
        owner.FeeCategories.AddRange(
            new FeeCategory { Id = _inscriptionCategoryId, SchoolId = Ecole, Name = "Inscription", IsRecurring = false },
            new FeeCategory { Id = _monthlyCategoryId, SchoolId = Ecole, Name = "Mensualité", IsRecurring = true });
        owner.Classrooms.Add(new Classroom { Id = _classroomId, SchoolId = Ecole, Name = "CM2", Level = "Primaire", Capacity = 40 });
        owner.SchoolYears.Add(new SchoolYear
        {
            Id = _yearId, SchoolId = Ecole, Label = "2026-2027",
            StartDate = SchoolYearStart, EndDate = SchoolYearStart.AddYears(1), IsActive = true
        });

        // Awa — inscription 15 000 + 9 mensualités de 10 000, 35 000 payés. Les frais ponctuels sont
        // soldés EN PREMIER (ordre IsRecurring puis Designation) : inscription, octobre et novembre
        // payés, la plus ancienne échéance en retard est le 1er décembre → 45 jours.
        var awa = AddDebtor(owner, "ELEV-0001", "Awa Fall", totalDue: 105_000m, amountPaid: 35_000m);
        AddLine(owner, awa, _monthlyCategoryId, "Mensualité", isRecurring: true, unitAmount: 10_000m, months: 9);
        AddLine(owner, awa, _inscriptionCategoryId, "Inscription", isRecurring: false, unitAmount: 15_000m, months: 1);

        // Baba — l'échéancier ACTIF prime sur les lignes de frais (qui le mettraient en retard depuis le
        // 1er octobre, 106 jours). 30 000 payés couvrent la 1re échéance : retard depuis le 5 novembre
        // → 71 jours. Un échéancier ANNULÉ, plus ancien, doit être ignoré.
        var baba = AddDebtor(owner, "ELEV-0002", "Baba Diop", totalDue: 90_000m, amountPaid: 30_000m);
        AddLine(owner, baba, _inscriptionCategoryId, "Scolarité", isRecurring: false, unitAmount: 90_000m, months: 1);
        AddPlan(owner, baba, FeeInstallmentPlanStatus.Cancelled,
            (1, new DateOnly(2026, 9, 1), 90_000m));
        AddPlan(owner, baba, FeeInstallmentPlanStatus.Active,
            (1, new DateOnly(2026, 10, 5), 30_000m),
            (2, new DateOnly(2026, 11, 5), 30_000m),
            (3, new DateOnly(2027, 2, 5), 30_000m));

        // Cheikh — solde dû, mais aucune échéance encore dépassée : hors du rapport.
        var cheikh = AddDebtor(owner, "ELEV-0003", "Cheikh Ba", totalDue: 50_000m, amountPaid: 0m);
        AddPlan(owner, cheikh, FeeInstallmentPlanStatus.Active, (1, new DateOnly(2027, 2, 1), 50_000m));

        // Diary — entièrement soldé : jamais débiteur.
        var diary = AddDebtor(owner, "ELEV-0004", "Diary Sow", totalDue: 15_000m, amountPaid: 15_000m);
        AddLine(owner, diary, _inscriptionCategoryId, "Inscription", isRecurring: false, unitAmount: 15_000m, months: 1);

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task Each_Debtor_Is_Aged_From_Its_Own_Fee_Lines_Or_Active_Plan_Most_Overdue_First()
    {
        await using var context = _db.NewAppContext(Ecole);
        var handler = new GetDebtorAgingReportQueryHandler(
            context, new FixedTimeProvider(new DateTimeOffset(Today.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero)));

        var report = await handler.Handle(new GetDebtorAgingReportQuery(), CancellationToken.None);

        report.GeneratedOn.Should().Be(Today);
        report.Debtors.Select(d => (d.Matricule, d.RemainingBalance, d.DaysOverdue)).Should().Equal(
            ("ELEV-0002", 60_000m, 71),
            ("ELEV-0001", 70_000m, 45));
        report.Debtors.Should().AllSatisfy(d => d.ClassroomName.Should().Be("CM2"));
    }

    private Guid AddDebtor(
        Persistence.ApplicationDbContext owner, string matricule, string fullName, decimal totalDue, decimal amountPaid)
    {
        var studentId = Guid.NewGuid();
        var enrollmentId = Guid.NewGuid();

        owner.Students.Add(new Student
        {
            Id = studentId, SchoolId = Ecole, Matricule = matricule, FullName = fullName,
            BirthDate = new DateOnly(2015, 1, 1), BirthPlace = "Dakar", Gender = "F", ClassroomId = _classroomId
        });
        owner.Enrollments.Add(new Enrollment
        {
            Id = enrollmentId, SchoolId = Ecole, StudentId = studentId, SchoolYearId = _yearId,
            ClassroomId = _classroomId, Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed,
            TotalDue = totalDue, AmountPaid = amountPaid, ReceiptNumber = $"REC-{matricule}",
            EnrolledAt = DateTimeOffset.UtcNow
        });

        return enrollmentId;
    }

    private static void AddLine(
        Persistence.ApplicationDbContext owner, Guid enrollmentId, Guid feeCategoryId, string designation,
        bool isRecurring, decimal unitAmount, int months) =>
        owner.EnrollmentFeeLines.Add(new EnrollmentFeeLine
        {
            SchoolId = Ecole, EnrollmentId = enrollmentId, FeeCategoryId = feeCategoryId,
            Designation = designation, IsRecurring = isRecurring, UnitAmount = unitAmount, Months = months,
            LineTotal = unitAmount * months
        });

    private static void AddPlan(
        Persistence.ApplicationDbContext owner, Guid enrollmentId, FeeInstallmentPlanStatus status,
        params (int SequenceNo, DateOnly DueDate, decimal Amount)[] installments)
    {
        var plan = new FeeInstallmentPlan { Id = Guid.NewGuid(), SchoolId = Ecole, EnrollmentId = enrollmentId, Status = status };
        owner.FeeInstallmentPlans.Add(plan);
        owner.FeeInstallments.AddRange(installments.Select(i => new FeeInstallment
        {
            SchoolId = Ecole, FeeInstallmentPlanId = plan.Id, SequenceNo = i.SequenceNo,
            Label = $"Échéance {i.SequenceNo}", Amount = i.Amount, DueDate = i.DueDate
        }));
    }
}

file sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
