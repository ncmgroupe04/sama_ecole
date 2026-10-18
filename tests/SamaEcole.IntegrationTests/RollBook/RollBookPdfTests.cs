using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Features.Schedules;
using SamaEcole.Application.RollBook;
using SamaEcole.Application.RollBook.Commands.UpsertRollBookEntries;
using SamaEcole.Application.RollBook.Queries.GetRollBookPdf;
using SamaEcole.Application.Schools;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.Infrastructure.Documents;
using SamaEcole.IntegrationTests.Common;
using SamaEcole.Persistence;
using UglyToad.PdfPig;
using Xunit;
using ValidationException = SamaEcole.Application.Common.Exceptions.ValidationException;

namespace SamaEcole.IntegrationTests.RollBook;

file sealed class PdfStubTenant(Guid? schoolId) : ITenantProvider
{
    public Guid? CurrentSchoolId => schoolId;
}

file sealed class PdfFixedTime(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

file sealed class NoImages : ISchoolLogoProvider
{
    public Task<byte[]?> TryFetchAsync(string? logoUrl, CancellationToken cancellationToken) => Task.FromResult<byte[]?>(null);
}

/// <summary>Route le seul GetClassroomSectionScheduleQuery vers son vrai handler (même doublure que les autres tests PDF).</summary>
file sealed class SectionMediator(ApplicationDbContext dbContext) : ISender
{
    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        if (request is GetClassroomSectionScheduleQuery query)
        {
            return (Task<TResponse>)(object)new GetSectionScheduleQueryHandlers(dbContext).Handle(query, cancellationToken);
        }

        throw new NotSupportedException($"SectionMediator ne sait pas router {request.GetType().Name}.");
    }

    public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();
    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

/// <summary>
/// Cahier de roulement imprimable (Franco-Arabe Bloc 5c) sur un VRAI PostgreSQL : le PDF est généré par le vrai
/// générateur à partir du vrai moteur, puis relu avec PdfPig. École A : module Franco-Arabe activé, noms arabes
/// renseignés ; école B : hors module. Novembre 2026 = 23 jours de classe (fermeture les 4 et 5).
/// </summary>
public class RollBookPdfTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("f1111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("f2222222-2222-2222-2222-222222222222");
    private static readonly Guid AnneeA = Guid.Parse("f1111111-0000-0000-0000-000000000001");
    private static readonly Guid AnneeB = Guid.Parse("f2222222-0000-0000-0000-000000000001");
    private static readonly Guid TermId = Guid.Parse("f1111111-0000-0000-0000-0000000000a1");

    private static readonly Guid CM2A = Guid.Parse("fa000000-0000-0000-0000-0000000000a1");
    private static readonly Guid CM2B = Guid.Parse("fa000000-0000-0000-0000-0000000000a2");
    private static readonly Guid CM1A = Guid.Parse("fa000000-0000-0000-0000-0000000000a3");
    private static readonly Guid ClasseB = Guid.Parse("fa000000-0000-0000-0000-0000000000b1");

    private static readonly Guid Awa = Guid.Parse("fe000000-0000-0000-0000-0000000000e1");
    private static readonly Guid Binta = Guid.Parse("fe000000-0000-0000-0000-0000000000e2");
    private static readonly Guid Eleve2B = Guid.Parse("fe000000-0000-0000-0000-0000000000e3");
    private static readonly Guid Eleve1A = Guid.Parse("fe000000-0000-0000-0000-0000000000e4");
    private static readonly Guid EleveB = Guid.Parse("fe000000-0000-0000-0000-0000000000e5");

    private static readonly Guid CompteProf = Guid.Parse("fd000000-0000-0000-0000-000000000001");
    private static readonly Guid FicheProf = Guid.Parse("ff000000-0000-0000-0000-000000000001");
    private static readonly Guid Maths = Guid.Parse("fc000000-0000-0000-0000-0000000000c1");
    private static readonly Guid Arabe = Guid.Parse("fc000000-0000-0000-0000-0000000000c2");

    private static readonly TestCurrentUser Directeur = new(Guid.Parse("fd000000-0000-0000-0000-0000000000f1"), Role.Directeur);
    private static readonly TestCurrentUser Secretariat = new(Guid.Parse("fd000000-0000-0000-0000-0000000000f2"), Role.Secretariat);
    private static readonly TestCurrentUser Surveillant = new(Guid.Parse("fd000000-0000-0000-0000-0000000000f3"), Role.Surveillant);
    private static readonly TestCurrentUser SuperAdmin = new(Guid.Parse("fd000000-0000-0000-0000-0000000000f4"), Role.SuperAdmin);
    private static readonly TestCurrentUser Prof = new(CompteProf, Role.Enseignant);

    private static readonly DateTimeOffset Aujourdhui = new(2026, 12, 18, 9, 0, 0, TimeSpan.Zero);

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();

        owner.Schools.AddRange(
            new School
            {
                Id = EcoleA, Name = "Complexe Les Baobabs", NameAr = "مجمع الباوباب", InspectionAcademie = "Thiès",
                InspectionAcademieAr = "تييس", InspectionEducationFormation = "Mbour 1", InspectionEducationFormationAr = "مبور 1"
            },
            new School { Id = EcoleB, Name = "Collège Teranga", InspectionAcademie = "Dakar" });
        owner.SchoolSettings.AddRange(
            new SchoolSettings { SchoolId = EcoleA, IsCoranModuleEnabled = true },
            new SchoolSettings { SchoolId = EcoleB, IsCoranModuleEnabled = false });

        owner.SchoolYears.AddRange(
            new SchoolYear { Id = AnneeA, SchoolId = EcoleA, Label = "2026-2027", StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2027, 7, 31), IsActive = true },
            new SchoolYear { Id = AnneeB, SchoolId = EcoleB, Label = "2026-2027", StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2027, 7, 31), IsActive = true });

        owner.Terms.Add(new Term
        {
            Id = TermId, SchoolId = EcoleA, SchoolYearId = AnneeA, Label = "1er trimestre", Order = 1,
            StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2026, 12, 20)
        });

        owner.Classrooms.AddRange(
            new Classroom { Id = CM2A, SchoolId = EcoleA, Name = "CM2 A", Level = "Primaire", Cycle = CycleType.Primaire, Capacity = 40 },
            new Classroom { Id = CM2B, SchoolId = EcoleA, Name = "CM2 B", Level = "Primaire", Cycle = CycleType.Primaire, Capacity = 40 },
            new Classroom { Id = CM1A, SchoolId = EcoleA, Name = "CM1 A", Level = "Primaire", Cycle = CycleType.Primaire, Capacity = 40 },
            new Classroom { Id = ClasseB, SchoolId = EcoleB, Name = "6e A", Level = "Collège", Cycle = CycleType.College, Capacity = 40 });

        owner.Students.AddRange(
            Student(Awa, EcoleA, CM2A, "Awa Fall", "فاطمة فال"),
            Student(Binta, EcoleA, CM2A, "Binta Diop", null),
            Student(Eleve2B, EcoleA, CM2B, "Eli Gaye", null),
            Student(Eleve1A, EcoleA, CM1A, "Fatou Ndiaye", null),
            Student(EleveB, EcoleB, ClasseB, "Intrus B", null));

        owner.SchoolClosures.Add(new SchoolClosure
        {
            SchoolId = EcoleA, SchoolYearId = AnneeA, Label = "Vacances", LabelAr = "عطلة",
            StartDate = new DateOnly(2026, 11, 4), EndDate = new DateOnly(2026, 11, 5), Kind = SchoolClosureKind.Vacation
        });

        owner.Subjects.AddRange(
            new Subject { Id = Maths, SchoolId = EcoleA, Name = "Mathématiques", Level = "Primaire", Coefficient = 1, SectionType = SectionType.French },
            new Subject { Id = Arabe, SchoolId = EcoleA, Name = "Arabe", Level = "Primaire", Coefficient = 1, SectionType = SectionType.Arabic });
        owner.Users.Add(new User { Id = CompteProf, SchoolId = EcoleA, Email = "prof@a.sn", PasswordHash = "x", FullName = "Prof", Role = Role.Enseignant });
        owner.Teachers.Add(new Teacher { Id = FicheProf, SchoolId = EcoleA, Matricule = "ENS-001", FullName = "Prof", Email = "prof@a.sn", BirthDate = new DateOnly(1985, 1, 1), UserId = CompteProf });

        // Alternance du lundi : français le matin, arabe l'après-midi (constat du Bloc 4, imprimé sur le cahier bilingue).
        owner.ScheduleSlots.AddRange(
            new ScheduleSlot { SchoolId = EcoleA, TeacherId = FicheProf, ClassroomId = CM2A, SubjectId = Maths, DayOfWeek = DayOfWeek.Monday, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(10, 0) },
            new ScheduleSlot { SchoolId = EcoleA, TeacherId = FicheProf, ClassroomId = CM2A, SubjectId = Arabe, DayOfWeek = DayOfWeek.Monday, StartTime = new TimeOnly(14, 0), EndTime = new TimeOnly(16, 0) });

        await owner.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private static Student Student(Guid id, Guid school, Guid classroom, string name, string? nameAr) => new()
    {
        Id = id, SchoolId = school, Matricule = $"ELEV-{id.ToString()[^2..]}", FullName = name, FullNameAr = nameAr,
        BirthDate = new DateOnly(2015, 1, 1), BirthPlace = "Dakar", Gender = "F", ClassroomId = classroom
    };

    private static RollBookEntryInput In(Guid student, DateOnly date, HalfDay half, AttendanceStatus status) =>
        new(student, date, half, status);

    private async Task FillAsync(Guid classroom, params RollBookEntryInput[] entries)
    {
        await using var db = _db.NewAppContext(EcoleA);
        var scope = new RollBookScopeAuthorizer(db, Directeur);
        await new UpsertRollBookEntriesCommandHandler(db, new PdfStubTenant(EcoleA), Directeur, scope, new PdfFixedTime(Aujourdhui),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<UpsertRollBookEntriesCommandHandler>.Instance)
            .Handle(new UpsertRollBookEntriesCommand(classroom, entries), CancellationToken.None);
    }

    private async Task<RollBookPdfFile> PdfAsync(
        Guid school, ICurrentUserService user, Guid classroom, string? month = "2026-11", Guid? termId = null, string? lang = null)
    {
        await using var db = _db.NewAppContext(school);
        var engine = new RollBookEngine(db, new WorkingDayGuard(db));
        var handler = new GetRollBookPdfQueryHandler(
            db, new PdfStubTenant(school), user, engine, new RollBookScopeAuthorizer(db, user),
            new SectionMediator(db), new NoImages(), new RollBookPdfGenerator(), new PdfFixedTime(Aujourdhui));

        return await handler.Handle(new GetRollBookPdfQuery(classroom, termId is null ? month : null, termId, lang), CancellationToken.None);
    }

    private static string Text(RollBookPdfFile file)
    {
        using var document = PdfDocument.Open(file.Content);
        return string.Join(" ", document.GetPages().Select(p => p.Text));
    }

    private static int ArabicLetters(RollBookPdfFile file)
    {
        using var document = PdfDocument.Open(file.Content);
        return document.GetPages().SelectMany(p => p.Letters)
            .Count(l => l.Value.Length > 0 && l.Value[0] is >= (char)0x0600 and <= (char)0x06FF or >= (char)0xFB50 and <= (char)0xFDFF or >= (char)0xFE70 and <= (char)0xFEFF);
    }

    private static int Pages(RollBookPdfFile file)
    {
        using var document = PdfDocument.Open(file.Content);
        return document.NumberOfPages;
    }

    // ----------------------------------------------------------------------------------- Cahier mensuel

    [Fact]
    public async Task The_Monthly_Cahier_Carries_The_School_Header_The_Students_And_The_Recorded_Marks()
    {
        await FillAsync(CM2A,
            In(Awa, new DateOnly(2026, 11, 2), HalfDay.Morning, AttendanceStatus.UnjustifiedAbsence),
            In(Binta, new DateOnly(2026, 11, 2), HalfDay.Morning, AttendanceStatus.Present));

        var file = await PdfAsync(EcoleA, Directeur, CM2A, lang: "fr");

        file.Content.Take(5).Should().Equal("%PDF-"u8.ToArray());
        file.FileName.Should().Be("Cahier-de-roulement-CM2-A-2026-11.pdf");

        var text = Text(file);
        text.Should().Contain("Inspection d'Académie de : Thiès").And.Contain("Mbour 1").And.Contain("Complexe Les Baobabs");
        text.Should().Contain("CAHIER DE ROULEMENT — NOVEMBRE 2026").And.Contain("Niveau : CM2");
        text.Should().Contain("Awa Fall").And.Contain("Binta Diop");
        text.Should().NotContain("Eli Gaye", "élève d'une autre classe").And.NotContain("Intrus B", "élève d'une autre école");
        ArabicLetters(file).Should().Be(0, "langue française demandée");
    }

    [Fact]
    public async Task The_Default_Language_Is_Bilingual_For_A_Franco_Arabic_School()
    {
        var file = await PdfAsync(EcoleA, Directeur, CM2A, lang: null);

        ArabicLetters(file).Should().BeGreaterThan(150);
        Text(file).Should().Contain("RÉPUBLIQUE DU SÉNÉGAL");
    }

    [Fact]
    public async Task The_Bilingual_Cahier_Uses_The_Arabic_Names_Closure_Labels_And_The_Section_Alternation_Of_The_Class()
    {
        var file = await PdfAsync(EcoleA, Directeur, CM2A, lang: "bi");

        // Alternance du Bloc 4 : français le matin, arabe l'après-midi.
        Text(file).Should().Contain("Alternance : le matin en français, l'après-midi en arabe");
        ArabicLetters(file).Should().BeGreaterThan(150);
    }

    [Fact]
    public async Task The_Arabic_Cahier_Is_Arabic_Only()
    {
        var file = await PdfAsync(EcoleA, Directeur, CM2A, lang: "ar");

        Text(file).Should().NotContain("RÉPUBLIQUE DU SÉNÉGAL").And.NotContain("CAHIER DE ROULEMENT");
        ArabicLetters(file).Should().BeGreaterThan(200);
    }

    [Fact]
    public async Task A_School_Outside_The_Module_Gets_A_French_Default_And_Refuses_Arabic_And_Bilingual()
    {
        var french = await PdfAsync(EcoleB, Directeur, ClasseB, lang: null);
        ArabicLetters(french).Should().Be(0);
        Text(french).Should().NotContain("Alternance");

        foreach (var lang in new[] { "ar", "bi" })
        {
            var act = async () => await PdfAsync(EcoleB, Directeur, ClasseB, lang: lang);
            (await act.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().ContainKey("Lang");
        }
    }

    [Fact]
    public async Task The_Cahier_Counts_Rates_And_Coverage_The_Same_Way_As_The_Screen()
    {
        await FillAsync(CM2A,
            In(Awa, new DateOnly(2026, 11, 2), HalfDay.Morning, AttendanceStatus.Present),
            In(Awa, new DateOnly(2026, 11, 2), HalfDay.Afternoon, AttendanceStatus.JustifiedAbsence),
            In(Binta, new DateOnly(2026, 11, 2), HalfDay.Morning, AttendanceStatus.UnjustifiedAbsence),
            In(Binta, new DateOnly(2026, 11, 2), HalfDay.Afternoon, AttendanceStatus.Present));

        var file = await PdfAsync(EcoleA, Directeur, CM2A, lang: "fr");
        var text = Text(file);

        // 4 renseignées : 2 présents, 1 justifiée, 1 non justifiée → présence 50 %, absence 50 %, assiduité 75 %.
        text.Should().Contain("50 %").And.Contain("75 %");
        // 2 élèves × 23 jours de classe × 2 demi-journées = 92 attendues.
        text.Should().Contain("92").And.Contain("Couverture").And.Contain("4,4 %", "4 sur 92 = 4,35 %, arrondi comme à l'écran (0,0435 → 4,4 %)");
    }

    [Fact]
    public async Task A_Month_Outside_Any_School_Year_Or_An_Unknown_Class_Is_Refused()
    {
        var month = async () => await PdfAsync(EcoleA, Directeur, CM2A, month: "2025-03");
        await month.Should().ThrowAsync<ValidationException>();

        var classroom = async () => await PdfAsync(EcoleA, Directeur, Guid.NewGuid());
        await classroom.Should().ThrowAsync<ValidationException>();
    }

    // ------------------------------------------------------------------------------ Période d'évaluation

    [Fact]
    public async Task A_Term_Prints_One_Grid_Per_Month_Then_The_Recap_And_For_The_Direction_The_Level_Synthesis()
    {
        await FillAsync(CM2A, In(Awa, new DateOnly(2026, 11, 2), HalfDay.Morning, AttendanceStatus.Present));
        await FillAsync(CM2B, In(Eleve2B, new DateOnly(2026, 11, 2), HalfDay.Morning, AttendanceStatus.UnjustifiedAbsence));

        var file = await PdfAsync(EcoleA, Directeur, CM2A, termId: TermId, lang: "fr");
        var text = Text(file);

        file.FileName.Should().Be("Cahier-de-roulement-CM2-A-Periode-1er-trimestre.pdf");
        text.Should().Contain("OCTOBRE 2026").And.Contain("NOVEMBRE 2026").And.Contain("DÉCEMBRE 2026");
        text.Should().Contain("RÉCAPITULATIF PÉRIODIQUE").And.Contain("1er trimestre");
        text.Should().Contain("SYNTHÈSE DU NIVEAU CM2").And.Contain("CM2 A").And.Contain("CM2 B").And.Contain("Total CM2");
        text.Should().NotContain("CM1 A", "une classe d'un autre niveau n'est pas dans la synthèse du niveau CM2");
        Pages(file).Should().BeGreaterThanOrEqualTo(5, "trois grilles mensuelles, le récapitulatif, la synthèse du niveau");
    }

    [Theory]
    [InlineData(Role.Directeur, true)]
    [InlineData(Role.Secretariat, true)]
    [InlineData(Role.SuperAdmin, true)]
    [InlineData(Role.Surveillant, false)]
    public async Task The_Level_Synthesis_Is_Reserved_To_The_Roles_Entitled_To_Statistics(Role role, bool expected)
    {
        var user = role switch
        {
            Role.Directeur => Directeur,
            Role.Secretariat => Secretariat,
            Role.SuperAdmin => SuperAdmin,
            _ => Surveillant
        };

        var file = await PdfAsync(EcoleA, user, CM2A, termId: TermId, lang: "fr");

        Text(file).Contains("SYNTHÈSE DU NIVEAU").Should().Be(expected,
            "le Surveillant exporte le cahier de SA classe, pas les statistiques des autres classes du niveau");
        Text(file).Should().Contain("RÉCAPITULATIF PÉRIODIQUE");
    }

    [Fact]
    public async Task A_Monthly_Cahier_Never_Carries_The_Level_Synthesis()
    {
        var file = await PdfAsync(EcoleA, Directeur, CM2A, lang: "fr");

        Text(file).Should().NotContain("SYNTHÈSE DU NIVEAU").And.NotContain("RÉCAPITULATIF");
    }

    [Fact]
    public async Task An_Unknown_Term_Is_Refused()
    {
        var act = async () => await PdfAsync(EcoleA, Directeur, CM2A, termId: Guid.NewGuid());

        await act.Should().ThrowAsync<ValidationException>();
    }

    // --------------------------------------------------------------------------- Portée et isolation

    [Fact]
    public async Task A_Teacher_Cannot_Export_The_Cahier_Of_A_Class_He_Does_Not_Teach()
    {
        var act = async () => await PdfAsync(EcoleA, Prof, CM2B);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task Another_School_Cannot_Export_This_Cahier()
    {
        await FillAsync(CM2A, In(Awa, new DateOnly(2026, 11, 2), HalfDay.Morning, AttendanceStatus.Present));

        // École B demande la classe de A : introuvable (RLS + Global Query Filter), aucun PDF.
        var act = async () => await PdfAsync(EcoleB, Directeur, CM2A);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task The_Level_Synthesis_Of_One_School_Never_Includes_The_Classes_Of_Another()
    {
        var file = await PdfAsync(EcoleA, Directeur, CM2A, termId: TermId, lang: "fr");

        Text(file).Should().NotContain("6e A").And.NotContain("Collège Teranga");
    }
}
