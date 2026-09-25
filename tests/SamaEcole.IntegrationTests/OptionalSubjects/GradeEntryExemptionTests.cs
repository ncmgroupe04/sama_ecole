using FluentAssertions;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Grades;
using SamaEcole.Application.Grades.Commands.CreateGrade;
using SamaEcole.Application.Grades.Commands.ImportGradeSheet;
using SamaEcole.Application.Grades.Queries.GetClassGrades;
using SamaEcole.Application.Grades.Queries.GetGradeSheetExcel;
using SamaEcole.Application.Grades.Queries.GetGradeSheetPdf;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.OptionalSubjects;

/// <summary>
/// Matières optionnelles — la saisie des notes ne propose ni n'accepte un élève dispensé : grille de saisie,
/// fiche imprimée, import Excel et saisie unitaire (spécification §4.2).
/// </summary>
[Trait("Category", "MultiTenant")]
public class GradeEntryExemptionTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("97777777-7777-7777-7777-777777777777");
    private static readonly Guid Annee = Guid.Parse("97777777-0000-0000-0000-000000000001");
    private static readonly Guid Trimestre = Guid.Parse("97777777-0000-0000-0000-0000000000d1");
    private static readonly Guid Classe = Guid.Parse("97777777-0000-0000-0000-0000000000c1");
    private static readonly Guid Maths = Guid.Parse("97777777-0000-0000-0000-0000000000a1");
    private static readonly Guid Arabe = Guid.Parse("97777777-0000-0000-0000-0000000000a2");
    private static readonly Guid Eps = Guid.Parse("97777777-0000-0000-0000-0000000000a3");
    private static readonly Guid EleveLibre = Guid.Parse("97777777-0000-0000-0000-0000000000e1");
    private static readonly Guid EleveDispense = Guid.Parse("97777777-0000-0000-0000-0000000000e2");
    private static readonly Guid Directeur = Guid.Parse("97777777-0000-0000-0000-0000000000f1");

    private sealed class StubTenantProvider(Guid? schoolId) : ITenantProvider
    {
        public Guid? CurrentSchoolId => schoolId;
    }

    private sealed class StubParser(params GradeSheetRow[] rows) : IGradeSheetImportParser
    {
        public IReadOnlyList<GradeSheetRow> Parse(byte[] fileContent, string fileName) => rows;
    }

    private sealed class SpyGenerator : IGradeSheetPdfGenerator
    {
        public GradeSheetPdfDto? Captured { get; private set; }

        public byte[] Generate(GradeSheetPdfDto sheet, byte[]? logo)
        {
            Captured = sheet;
            return [0x25, 0x50, 0x44, 0x46];
        }
    }

    private sealed class FixedLogoProvider(byte[]? logo) : ISchoolLogoProvider
    {
        public Task<byte[]?> TryFetchAsync(string? logoUrl, CancellationToken cancellationToken) => Task.FromResult(logo);
    }

    private sealed class SpyExcel : IGradeSheetExcelGenerator
    {
        public IReadOnlyList<GradeSheetStudentRow> Rows { get; private set; } = [];

        public byte[] Generate(IReadOnlyList<GradeSheetStudentRow> rows, decimal gradingScale)
        {
            Rows = rows;
            return [0x50, 0x4B];
        }
    }

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();

        owner.Schools.Add(new School { Id = Ecole, Name = "Collège A" });
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
        owner.Classrooms.Add(new Classroom { Id = Classe, SchoolId = Ecole, Name = "4ème A", Level = "Collège", Cycle = CycleType.College, Capacity = 40 });
        owner.Subjects.AddRange(
            new Subject { Id = Maths, SchoolId = Ecole, Name = "Mathématiques", Level = "Collège", Coefficient = 4 },
            new Subject { Id = Arabe, SchoolId = Ecole, Name = "Arabe", Level = "Collège", Coefficient = 2, IsOptional = true, OptionGroup = "LV2" },
            new Subject { Id = Eps, SchoolId = Ecole, Name = "EPS", Level = "Collège", Coefficient = 1 });
        owner.Students.AddRange(
            new Student { Id = EleveLibre, SchoolId = Ecole, Matricule = "ELEV-0001", FullName = "Awa Fall", BirthDate = new DateOnly(2011, 1, 1), BirthPlace = "Dakar", Gender = "F", ClassroomId = Classe },
            new Student { Id = EleveDispense, SchoolId = Ecole, Matricule = "ELEV-0002", FullName = "Modou Diop", BirthDate = new DateOnly(2011, 1, 1), BirthPlace = "Dakar", Gender = "M", ClassroomId = Classe });

        var inscriptionLibre = new Enrollment
        {
            SchoolId = Ecole, StudentId = EleveLibre, SchoolYearId = Annee, ClassroomId = Classe,
            Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed, ReceiptNumber = "R-1"
        };
        var inscriptionDispense = new Enrollment
        {
            SchoolId = Ecole, StudentId = EleveDispense, SchoolYearId = Annee, ClassroomId = Classe,
            Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed, ReceiptNumber = "R-2"
        };
        owner.Enrollments.AddRange(inscriptionLibre, inscriptionDispense);
        owner.EnrollmentSubjectExemptions.AddRange(
            // Option non suivie (sans motif) ET matière obligatoire dispensée (avec motif) : deux sortes de
            // dispenses, une même conséquence sur la saisie.
            new EnrollmentSubjectExemption { SchoolId = Ecole, EnrollmentId = inscriptionDispense.Id, SubjectId = Arabe },
            new EnrollmentSubjectExemption { SchoolId = Ecole, EnrollmentId = inscriptionDispense.Id, SubjectId = Eps, Reason = "Inaptitude médicale" });

        await owner.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private static TestCurrentUser Chef => new(Directeur, Role.Directeur);

    [Fact]
    public async Task The_Grade_Grid_Lists_Only_The_Students_Who_Follow_The_Subject()
    {
        await using var ctx = _db.NewAppContext(Ecole);
        var handler = new GetClassGradesQueryHandler(
            ctx, new GradeCorrectionAuthorizer(ctx, Chef, TimeProvider.System));

        var arabe = await handler.Handle(new GetClassGradesQuery(Classe, Arabe, Trimestre), default);
        var maths = await handler.Handle(new GetClassGradesQuery(Classe, Maths, Trimestre), default);

        arabe.Select(r => r.StudentId).Should().BeEquivalentTo([EleveLibre]);
        maths.Select(r => r.StudentId).Should().BeEquivalentTo([EleveLibre, EleveDispense],
            "une matière obligatoire garde toute la classe");
    }

    [Fact]
    public async Task Entering_A_Grade_For_An_Exempted_Subject_Is_Refused_On_The_Subject_Field()
    {
        await using var ctx = _db.NewAppContext(Ecole);
        var handler = new CreateGradeCommandHandler(ctx, new StubTenantProvider(Ecole), Chef);

        var act = () => handler.Handle(
            new CreateGradeCommand(EleveDispense, Arabe, Trimestre, EvaluationType.Devoir1, 12m), default);

        (await act.Should().ThrowAsync<ValidationException>())
            .Which.Errors.Should().ContainKey("SubjectId").WhoseValue.Should().Contain(m => m.Contains("dispensé"));
    }

    [Fact]
    public async Task The_Same_Student_Can_Still_Be_Graded_In_A_Mandatory_Subject_And_The_Others_In_The_Option()
    {
        await using var ctx = _db.NewAppContext(Ecole);
        var handler = new CreateGradeCommandHandler(ctx, new StubTenantProvider(Ecole), Chef);

        var maths = await handler.Handle(
            new CreateGradeCommand(EleveDispense, Maths, Trimestre, EvaluationType.Devoir1, 12m), default);
        var arabe = await handler.Handle(
            new CreateGradeCommand(EleveLibre, Arabe, Trimestre, EvaluationType.Devoir1, 14m), default);

        maths.Value.Should().Be(12m);
        arabe.Value.Should().Be(14m);
    }

    [Fact]
    public async Task An_Import_Row_For_An_Exempted_Student_Is_Rejected_With_A_Dedicated_Message()
    {
        await using var ctx = _db.NewAppContext(Ecole);
        var user = Chef;
        var handler = new ImportGradeSheetCommandHandler(
            ctx, new StubTenantProvider(Ecole),
            new StubParser(new GradeSheetRow(2, "ELEV-0001", "8", "", ""), new GradeSheetRow(3, "ELEV-0002", "9", "", "")),
            user, new GradeCorrectionAuthorizer(ctx, user, TimeProvider.System));

        var act = () => handler.Handle(
            new ImportGradeSheetCommand(Classe, Arabe, Trimestre, true, [0x1], "notes.xlsx"), default);

        (await act.Should().ThrowAsync<ValidationException>())
            .Which.Errors.Should().ContainSingle()
            .Which.Should().Match<KeyValuePair<string, string[]>>(
                e => e.Key == "Ligne 3" && e.Value.Single().Contains("dispensé"));
    }

    [Fact]
    public async Task An_Import_Of_Students_Who_All_Follow_The_Subject_Is_Accepted()
    {
        await using var ctx = _db.NewAppContext(Ecole);
        var user = Chef;
        var handler = new ImportGradeSheetCommandHandler(
            ctx, new StubTenantProvider(Ecole),
            new StubParser(new GradeSheetRow(2, "ELEV-0001", "8", "", "")),
            user, new GradeCorrectionAuthorizer(ctx, user, TimeProvider.System));

        var act = () => handler.Handle(
            new ImportGradeSheetCommand(Classe, Arabe, Trimestre, true, [0x1], "notes.xlsx"), default);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task The_Printed_Grade_Sheet_Omits_The_Exempted_Student()
    {
        await using var ctx = _db.NewAppContext(Ecole);
        var spy = new SpyGenerator();
        var handler = new GetGradeSheetPdfQueryHandler(ctx, new StubTenantProvider(Ecole), spy, new FixedLogoProvider(null));

        await handler.Handle(new GetGradeSheetPdfQuery(Classe, Arabe, Trimestre, EvaluationType.Devoir1), default);

        spy.Captured!.Students.Select(s => s.Matricule).Should().Equal("ELEV-0001");
    }

    [Fact]
    public async Task The_Excel_Grade_Sheet_Omits_The_Exempted_Student()
    {
        await using var ctx = _db.NewAppContext(Ecole);
        var spy = new SpyExcel();
        var handler = new GetGradeSheetExcelQueryHandler(ctx, spy);

        await handler.Handle(new GetGradeSheetExcelQuery(Classe, Arabe, Trimestre), default);

        spy.Rows.Select(r => r.Matricule).Should().Equal("ELEV-0001");
    }

    // ---- Matière OBLIGATOIRE dispensée : même effet sur la saisie que l'option non suivie ----------------

    [Fact]
    public async Task The_Grade_Grid_Omits_A_Student_Exempted_From_A_Mandatory_Subject()
    {
        await using var ctx = _db.NewAppContext(Ecole);
        var handler = new GetClassGradesQueryHandler(
            ctx, new GradeCorrectionAuthorizer(ctx, Chef, TimeProvider.System));

        var eps = await handler.Handle(new GetClassGradesQuery(Classe, Eps, Trimestre), default);

        eps.Select(r => r.StudentId).Should().BeEquivalentTo([EleveLibre]);
    }

    [Fact]
    public async Task Entering_A_Grade_For_A_Mandatory_Subject_The_Student_Is_Exempted_From_Is_Refused()
    {
        await using var ctx = _db.NewAppContext(Ecole);
        var handler = new CreateGradeCommandHandler(ctx, new StubTenantProvider(Ecole), Chef);

        var act = () => handler.Handle(
            new CreateGradeCommand(EleveDispense, Eps, Trimestre, EvaluationType.Devoir1, 12m), default);

        (await act.Should().ThrowAsync<ValidationException>())
            .Which.Errors.Should().ContainKey("SubjectId").WhoseValue.Should().Contain(m => m.Contains("dispensé"));
    }
}
