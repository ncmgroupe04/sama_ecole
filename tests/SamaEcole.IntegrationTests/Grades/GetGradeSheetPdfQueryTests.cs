using SamaEcole.Application.ClassSubjects;
using FluentAssertions;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Grades.Queries.GetGradeSheetPdf;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Grades;

/// <summary>
/// Évolution N°1 — fiche de saisie papier. La requête lit la base (classe, matière, trimestre,
/// élèves, barème) : elle est testée contre un vrai PostgreSQL, avec un générateur espion qui capture
/// ce qui serait imprimé (ordre des élèves, barème, libellés) — le rendu QuestPDF a ses propres tests.
/// </summary>
public class GetGradeSheetPdfQueryTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid AnneeA = Guid.Parse("aaaa1111-0000-0000-0000-000000000001");
    private static readonly Guid TrimestreA = Guid.Parse("aaaa1111-0000-0000-0000-000000000002");
    private static readonly Guid ClasseA = Guid.Parse("cccccccc-0000-0000-0000-000000000001");
    private static readonly Guid ClasseB = Guid.Parse("cccccccc-0000-0000-0000-000000000002");
    private static readonly Guid Maths = Guid.Parse("dddddddd-0000-0000-0000-00000000000d");
    private static readonly Guid Competences = Guid.Parse("dddddddd-0000-0000-0000-00000000000e");
    private static readonly Guid Domaine = Guid.Parse("dddddddd-0000-0000-0000-00000000000f");
    private static readonly Guid Activite = Guid.Parse("dddddddd-0000-0000-0000-000000000010");

    private sealed class StubTenantProvider(Guid? schoolId) : ITenantProvider
    {
        public Guid? CurrentSchoolId => schoolId;
    }

    private sealed class SpyGenerator : IGradeSheetPdfGenerator
    {
        public GradeSheetPdfDto? Captured { get; private set; }
        public byte[]? CapturedLogo { get; private set; }

        public byte[] Generate(GradeSheetPdfDto sheet, byte[]? logo)
        {
            Captured = sheet;
            CapturedLogo = logo;
            return [0x25, 0x50, 0x44, 0x46];   // « %PDF »
        }
    }

    private sealed class FixedLogoProvider(byte[]? logo) : ISchoolLogoProvider
    {
        public Task<byte[]?> TryFetchAsync(string? logoUrl, CancellationToken cancellationToken) => Task.FromResult(logo);
    }

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();

        owner.Schools.AddRange(
            new School { Id = EcoleA, Name = "École Les Baobabs" },
            new School { Id = EcoleB, Name = "École Voisine" });
        owner.SchoolYears.Add(new SchoolYear
        {
            Id = AnneeA, SchoolId = EcoleA, Label = "2026-2027",
            StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2027, 7, 31), IsActive = true
        });
        owner.Terms.Add(new Term
        {
            Id = TrimestreA, SchoolId = EcoleA, SchoolYearId = AnneeA, Label = "1er trimestre", Order = 1,
            StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2027, 1, 15)
        });
        owner.Classrooms.AddRange(
            new Classroom { Id = ClasseA, SchoolId = EcoleA, Name = "CM2 A", Level = "Primaire", Cycle = CycleType.Primaire, Capacity = 40 },
            new Classroom { Id = ClasseB, SchoolId = EcoleB, Name = "6e B", Level = "Collège", Cycle = CycleType.College, Capacity = 40 });

        owner.Subjects.AddRange(
            new Subject { Id = Maths, SchoolId = EcoleA, Name = "Mathématiques", Level = "Primaire", Coefficient = 4 },
            new Subject { Id = Competences, SchoolId = EcoleA, Name = "Compétences", Level = "Primaire", Coefficient = 1, MaxScore = 60 },
            new Subject { Id = Domaine, SchoolId = EcoleA, Name = "Langue et Communication", Level = "Primaire", Coefficient = 1 });
        // Une activité rattachée au domaine : c'est ce lien qui fait du domaine un simple regroupement.
        owner.Subjects.Add(new Subject { Id = Activite, SchoolId = EcoleA, Name = "Vocabulaire", Level = "Primaire", Coefficient = 1, ParentSubjectId = Domaine });

        // Ordre d'insertion volontairement DÉSORDONNÉ, accents compris : « Élodie » doit précéder « Emma ».
        var order = new[] { ("ELEV-0004", "Emma Sow"), ("ELEV-0001", "Zoé Sow"), ("ELEV-0003", "Élodie Sow"), ("ELEV-0002", "Eric Sow"), ("ELEV-0005", "Awa Sow") };
        foreach (var (matricule, name) in order)
        {
            owner.Students.Add(new Student
            {
                SchoolId = EcoleA, Matricule = matricule, FullName = name, BirthDate = new DateOnly(2015, 1, 1),
                BirthPlace = "Dakar", Gender = "M", ClassroomId = ClasseA
            });
        }

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private async Task<(GradeSheetPdfResult Result, SpyGenerator Spy)> RunAsync(
        Guid classroomId, Guid subjectId, EvaluationType type = EvaluationType.Devoir1, byte[]? logo = null)
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var spy = new SpyGenerator();
        var handler = new GetGradeSheetPdfQueryHandler(ctx, new StubTenantProvider(EcoleA), spy, new FixedLogoProvider(logo), new SubjectFollowScope(ctx));

        var result = await handler.Handle(
            new GetGradeSheetPdfQuery(classroomId, subjectId, TrimestreA, type), CancellationToken.None);

        return (result, spy);
    }

    [Fact]
    public async Task Students_Are_Listed_Alphabetically_In_French_Order()
    {
        var (_, spy) = await RunAsync(ClasseA, Maths);

        spy.Captured!.Students.Select(s => s.FullName).Should().Equal(
            "Awa Sow", "Élodie Sow", "Emma Sow", "Eric Sow", "Zoé Sow");
    }

    [Fact]
    public async Task The_Sheet_Carries_The_Identification_Printed_In_The_Header()
    {
        var (result, spy) = await RunAsync(ClasseA, Maths, EvaluationType.Composition);

        var sheet = spy.Captured!;
        sheet.SchoolName.Should().Be("École Les Baobabs");
        sheet.SchoolYearLabel.Should().Be("2026-2027");
        sheet.TermLabel.Should().Be("1er trimestre");
        sheet.ClassroomName.Should().Be("CM2 A");
        sheet.SubjectName.Should().Be("Mathématiques");
        sheet.EvaluationLabel.Should().Be("Composition");
        result.FileName.Should().Be("Fiche-Notes-CM2-A-Mathématiques-Composition.pdf");
    }

    [Theory]
    [InlineData(EvaluationType.Devoir1, "Devoir 1")]
    [InlineData(EvaluationType.Devoir2, "Devoir 2")]
    [InlineData(EvaluationType.Composition, "Composition")]
    public async Task Each_Evaluation_Has_Its_Own_Label(EvaluationType type, string label)
    {
        var (_, spy) = await RunAsync(ClasseA, Maths, type);

        spy.Captured!.EvaluationLabel.Should().Be(label);
    }

    [Fact]
    public async Task The_Scale_Is_The_Cycle_Scale_When_The_Subject_Sets_None()
    {
        var (_, spy) = await RunAsync(ClasseA, Maths);   // Primaire → /10

        spy.Captured!.MaxScore.Should().Be(10);
    }

    [Fact]
    public async Task The_Scale_Is_The_Subjects_Own_When_It_Declares_One()
    {
        var (_, spy) = await RunAsync(ClasseA, Competences);   // grille APC : /60

        spy.Captured!.MaxScore.Should().Be(60);
    }

    [Fact]
    public async Task The_School_Logo_Is_Passed_To_The_Generator_When_Available()
    {
        var logo = new byte[] { 1, 2, 3 };

        var (_, spy) = await RunAsync(ClasseA, Maths, logo: logo);

        spy.CapturedLogo.Should().Equal(logo);
    }

    [Fact]
    public async Task A_Classroom_Of_Another_School_Is_Not_Found()
    {
        var act = () => RunAsync(ClasseB, Maths);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task An_Unknown_Subject_Is_Not_Found()
    {
        var act = () => RunAsync(ClasseA, Guid.NewGuid());

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task An_Evaluation_Domain_Cannot_Have_A_Sheet()
    {
        // Un domaine APC ne porte jamais de note : sa fiche de saisie n'aurait aucun sens.
        var act = () => RunAsync(ClasseA, Domaine);

        await act.Should().ThrowAsync<ValidationException>();
    }
}
