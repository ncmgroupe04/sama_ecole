using FluentAssertions;
using SamaEcole.Application.ClassSubjects;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;
using ValidationException = SamaEcole.Application.Common.Exceptions.ValidationException;

namespace SamaEcole.IntegrationTests.Exemptions;

/// <summary>
/// La dispense est une SECONDE source d'exclusion de <see cref="SubjectFollowScope"/> : matières exclues, élèves
/// autorisés sur une matière, garde d'écriture d'une note. École : « 4ème A » AVEC programme (Maths et EPS communs,
/// Espagnol et Arabe en option « LV2 ») et « 5ème A » SANS programme.
/// </summary>
[Trait("Category", "MultiTenant")]
public class SubjectFollowScopeExemptionTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("c1111111-1111-1111-1111-111111111111");
    private static readonly Guid Annee = Guid.Parse("c1111111-0000-0000-0000-00000000000a");
    private static readonly Guid Trimestre = Guid.Parse("c1111111-0000-0000-0000-00000000000b");

    private static readonly Guid Classe = Guid.Parse("c1111111-0000-0000-0000-0000000000c1");
    private static readonly Guid ClasseSansProgramme = Guid.Parse("c1111111-0000-0000-0000-0000000000c2");

    private static readonly Guid Maths = Guid.Parse("c1111111-0000-0000-0000-0000000000b1");
    private static readonly Guid Eps = Guid.Parse("c1111111-0000-0000-0000-0000000000b2");
    private static readonly Guid Espagnol = Guid.Parse("c1111111-0000-0000-0000-0000000000b3");
    private static readonly Guid Arabe = Guid.Parse("c1111111-0000-0000-0000-0000000000b4");
    private static readonly Guid MathsCinquieme = Guid.Parse("c1111111-0000-0000-0000-0000000000b5");

    private static readonly Guid EleveA = Guid.Parse("c1111111-0000-0000-0000-0000000000e1");
    private static readonly Guid EleveB = Guid.Parse("c1111111-0000-0000-0000-0000000000e2");
    private static readonly Guid EleveC = Guid.Parse("c1111111-0000-0000-0000-0000000000e3");
    private static readonly Guid EleveD = Guid.Parse("c1111111-0000-0000-0000-0000000000e4");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();

        owner.Schools.Add(new School { Id = Ecole, Name = "École de test" });
        owner.SchoolYears.Add(new SchoolYear
        {
            Id = Annee, SchoolId = Ecole, Label = "2026-2027",
            StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true
        });
        owner.Terms.Add(new Term
        {
            Id = Trimestre, SchoolId = Ecole, SchoolYearId = Annee, Label = "1er trimestre", Order = 1,
            StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 12, 20)
        });
        owner.Classrooms.AddRange(
            new Classroom { Id = Classe, SchoolId = Ecole, Name = "4ème A", Level = "Collège", Cycle = CycleType.College, Capacity = 40 },
            new Classroom { Id = ClasseSansProgramme, SchoolId = Ecole, Name = "5ème A", Level = "Collège", Cycle = CycleType.College, Capacity = 40 });
        owner.Subjects.AddRange(
            new Subject { Id = Maths, SchoolId = Ecole, Name = "Mathématiques", Level = "Collège", Coefficient = 4 },
            new Subject { Id = Eps, SchoolId = Ecole, Name = "EPS", Level = "Collège", Coefficient = 1 },
            new Subject { Id = Espagnol, SchoolId = Ecole, Name = "Espagnol", Level = "Collège", Coefficient = 2 },
            new Subject { Id = Arabe, SchoolId = Ecole, Name = "Arabe", Level = "Collège", Coefficient = 2 },
            new Subject { Id = MathsCinquieme, SchoolId = Ecole, Name = "Mathématiques 5e", Level = "Collège", Coefficient = 4 });

        var maths = Program(Maths, 1);
        var eps = Program(Eps, 2);
        var espagnol = Program(Espagnol, 3, "LV2");
        var arabe = Program(Arabe, 4, "LV2");
        owner.ClassSubjects.AddRange(maths, eps, espagnol, arabe);

        owner.Students.AddRange(
            Student(EleveA, "ELEV-1", "Awa", Classe),
            Student(EleveB, "ELEV-2", "Modou", Classe),
            Student(EleveC, "ELEV-3", "Fatou", Classe),
            Student(EleveD, "ELEV-4", "Ibra", ClasseSansProgramme));

        owner.StudentSubjectEnrollments.AddRange(
            Enrollment(EleveA, espagnol.Id), Enrollment(EleveB, arabe.Id), Enrollment(EleveC, espagnol.Id));

        owner.StudentSubjectExemptions.AddRange(
            new StudentSubjectExemption { SchoolId = Ecole, StudentId = EleveA, SubjectId = Eps, SchoolYearId = Annee, Reason = "Inaptitude médicale" },
            new StudentSubjectExemption { SchoolId = Ecole, StudentId = EleveD, SubjectId = MathsCinquieme, SchoolYearId = Annee, Reason = "Dispense accordée" });

        await owner.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task Excluded_Subjects_Are_The_Program_Exclusions_Plus_The_Exemptions()
    {
        await using var db = _db.NewAppContext(Ecole);

        var excluded = await new SubjectFollowScope(db).ExcludedSubjectsAsync(EleveA, Annee, default);

        excluded.Should().BeEquivalentTo([Arabe, Eps], "l'option non choisie ET la matière dispensée");
    }

    [Fact]
    public async Task A_Class_Without_A_Program_Still_Excludes_An_Exempted_Subject()
    {
        await using var db = _db.NewAppContext(Ecole);

        (await new SubjectFollowScope(db).ExcludedSubjectsAsync(EleveD, Annee, default)).Should().BeEquivalentTo([MathsCinquieme]);
    }

    [Fact]
    public async Task A_Student_Without_Any_Exemption_Is_Excluded_From_Nothing_More_Than_Before()
    {
        await using var db = _db.NewAppContext(Ecole);

        (await new SubjectFollowScope(db).ExcludedSubjectsAsync(EleveC, Annee, default)).Should().BeEquivalentTo([Arabe]);
    }

    [Fact]
    public async Task Restricted_Students_Stay_Null_For_A_Common_Subject_Nobody_Is_Exempted_From()
    {
        await using var db = _db.NewAppContext(Ecole);

        (await new SubjectFollowScope(db).RestrictedStudentsAsync(Classe, Maths, Annee, default)).Should().BeNull();
    }

    [Fact]
    public async Task Restricted_Students_Of_A_Common_Subject_Exclude_The_Exempted_Ones()
    {
        await using var db = _db.NewAppContext(Ecole);

        var allowed = await new SubjectFollowScope(db).RestrictedStudentsAsync(Classe, Eps, Annee, default);

        allowed.Should().BeEquivalentTo([EleveB, EleveC], "toute la classe sauf l'élève dispensé");
    }

    [Fact]
    public async Task Restricted_Students_Of_An_Option_Are_The_Choosers_Minus_The_Exempted_Ones()
    {
        await using var db = _db.NewAppContext(Ecole);

        var allowed = await new SubjectFollowScope(db).RestrictedStudentsAsync(Classe, Espagnol, Annee, default);

        allowed.Should().BeEquivalentTo([EleveA, EleveC], "les élèves qui l'ont choisie ; aucun n'est dispensé de l'Espagnol");
    }

    [Fact]
    public async Task Ensure_Follows_Refuses_An_Exempted_Subject_With_A_Dedicated_Message()
    {
        await using var db = _db.NewAppContext(Ecole);

        var act = () => new SubjectFollowScope(db).EnsureFollowsAsync(EleveA, Eps, Trimestre, "SubjectId", default);

        (await act.Should().ThrowAsync<ValidationException>())
            .Which.Errors.Should().ContainKey("SubjectId").WhoseValue.Should().ContainMatch("*dispensé*");
    }

    [Fact]
    public async Task Exemptions_Are_Memoised_Per_Student_And_Year()
    {
        await using var db = _db.NewAppContext(Ecole);
        var scope = new SubjectFollowScope(db);

        var first = await scope.ExemptionsAsync(EleveA, Annee, default);
        var second = await scope.ExemptionsAsync(EleveA, Annee, default);

        second.Should().BeSameAs(first);
        first.Should().ContainSingle().Which.SubjectId.Should().Be(Eps);
    }

    private static ClassSubject Program(Guid subject, int order, string? optionGroup = null) => new()
    {
        Id = Guid.NewGuid(), SchoolId = Ecole, ClassroomId = Classe, SubjectId = subject, OptionGroup = optionGroup,
        IsActive = true, DisplayOrder = order
    };

    private static Student Student(Guid id, string matricule, string name, Guid classroom) => new()
    {
        Id = id, SchoolId = Ecole, Matricule = matricule, FullName = name, BirthDate = new DateOnly(2011, 1, 1),
        BirthPlace = "Dakar", Gender = "F", ClassroomId = classroom
    };

    private static StudentSubjectEnrollment Enrollment(Guid student, Guid classSubject) => new()
    {
        SchoolId = Ecole, StudentId = student, ClassSubjectId = classSubject, SchoolYearId = Annee
    };
}
