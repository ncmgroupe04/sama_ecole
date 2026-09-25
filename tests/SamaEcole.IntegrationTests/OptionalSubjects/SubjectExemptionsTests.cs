using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.OptionalSubjects;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.OptionalSubjects;

/// <summary>
/// Quelles matières un élève ne suit pas (et quels élèves ne suivent pas une matière) ? Deux sortes de
/// dispenses : une OPTION non suivie (sans motif) et une matière OBLIGATOIRE dispensée (avec motif).
/// </summary>
[Trait("Category", "MultiTenant")]
public class SubjectExemptionsTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("94444444-4444-4444-4444-444444444444");
    private static readonly Guid Autre = Guid.Parse("95555555-5555-5555-5555-555555555555");
    private static readonly Guid Annee1 = Guid.Parse("94444444-0000-0000-0000-000000000001");
    private static readonly Guid Annee0 = Guid.Parse("94444444-0000-0000-0000-000000000002");
    private static readonly Guid AnneeAutre = Guid.Parse("95555555-0000-0000-0000-000000000001");
    private static readonly Guid Classe = Guid.Parse("94444444-0000-0000-0000-0000000000c1");
    private static readonly Guid ClasseAutre = Guid.Parse("95555555-0000-0000-0000-0000000000c1");
    private static readonly Guid Arabe = Guid.Parse("94444444-0000-0000-0000-0000000000a1");
    private static readonly Guid Eps = Guid.Parse("94444444-0000-0000-0000-0000000000a2");
    private static readonly Guid ArabeAutre = Guid.Parse("95555555-0000-0000-0000-0000000000a1");

    private static readonly Guid EleveDispense = Guid.Parse("94444444-0000-0000-0000-0000000000e1");
    private static readonly Guid EleveAnnule = Guid.Parse("94444444-0000-0000-0000-0000000000e2");
    private static readonly Guid EleveLibre = Guid.Parse("94444444-0000-0000-0000-0000000000e3");
    private static readonly Guid EleveEps = Guid.Parse("94444444-0000-0000-0000-0000000000e4");
    private static readonly Guid EleveEpsSansMotif = Guid.Parse("94444444-0000-0000-0000-0000000000e5");
    private static readonly Guid EleveAutre = Guid.Parse("95555555-0000-0000-0000-0000000000e1");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();

        owner.Schools.AddRange(new School { Id = Ecole, Name = "A" }, new School { Id = Autre, Name = "B" });
        owner.SchoolYears.AddRange(
            new SchoolYear { Id = Annee1, SchoolId = Ecole, Label = "2026-2027", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true },
            new SchoolYear { Id = Annee0, SchoolId = Ecole, Label = "2025-2026", StartDate = new DateOnly(2025, 9, 1), EndDate = new DateOnly(2026, 6, 30) },
            new SchoolYear { Id = AnneeAutre, SchoolId = Autre, Label = "2026-2027", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true });
        owner.Classrooms.AddRange(
            new Classroom { Id = Classe, SchoolId = Ecole, Name = "4ème A", Level = "Collège", Cycle = CycleType.College, Capacity = 40 },
            new Classroom { Id = ClasseAutre, SchoolId = Autre, Name = "4ème A", Level = "Collège", Cycle = CycleType.College, Capacity = 40 });
        owner.Subjects.AddRange(
            new Subject { Id = Arabe, SchoolId = Ecole, Name = "Arabe", Level = "Collège", Coefficient = 2, IsOptional = true, OptionGroup = "LV2" },
            new Subject { Id = Eps, SchoolId = Ecole, Name = "EPS", Level = "Collège", Coefficient = 1 },
            new Subject { Id = ArabeAutre, SchoolId = Autre, Name = "Arabe", Level = "Collège", Coefficient = 2, IsOptional = true, OptionGroup = "LV2" });
        owner.Students.AddRange(
            Student(EleveDispense, Ecole, "E1", Classe), Student(EleveAnnule, Ecole, "E2", Classe),
            Student(EleveLibre, Ecole, "E3", Classe), Student(EleveEps, Ecole, "E4", Classe),
            Student(EleveEpsSansMotif, Ecole, "E5", Classe), Student(EleveAutre, Autre, "E6", ClasseAutre));

        var inscriptionDispense = Enrollment(EleveDispense, Ecole, Annee1, Classe, EnrollmentStatus.Confirmed, "R1");
        var inscriptionAnnulee = Enrollment(EleveAnnule, Ecole, Annee1, Classe, EnrollmentStatus.Cancelled, "R2");
        var inscriptionLibre = Enrollment(EleveLibre, Ecole, Annee1, Classe, EnrollmentStatus.Confirmed, "R3");
        var inscriptionEps = Enrollment(EleveEps, Ecole, Annee1, Classe, EnrollmentStatus.Confirmed, "R4");
        var inscriptionEpsSansMotif = Enrollment(EleveEpsSansMotif, Ecole, Annee1, Classe, EnrollmentStatus.Confirmed, "R5");
        var inscriptionAutre = Enrollment(EleveAutre, Autre, AnneeAutre, ClasseAutre, EnrollmentStatus.Confirmed, "R6");
        owner.Enrollments.AddRange(
            inscriptionDispense, inscriptionAnnulee, inscriptionLibre, inscriptionEps, inscriptionEpsSansMotif, inscriptionAutre);

        owner.EnrollmentSubjectExemptions.AddRange(
            new EnrollmentSubjectExemption { SchoolId = Ecole, EnrollmentId = inscriptionDispense.Id, SubjectId = Arabe },
            new EnrollmentSubjectExemption { SchoolId = Ecole, EnrollmentId = inscriptionAnnulee.Id, SubjectId = Arabe },
            new EnrollmentSubjectExemption { SchoolId = Ecole, EnrollmentId = inscriptionEps.Id, SubjectId = Eps, Reason = "Inaptitude médicale" },
            new EnrollmentSubjectExemption { SchoolId = Ecole, EnrollmentId = inscriptionEpsSansMotif.Id, SubjectId = Eps },
            new EnrollmentSubjectExemption { SchoolId = Autre, EnrollmentId = inscriptionAutre.Id, SubjectId = ArabeAutre });

        await owner.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task An_Option_Not_Followed_Is_Reported_As_Hidden_For_The_Year_Of_His_Enrollment()
    {
        await using var db = _db.NewAppContext(Ecole);

        var exemptions = await SubjectExemptions.ForStudentAsync(db, EleveDispense, Annee1, default);

        exemptions.Subjects.Select(s => s.SubjectId).Should().BeEquivalentTo([Arabe]);
        exemptions.HiddenIds.Should().BeEquivalentTo([Arabe]);
        exemptions.Mandatory.Should().BeEmpty("une option non suivie disparaît du bulletin, elle n'y est pas marquée");
        exemptions.Contains(Arabe).Should().BeTrue();
    }

    [Fact]
    public async Task A_Mandatory_Subject_With_A_Reason_Is_Reported_As_Mandatory_With_Its_Name_And_Coefficient()
    {
        await using var db = _db.NewAppContext(Ecole);

        var exemptions = await SubjectExemptions.ForStudentAsync(db, EleveEps, Annee1, default);

        exemptions.Mandatory.Should().ContainSingle()
            .Which.Should().Match<ExemptSubject>(s => s.SubjectId == Eps && s.Name == "EPS" && s.Coefficient == 1m && s.IsMandatory);
        exemptions.HiddenIds.Should().BeEmpty("une matière obligatoire dispensée reste sur le bulletin, marquée");
        exemptions.Contains(Eps).Should().BeTrue("elle n'entre plus dans les moyennes ni dans la saisie");
    }

    [Fact]
    public async Task A_Row_Without_A_Reason_On_A_Mandatory_Subject_Is_Inert()
    {
        await using var db = _db.NewAppContext(Ecole);

        var exemptions = await SubjectExemptions.ForStudentAsync(db, EleveEpsSansMotif, Annee1, default);

        exemptions.IsEmpty.Should().BeTrue("sans motif, ce n'est pas une dispense valide d'une matière obligatoire");
    }

    [Fact]
    public async Task A_Student_Without_Any_Exemption_Follows_Everything()
    {
        await using var db = _db.NewAppContext(Ecole);

        (await SubjectExemptions.ForStudentAsync(db, EleveLibre, Annee1, default)).IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task An_Exemption_Never_Applies_To_Another_School_Year()
    {
        await using var db = _db.NewAppContext(Ecole);

        (await SubjectExemptions.ForStudentAsync(db, EleveDispense, Annee0, default)).IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task A_Cancelled_Enrollment_Exempts_Nobody()
    {
        await using var db = _db.NewAppContext(Ecole);

        (await SubjectExemptions.ForStudentAsync(db, EleveAnnule, Annee1, default)).IsEmpty.Should().BeTrue();
        (await SubjectExemptions.StudentsExemptFromAsync(db, Arabe, Annee1, default)).Should().BeEquivalentTo([EleveDispense]);
    }

    [Fact]
    public async Task Students_Exempt_From_A_Subject_Include_Both_Kinds_And_Skip_The_Inert_Row()
    {
        await using var db = _db.NewAppContext(Ecole);

        (await SubjectExemptions.StudentsExemptFromAsync(db, Eps, Annee1, default)).Should().BeEquivalentTo([EleveEps]);
    }

    [Fact]
    public async Task An_Option_Row_Is_Ignored_Once_The_Subject_Is_No_Longer_Optional()
    {
        await using (var owner = _db.NewOwnerContext())
        {
            (await owner.Subjects.IgnoreQueryFilters().SingleAsync(s => s.Id == Arabe)).IsOptional = false;
            await owner.SaveChangesAsync();
        }

        await using var db = _db.NewAppContext(Ecole);

        (await SubjectExemptions.ForStudentAsync(db, EleveDispense, Annee1, default)).IsEmpty.Should().BeTrue(
            "repasser une matière en « obligatoire » la rend à tous : la ligne, sans motif, devient inerte");
        (await SubjectExemptions.StudentsExemptFromAsync(db, Arabe, Annee1, default)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_Soft_Deleted_Exemption_Is_Ignored()
    {
        await using (var owner = _db.NewOwnerContext())
        {
            var row = owner.EnrollmentSubjectExemptions.IgnoreQueryFilters().Single(x => x.SchoolId == Ecole && x.SubjectId == Arabe && !x.IsDeleted
                && owner.Enrollments.IgnoreQueryFilters().Any(e => e.Id == x.EnrollmentId && e.StudentId == EleveDispense));
            row.SoftDelete("test");
            await owner.SaveChangesAsync();
        }

        await using var db = _db.NewAppContext(Ecole);

        (await SubjectExemptions.ForStudentAsync(db, EleveDispense, Annee1, default)).IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task Another_School_Never_Sees_These_Exemptions()
    {
        await using var db = _db.NewAppContext(Autre);

        (await SubjectExemptions.StudentsExemptFromAsync(db, Arabe, Annee1, default)).Should().BeEmpty();
        (await SubjectExemptions.ForStudentAsync(db, EleveDispense, Annee1, default)).IsEmpty.Should().BeTrue();
        (await SubjectExemptions.StudentsExemptFromAsync(db, ArabeAutre, AnneeAutre, default)).Should().BeEquivalentTo([EleveAutre]);
    }

    private static Student Student(Guid id, Guid school, string matricule, Guid classroom) => new()
    {
        Id = id, SchoolId = school, Matricule = matricule, FullName = matricule,
        BirthDate = new DateOnly(2011, 1, 1), BirthPlace = "Dakar", Gender = "F", ClassroomId = classroom
    };

    private static Enrollment Enrollment(Guid student, Guid school, Guid year, Guid classroom, EnrollmentStatus status, string receipt) => new()
    {
        SchoolId = school, StudentId = student, SchoolYearId = year, ClassroomId = classroom,
        Type = EnrollmentType.NewEnrollment, Status = status, ReceiptNumber = receipt
    };
}
