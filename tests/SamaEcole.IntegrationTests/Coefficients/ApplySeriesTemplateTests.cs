using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Coefficients;
using SamaEcole.Application.Coefficients.Commands;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;
using ValidationException = SamaEcole.Application.Common.Exceptions.ValidationException;

namespace SamaEcole.IntegrationTests.Coefficients;

file sealed class StubTenant(Guid? schoolId) : ITenantProvider
{
    public Guid? CurrentSchoolId => schoolId;
}

/// <summary>Modèle de TEST injecté : les tests ne se couplent jamais aux valeurs nationales.</summary>
file sealed class FakeTemplates(params TemplateLine[] lines) : ISeriesTemplateProvider
{
    public IReadOnlyList<TemplateLine> For(string series) => series == "S2" ? lines : [];
}

/// <summary>
/// Évolution N°4, arbitrage A5 — « Appliquer le modèle » matérialise le modèle d'une série en surcharges de
/// série, sans jamais toucher au coefficient des matières, et le dit quand quelque chose ne correspond pas.
/// </summary>
[Trait("Category", "MultiTenant")]
public class ApplySeriesTemplateTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("a1111111-1111-1111-1111-111111111111");
    private static readonly Guid AutreEcole = Guid.Parse("a2222222-2222-2222-2222-222222222222");
    private static readonly Guid Annee = Guid.Parse("a1111111-0000-0000-0000-000000000001");
    private static readonly Guid AnneeAutre = Guid.Parse("a2222222-0000-0000-0000-000000000001");

    private static readonly Guid Maths = Guid.Parse("acccccc1-0000-0000-0000-0000000000c1");
    private static readonly Guid Francais = Guid.Parse("acccccc1-0000-0000-0000-0000000000c2");
    private static readonly Guid Dessin = Guid.Parse("acccccc1-0000-0000-0000-0000000000c3");
    private static readonly Guid Calcul = Guid.Parse("acccccc1-0000-0000-0000-0000000000c4");
    private static readonly Guid MathsAutre = Guid.Parse("addddddd-0000-0000-0000-0000000000d1");

    private static readonly ISeriesTemplateProvider Model = new FakeTemplates(
        new TemplateLine("Mathématiques", ["Maths"], 5m),
        new TemplateLine("Français", [], 2m),
        new TemplateLine("Philosophie", ["Philo"], 2m)); // aucune matière de l'école : « non trouvée »

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();

        owner.Schools.AddRange(
            new School { Id = Ecole, Name = "Lycée A" },
            new School { Id = AutreEcole, Name = "Lycée B" });

        owner.SchoolYears.AddRange(
            new SchoolYear { Id = Annee, SchoolId = Ecole, Label = "2026-2027", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true },
            new SchoolYear { Id = AnneeAutre, SchoolId = AutreEcole, Label = "2026-2027", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true });

        owner.Subjects.AddRange(
            new Subject { Id = Maths, SchoolId = Ecole, Name = "Maths", Level = "Terminale S2", Coefficient = 4 },
            new Subject { Id = Francais, SchoolId = Ecole, Name = "Francais", Level = "Terminale S2", Coefficient = 3 },
            new Subject { Id = Dessin, SchoolId = Ecole, Name = "Dessin", Level = "Terminale S2", Coefficient = 1 },
            new Subject { Id = Calcul, SchoolId = Ecole, Name = "Maths", Level = "Primaire", Coefficient = 2 },
            new Subject { Id = MathsAutre, SchoolId = AutreEcole, Name = "Maths", Level = "Lycée", Coefficient = 4 });

        await owner.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task Applying_The_Template_Creates_Series_Overrides_And_Reports_What_Did_Not_Match()
    {
        await using var db = _db.NewAppContext(Ecole);

        var result = await ApplyAsync(db, Ecole, "s2");

        result.Applied.Should().Be(2, "Maths (alias) et Français (accents ignorés)");
        result.Updated.Should().Be(0);
        result.SkippedExisting.Should().Be(0);
        result.UnmatchedTemplateLines.Should().Equal("Philosophie");
        result.UncoveredSubjects.Should().Equal(
            new[] { "Dessin (Terminale S2)" }, "le primaire n'est pas candidat");

        await using var reread = _db.NewAppContext(Ecole);
        var rows = await reread.SubjectCoefficientOverrides.ToListAsync();
        rows.Should().OnlyContain(o => o.Series == "S2" && o.ClassroomId == null && o.SchoolYearId == Annee);
        rows.Single(o => o.SubjectId == Maths).Coefficient.Should().Be(5m);
        rows.Single(o => o.SubjectId == Francais).Coefficient.Should().Be(2m);
    }

    [Fact]
    public async Task The_Subject_Coefficients_Are_Never_Touched()
    {
        await using var db = _db.NewAppContext(Ecole);
        await ApplyAsync(db, Ecole, "S2", overwrite: true);

        await using var reread = _db.NewAppContext(Ecole);
        var subjects = await reread.Subjects.ToDictionaryAsync(s => s.Id, s => s.Coefficient);
        subjects[Maths].Should().Be(4m);
        subjects[Francais].Should().Be(3m);
        subjects[Dessin].Should().Be(1m);
    }

    [Fact]
    public async Task Without_Overwrite_An_Existing_Override_Is_Kept_And_The_Call_Is_Idempotent()
    {
        await SeedOverrideAsync(Maths, 7m); // posée à la main par le Directeur

        await using var db = _db.NewAppContext(Ecole);
        var first = await ApplyAsync(db, Ecole, "S2");
        first.Applied.Should().Be(1, "seul Français est créé");
        first.SkippedExisting.Should().Be(1);

        await using var check = _db.NewAppContext(Ecole);
        (await check.SubjectCoefficientOverrides.SingleAsync(o => o.SubjectId == Maths)).Coefficient
            .Should().Be(7m, "jamais écrasée sans overwrite");

        await using var db2 = _db.NewAppContext(Ecole);
        var second = await ApplyAsync(db2, Ecole, "S2");
        (second.Applied, second.Updated).Should().Be((0, 0), "rejouée, elle ne change plus rien");
    }

    [Fact]
    public async Task With_Overwrite_An_Existing_Override_Is_Replaced_Only_When_The_Value_Differs()
    {
        await SeedOverrideAsync(Maths, 7m);
        await SeedOverrideAsync(Francais, 2m); // déjà égale au modèle

        await using var db = _db.NewAppContext(Ecole);
        var result = await ApplyAsync(db, Ecole, "S2", overwrite: true);

        result.Updated.Should().Be(1, "Maths passe de 7 à 5");
        result.SkippedExisting.Should().Be(1, "Français est déjà à 2");

        await using var check = _db.NewAppContext(Ecole);
        (await check.SubjectCoefficientOverrides.SingleAsync(o => o.SubjectId == Maths)).Coefficient.Should().Be(5m);
    }

    [Fact]
    public async Task A_Series_Without_A_Template_Is_Refused_Clearly_And_Writes_Nothing()
    {
        await using var db = _db.NewAppContext(Ecole);

        var act = async () => await ApplyAsync(db, Ecole, "TECH");

        (await act.Should().ThrowAsync<ValidationException>()).Which.Errors["Series"].Single()
            .Should().Contain("Aucun modèle national");

        await using var check = _db.NewAppContext(Ecole);
        (await check.SubjectCoefficientOverrides.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Another_Schools_Subjects_And_Overrides_Are_Never_Touched()
    {
        await using var db = _db.NewAppContext(Ecole);
        await ApplyAsync(db, Ecole, "S2");

        await using var autre = _db.NewAppContext(AutreEcole);
        (await autre.SubjectCoefficientOverrides.CountAsync()).Should().Be(0);

        // Et l'inverse : appliquer chez B ne crée rien chez A et ne voit pas les matières de A.
        var result = await ApplyAsync(autre, AutreEcole, "S2");
        result.Applied.Should().Be(1, "seule la matière de B");

        await using var check = _db.NewAppContext(Ecole);
        (await check.SubjectCoefficientOverrides.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task The_Real_National_Model_Recognises_Common_Subject_Names()
    {
        await using var db = _db.NewAppContext(Ecole);

        var result = await new ApplySeriesTemplateCommandHandler(db, new StubTenant(Ecole), new NationalSeriesTemplateProvider())
            .Handle(new ApplySeriesTemplateCommand("S2"), default);

        result.Applied.Should().Be(2, "« Maths » et « Francais » sont reconnus par le modèle national S2");
        result.UncoveredSubjects.Should().Equal("Dessin (Terminale S2)");
    }

    private static Task<ApplyTemplateResult> ApplyAsync(
        SamaEcole.Persistence.ApplicationDbContext db, Guid schoolId, string series, bool overwrite = false)
        => new ApplySeriesTemplateCommandHandler(db, new StubTenant(schoolId), Model)
            .Handle(new ApplySeriesTemplateCommand(series, overwrite), default);

    private async Task SeedOverrideAsync(Guid subjectId, decimal coefficient)
    {
        await using var owner = _db.NewOwnerContext();
        owner.SubjectCoefficientOverrides.Add(new SubjectCoefficientOverride
        {
            SchoolId = Ecole, SchoolYearId = Annee, SubjectId = subjectId, Series = "S2", Coefficient = coefficient
        });
        await owner.SaveChangesAsync();
    }
}
