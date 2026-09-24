using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.SchoolYears.Commands.CreateSchoolYear;
using SamaEcole.Application.SchoolYears.Commands.UpdateSchoolYear;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.SchoolYears;

/// <summary>
/// Évolution N°2 — les périodes d'une année suivent SchoolSettings.EvaluationPeriodType à la CRÉATION,
/// et une année déjà créée GARDE son nombre de périodes quand ses dates changent, même si le réglage
/// de l'école a changé entre-temps (les notes pointent un TermId : jamais de période fabriquée ni perdue).
/// </summary>
public class SchoolYearPeriodsTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("11111111-1111-1111-1111-111111111111");

    // Loin dans le futur : l'année n'est jamais « terminée », quelle que soit la date d'exécution.
    private static readonly DateOnly Debut = new(2040, 10, 1);
    private static readonly DateOnly Fin = new(2041, 6, 30);

    private sealed class StubTenantProvider(Guid? schoolId) : ITenantProvider
    {
        public Guid? CurrentSchoolId => schoolId;
    }

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();
        owner.Schools.Add(new School { Id = Ecole, Name = "École A" });
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private async Task SetSettingsAsync(EvaluationPeriodType type, int customCount = 3)
    {
        await using var owner = _db.NewOwnerContext();
        var existing = await owner.SchoolSettings.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.SchoolId == Ecole);
        if (existing is null)
        {
            owner.SchoolSettings.Add(new SchoolSettings { SchoolId = Ecole, EvaluationPeriodType = type, CustomPeriodCount = customCount });
        }
        else
        {
            existing.EvaluationPeriodType = type;
            existing.CustomPeriodCount = customCount;
        }

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    private async Task<Guid> CreateYearAsync()
    {
        await using var ctx = _db.NewAppContext(Ecole);
        var year = await new CreateSchoolYearCommandHandler(ctx, new StubTenantProvider(Ecole), TimeProvider.System)
            .Handle(new CreateSchoolYearCommand { Label = "2040-2041", StartDate = Debut, EndDate = Fin }, CancellationToken.None);
        return year.Id;
    }

    private async Task<List<Term>> TermsOfAsync(Guid yearId)
    {
        await using var ctx = _db.NewAppContext(Ecole);
        return await ctx.Terms.AsNoTracking().Where(t => t.SchoolYearId == yearId).OrderBy(t => t.Order).ToListAsync();
    }

    [Fact]
    public async Task A_Semester_School_Gets_Two_Semesters()
    {
        await SetSettingsAsync(EvaluationPeriodType.Semester);

        var terms = await TermsOfAsync(await CreateYearAsync());

        terms.Select(t => t.Label).Should().Equal("1er semestre", "2e semestre");
        terms.Select(t => t.Order).Should().Equal(1, 2);
        terms[0].StartDate.Should().Be(Debut);
        terms[^1].EndDate.Should().Be(Fin);
    }

    [Fact]
    public async Task A_Custom_School_Gets_The_Requested_Number_Of_Periods()
    {
        await SetSettingsAsync(EvaluationPeriodType.Custom, customCount: 4);

        var terms = await TermsOfAsync(await CreateYearAsync());

        terms.Select(t => t.Label).Should().Equal("1re période", "2e période", "3e période", "4e période");
    }

    [Fact]
    public async Task A_School_Without_Settings_Keeps_Three_Trimesters()
    {
        // École antérieure à JGK-B02 : aucune ligne SchoolSettings, comportement historique.
        var terms = await TermsOfAsync(await CreateYearAsync());

        terms.Select(t => t.Label).Should().Equal("1er trimestre", "2e trimestre", "3e trimestre");
    }

    [Fact]
    public async Task Changing_The_Dates_Keeps_The_Year_Own_Number_Of_Periods_Whatever_The_Setting_Became()
    {
        await SetSettingsAsync(EvaluationPeriodType.Semester);
        var yearId = await CreateYearAsync();
        var before = await TermsOfAsync(yearId);

        // L'école repasse en Trimestriel, puis prolonge l'année de deux mois.
        await SetSettingsAsync(EvaluationPeriodType.Trimester);
        var newEnd = new DateOnly(2041, 8, 31);

        await using (var ctx = _db.NewAppContext(Ecole))
        {
            await new UpdateSchoolYearCommandHandler(
                    ctx, new StubTenantProvider(Ecole), TimeProvider.System, NullLogger<UpdateSchoolYearCommandHandler>.Instance)
                .Handle(new UpdateSchoolYearCommand { Id = yearId, Label = "2040-2041", StartDate = Debut, EndDate = newEnd }, CancellationToken.None);
        }

        var after = await TermsOfAsync(yearId);

        after.Should().HaveCount(2, "l'année garde ses deux semestres");
        after.Select(t => t.Id).Should().Equal(before.Select(t => t.Id), "les périodes gardent leur identité (les notes y sont rattachées)");
        after.Select(t => t.Label).Should().Equal("1er semestre", "2e semestre");
        after[0].StartDate.Should().Be(Debut);
        after[1].StartDate.Should().Be(after[0].EndDate.AddDays(1));
        after[1].EndDate.Should().Be(newEnd);
    }
}
