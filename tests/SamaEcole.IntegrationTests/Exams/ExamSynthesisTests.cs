using FluentAssertions;
using MediatR;
using SamaEcole.Application.ClassSubjects;
using SamaEcole.Application.Coefficients;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Exams.Common;
using SamaEcole.Application.Exams.Queries.GetExamSynthesisPdf;
using SamaEcole.Application.Grades.Queries.GetGradeSummary;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.Infrastructure.Documents;
using SamaEcole.IntegrationTests.Common;
using SamaEcole.Persistence;
using UglyToad.PdfPig;
using Xunit;

namespace SamaEcole.IntegrationTests.Exams;

/// <summary>
/// PV de session et fiche synthétique d'examen (module Franco-Arabe, bloc 4) sur de vraies données : moyennes
/// annuelles par section, IEN repris tel quel, nom arabe, résultat, filtre des dossiers, isolation d'école — puis le
/// PDF réellement généré.
///
/// Awa (BFEM, dossier Transmis, IEN renseigné, nom arabe) : période 1 → français 12, arabe 11,2 ; période 2 → français
/// 14, arabe 11,6 ; annuelle : français 13, arabe 11,4, générale (11,5556 + 12,6667) ÷ 2.
/// </summary>
[Trait("Category", "MultiTenant")]
public class ExamSynthesisTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("9a111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleSansModule = Guid.Parse("9a111111-1111-1111-1111-111111111112");
    private static readonly Guid Annee = Guid.Parse("9a111111-0000-0000-0000-000000000001");
    private static readonly Guid Periode1 = Guid.Parse("9a111111-0000-0000-0000-0000000000d1");
    private static readonly Guid Periode2 = Guid.Parse("9a111111-0000-0000-0000-0000000000d2");
    private static readonly Guid Classe = Guid.Parse("9a111111-0000-0000-0000-0000000000c1");
    private static readonly Guid Session = Guid.Parse("9a111111-0000-0000-0000-0000000000f1");
    private static readonly Guid SessionVide = Guid.Parse("9a111111-0000-0000-0000-0000000000f2");

    private static readonly Guid Maths = Guid.Parse("9a111111-0000-0000-0000-0000000000a1");
    private static readonly Guid Fiqh = Guid.Parse("9a111111-0000-0000-0000-0000000000a2");
    private static readonly Guid LangueArabe = Guid.Parse("9a111111-0000-0000-0000-0000000000a3");

    private static readonly Guid Awa = Guid.Parse("9a111111-0000-0000-0000-0000000000e1");
    private static readonly Guid Moussa = Guid.Parse("9a111111-0000-0000-0000-0000000000e2");
    private static readonly Guid Fatou = Guid.Parse("9a111111-0000-0000-0000-0000000000e3");
    private static readonly Guid DossierAwa = Guid.Parse("9a111111-0000-0000-0000-000000000da1");
    private static readonly Guid DossierMoussa = Guid.Parse("9a111111-0000-0000-0000-000000000da2");
    private static readonly Guid DossierFatou = Guid.Parse("9a111111-0000-0000-0000-000000000da3");

    // École sans le module : un élève avec un nom arabe saisi, qui ne doit PAS sortir sur le document.
    private static readonly Guid AnneeT = Guid.Parse("9a111111-0000-0000-0000-000000000011");
    private static readonly Guid PeriodeT = Guid.Parse("9a111111-0000-0000-0000-0000000000e9");
    private static readonly Guid ClasseT = Guid.Parse("9a111111-0000-0000-0000-0000000000c9");
    private static readonly Guid SessionT = Guid.Parse("9a111111-0000-0000-0000-0000000000f9");
    private static readonly Guid MathsT = Guid.Parse("9a111111-0000-0000-0000-0000000000a9");
    private static readonly Guid EleveT = Guid.Parse("9a111111-0000-0000-0000-0000000000e8");
    private static readonly Guid DossierT = Guid.Parse("9a111111-0000-0000-0000-000000000da9");

    public async Task InitializeAsync()
    {
        PdfFonts.EnsureRegistered();
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();

        owner.Schools.AddRange(
            new School { Id = Ecole, Name = "Collège franco-arabe", InspectionAcademie = "Thiès", InspectionEducationFormation = "Mbour 1" },
            new School { Id = EcoleSansModule, Name = "Collège témoin" });
        owner.SchoolSettings.AddRange(
            new SchoolSettings { SchoolId = Ecole, IsCoranModuleEnabled = true },
            new SchoolSettings { SchoolId = EcoleSansModule, IsCoranModuleEnabled = false });

        owner.SchoolYears.AddRange(Year(Annee, Ecole), Year(AnneeT, EcoleSansModule));
        owner.Terms.AddRange(Term(Periode1, Annee, Ecole, 1), Term(Periode2, Annee, Ecole, 2), Term(PeriodeT, AnneeT, EcoleSansModule, 1));

        owner.Classrooms.AddRange(
            new Classroom { Id = Classe, SchoolId = Ecole, Name = "3ème A", Level = "Collège", Cycle = CycleType.College, Capacity = 40 },
            new Classroom { Id = ClasseT, SchoolId = EcoleSansModule, Name = "3ème T", Level = "Collège", Cycle = CycleType.College, Capacity = 40 });

        owner.Subjects.AddRange(
            new Subject { Id = Maths, SchoolId = Ecole, Name = "Mathématiques", Level = "Collège", Coefficient = 4 },
            new Subject { Id = Fiqh, SchoolId = Ecole, Name = "Fiqh", Level = "Collège", Coefficient = 2, SectionType = SectionType.IslamicStudies },
            new Subject { Id = LangueArabe, SchoolId = Ecole, Name = "Langue arabe", Level = "Collège", Coefficient = 3, SectionType = SectionType.Arabic },
            new Subject { Id = MathsT, SchoolId = EcoleSansModule, Name = "Mathématiques", Level = "Collège", Coefficient = 4 });

        owner.Students.AddRange(
            Student(Awa, Ecole, Classe, "ELEV-A", "Awa Fall", "آوا فال", "IEN-0000001"),
            Student(Moussa, Ecole, Classe, "ELEV-M", "Moussa Ba", null, null),
            Student(Fatou, Ecole, Classe, "ELEV-F", "Fatou Sy", null, null),
            Student(EleveT, EcoleSansModule, ClasseT, "ELEV-T", "Seydou Ndiaye", "سيدو انداي", "IEN-0000009"));

        AddGrades(owner, Awa, Periode1, (Maths, 12), (Fiqh, 16), (LangueArabe, 8));
        AddGrades(owner, Awa, Periode2, (Maths, 14), (Fiqh, 14), (LangueArabe, 10));
        AddGrades(owner, Moussa, Periode1, (Maths, 10));
        owner.Grades.Add(new Grade
        {
            SchoolId = EcoleSansModule, StudentId = EleveT, SubjectId = MathsT, TermId = PeriodeT,
            EvaluationType = EvaluationType.Composition, Value = 15
        });

        owner.ExamSessions.AddRange(
            new ExamSession { Id = Session, SchoolId = Ecole, SchoolYearId = Annee, ExamType = ExamType.BFEM },
            new ExamSession { Id = SessionVide, SchoolId = Ecole, SchoolYearId = Annee, ExamType = ExamType.BAC, Series = "L" },
            new ExamSession { Id = SessionT, SchoolId = EcoleSansModule, SchoolYearId = AnneeT, ExamType = ExamType.BFEM });

        owner.ExamDossiers.AddRange(
            Dossier(DossierAwa, Ecole, Session, Awa, Classe, "BFEM-002", ExamDossierStatus.Transmis),
            Dossier(DossierMoussa, Ecole, Session, Moussa, Classe, "BFEM-001", ExamDossierStatus.Complet),
            Dossier(DossierFatou, Ecole, Session, Fatou, Classe, null, ExamDossierStatus.Incomplet),
            Dossier(DossierT, EcoleSansModule, SessionT, EleveT, ClasseT, "T-001", ExamDossierStatus.Valide));

        owner.ExamResults.Add(new ExamResult
        {
            SchoolId = Ecole, ExamDossierId = DossierAwa, IsAdmitted = true, Mention = ExamMention.AssezBien,
            AverageScore = 12.5m, DeliberatedOn = new DateOnly(2027, 7, 10)
        });

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private async Task<SamaEcole.Application.Exams.ExamSynthesisModel> BuildAsync(Guid school, Guid session, Guid? dossier = null)
    {
        await using var db = _db.NewAppContext(school);
        return await new ExamSynthesisBuilder(db, new Tenant(school), new FakeSummaryMediator(db))
            .BuildAsync(session, dossier, CancellationToken.None);
    }

    // ---------------------------------------------------------------- Contenu du modèle

    [Fact]
    public async Task The_Pv_Lists_Only_Complete_Transmitted_Or_Validated_Dossiers_In_Table_Number_Order()
    {
        var model = await BuildAsync(Ecole, Session);

        model.Candidates.Select(c => c.StudentFullName).Should().Equal("Moussa Ba", "Awa Fall");
        model.Candidates.Select(c => c.CandidateNumber).Should().Equal("BFEM-001", "BFEM-002");
        model.Candidates.Should().NotContain(c => c.StudentFullName == "Fatou Sy", "un dossier Incomplet n'a rien à faire sur un PV");
        model.ExamType.Should().Be(ExamType.BFEM);
        model.IsBilingualArabic.Should().BeTrue();
    }

    [Fact]
    public async Task A_Candidate_Carries_His_Ien_His_Arabic_Name_And_His_Result()
    {
        var awa = (await BuildAsync(Ecole, Session)).Candidates.Single(c => c.StudentFullName == "Awa Fall");

        awa.IenNumber.Should().Be("IEN-0000001");
        awa.StudentFullNameAr.Should().Be("آوا فال");
        awa.IsAdmitted.Should().BeTrue();
        awa.Mention.Should().Be(ExamMention.AssezBien);
        awa.ExamAverage.Should().Be(12.5m);
        awa.ClassroomName.Should().Be("3ème A");
        awa.GradingScale.Should().Be(20);
    }

    [Fact]
    public async Task A_Student_Without_Ien_Or_Arabic_Name_Gets_Neither_Invented()
    {
        var moussa = (await BuildAsync(Ecole, Session)).Candidates.Single(c => c.StudentFullName == "Moussa Ba");

        moussa.IenNumber.Should().BeNull("jamais un IEN inventé — l'administration seule l'attribue");
        moussa.StudentFullNameAr.Should().BeNull();
        moussa.IsAdmitted.Should().BeNull("le jury n'a rien saisi : ni admis ni refusé");
    }

    [Fact]
    public async Task The_Annual_Averages_Are_The_Mean_Of_The_Term_Averages_Per_Section()
    {
        var awa = (await BuildAsync(Ecole, Session)).Candidates.Single(c => c.StudentFullName == "Awa Fall");

        awa.FrenchAverage.Should().Be(13m, "(12 + 14) ÷ 2");
        awa.ArabicAverage.Should().Be(11.4m, "(11,2 + 11,6) ÷ 2");
        awa.CombinedAverage.Should().BeApproximately((104m / 9m + 114m / 9m) / 2m, 0.0001m,
            "moyenne des moyennes générales de période : la règle de la moyenne annuelle du bulletin");
    }

    [Fact]
    public async Task A_Section_Without_Any_Grade_Has_No_Average_Not_A_Zero()
    {
        var moussa = (await BuildAsync(Ecole, Session)).Candidates.Single(c => c.StudentFullName == "Moussa Ba");

        moussa.FrenchAverage.Should().Be(10m);
        moussa.ArabicAverage.Should().BeNull();
    }

    [Fact]
    public async Task A_Single_Dossier_Is_Built_Whatever_Its_Status()
    {
        var model = await BuildAsync(Ecole, Session, DossierFatou);

        model.Candidates.Should().ContainSingle().Which.StudentFullName.Should().Be("Fatou Sy");
    }

    [Fact]
    public async Task A_Session_Without_Eligible_Dossier_Is_A_Validation_Error()
    {
        var act = () => BuildAsync(Ecole, SessionVide);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task A_Session_Of_Another_School_Is_Not_Found()
    {
        var act = () => BuildAsync(Ecole, SessionT);

        await act.Should().ThrowAsync<KeyNotFoundException>("la session de l'autre école est invisible (RLS + filtre)");
    }

    [Fact]
    public async Task A_School_Without_The_Module_Gets_No_Arabic_Name_And_No_Section_Average()
    {
        var model = await BuildAsync(EcoleSansModule, SessionT);

        model.IsBilingualArabic.Should().BeFalse();
        var seydou = model.Candidates.Should().ContainSingle().Subject;
        seydou.StudentFullNameAr.Should().BeNull("le nom arabe saisi ne sort pas hors module");
        seydou.FrenchAverage.Should().BeNull();
        seydou.ArabicAverage.Should().BeNull();
        seydou.CombinedAverage.Should().Be(15m);
        seydou.IenNumber.Should().Be("IEN-0000009");
    }

    // ---------------------------------------------------------------- PDF réellement généré

    private async Task<byte[]> PvPdfAsync(Guid school, Guid session)
    {
        await using var db = _db.NewAppContext(school);
        var handlers = new GetExamSynthesisPdfQueryHandlers(
            db, new ExamSynthesisBuilder(db, new Tenant(school), new FakeSummaryMediator(db)), new NoLogo(), new ExamSynthesisPdfGenerator());
        return await handlers.Handle(new GetExamSessionPvPdfQuery(session), CancellationToken.None);
    }

    private async Task<byte[]> SheetPdfAsync(Guid school, Guid dossier)
    {
        await using var db = _db.NewAppContext(school);
        var handlers = new GetExamSynthesisPdfQueryHandlers(
            db, new ExamSynthesisBuilder(db, new Tenant(school), new FakeSummaryMediator(db)), new NoLogo(), new ExamSynthesisPdfGenerator());
        return await handlers.Handle(new GetExamCandidateSynthesisPdfQuery(dossier), CancellationToken.None);
    }

    private static string Text(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        return string.Join(" ", document.GetPages().Select(p => p.Text));
    }

    [Fact]
    public async Task The_Session_Pv_Is_A_Landscape_Pdf_With_Both_Candidates_And_Their_Ien()
    {
        var pdf = await PvPdfAsync(Ecole, Session);

        using var document = PdfDocument.Open(pdf);
        document.GetPage(1).Width.Should().BeGreaterThan(document.GetPage(1).Height);
        var text = Text(pdf);
        text.Should().Contain("PROCÈS-VERBAL DE SESSION").And.Contain("BFEM FRANCO-ARABE");
        text.Should().Contain("Moussa Ba").And.Contain("Awa Fall").And.Contain("IEN-0000001");
        text.Should().NotContain("Fatou Sy");
        text.Should().Contain("Admis(e)").And.Contain("Assez Bien");
    }

    [Fact]
    public async Task The_Candidate_Sheet_Prints_The_Candidate_Identity_And_Averages()
    {
        var text = Text(await SheetPdfAsync(Ecole, DossierAwa));

        text.Should().Contain("FICHE SYNTHÉTIQUE DU CANDIDAT").And.Contain("Awa Fall").And.Contain("IEN-0000001");
        text.Should().Contain("13 /20").And.Contain("11,4 /20");
    }

    [Fact]
    public async Task A_Missing_Ien_Is_Printed_As_Not_Attributed()
    {
        var text = Text(await SheetPdfAsync(Ecole, DossierMoussa));

        text.Should().Contain("non attribué");
    }

    [Fact]
    public async Task A_School_Without_The_Module_Gets_A_French_Only_Document()
    {
        var text = Text(await PvPdfAsync(EcoleSansModule, SessionT));

        text.Should().Contain("PROCÈS-VERBAL DE SESSION — BFEM");
        text.Should().NotContain("FRANCO-ARABE");
        text.Should().NotContain("Moy. FR", "les colonnes par section n'existent que pour un établissement franco-arabe");
    }

    // ---------------------------------------------------------------- Aides

    private static SchoolYear Year(Guid id, Guid school) => new()
    {
        Id = id, SchoolId = school, Label = "2026-2027",
        StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true
    };

    private static Term Term(Guid id, Guid year, Guid school, int order) => new()
    {
        Id = id, SchoolId = school, SchoolYearId = year, Label = $"{order}er trimestre", Order = order,
        StartDate = new DateOnly(2026, 9, 1).AddMonths(3 * (order - 1)), EndDate = new DateOnly(2026, 11, 30).AddMonths(3 * (order - 1))
    };

    private static Student Student(Guid id, Guid school, Guid classroom, string matricule, string name, string? nameAr, string? ien) => new()
    {
        Id = id, SchoolId = school, Matricule = matricule, FullName = name, FullNameAr = nameAr, IenNumber = ien,
        BirthDate = new DateOnly(2011, 4, 2), BirthPlace = "Dakar", Gender = "F", ClassroomId = classroom
    };

    private static ExamDossier Dossier(Guid id, Guid school, Guid session, Guid student, Guid classroom, string? number, ExamDossierStatus status) => new()
    {
        Id = id, SchoolId = school, ExamSessionId = session, StudentId = student, ClassroomId = classroom,
        CandidateNumber = number, ExamCenterName = "Collège de Mbour", Status = status
    };

    private static void AddGrades(ApplicationDbContext owner, Guid student, Guid term, params (Guid Subject, decimal Value)[] grades)
    {
        foreach (var (subject, value) in grades)
        {
            owner.Grades.Add(new Grade
            {
                SchoolId = Ecole, StudentId = student, SubjectId = subject, TermId = term,
                EvaluationType = EvaluationType.Composition, Value = value
            });
        }
    }

    private sealed class Tenant(Guid school) : ITenantProvider
    {
        public Guid? CurrentSchoolId => school;
    }

    private sealed class NoLogo : ISchoolLogoProvider
    {
        public Task<byte[]?> TryFetchAsync(string? url, CancellationToken cancellationToken) => Task.FromResult<byte[]?>(null);
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
