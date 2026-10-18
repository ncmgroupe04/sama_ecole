using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Classrooms.Deletion;
using SamaEcole.Application.Common.Deletion;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Subjects.Deletion;
using SamaEcole.Application.Teachers.Deletion;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Deletion;

/// <summary>
/// Suppression guidée des classes, matières et enseignants (spec 2026-10-17, §6) : blocage financier propagé, liens
/// énumérés précisément, rupture explicite par archivage, liens non rompables refusés, isolation entre écoles.
/// </summary>
[Trait("Category", "MultiTenant")]
public class EntityDeletionPoliciesTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("11111111-dddd-1111-1111-111111111111");
    private static readonly Guid AutreEcole = Guid.Parse("22222222-dddd-1111-1111-111111111111");
    private static readonly Guid Annee = Guid.Parse("bbbbbbbb-dddd-0000-0000-00000000000b");
    private static readonly Guid Classe = Guid.Parse("aaaaaaaa-dddd-0000-0000-00000000000a");
    private static readonly Guid ClasseLibre = Guid.Parse("aaaaaaaa-dddd-0000-0000-00000000000f");
    private static readonly Guid Eleve = Guid.Parse("eeeeeeee-dddd-0000-0000-00000000000e");
    private static readonly Guid Inscription = Guid.Parse("11111111-dddd-0000-0000-0000000000f1");
    private static readonly Guid Caissier = Guid.Parse("dddddddd-dddd-0000-0000-00000000000d");
    private static readonly Guid Matiere = Guid.Parse("55555555-dddd-0000-0000-000000000001");
    private static readonly Guid MatiereLibre = Guid.Parse("55555555-dddd-0000-0000-000000000002");
    private static readonly Guid Prof = Guid.Parse("77777777-dddd-0000-0000-000000000001");
    private static readonly Guid ProfLibre = Guid.Parse("77777777-dddd-0000-0000-000000000002");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();
        owner.Schools.Add(new School { Id = Ecole, Name = "École A" });
        owner.Schools.Add(new School { Id = AutreEcole, Name = "École B" });
        owner.Classrooms.Add(new Classroom { Id = Classe, SchoolId = Ecole, Name = "CM2", Level = "Primaire", Capacity = 40 });
        owner.Classrooms.Add(new Classroom { Id = ClasseLibre, SchoolId = Ecole, Name = "CM1", Level = "Primaire", Capacity = 40 });
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
        owner.Subjects.Add(new Subject { Id = Matiere, SchoolId = Ecole, Name = "Mathématiques", Level = "Primaire", Coefficient = 2m });
        owner.Subjects.Add(new Subject { Id = MatiereLibre, SchoolId = Ecole, Name = "Dessin", Level = "Primaire", Coefficient = 1m });
        owner.Teachers.Add(new Teacher
        {
            Id = Prof, SchoolId = Ecole, Matricule = "ENS-2026-001", FullName = "Mame Diarra", Email = "mame@ecole-a.sn",
            BirthDate = new DateOnly(1985, 3, 3)
        });
        owner.Teachers.Add(new Teacher
        {
            Id = ProfLibre, SchoolId = Ecole, Matricule = "ENS-2026-002", FullName = "Ibra Sow", Email = "ibra@ecole-a.sn",
            BirthDate = new DateOnly(1980, 1, 1)
        });
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private async Task<DeletionImpact> AnalyzeAsync(EntityDeletionPolicy policy, Guid id, Guid? school = null)
    {
        await using var ctx = _db.NewAppContext(school ?? Ecole);
        return await EntityDeletionAnalyzer.AnalyzeAsync(ctx, policy, id, CancellationToken.None);
    }

    private async Task<DeletionImpact> UnlinkAsync(EntityDeletionPolicy policy, Guid id)
    {
        await using var ctx = _db.NewAppContext(Ecole);
        return await EntityDeletionAnalyzer.UnlinkAsync(ctx, policy, id, "directeur", CancellationToken.None);
    }

    private async Task AddPaymentAsync(PaymentStatus status)
    {
        await using var owner = _db.NewOwnerContext();
        var payment = new Payment
        {
            SchoolId = Ecole, EnrollmentId = Inscription, Amount = 30_000m, Method = PaymentMethod.Cash, Status = status,
            BalanceAfter = 70_000m, ReceiptNumber = "REC-1", ReceivedByUserId = Caissier, PaidAt = DateTimeOffset.UtcNow
        };
        if (status == PaymentStatus.Cancelled)
        {
            payment.CancelledAt = DateTimeOffset.UtcNow;
            payment.CancellationOutcome = PaymentCancellationOutcome.KeptBySchool;
        }
        owner.Payments.Add(payment);
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    private async Task ArchiveStudentAsync()
    {
        await using var owner = _db.NewOwnerContext();
        var student = await owner.Students.IgnoreQueryFilters().SingleAsync(s => s.Id == Eleve);
        student.SoftDelete("directeur");
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    // ------------------------------------------------------------------------------------------ Classes

    [Fact]
    public async Task A_Classroom_With_An_Active_Payment_Is_Financially_Blocked()
    {
        await AddPaymentAsync(PaymentStatus.Partial);

        var impact = await AnalyzeAsync(ClassroomDeletionPolicy.Policy, Classe);

        impact.FinancialBlock.Should().BeTrue();
        impact.CanDelete.Should().BeFalse();
        impact.Message.Should().Be(ClassroomDeletionPolicy.FinancialBlock);
        impact.Links.Should().BeEmpty();
    }

    [Fact]
    public async Task A_Cancelled_Payment_Does_Not_Block_A_Classroom_And_Attached_Students_Are_Not_Breakable()
    {
        await AddPaymentAsync(PaymentStatus.Cancelled);

        var impact = await AnalyzeAsync(ClassroomDeletionPolicy.Policy, Classe);

        impact.FinancialBlock.Should().BeFalse();
        impact.Links.Should().ContainSingle(l => l.Kind == "Student" && !l.Breakable).Which.Label.Should().Be("1 élève rattaché");
        impact.Links.Should().ContainSingle(l => l.Kind == "Enrollment" && l.Breakable);
        impact.Message.Should().StartWith("Cette classe est actuellement rattachée à ");
    }

    [Fact]
    public async Task Unlinking_A_Classroom_Is_Refused_While_Students_Are_Attached()
    {
        var act = async () => await UnlinkAsync(ClassroomDeletionPolicy.Policy, Classe);

        var error = (await act.Should().ThrowAsync<BusinessRuleException>()).Which;
        error.Code.Should().Be(DeletionErrorCodes.DependenciesNotBreakable);
        error.Message.Should().Contain("1 élève rattaché").And.Contain("Transférez ces élèves");

        await using var check = _db.NewAppContext(Ecole);
        (await check.Enrollments.CountAsync(e => e.ClassroomId == Classe)).Should().Be(1, "rien n'est rompu tant que des élèves sont rattachés");
    }

    [Fact]
    public async Task Unlinking_A_Classroom_Archives_Its_Links_Once_No_Student_Is_Attached()
    {
        await ArchiveStudentAsync();
        await using (var owner = _db.NewOwnerContext())
        {
            var category = new FeeCategory { Id = Guid.NewGuid(), SchoolId = Ecole, Name = "Scolarité" };
            owner.FeeCategories.Add(category);
            owner.ClassFees.Add(new ClassFee { SchoolId = Ecole, FeeCategoryId = category.Id, ClassroomId = Classe, Amount = 50_000m });
            owner.ClassSubjects.Add(new ClassSubject { SchoolId = Ecole, ClassroomId = Classe, SubjectId = Matiere });
            owner.TeacherAssignments.Add(new TeacherAssignment
            {
                SchoolId = Ecole, TeacherId = Prof, ClassroomId = Classe, SubjectId = Matiere, SchoolYearId = Annee
            });
            await owner.SaveChangesAsync(CancellationToken.None);
        }

        var before = await AnalyzeAsync(ClassroomDeletionPolicy.Policy, Classe);
        before.Links.Select(l => l.Kind).Should().BeEquivalentTo(["Enrollment", "ClassSubject", "TeacherAssignment", "ClassFee"]);

        var after = await UnlinkAsync(ClassroomDeletionPolicy.Policy, Classe);

        after.CanDelete.Should().BeTrue();

        await using var owner2 = _db.NewOwnerContext();
        (await owner2.ClassFees.IgnoreQueryFilters().SingleAsync(f => f.ClassroomId == Classe)).IsDeleted.Should().BeTrue("archivé, pas effacé");
        (await owner2.Enrollments.IgnoreQueryFilters().SingleAsync(e => e.Id == Inscription)).DeletedBy.Should().Be("directeur");
    }

    [Fact]
    public async Task A_Classroom_Without_Any_Link_Can_Be_Deleted()
    {
        (await AnalyzeAsync(ClassroomDeletionPolicy.Policy, ClasseLibre)).CanDelete.Should().BeTrue();
    }

    // ------------------------------------------------------------------------------------------ Matières

    [Fact]
    public async Task A_Subject_Lists_Its_Links_And_Its_Activities_Are_Not_Breakable()
    {
        await using (var owner = _db.NewOwnerContext())
        {
            owner.ClassSubjects.Add(new ClassSubject { SchoolId = Ecole, ClassroomId = Classe, SubjectId = Matiere });
            owner.TeacherSubjects.Add(new TeacherSubject { SchoolId = Ecole, TeacherId = Prof, SubjectId = Matiere });
            owner.Subjects.Add(new Subject
            {
                Id = Guid.NewGuid(), SchoolId = Ecole, Name = "Calcul mental", Level = "Primaire", Coefficient = 1m, ParentSubjectId = Matiere
            });
            await owner.SaveChangesAsync(CancellationToken.None);
        }

        var impact = await AnalyzeAsync(SubjectDeletionPolicy.Policy, Matiere);

        impact.FinancialBlock.Should().BeFalse();
        impact.Links.Select(l => l.Kind).Should().BeEquivalentTo(["ChildSubject", "ClassSubject", "TeacherSubject"]);
        impact.Links.Single(l => l.Kind == "ChildSubject").Breakable.Should().BeFalse();
        impact.Message.Should().StartWith("Cette matière est actuellement rattachée à ");

        var act = async () => await UnlinkAsync(SubjectDeletionPolicy.Policy, Matiere);
        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be(DeletionErrorCodes.DependenciesNotBreakable);
    }

    [Fact]
    public async Task Unlinking_A_Subject_Archives_Its_Links()
    {
        await using (var owner = _db.NewOwnerContext())
        {
            owner.ClassSubjects.Add(new ClassSubject { SchoolId = Ecole, ClassroomId = Classe, SubjectId = Matiere });
            owner.TeacherSubjects.Add(new TeacherSubject { SchoolId = Ecole, TeacherId = Prof, SubjectId = Matiere });
            await owner.SaveChangesAsync(CancellationToken.None);
        }

        var after = await UnlinkAsync(SubjectDeletionPolicy.Policy, Matiere);

        after.CanDelete.Should().BeTrue();
        await using var owner2 = _db.NewOwnerContext();
        (await owner2.ClassSubjects.IgnoreQueryFilters().SingleAsync(c => c.SubjectId == Matiere)).IsDeleted.Should().BeTrue();
        (await owner2.TeacherSubjects.IgnoreQueryFilters().SingleAsync(t => t.SubjectId == Matiere)).IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task A_Subject_Without_Any_Link_Can_Be_Deleted()
    {
        (await AnalyzeAsync(SubjectDeletionPolicy.Policy, MatiereLibre)).CanDelete.Should().BeTrue();
    }

    // ------------------------------------------------------------------------------------------ Enseignants

    private async Task<Guid> AddContractAsync(Guid teacher)
    {
        await using var owner = _db.NewOwnerContext();
        var contract = new EmployeeContract
        {
            Id = Guid.NewGuid(), SchoolId = Ecole, TeacherId = teacher, Type = ContractType.Permanent, BaseSalary = 150_000m
        };
        owner.EmployeeContracts.Add(contract);
        await owner.SaveChangesAsync(CancellationToken.None);
        return contract.Id;
    }

    [Fact]
    public async Task A_Teacher_With_A_Payslip_Is_Financially_Blocked()
    {
        var contract = await AddContractAsync(Prof);
        await using (var owner = _db.NewOwnerContext())
        {
            owner.FichePaies.Add(new FichePaie
            {
                SchoolId = Ecole, EmployeeContractId = contract, Month = 10, Year = 2026, GrossSalary = 150_000m, NetSalary = 130_000m
            });
            await owner.SaveChangesAsync(CancellationToken.None);
        }

        var impact = await AnalyzeAsync(TeacherDeletionPolicy.Policy, Prof);

        impact.FinancialBlock.Should().BeTrue();
        impact.Message.Should().Be(TeacherDeletionPolicy.FinancialBlock);
        impact.Links.Should().BeEmpty();
    }

    [Fact]
    public async Task Unlinking_A_Teacher_Archives_The_Contract_And_The_Assignments()
    {
        await AddContractAsync(Prof);
        await using (var owner = _db.NewOwnerContext())
        {
            owner.TeacherAssignments.Add(new TeacherAssignment
            {
                SchoolId = Ecole, TeacherId = Prof, ClassroomId = Classe, SubjectId = Matiere, SchoolYearId = Annee
            });
            await owner.SaveChangesAsync(CancellationToken.None);
        }

        var before = await AnalyzeAsync(TeacherDeletionPolicy.Policy, Prof);
        before.Links.Select(l => l.Kind).Should().BeEquivalentTo(["EmployeeContract", "TeacherAssignment"]);
        before.Message.Should().StartWith("Cet enseignant est actuellement rattaché à ");

        var after = await UnlinkAsync(TeacherDeletionPolicy.Policy, Prof);

        after.CanDelete.Should().BeTrue();
        await using var owner2 = _db.NewOwnerContext();
        (await owner2.EmployeeContracts.IgnoreQueryFilters().SingleAsync(c => c.TeacherId == Prof)).IsDeleted.Should().BeTrue("archivé, pas effacé");
    }

    [Fact]
    public async Task A_Teacher_Without_Any_Link_Can_Be_Deleted()
    {
        (await AnalyzeAsync(TeacherDeletionPolicy.Policy, ProfLibre)).CanDelete.Should().BeTrue();
    }

    // ------------------------------------------------------------------------------------------ Isolation

    [Fact]
    public async Task Another_School_Cannot_Analyze_Or_Unlink_These_Elements()
    {
        foreach (var (policy, id) in new[]
                 {
                     (ClassroomDeletionPolicy.Policy, Classe),
                     (SubjectDeletionPolicy.Policy, Matiere),
                     (TeacherDeletionPolicy.Policy, Prof)
                 })
        {
            var analyze = async () => await AnalyzeAsync(policy, id, AutreEcole);
            await analyze.Should().ThrowAsync<KeyNotFoundException>(policy.Resource);
        }
    }
}
