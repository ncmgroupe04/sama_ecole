using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Deletion;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.SchoolYears.Deletion;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Deletion;

/// <summary>
/// Suppression guidée d'une année scolaire en mode réel (spec 2026-10-17, §6) : blocage financier (paiement non annulé),
/// liens énumérés puis rompus explicitement (inscriptions, notes, examens), configuration intrinsèque, isolation.
/// </summary>
[Trait("Category", "MultiTenant")]
public class SchoolYearDeletionPolicyTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("11111111-aaaa-1111-1111-111111111111");
    private static readonly Guid AutreEcole = Guid.Parse("22222222-aaaa-1111-1111-111111111111");
    private static readonly Guid Annee = Guid.Parse("bbbbbbbb-aaaa-0000-0000-00000000000b");
    private static readonly Guid AnneeLibre = Guid.Parse("bbbbbbbb-aaaa-0000-0000-00000000000c");
    private static readonly Guid Classe = Guid.Parse("aaaaaaaa-aaaa-0000-0000-00000000000a");
    private static readonly Guid Eleve = Guid.Parse("eeeeeeee-aaaa-0000-0000-00000000000e");
    private static readonly Guid Matiere = Guid.Parse("55555555-aaaa-0000-0000-000000000005");
    private static readonly Guid Inscription = Guid.Parse("11111111-aaaa-0000-0000-0000000000f1");
    private static readonly Guid Trimestre = Guid.Parse("77777777-aaaa-0000-0000-000000000007");
    private static readonly Guid Caissier = Guid.Parse("dddddddd-aaaa-0000-0000-00000000000d");

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
        owner.SchoolYears.Add(new SchoolYear
        {
            Id = AnneeLibre, SchoolId = Ecole, Label = "2027-2028",
            StartDate = new DateOnly(2027, 10, 1), EndDate = new DateOnly(2028, 6, 30)
        });
        owner.Students.Add(new Student
        {
            Id = Eleve, SchoolId = Ecole, Matricule = "ELEV-2026-0001", FullName = "Awa Fall",
            BirthDate = new DateOnly(2015, 5, 20), BirthPlace = "Dakar", Gender = "F", ClassroomId = Classe
        });
        owner.Subjects.Add(new Subject { Id = Matiere, SchoolId = Ecole, Name = "Maths", Level = "Primaire", Coefficient = 2 });
        owner.Enrollments.Add(new Enrollment
        {
            Id = Inscription, SchoolId = Ecole, StudentId = Eleve, SchoolYearId = Annee, ClassroomId = Classe,
            Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed,
            TotalDue = 100_000m, AmountPaid = 0m, ReceiptNumber = "REC-2026-0001", EnrolledAt = DateTimeOffset.UtcNow
        });
        owner.Terms.Add(new Term
        {
            Id = Trimestre, SchoolId = Ecole, SchoolYearId = Annee, Label = "Trimestre 1", Order = 1,
            StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2026, 12, 20)
        });
        owner.Users.Add(new User
        {
            Id = Caissier, SchoolId = Ecole, Email = "c@ecole-a.sn", PasswordHash = "h", FullName = "Caissier", Role = Role.Finance
        });
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private async Task<DeletionImpact> AnalyzeAsync(Guid id, Guid? school = null)
    {
        await using var ctx = _db.NewAppContext(school ?? Ecole);
        return await EntityDeletionAnalyzer.AnalyzeAsync(ctx, SchoolYearDeletionPolicy.Policy, id, CancellationToken.None);
    }

    private async Task<DeletionImpact> UnlinkAsync(Guid id)
    {
        await using var ctx = _db.NewAppContext(Ecole);
        return await EntityDeletionAnalyzer.UnlinkAsync(ctx, SchoolYearDeletionPolicy.Policy, id, "directeur", CancellationToken.None);
    }

    private async Task AddPaymentAsync(PaymentStatus status)
    {
        await using var owner = _db.NewOwnerContext();
        var payment = new Payment
        {
            Id = Guid.NewGuid(), SchoolId = Ecole, EnrollmentId = Inscription, Amount = 30_000m, Method = PaymentMethod.Cash,
            Status = status, BalanceAfter = 70_000m, ReceiptNumber = "REC-" + Guid.NewGuid().ToString()[..6],
            ReceivedByUserId = Caissier, PaidAt = DateTimeOffset.UtcNow
        };
        if (status == PaymentStatus.Cancelled)
        {
            payment.CancelledAt = DateTimeOffset.UtcNow;
            payment.CancelledByUserId = Caissier;
            payment.CancellationReason = "Erreur de saisie";
        }
        owner.Payments.Add(payment);
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    [Fact]
    public async Task A_Year_Without_Data_Is_Deletable_Because_Its_Configuration_Is_Intrinsic()
    {
        (await AnalyzeAsync(AnneeLibre)).CanDelete.Should().BeTrue();
    }

    [Fact]
    public async Task A_Year_With_An_Active_Payment_Is_Blocked_Financially_And_Cannot_Be_Unlinked()
    {
        await AddPaymentAsync(PaymentStatus.Partial);

        var impact = await AnalyzeAsync(Annee);

        impact.FinancialBlock.Should().BeTrue();
        impact.CanDelete.Should().BeFalse();
        impact.Message.Should().Be(SchoolYearDeletionPolicy.FinancialBlock);

        var act = async () => await UnlinkAsync(Annee);
        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be(DeletionErrorCodes.FinancialRecordsExist);
    }

    [Fact]
    public async Task A_Cancelled_Payment_No_Longer_Blocks_The_Year()
    {
        await AddPaymentAsync(PaymentStatus.Cancelled);

        var impact = await AnalyzeAsync(Annee);

        impact.FinancialBlock.Should().BeFalse();
        impact.Links.Should().ContainSingle(l => l.Kind == "Enrollment" && l.Breakable);
    }

    [Fact]
    public async Task Enrollments_Grades_And_Exam_Sessions_Are_Listed_Then_Archived_By_The_Explicit_Unlink()
    {
        await using (var owner = _db.NewOwnerContext())
        {
            owner.Grades.Add(new Grade
            {
                SchoolId = Ecole, StudentId = Eleve, SubjectId = Matiere, TermId = Trimestre,
                EvaluationType = EvaluationType.Devoir1, Value = 14m
            });
            var session = new ExamSession { Id = Guid.NewGuid(), SchoolId = Ecole, SchoolYearId = Annee, ExamType = ExamType.CFEE };
            owner.ExamSessions.Add(session);
            owner.ExamDossiers.Add(new ExamDossier
            {
                SchoolId = Ecole, ExamSessionId = session.Id, StudentId = Eleve, ClassroomId = Classe
            });
            await owner.SaveChangesAsync(CancellationToken.None);
        }

        var before = await AnalyzeAsync(Annee);
        before.CanDelete.Should().BeFalse();
        before.Links.Select(l => l.Kind).Should().BeEquivalentTo("Enrollment", "Grade", "ExamSession");
        before.Links.Should().OnlyContain(l => l.Breakable);
        before.Message.Should().StartWith("Cette année scolaire est actuellement rattachée");

        var after = await UnlinkAsync(Annee);
        after.CanDelete.Should().BeTrue();

        await using var check = _db.NewOwnerContext();
        (await check.Enrollments.IgnoreQueryFilters().SingleAsync(e => e.Id == Inscription)).IsDeleted.Should().BeTrue("archivée, pas effacée");
        (await check.Grades.IgnoreQueryFilters().SingleAsync(g => g.TermId == Trimestre)).IsDeleted.Should().BeTrue();
        (await check.ExamSessions.IgnoreQueryFilters().SingleAsync(s => s.SchoolYearId == Annee)).IsDeleted.Should().BeTrue();
        (await check.ExamDossiers.IgnoreQueryFilters().SingleAsync(d => d.StudentId == Eleve)).IsDeleted.Should().BeTrue();
        (await check.Terms.IgnoreQueryFilters().SingleAsync(t => t.Id == Trimestre)).IsDeleted
            .Should().BeFalse("le trimestre est intrinsèque : il n'est archivé qu'avec l'année, par le handler de suppression");
    }

    [Fact]
    public async Task Archiving_The_Intrinsic_Configuration_Archives_Terms_Assignments_And_Closures()
    {
        await using (var ctx = _db.NewAppContext(Ecole))
        {
            await EntityDeletionAnalyzer.ArchiveIntrinsicAsync(
                ctx, SchoolYearDeletionPolicy.Policy, Annee, "directeur", CancellationToken.None);
            await ctx.SaveChangesAsync(CancellationToken.None);
        }

        await using var check = _db.NewOwnerContext();
        (await check.Terms.IgnoreQueryFilters().SingleAsync(t => t.Id == Trimestre)).IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task Another_Schools_Year_Is_Not_Found()
    {
        var act = async () => await AnalyzeAsync(Annee, AutreEcole);
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
