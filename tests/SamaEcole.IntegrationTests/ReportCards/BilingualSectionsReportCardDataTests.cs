using FluentAssertions;
using MediatR;
using SamaEcole.Application.Coefficients;
using SamaEcole.Application.ClassSubjects;
using SamaEcole.Application.Grades.Queries.GetGradeSummary;
using SamaEcole.Application.ReportCards.Queries.GetReportCardPdf;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using SamaEcole.Persistence;
using Xunit;

namespace SamaEcole.IntegrationTests.ReportCards;

/// <summary>
/// Le câblage base → <see cref="BilingualSectionsDto"/> du bulletin franco-arabe : moyenne et rang PAR SECTION,
/// sur de vraies notes en base.
///
/// Classe « 4ème A » : Maths (français, coef 4), EPS (commun, coef 1), Fiqh (sciences islamiques, coef 2),
/// Langue arabe (arabe, coef 3). Trois élèves aux profils croisés, pour que les trois classements diffèrent :
///   A : Maths 12, EPS 14, Fiqh 16, Arabe 8   → français 12,4 · arabe 11,2 · général 11,8
///   B : Maths 16, EPS 16, Fiqh 10, Arabe 10  → français 16   · arabe 10   · général 13
///   C : Maths 8,  EPS 8,  Fiqh 18, Arabe 18  → français 8    · arabe 18   · général 13
/// Un quatrième élève n'a que des notes françaises (français 11,4) : il s'intercale en français (C est alors 4e),
/// et n'entre pas en compétition dans la section arabe où il n'a aucune moyenne.
/// </summary>
[Trait("Category", "MultiTenant")]
public class BilingualSectionsReportCardDataTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("98888888-8888-8888-8888-888888888888");
    private static readonly Guid EcoleSansModule = Guid.Parse("98888888-8888-8888-8888-888888888889");
    private static readonly Guid Annee = Guid.Parse("98888888-0000-0000-0000-000000000001");
    private static readonly Guid Trimestre = Guid.Parse("98888888-0000-0000-0000-0000000000d1");
    private static readonly Guid Classe = Guid.Parse("98888888-0000-0000-0000-0000000000c1");

    private static readonly Guid Maths = Guid.Parse("98888888-0000-0000-0000-0000000000a1");
    private static readonly Guid Eps = Guid.Parse("98888888-0000-0000-0000-0000000000a2");
    private static readonly Guid Fiqh = Guid.Parse("98888888-0000-0000-0000-0000000000a3");
    private static readonly Guid LangueArabe = Guid.Parse("98888888-0000-0000-0000-0000000000a4");

    private static readonly Guid EleveA = Guid.Parse("98888888-0000-0000-0000-0000000000e1");
    private static readonly Guid EleveB = Guid.Parse("98888888-0000-0000-0000-0000000000e2");
    private static readonly Guid EleveC = Guid.Parse("98888888-0000-0000-0000-0000000000e3");
    private static readonly Guid EleveFrancais = Guid.Parse("98888888-0000-0000-0000-0000000000e4");

    // École témoin : mêmes matières arabes mais module désactivé.
    private static readonly Guid AnneeT = Guid.Parse("98888888-0000-0000-0000-000000000011");
    private static readonly Guid TrimestreT = Guid.Parse("98888888-0000-0000-0000-0000000000f1");
    private static readonly Guid ClasseT = Guid.Parse("98888888-0000-0000-0000-0000000000f2");
    private static readonly Guid FiqhT = Guid.Parse("98888888-0000-0000-0000-0000000000f3");
    private static readonly Guid EleveT = Guid.Parse("98888888-0000-0000-0000-0000000000f4");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();

        owner.Schools.AddRange(new School { Id = Ecole, Name = "Collège franco-arabe" }, new School { Id = EcoleSansModule, Name = "Collège témoin" });
        owner.SchoolSettings.AddRange(
            new SchoolSettings { SchoolId = Ecole, IsCoranModuleEnabled = true },
            new SchoolSettings { SchoolId = EcoleSansModule, IsCoranModuleEnabled = false });

        owner.SchoolYears.AddRange(
            NewYear(Annee, Ecole), NewYear(AnneeT, EcoleSansModule));
        owner.Terms.AddRange(NewTerm(Trimestre, Annee, Ecole), NewTerm(TrimestreT, AnneeT, EcoleSansModule));

        owner.Classrooms.AddRange(
            new Classroom { Id = Classe, SchoolId = Ecole, Name = "4ème A", Level = "Collège", Cycle = CycleType.College, Capacity = 40 },
            new Classroom { Id = ClasseT, SchoolId = EcoleSansModule, Name = "4ème T", Level = "Collège", Cycle = CycleType.College, Capacity = 40 });

        owner.Subjects.AddRange(
            NewSubject(Maths, "Mathématiques", 4, SectionType.French, null),
            NewSubject(Eps, "EPS", 1, SectionType.Common, null),
            NewSubject(Fiqh, "Fiqh", 2, SectionType.IslamicStudies, "الفقه"),
            NewSubject(LangueArabe, "Langue arabe", 3, SectionType.Arabic, "اللغة العربية"));
        owner.Subjects.Add(new Subject
        {
            Id = FiqhT, SchoolId = EcoleSansModule, Name = "Fiqh", Level = "Collège", Coefficient = 2,
            SectionType = SectionType.IslamicStudies, NameAr = "الفقه"
        });

        owner.Students.AddRange(
            NewStudent(EleveA, "ELEV-A", Classe, Ecole), NewStudent(EleveB, "ELEV-B", Classe, Ecole),
            NewStudent(EleveC, "ELEV-C", Classe, Ecole), NewStudent(EleveFrancais, "ELEV-F", Classe, Ecole),
            NewStudent(EleveT, "ELEV-T", ClasseT, EcoleSansModule));

        AddGrades(owner, EleveA, (Maths, 12), (Eps, 14), (Fiqh, 16), (LangueArabe, 8));
        AddGrades(owner, EleveB, (Maths, 16), (Eps, 16), (Fiqh, 10), (LangueArabe, 10));
        AddGrades(owner, EleveC, (Maths, 8), (Eps, 8), (Fiqh, 18), (LangueArabe, 18));
        AddGrades(owner, EleveFrancais, (Maths, 11), (Eps, 13));

        owner.Grades.Add(new Grade
        {
            SchoolId = EcoleSansModule, StudentId = EleveT, SubjectId = FiqhT, TermId = TrimestreT,
            EvaluationType = EvaluationType.Composition, Value = 15
        });

        await owner.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private async Task<ReportCardDto> BuildAsync(Guid school, Guid student, Guid term)
    {
        await using var db = _db.NewAppContext(school);
        return await new ReportCardDataService(new FakeSummaryMediator(db), db)
            .BuildAsync(student, term, CancellationToken.None);
    }

    [Fact]
    public async Task The_Sections_Carry_Their_Own_Average()
    {
        var card = await BuildAsync(Ecole, EleveA, Trimestre);

        card.Sections.Should().NotBeNull();
        card.Sections!.French.TotalCoefficients.Should().Be(5);
        card.Sections.French.Average.Should().Be(12.4m);
        card.Sections.Arabic.TotalCoefficients.Should().Be(5);
        card.Sections.Arabic.Average.Should().Be(11.2m);
    }

    [Fact]
    public async Task The_General_Average_Is_The_Coefficient_Weighted_Combination_Of_The_Two_Sections()
    {
        var card = await BuildAsync(Ecole, EleveA, Trimestre);

        var sections = card.Sections!;
        var combined = (sections.French.TotalPoints + sections.Arabic.TotalPoints)
                       / (sections.French.TotalCoefficients + sections.Arabic.TotalCoefficients);

        card.GeneralAverage.Should().Be(11.8m);
        combined.Should().Be(card.GeneralAverage);
    }

    [Theory]
    [InlineData("A", 2, 2, 3)]
    [InlineData("B", 1, 3, 1)]
    [InlineData("C", 4, 1, 1)]
    public async Task Each_Student_Is_Ranked_In_Each_Section_And_Overall(string who, int frenchRank, int arabicRank, int generalRank)
    {
        var student = who switch { "A" => EleveA, "B" => EleveB, _ => EleveC };

        var card = await BuildAsync(Ecole, student, Trimestre);

        card.Sections!.French.Rank.Should().Be(frenchRank);
        card.Sections.Arabic.Rank.Should().Be(arabicRank);
        card.GeneralRank.Should().Be(generalRank);
    }

    [Fact]
    public async Task Every_Subject_Is_Assigned_To_Its_Pole_For_The_Layout()
    {
        var card = await BuildAsync(Ecole, EleveA, Trimestre);

        card.Sections!.SubjectSections.Should().BeEquivalentTo(new Dictionary<Guid, SectionType>
        {
            [Maths] = SectionType.French,
            [Eps] = SectionType.Common,
            [Fiqh] = SectionType.IslamicStudies,
            [LangueArabe] = SectionType.Arabic
        });
    }

    [Fact]
    public async Task A_Student_With_No_Arabic_Subject_Keeps_The_Ordinary_Layout()
    {
        var card = await BuildAsync(Ecole, EleveFrancais, Trimestre);

        card.Sections.Should().BeNull("aucune matière arabe : pas de découpage, le bulletin reste celui d'avant");
    }

    [Fact]
    public async Task A_School_Without_The_Module_Never_Gets_Sections_Even_With_Arabic_Subjects()
    {
        var card = await BuildAsync(EcoleSansModule, EleveT, TrimestreT);

        card.IsBilingualArabic.Should().BeFalse();
        card.Sections.Should().BeNull();
    }

    private static SchoolYear NewYear(Guid id, Guid school) => new()
    {
        Id = id, SchoolId = school, Label = "2026-2027",
        StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true
    };

    private static Term NewTerm(Guid id, Guid year, Guid school) => new()
    {
        Id = id, SchoolId = school, SchoolYearId = year, Label = "1er trimestre", Order = 1,
        StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 12, 20)
    };

    private static Subject NewSubject(Guid id, string name, decimal coefficient, SectionType pole, string? nameAr) => new()
    {
        Id = id, SchoolId = Ecole, Name = name, NameAr = nameAr, Level = "Collège", Coefficient = coefficient, SectionType = pole
    };

    private static Student NewStudent(Guid id, string matricule, Guid classroom, Guid school) => new()
    {
        Id = id, SchoolId = school, Matricule = matricule, FullName = matricule,
        BirthDate = new DateOnly(2011, 1, 1), BirthPlace = "Dakar", Gender = "F", ClassroomId = classroom
    };

    private static void AddGrades(ApplicationDbContext owner, Guid student, params (Guid Subject, decimal Value)[] grades)
    {
        foreach (var (subject, value) in grades)
        {
            owner.Grades.Add(new Grade
            {
                SchoolId = Ecole, StudentId = student, SubjectId = subject, TermId = Trimestre,
                EvaluationType = EvaluationType.Composition, Value = value
            });
        }
    }

    /// <summary>Doublure recopiée de ExemptSubjectReportCardDataTests : route le seul GetGradeSummaryQuery vers son vrai handler.</summary>
    private sealed class FakeSummaryMediator(ApplicationDbContext dbContext) : ISender
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is GetGradeSummaryQuery query)
            {
                return (Task<TResponse>)(object)new GetGradeSummaryQueryHandler(dbContext, new CoefficientOverrideLoader(dbContext), new SubjectFollowScope(dbContext)).Handle(query, cancellationToken);
            }

            throw new NotSupportedException($"FakeSummaryMediator ne sait pas router {request.GetType().Name}.");
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
