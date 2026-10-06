using FluentAssertions;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Enrollments.Commands.CreateEnrollment;
using SamaEcole.Application.Finance.Commands.ApplyFeeInstallmentPlanToClassroom;
using SamaEcole.Application.Finance.Commands.UpdateFeeCategory;
using SamaEcole.Application.Finance.Queries.GetDebtorAgingReport;
using SamaEcole.Application.Finance.Queries.GetDuesNotice;
using SamaEcole.Application.Finance.Queries.GetFinanceDashboard;
using SamaEcole.Application.Finance.Queries.GetStudentBalance;
using SamaEcole.Application.Finance.Queries.SearchStudentsForCashier;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using SamaEcole.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace SamaEcole.IntegrationTests.Finance;

/// <summary>
/// Frais optionnels, Tâche 3 — le dû annuel FILTRÉ par les cases cochées doit traverser tous les lecteurs
/// de la Finance : solde de l'élève, sommation, rapport des débiteurs, recherche caisse, tableau de bord,
/// échéancier de classe. Ces tests inscrivent de vrais élèves (vrai handler, vraie sélection) puis
/// interrogent chaque lecteur : ils prouvent qu'aucun ne recalcule le dû depuis le barème de la classe,
/// où le frais décoché figure encore.
///
/// Trois élèves de la même classe, aux factures différentes :
///   Awa    — tous les frais optionnels cochés ;
///   Fatou  — l'uniforme seul ;
///   Modou  — aucun frais optionnel.
/// </summary>
[Trait("Category", "MultiTenant")]
public class OptionalFeesAnnualDueTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("44444444-0000-0000-0000-000000000001");
    private static readonly DateOnly SchoolYearStart = new(2026, 10, 1);
    private static readonly DateOnly Today = new(2027, 1, 15);
    private static readonly DateTimeOffset Now = new(Today.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero);

    private const int TuitionMonths = 9;
    private const decimal Inscription = 10_000m;
    private const decimal Mensualite = 15_000m;
    private const decimal Uniforme = 25_000m;
    private const decimal TenueSport = 8_000m;

    private const decimal Mandatory = Inscription + Mensualite * TuitionMonths; // 145 000
    private const decimal AwaTotal = Mandatory + Uniforme + TenueSport;          // 178 000
    private const decimal FatouTotal = Mandatory + Uniforme;                     // 170 000
    private const decimal ModouTotal = Mandatory;                                // 145 000

    private readonly Guid _classroomId = Guid.NewGuid();
    private readonly Guid _inscriptionId = Guid.NewGuid();
    private readonly Guid _mensualiteId = Guid.NewGuid();
    private readonly Guid _uniformeId = Guid.NewGuid();
    private readonly Guid _tenueId = Guid.NewGuid();

    private Guid _awa, _fatou, _modou;
    private Guid _awaEnrollment, _fatouEnrollment, _modouEnrollment;

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using (var owner = _db.NewOwnerContext())
        {
            owner.Schools.Add(new School { Id = Ecole, Name = "École Frais Optionnels" });
            owner.TenantSubscriptions.Add(RlsTestDatabase.UnlimitedSubscription(Ecole));
            owner.Classrooms.Add(new Classroom { Id = _classroomId, SchoolId = Ecole, Name = "6e A", Level = "Collège", Capacity = 40 });
            owner.SchoolYears.Add(new SchoolYear
            {
                Id = Guid.NewGuid(), SchoolId = Ecole, Label = "2026-2027",
                StartDate = SchoolYearStart, EndDate = SchoolYearStart.AddYears(1), IsActive = true
            });
            owner.SchoolSettings.Add(new SchoolSettings { SchoolId = Ecole, TuitionMonthsPerYear = TuitionMonths });
            owner.FeeCategories.AddRange(
                new FeeCategory { Id = _inscriptionId, SchoolId = Ecole, Name = "Inscription" },
                new FeeCategory { Id = _mensualiteId, SchoolId = Ecole, Name = "Mensualité", IsRecurring = true },
                new FeeCategory { Id = _uniformeId, SchoolId = Ecole, Name = "Uniforme", IsOptional = true },
                new FeeCategory { Id = _tenueId, SchoolId = Ecole, Name = "Tenue de sport", IsOptional = true });
            owner.ClassFees.AddRange(
                new ClassFee { SchoolId = Ecole, FeeCategoryId = _inscriptionId, ClassroomId = _classroomId, Amount = Inscription },
                new ClassFee { SchoolId = Ecole, FeeCategoryId = _mensualiteId, ClassroomId = _classroomId, Amount = Mensualite },
                new ClassFee { SchoolId = Ecole, FeeCategoryId = _uniformeId, ClassroomId = _classroomId, Amount = Uniforme },
                new ClassFee { SchoolId = Ecole, FeeCategoryId = _tenueId, ClassroomId = _classroomId, Amount = TenueSport });
            await owner.SaveChangesAsync(CancellationToken.None);
        }

        (_awa, _awaEnrollment) = await EnrollAsync("Awa Ndiaye", [_uniformeId, _tenueId]);
        (_fatou, _fatouEnrollment) = await EnrollAsync("Fatou Sow", [_uniformeId]);
        (_modou, _modouEnrollment) = await EnrollAsync("Modou Fall", []);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private async Task<(Guid StudentId, Guid EnrollmentId)> EnrollAsync(string fullName, IReadOnlyList<Guid> optionalFees)
    {
        await using var db = _db.NewAppContext(Ecole);
        var handler = new CreateEnrollmentCommandHandler(
            db, new StubTenantProvider(Ecole), _db.NewGenerator(db), TimeProvider.System,
            new NoOpKpiCache(), new TestCurrentUser(), _db.NewQuotaGuard(db, Ecole));

        var receipt = await handler.Handle(new CreateEnrollmentCommand
        {
            Type = EnrollmentType.NewEnrollment,
            ClassroomId = _classroomId,
            FullName = fullName,
            BirthDate = new DateOnly(2013, 3, 3),
            BirthPlace = "Dakar",
            Gender = "M",
            OptionalFeeCategoryIds = optionalFees
        }, CancellationToken.None);

        await using var check = _db.NewAppContext(Ecole);
        var studentId = await check.Enrollments.Where(e => e.Id == receipt.EnrollmentId).Select(e => e.StudentId).SingleAsync();
        return (studentId, receipt.EnrollmentId);
    }

    // ------------------------------------------------------------------ Solde de l'élève

    [Fact]
    public async Task The_Student_Balance_Only_Counts_The_Checked_Fees()
    {
        await using var db = _db.NewAppContext(Ecole);
        var handler = new GetStudentBalanceQueryHandler(db, new FixedTime(Now));

        var awa = await handler.Handle(new GetStudentBalanceQuery(_awa), CancellationToken.None);
        var fatou = await handler.Handle(new GetStudentBalanceQuery(_fatou), CancellationToken.None);
        var modou = await handler.Handle(new GetStudentBalanceQuery(_modou), CancellationToken.None);

        awa.TotalDue.Should().Be(AwaTotal);
        fatou.TotalDue.Should().Be(FatouTotal);
        modou.TotalDue.Should().Be(ModouTotal);
        modou.RemainingBalance.Should().Be(ModouTotal);

        // Le calendrier d'échéances se recompose depuis les lignes figées : il additionne exactement le dû.
        modou.Installments.Sum(i => i.Amount).Should().Be(ModouTotal);
        modou.Installments.Should().NotContain(i => i.Designation.Contains("Uniforme") || i.Designation.Contains("Tenue"));
        fatou.Installments.Should().Contain(i => i.Designation.Contains("Uniforme"));
        fatou.Installments.Should().NotContain(i => i.Designation.Contains("Tenue"));
        awa.Installments.Sum(i => i.Amount).Should().Be(AwaTotal);
    }

    // ------------------------------------------------------------------ Sommation

    [Fact]
    public async Task The_Dues_Notice_Does_Not_Claim_An_Unchecked_Fee()
    {
        await using var db = _db.NewAppContext(Ecole);
        var handler = new GetDuesNoticeQueryHandler(db, new FixedTime(Now));

        var modou = await handler.Handle(new GetDuesNoticeQuery(_modouEnrollment), CancellationToken.None);
        var awa = await handler.Handle(new GetDuesNoticeQuery(_awaEnrollment), CancellationToken.None);

        modou.TotalDue.Should().Be(ModouTotal);
        modou.RemainingBalance.Should().Be(ModouTotal);
        modou.OverdueInstallments.Should().NotContain(i => i.Designation.Contains("Uniforme") || i.Designation.Contains("Tenue"));

        awa.TotalDue.Should().Be(AwaTotal);
        awa.OverdueInstallments.Should().Contain(i => i.Designation.Contains("Uniforme"));
        awa.OverdueInstallments.Should().Contain(i => i.Designation.Contains("Tenue"));
    }

    // ------------------------------------------------------------------ Rapport des débiteurs

    [Fact]
    public async Task The_Debtor_Report_Shows_Each_Students_Own_Balance()
    {
        await using var db = _db.NewAppContext(Ecole);
        var handler = new GetDebtorAgingReportQueryHandler(db, new FixedTime(Now));

        var report = await handler.Handle(new GetDebtorAgingReportQuery(), CancellationToken.None);

        report.Debtors.Select(d => (d.StudentFullName, d.RemainingBalance)).Should().BeEquivalentTo(new[]
        {
            ("Awa Ndiaye", AwaTotal),
            ("Fatou Sow", FatouTotal),
            ("Modou Fall", ModouTotal)
        });
    }

    // ------------------------------------------------------------------ Recherche caisse

    [Fact]
    public async Task The_Cashier_Search_Quotes_The_Filtered_Due_And_The_Filtered_Amount_Due_Now()
    {
        await using var db = _db.NewAppContext(Ecole);
        var handler = new SearchStudentsForCashierQueryHandler(db);

        var results = await handler.Handle(new SearchStudentsForCashierQuery("elev"), CancellationToken.None);

        var modou = results.Single(r => r.FullName == "Modou Fall");
        modou.TotalDue.Should().Be(ModouTotal);
        modou.RemainingBalance.Should().Be(ModouTotal);
        // Engagement initial = frais ponctuels + 1er mois : sans uniforme ni tenue pour Modou.
        modou.DueNowTotal.Should().Be(Inscription + Mensualite);

        var awa = results.Single(r => r.FullName == "Awa Ndiaye");
        awa.TotalDue.Should().Be(AwaTotal);
        awa.DueNowTotal.Should().Be(Inscription + Uniforme + TenueSport + Mensualite);
    }

    // ------------------------------------------------------------------ Tableau de bord

    [Fact]
    public async Task The_Finance_Dashboard_Sums_The_Filtered_Annual_Dues()
    {
        await using var db = _db.NewAppContext(Ecole);
        var handler = new GetFinanceDashboardQueryHandler(db, new FixedTime(Now), new NoOpKpiCache());

        var dashboard = await handler.Handle(new GetFinanceDashboardQuery(), CancellationToken.None);

        dashboard.OutstandingBalance.Should().Be(AwaTotal + FatouTotal + ModouTotal, "rien n'est encore encaissé");
    }

    // ------------------------------------------------------------------ Échéancier de classe

    [Fact]
    public async Task A_Class_Installment_Plan_Is_Computed_On_Each_Students_Own_Filtered_Due()
    {
        await using (var db = _db.NewAppContext(Ecole))
        {
            var handler = new ApplyFeeInstallmentPlanToClassroomCommandHandler(db, new StubTenantProvider(Ecole), new FixedTime(Now));
            var result = await handler.Handle(new ApplyFeeInstallmentPlanToClassroomCommand(
                _classroomId, "Deux tranches",
                [new InstallmentTemplateLine("Tranche 1", 0.5m, 0), new InstallmentTemplateLine("Tranche 2", 0.5m, 30)]),
                CancellationToken.None);

            result.AppliedCount.Should().Be(3);
        }

        await using var check = _db.NewAppContext(Ecole);
        async Task<List<decimal>> AmountsOf(Guid enrollmentId) => await (
            from p in check.FeeInstallmentPlans
            join i in check.FeeInstallments on p.Id equals i.FeeInstallmentPlanId
            where p.EnrollmentId == enrollmentId && p.Status == FeeInstallmentPlanStatus.Active
            orderby i.SequenceNo
            select i.Amount).ToListAsync();

        (await AmountsOf(_modouEnrollment)).Should().Equal(72_500m, 72_500m);   // 145 000 / 2
        (await AmountsOf(_fatouEnrollment)).Should().Equal(85_000m, 85_000m);   // 170 000 / 2
        (await AmountsOf(_awaEnrollment)).Should().Equal(89_000m, 89_000m);     // 178 000 / 2
    }

    // ------------------------------------------------------------------ Protection des frais obligatoires

    [Fact]
    public async Task Making_A_Category_Mandatory_Later_Never_Rewrites_An_Existing_Enrollment()
    {
        await using (var db = _db.NewAppContext(Ecole))
        {
            await new UpdateFeeCategoryCommandHandler(db)
                .Handle(new UpdateFeeCategoryCommand(_uniformeId, IsOptional: false), CancellationToken.None);
        }

        await using var read = _db.NewAppContext(Ecole);
        var balance = await new GetStudentBalanceQueryHandler(read, new FixedTime(Now))
            .Handle(new GetStudentBalanceQuery(_modou), CancellationToken.None);

        balance.TotalDue.Should().Be(ModouTotal, "l'inscription est un instantané : le dû déjà posé ne bouge jamais (règle #4)");
    }

    [Fact]
    public async Task Once_A_Category_Is_Mandatory_It_Is_Billed_To_Every_New_Student_Whatever_The_Form_Sends()
    {
        await using (var db = _db.NewAppContext(Ecole))
        {
            await new UpdateFeeCategoryCommandHandler(db)
                .Handle(new UpdateFeeCategoryCommand(_uniformeId, IsOptional: false), CancellationToken.None);
        }

        // Un formulaire périmé (ancien onglet) décoche l'uniforme : il est devenu obligatoire, il est dû.
        var (studentId, _) = await EnrollAsync("Ibrahima Ba", [_tenueId]);

        await using var read = _db.NewAppContext(Ecole);
        var balance = await new GetStudentBalanceQueryHandler(read, new FixedTime(Now))
            .Handle(new GetStudentBalanceQuery(studentId), CancellationToken.None);

        balance.TotalDue.Should().Be(Mandatory + Uniforme + TenueSport);
    }

    [Fact]
    public async Task A_Mandatory_Fee_Cannot_Be_Removed_By_A_Forged_Payload()
    {
        await using var db = _db.NewAppContext(Ecole);
        var handler = new CreateEnrollmentCommandHandler(
            db, new StubTenantProvider(Ecole), _db.NewGenerator(db), TimeProvider.System,
            new NoOpKpiCache(), new TestCurrentUser(), _db.NewQuotaGuard(db, Ecole));

        // Ni la mensualité ni l'inscription ne se « cochent » ni ne se décochent : c'est refusé, pas ignoré.
        var act = async () => await handler.Handle(new CreateEnrollmentCommand
        {
            Type = EnrollmentType.NewEnrollment,
            ClassroomId = _classroomId,
            FullName = "Requête Forgée",
            BirthDate = new DateOnly(2013, 3, 3),
            BirthPlace = "Dakar",
            Gender = "F",
            OptionalFeeCategoryIds = [_mensualiteId]
        }, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    private sealed class StubTenantProvider(Guid? schoolId) : ITenantProvider
    {
        public Guid? CurrentSchoolId => schoolId;
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class NoOpKpiCache : IKpiCacheService
    {
        public Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken)
            => factory(cancellationToken);

        public void Invalidate(string key) { }
    }
}
