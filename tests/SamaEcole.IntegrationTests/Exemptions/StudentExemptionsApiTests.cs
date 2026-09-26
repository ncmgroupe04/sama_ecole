using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.ClassSubjects.Commands;
using SamaEcole.Application.ClassSubjects.Queries;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Exemptions;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using SamaEcole.Persistence;
using Xunit;
using ValidationException = SamaEcole.Application.Common.Exceptions.ValidationException;

namespace SamaEcole.IntegrationTests.Exemptions;

file sealed class StubTenantProvider(Guid? schoolId) : ITenantProvider
{
    public Guid? CurrentSchoolId => schoolId;
}

/// <summary>
/// API des dispenses d'un élève (handlers) : lecture des matières dispensables avec leur état, remplacement idempotent
/// de la liste, motif obligatoire, aucune écriture si une seule ligne est invalide, isolation entre écoles.
/// École A : année active, « 4ème A » AVEC programme (Maths, EPS communs ; Espagnol en option LV2) ; l'élève a 2 notes
/// d'EPS et 1 de Maths. École B : un élève et une matière.
/// </summary>
[Trait("Category", "MultiTenant")]
public class StudentExemptionsApiTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("c1111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("c2222222-2222-2222-2222-222222222222");
    private static readonly Guid Annee = Guid.Parse("c1111111-0000-0000-0000-000000000001");
    private static readonly Guid AnneeB = Guid.Parse("c2222222-0000-0000-0000-000000000001");
    private static readonly Guid Trimestre = Guid.Parse("c1111111-0000-0000-0000-00000000000b");
    private static readonly Guid Classe = Guid.Parse("c1111111-0000-0000-0000-0000000000c1");
    private static readonly Guid ClasseB = Guid.Parse("c2222222-0000-0000-0000-0000000000c1");
    private static readonly Guid Maths = Guid.Parse("c1111111-0000-0000-0000-0000000000b1");
    private static readonly Guid Eps = Guid.Parse("c1111111-0000-0000-0000-0000000000b2");
    private static readonly Guid Espagnol = Guid.Parse("c1111111-0000-0000-0000-0000000000b3");
    private static readonly Guid MathsB = Guid.Parse("c2222222-0000-0000-0000-0000000000b1");
    private static readonly Guid Eleve = Guid.Parse("c1111111-0000-0000-0000-0000000000e1");
    private static readonly Guid EleveB = Guid.Parse("c2222222-0000-0000-0000-0000000000e1");

    private static readonly TestCurrentUser Directeur = new(Guid.Parse("cd000000-0000-0000-0000-000000000001"), Role.Directeur);

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();

        owner.Schools.AddRange(new School { Id = EcoleA, Name = "École A" }, new School { Id = EcoleB, Name = "École B" });
        owner.SchoolYears.AddRange(
            new SchoolYear { Id = Annee, SchoolId = EcoleA, Label = "2026-2027", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true },
            new SchoolYear { Id = AnneeB, SchoolId = EcoleB, Label = "2026-2027", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true });
        owner.Terms.Add(new Term
        {
            Id = Trimestre, SchoolId = EcoleA, SchoolYearId = Annee, Label = "1er trimestre", Order = 1,
            StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 12, 20)
        });
        owner.Classrooms.AddRange(
            new Classroom { Id = Classe, SchoolId = EcoleA, Name = "4ème A", Level = "Collège", Cycle = CycleType.College, Capacity = 40 },
            new Classroom { Id = ClasseB, SchoolId = EcoleB, Name = "4ème A", Level = "Collège", Cycle = CycleType.College, Capacity = 40 });
        owner.Subjects.AddRange(
            new Subject { Id = Maths, SchoolId = EcoleA, Name = "Mathématiques", Level = "Collège", Coefficient = 2 },
            new Subject { Id = Eps, SchoolId = EcoleA, Name = "EPS", Level = "Collège", Coefficient = 1 },
            new Subject { Id = Espagnol, SchoolId = EcoleA, Name = "Espagnol", Level = "Collège", Coefficient = 1 },
            new Subject { Id = MathsB, SchoolId = EcoleB, Name = "Mathématiques", Level = "Collège", Coefficient = 2 });
        owner.ClassSubjects.AddRange(
            Program(Maths, 1), Program(Eps, 2), Program(Espagnol, 3, optionGroup: "LV2"));
        owner.Students.AddRange(
            new Student { Id = Eleve, SchoolId = EcoleA, Matricule = "ELEV-A1", FullName = "Awa Fall", BirthDate = new DateOnly(2011, 1, 1), BirthPlace = "Dakar", Gender = "F", ClassroomId = Classe },
            new Student { Id = EleveB, SchoolId = EcoleB, Matricule = "ELEV-B1", FullName = "Ibra B", BirthDate = new DateOnly(2011, 1, 1), BirthPlace = "Dakar", Gender = "M", ClassroomId = ClasseB });
        owner.Grades.AddRange(
            Grade(Eps, EvaluationType.Devoir1, 12), Grade(Eps, EvaluationType.Composition, 14), Grade(Maths, EvaluationType.Devoir1, 10));

        await owner.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    // ------------------------------------------------------------------ lecture

    [Fact]
    public async Task Get_Lists_The_Dispensable_Subjects_With_Their_State_And_The_Grades_A_Dispense_Would_Hide()
    {
        var dto = await GetAsync();

        dto.StudentId.Should().Be(Eleve);
        dto.SchoolYearId.Should().Be(Annee);
        dto.Subjects.Select(s => s.Name).Should().Equal("EPS", "Mathématiques");   // pas l'option Espagnol
        dto.Subjects.Should().OnlyContain(s => !s.IsExempt && s.Reason == null);
        dto.Subjects.Single(s => s.SubjectId == Eps).GradeCount.Should().Be(2);
        dto.Subjects.Single(s => s.SubjectId == Maths).GradeCount.Should().Be(1);
    }

    [Fact]
    public async Task Get_Shows_The_Reason_And_The_State_Once_Recorded()
    {
        await SetAsync(Eleve, new SubjectExemptionInput(Eps, "Inaptitude médicale"));

        var eps = (await GetAsync()).Subjects.Single(s => s.SubjectId == Eps);

        eps.IsExempt.Should().BeTrue();
        eps.Reason.Should().Be("Inaptitude médicale");
        eps.GradeCount.Should().Be(2, "les notes restent en base, la dispense les masque seulement");
    }

    // ------------------------------------------------------------------ écriture

    [Fact]
    public async Task Set_Records_The_Exemption_With_A_Trimmed_Reason()
    {
        await SetAsync(Eleve, new SubjectExemptionInput(Eps, "  Inaptitude médicale  "));

        var rows = await RowsAsync();
        var row = rows.Should().ContainSingle().Subject;
        row.SubjectId.Should().Be(Eps);
        row.Reason.Should().Be("Inaptitude médicale");
        row.SchoolYearId.Should().Be(Annee);
        row.SchoolId.Should().Be(EcoleA);
    }

    [Fact]
    public async Task Set_Replaces_The_Whole_List_Idempotently()
    {
        await SetAsync(Eleve, new SubjectExemptionInput(Eps, "Inaptitude"));
        await SetAsync(Eleve, new SubjectExemptionInput(Eps, "Inaptitude"));
        (await RowsAsync()).Should().ContainSingle();

        await SetAsync(Eleve, new SubjectExemptionInput(Maths, "x"));

        var rows = await RowsAsync();
        rows.Should().ContainSingle().Which.SubjectId.Should().Be(Maths);

        // EPS n'est plus active mais n'a pas disparu de la base : suppression logique (règle #6).
        await using var owner = _db.NewOwnerContext();
        var eps = await owner.StudentSubjectExemptions.IgnoreQueryFilters().SingleAsync(x => x.SubjectId == Eps);
        eps.IsDeleted.Should().BeTrue();
        eps.DeletedBy.Should().Be(Directeur.UserId.ToString());
    }

    [Fact]
    public async Task Changing_The_Reason_Updates_The_Row_Without_A_Duplicate()
    {
        await SetAsync(Eleve, new SubjectExemptionInput(Eps, "Premier motif"));
        var before = (await RowsAsync()).Single().Id;

        await SetAsync(Eleve, new SubjectExemptionInput(Eps, "Second motif"));

        var row = (await RowsAsync()).Should().ContainSingle().Subject;
        row.Id.Should().Be(before);
        row.Reason.Should().Be("Second motif");
    }

    [Fact]
    public async Task An_Empty_List_Removes_Every_Exemption()
    {
        await SetAsync(Eleve, new SubjectExemptionInput(Eps, "A"), new SubjectExemptionInput(Maths, "B"));

        await SetAsync(Eleve);

        (await RowsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task A_Soft_Deleted_Row_Can_Be_Recreated()
    {
        await SetAsync(Eleve, new SubjectExemptionInput(Eps, "A"));
        await SetAsync(Eleve);

        await SetAsync(Eleve, new SubjectExemptionInput(Eps, "B"));

        (await RowsAsync()).Should().ContainSingle().Which.Reason.Should().Be("B");
    }

    // ------------------------------------------------------------------ refus

    [Fact]
    public async Task A_Missing_Reason_Is_Refused_And_Writes_Nothing()
    {
        await SetAsync(Eleve, new SubjectExemptionInput(Eps, "Inaptitude médicale"));

        var act = () => SetAsync(Eleve, new SubjectExemptionInput(Maths, "OK"), new SubjectExemptionInput(Eps, "   "));

        var error = (await act.Should().ThrowAsync<ValidationException>()).Which;
        error.Errors.Should().ContainKey("Exemptions");
        error.Errors["Exemptions"].Single().Should().Contain("motif").And.Contain("EPS");

        // Tout ou rien : ni Maths ajoutée, ni EPS modifiée.
        var rows = await RowsAsync();
        rows.Should().ContainSingle().Which.Reason.Should().Be("Inaptitude médicale");
        rows.Single().SubjectId.Should().Be(Eps);
    }

    [Fact]
    public async Task An_Option_Or_A_Foreign_Subject_Cannot_Be_Exempted()
    {
        var option = () => SetAsync(Eleve, new SubjectExemptionInput(Espagnol, "Raison"));
        await option.Should().ThrowAsync<ValidationException>();

        var foreign = () => SetAsync(Eleve, new SubjectExemptionInput(MathsB, "Raison"));
        await foreign.Should().ThrowAsync<ValidationException>();

        (await RowsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task The_Same_Subject_Twice_Is_Refused()
    {
        var act = () => SetAsync(Eleve, new SubjectExemptionInput(Eps, "A"), new SubjectExemptionInput(Eps, "B"));

        await act.Should().ThrowAsync<ValidationException>();
        (await RowsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task An_Unknown_Or_Foreign_Student_Is_A_404()
    {
        var unknown = () => SetAsync(Guid.NewGuid(), new SubjectExemptionInput(Eps, "A"));
        await unknown.Should().ThrowAsync<KeyNotFoundException>();

        var foreign = () => SetAsync(EleveB, new SubjectExemptionInput(Eps, "A"));   // élève de l'école B, session de l'école A
        await foreign.Should().ThrowAsync<KeyNotFoundException>();

        await using var db = _db.NewAppContext(EcoleA);
        var read = () => new GetStudentExemptionsQueryHandler(db).Handle(new GetStudentExemptionsQuery(EleveB), default);
        await read.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Without_An_Active_School_Year_The_Request_Is_Refused()
    {
        await using (var owner = _db.NewOwnerContext())
        {
            var year = await owner.SchoolYears.IgnoreQueryFilters().SingleAsync(y => y.Id == Annee);
            year.IsActive = false;
            await owner.SaveChangesAsync();
        }

        var write = () => SetAsync(Eleve, new SubjectExemptionInput(Eps, "A"));
        await write.Should().ThrowAsync<ValidationException>();

        await using var db = _db.NewAppContext(EcoleA);
        var read = () => new GetStudentExemptionsQueryHandler(db).Handle(new GetStudentExemptionsQuery(Eleve), default);
        await read.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public void A_Reason_With_Html_Is_Refused_By_The_Validator()
    {
        var command = new SetStudentExemptionsCommand(Eleve, [new SubjectExemptionInput(Eps, "<script>alert(1)</script>")]);

        new SetStudentExemptionsCommandValidator().Validate(command).IsValid.Should().BeFalse();
        new SetStudentExemptionsCommandValidator()
            .Validate(new SetStudentExemptionsCommand(Eleve, [new SubjectExemptionInput(Eps, "Inaptitude médicale")])).IsValid.Should().BeTrue();
    }

    [Fact]
    public void The_Command_Is_Audited_But_The_Reason_Is_Not_Part_Of_Any_Message()
        => typeof(IAuditableRequest).IsAssignableFrom(typeof(SetStudentExemptionsCommand)).Should().BeTrue();

    // ------------------------------------------------------------------ isolation

    [Fact]
    public async Task Exemptions_Of_One_School_Are_Invisible_To_The_Other()
    {
        await SetAsync(Eleve, new SubjectExemptionInput(Eps, "Inaptitude"));

        await using var autre = _db.NewAppContext(EcoleB);
        (await autre.StudentSubjectExemptions.CountAsync()).Should().Be(0);
    }

    // ------------------------------------------------------------------ helpers

    private ClassSubject Program(Guid subject, int order, string? optionGroup = null) => new()
    {
        SchoolId = EcoleA, ClassroomId = Classe, SubjectId = subject, OptionGroup = optionGroup, DisplayOrder = order
    };

    private Grade Grade(Guid subject, EvaluationType type, decimal value) => new()
    {
        SchoolId = EcoleA, StudentId = Eleve, SubjectId = subject, TermId = Trimestre, EvaluationType = type, Value = value
    };

    private async Task<StudentExemptionsDto> GetAsync()
    {
        await using var db = _db.NewAppContext(EcoleA);
        return await new GetStudentExemptionsQueryHandler(db).Handle(new GetStudentExemptionsQuery(Eleve), default);
    }

    private async Task SetAsync(Guid student, params SubjectExemptionInput[] exemptions)
    {
        await using var db = _db.NewAppContext(EcoleA);
        await new SetStudentExemptionsCommandHandler(db, new StubTenantProvider(EcoleA), Directeur)
            .Handle(new SetStudentExemptionsCommand(student, exemptions), default);
    }

    /// <summary>Les dispenses ACTIVES de l'élève, lues par un contexte applicatif du tenant.</summary>
    private async Task<List<StudentSubjectExemption>> RowsAsync()
    {
        await using var db = _db.NewAppContext(EcoleA);
        return await db.StudentSubjectExemptions.AsNoTracking().Where(x => x.StudentId == Eleve).ToListAsync();
    }
}
