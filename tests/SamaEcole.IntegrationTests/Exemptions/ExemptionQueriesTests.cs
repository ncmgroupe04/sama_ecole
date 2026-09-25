using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Exemptions;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Exemptions;

/// <summary>
/// Les requêtes de dispenses (<see cref="ExemptionQueries"/>) : quelles matières se dispensent, quelles dispenses un
/// élève porte pour une année, et à qui elles sont invisibles. École A : année active <c>Annee1</c> et année passée
/// <c>Annee0</c> ; « 4ème A » AVEC programme, « 5ème A » SANS programme ; école B : un élève dispensé de sa matière.
/// </summary>
public class ExemptionQueriesTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("b1111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("b2222222-2222-2222-2222-222222222222");
    private static readonly Guid Annee1 = Guid.Parse("b1111111-0000-0000-0000-000000000001");
    private static readonly Guid Annee0 = Guid.Parse("b1111111-0000-0000-0000-000000000002");
    private static readonly Guid AnneeB = Guid.Parse("b2222222-0000-0000-0000-000000000001");

    private static readonly Guid ClasseAvecProgramme = Guid.Parse("b1111111-0000-0000-0000-0000000000c1");
    private static readonly Guid ClasseSansProgramme = Guid.Parse("b1111111-0000-0000-0000-0000000000c2");
    private static readonly Guid ClasseB = Guid.Parse("b2222222-0000-0000-0000-0000000000c1");

    private static readonly Guid Maths = Guid.Parse("b1111111-0000-0000-0000-0000000000b1");
    private static readonly Guid Eps = Guid.Parse("b1111111-0000-0000-0000-0000000000b2");
    private static readonly Guid Espagnol = Guid.Parse("b1111111-0000-0000-0000-0000000000b3");
    private static readonly Guid Latin = Guid.Parse("b1111111-0000-0000-0000-0000000000b4");
    private static readonly Guid LangCom = Guid.Parse("b1111111-0000-0000-0000-0000000000b5");
    private static readonly Guid Vocabulaire = Guid.Parse("b1111111-0000-0000-0000-0000000000b6");
    private static readonly Guid EpsB = Guid.Parse("b2222222-0000-0000-0000-0000000000b1");

    private static readonly Guid EleveDispense = Guid.Parse("b1111111-0000-0000-0000-0000000000e1");
    private static readonly Guid EleveAnneePassee = Guid.Parse("b1111111-0000-0000-0000-0000000000e2");
    private static readonly Guid EleveLibre = Guid.Parse("b1111111-0000-0000-0000-0000000000e3");
    private static readonly Guid EleveB = Guid.Parse("b2222222-0000-0000-0000-0000000000e1");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();

        owner.Schools.AddRange(new School { Id = EcoleA, Name = "École A" }, new School { Id = EcoleB, Name = "École B" });
        owner.SchoolYears.AddRange(
            new SchoolYear { Id = Annee1, SchoolId = EcoleA, Label = "2026-2027", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true },
            new SchoolYear { Id = Annee0, SchoolId = EcoleA, Label = "2025-2026", StartDate = new DateOnly(2025, 9, 1), EndDate = new DateOnly(2026, 6, 30) },
            new SchoolYear { Id = AnneeB, SchoolId = EcoleB, Label = "2026-2027", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true });

        owner.Classrooms.AddRange(
            new Classroom { Id = ClasseAvecProgramme, SchoolId = EcoleA, Name = "4ème A", Level = "Collège", Cycle = CycleType.College, Capacity = 40 },
            new Classroom { Id = ClasseSansProgramme, SchoolId = EcoleA, Name = "5ème A", Level = "Collège", Cycle = CycleType.College, Capacity = 40 },
            new Classroom { Id = ClasseB, SchoolId = EcoleB, Name = "4ème A", Level = "Collège", Cycle = CycleType.College, Capacity = 40 });

        owner.Subjects.AddRange(
            new Subject { Id = Maths, SchoolId = EcoleA, Name = "Mathématiques", Level = "Collège", Coefficient = 2 },
            new Subject { Id = Eps, SchoolId = EcoleA, Name = "EPS", Level = "Collège", Coefficient = 1 },
            new Subject { Id = Espagnol, SchoolId = EcoleA, Name = "Espagnol", Level = "Collège", Coefficient = 1 },
            new Subject { Id = Latin, SchoolId = EcoleA, Name = "Latin", Level = "Collège", Coefficient = 1 },
            new Subject { Id = LangCom, SchoolId = EcoleA, Name = "Lang & Com.", Level = "Collège", Coefficient = 3 },
            new Subject { Id = Vocabulaire, SchoolId = EcoleA, Name = "Vocabulaire", Level = "Collège", Coefficient = 1, ParentSubjectId = LangCom },
            new Subject { Id = EpsB, SchoolId = EcoleB, Name = "EPS", Level = "Collège", Coefficient = 1 });

        // Programme de « 4ème A » : Maths et EPS communs, Espagnol en option (LV2), Latin désactivé, un domaine et son activité.
        owner.ClassSubjects.AddRange(
            Program(ClasseAvecProgramme, Maths, order: 1),
            Program(ClasseAvecProgramme, Eps, order: 2),
            Program(ClasseAvecProgramme, Espagnol, order: 3, optionGroup: "LV2"),
            Program(ClasseAvecProgramme, Latin, order: 4, active: false),
            Program(ClasseAvecProgramme, LangCom, order: 5),
            Program(ClasseAvecProgramme, Vocabulaire, order: 6));

        owner.Students.AddRange(
            Student(EleveDispense, EcoleA, "ELEV-A1", "Awa Dispensée", ClasseAvecProgramme),
            Student(EleveAnneePassee, EcoleA, "ELEV-A2", "Modou Passé", ClasseAvecProgramme),
            Student(EleveLibre, EcoleA, "ELEV-A3", "Fatou Libre", ClasseAvecProgramme),
            Student(EleveB, EcoleB, "ELEV-B1", "Ibra B", ClasseB));

        owner.StudentSubjectExemptions.AddRange(
            Exemption(EcoleA, EleveDispense, Eps, Annee1, "Inaptitude médicale"),
            Exemption(EcoleA, EleveAnneePassee, Eps, Annee0, "Inaptitude passée"),
            Exemption(EcoleB, EleveB, EpsB, AnneeB, "Autre école"));

        await owner.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task Dispensable_Subjects_Of_A_Class_With_A_Program_Are_The_Active_Common_Autonomous_Ones()
    {
        await using var db = _db.NewAppContext(EcoleA);

        var subjects = await ExemptionQueries.DispensableAsync(db, ClasseAvecProgramme, default);

        // Ni l'option Espagnol, ni Latin (désactivé), ni le domaine « Lang & Com. » ni son activité « Vocabulaire ».
        subjects.Select(s => s.Name).Should().Equal("EPS", "Mathématiques");
    }

    [Fact]
    public async Task Dispensable_Subjects_Of_A_Class_Without_A_Program_Are_The_Autonomous_Subjects_Of_Its_Level()
    {
        await using var db = _db.NewAppContext(EcoleA);

        var subjects = await ExemptionQueries.DispensableAsync(db, ClasseSansProgramme, default);

        // Triées par nom : « EPS » précède « Espagnol » (P avant S), quelle que soit la casse.
        subjects.Select(s => s.Name).Should().Equal("EPS", "Espagnol", "Latin", "Mathématiques");
        subjects.Select(s => s.Name).Should().NotContain(["Lang & Com.", "Vocabulaire"], "un domaine et une activité ne se dispensent pas");
    }

    [Fact]
    public async Task Dispensable_Subjects_Of_An_Unknown_Class_Are_None()
    {
        await using var db = _db.NewAppContext(EcoleA);

        (await ExemptionQueries.DispensableAsync(db, Guid.NewGuid(), default)).Should().BeEmpty();
    }

    [Fact]
    public async Task The_Level_Of_A_Class_Without_A_Program_Is_Compared_Without_Case_Or_Edge_Spaces()
    {
        await using (var owner = _db.NewOwnerContext())
        {
            var eps = await owner.Subjects.IgnoreQueryFilters().SingleAsync(s => s.Id == Eps);
            eps.Level = " collège ";
            await owner.SaveChangesAsync();
        }

        await using var db = _db.NewAppContext(EcoleA);

        (await ExemptionQueries.DispensableAsync(db, ClasseSansProgramme, default)).Select(s => s.Name).Should().Contain("EPS");
    }

    [Fact]
    public async Task An_Exemption_Is_Reported_For_The_Year_It_Was_Recorded_Only()
    {
        await using var db = _db.NewAppContext(EcoleA);

        var current = await ExemptionQueries.ForStudentAsync(db, EleveDispense, Annee1, default);
        var otherYear = await ExemptionQueries.ForStudentAsync(db, EleveAnneePassee, Annee1, default);
        var free = await ExemptionQueries.ForStudentAsync(db, EleveLibre, Annee1, default);

        current.Should().ContainSingle().Which.Should().Be(new ExemptSubject(Eps, "EPS", 1m));
        otherYear.Should().BeEmpty();
        free.Should().BeEmpty("sans dispense, tout est comme avant");
    }

    [Fact]
    public async Task Students_Exempt_From_A_Subject_Are_Listed_For_That_Year()
    {
        await using var db = _db.NewAppContext(EcoleA);

        (await ExemptionQueries.StudentsAsync(db, Eps, Annee1, default)).Should().BeEquivalentTo([EleveDispense]);
        (await ExemptionQueries.StudentsAsync(db, Eps, Annee0, default)).Should().BeEquivalentTo([EleveAnneePassee]);
        (await ExemptionQueries.StudentsAsync(db, Maths, Annee1, default)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_Soft_Deleted_Exemption_Is_Ignored()
    {
        await using (var owner = _db.NewOwnerContext())
        {
            var row = await owner.StudentSubjectExemptions.IgnoreQueryFilters()
                .SingleAsync(x => x.StudentId == EleveDispense && x.SchoolYearId == Annee1);
            row.SoftDelete("test");
            await owner.SaveChangesAsync();
        }

        await using var db = _db.NewAppContext(EcoleA);

        (await ExemptionQueries.ForStudentAsync(db, EleveDispense, Annee1, default)).Should().BeEmpty();
        (await ExemptionQueries.StudentsAsync(db, Eps, Annee1, default)).Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "MultiTenant")]
    public async Task Another_School_Never_Sees_These_Exemptions()
    {
        await using var db = _db.NewAppContext(EcoleB);

        (await ExemptionQueries.ForStudentAsync(db, EleveDispense, Annee1, default)).Should().BeEmpty();
        (await ExemptionQueries.StudentsAsync(db, Eps, Annee1, default)).Should().BeEmpty();
        (await ExemptionQueries.StudentsAsync(db, EpsB, AnneeB, default)).Should().BeEquivalentTo([EleveB]);
        (await ExemptionQueries.DispensableAsync(db, ClasseAvecProgramme, default)).Should().BeEmpty("la classe d'une autre école est invisible");
    }

    private static ClassSubject Program(Guid classroom, Guid subject, int order, string? optionGroup = null, bool active = true) => new()
    {
        SchoolId = EcoleA, ClassroomId = classroom, SubjectId = subject, OptionGroup = optionGroup, IsActive = active, DisplayOrder = order
    };

    private static Student Student(Guid id, Guid school, string matricule, string name, Guid classroom) => new()
    {
        Id = id, SchoolId = school, Matricule = matricule, FullName = name, BirthDate = new DateOnly(2011, 1, 1),
        BirthPlace = "Dakar", Gender = "F", ClassroomId = classroom
    };

    private static StudentSubjectExemption Exemption(Guid school, Guid student, Guid subject, Guid year, string reason) => new()
    {
        SchoolId = school, StudentId = student, SubjectId = subject, SchoolYearId = year, Reason = reason
    };
}
