using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.SchoolYears.Commands.ApplyEvaluationPeriods;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.SchoolYears;

/// <summary>
/// Évolution N°2 — « Appliquer le découpage » sur une année existante : remplace ses périodes par
/// celles du réglage courant, mais SEULEMENT si aucune note ni appréciation de bulletin n'y est
/// rattachée (blocage strict), jamais sur une année terminée, et jamais chez un autre tenant.
/// </summary>
[Trait("Category", "MultiTenant")]
public class ApplyEvaluationPeriodsTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Directeur = Guid.Parse("ffffffff-0000-0000-0000-0000000000e1");

    private static readonly Guid AnneeA = Guid.Parse("aaaa1111-0000-0000-0000-000000000001");
    private static readonly Guid AnneeB = Guid.Parse("bbbb2222-0000-0000-0000-000000000001");
    private static readonly Guid AnneeTerminee = Guid.Parse("aaaa1111-0000-0000-0000-000000000009");
    private static readonly Guid Trimestre1 = Guid.Parse("aaaa1111-0000-0000-0000-000000000011");
    private static readonly Guid Classe = Guid.Parse("cccccccc-0000-0000-0000-000000000001");
    private static readonly Guid Maths = Guid.Parse("dddddddd-0000-0000-0000-00000000000d");
    private static readonly Guid Eleve = Guid.Parse("eeeeeeee-0000-0000-0000-000000000001");

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

        owner.Schools.AddRange(new School { Id = EcoleA, Name = "École A" }, new School { Id = EcoleB, Name = "École B" });
        owner.SchoolYears.AddRange(
            new SchoolYear { Id = AnneeA, SchoolId = EcoleA, Label = "2040-2041", StartDate = Debut, EndDate = Fin, IsActive = true },
            new SchoolYear { Id = AnneeB, SchoolId = EcoleB, Label = "2040-2041", StartDate = Debut, EndDate = Fin, IsActive = true },
            new SchoolYear
            {
                Id = AnneeTerminee, SchoolId = EcoleA, Label = "2019-2020",
                StartDate = new DateOnly(2019, 10, 1), EndDate = new DateOnly(2020, 6, 30)
            });

        // Trois trimestres historiques sur chaque année (celui de l'année A porte un Id connu).
        foreach (var (yearId, schoolId, firstTermId) in new[] { (AnneeA, EcoleA, Trimestre1), (AnneeB, EcoleB, Guid.NewGuid()), (AnneeTerminee, EcoleA, Guid.NewGuid()) })
        {
            var start = yearId == AnneeTerminee ? new DateOnly(2019, 10, 1) : Debut;
            owner.Terms.AddRange(
                new Term { Id = firstTermId, SchoolId = schoolId, SchoolYearId = yearId, Label = "1er trimestre", Order = 1, StartDate = start, EndDate = start.AddDays(90) },
                new Term { SchoolId = schoolId, SchoolYearId = yearId, Label = "2e trimestre", Order = 2, StartDate = start.AddDays(91), EndDate = start.AddDays(181) },
                new Term { SchoolId = schoolId, SchoolYearId = yearId, Label = "3e trimestre", Order = 3, StartDate = start.AddDays(182), EndDate = start.AddDays(270) });
        }

        owner.Classrooms.Add(new Classroom { Id = Classe, SchoolId = EcoleA, Name = "CM2 A", Level = "Primaire", Cycle = CycleType.Primaire, Capacity = 40 });
        owner.Subjects.Add(new Subject { Id = Maths, SchoolId = EcoleA, Name = "Mathématiques", Level = "Primaire", Coefficient = 4 });
        owner.Students.Add(new Student
        {
            Id = Eleve, SchoolId = EcoleA, Matricule = "ELEV-0001", FullName = "Awa Fall",
            BirthDate = new DateOnly(2015, 1, 1), BirthPlace = "Dakar", Gender = "F", ClassroomId = Classe
        });

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private async Task SetSettingsAsync(EvaluationPeriodType type, int customCount = 3)
    {
        await using var owner = _db.NewOwnerContext();
        owner.SchoolSettings.Add(new SchoolSettings { SchoolId = EcoleA, EvaluationPeriodType = type, CustomPeriodCount = customCount });
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    private async Task<IReadOnlyList<SamaEcole.Application.SchoolYears.Queries.GetTerms.TermDto>> ApplyAsync(Guid yearId)
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        return await new ApplyEvaluationPeriodsCommandHandler(
                ctx, new StubTenantProvider(EcoleA), new TestCurrentUser(Directeur, Role.Directeur), TimeProvider.System)
            .Handle(new ApplyEvaluationPeriodsCommand(yearId), CancellationToken.None);
    }

    private async Task<List<Term>> LiveTermsAsync(Guid yearId)
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        return await ctx.Terms.AsNoTracking().Where(t => t.SchoolYearId == yearId).OrderBy(t => t.Order).ToListAsync();
    }

    [Fact]
    public async Task An_Empty_Year_Gets_The_Semesters_And_The_Old_Trimesters_Are_Archived_Not_Erased()
    {
        await SetSettingsAsync(EvaluationPeriodType.Semester);

        var result = await ApplyAsync(AnneeA);

        result.Select(t => t.Label).Should().Equal("1er semestre", "2e semestre");
        (await LiveTermsAsync(AnneeA)).Should().HaveCount(2);

        await using var owner = _db.NewOwnerContext();
        var archived = await owner.Terms.IgnoreQueryFilters()
            .Where(t => t.SchoolYearId == AnneeA && t.IsDeleted).ToListAsync();
        archived.Should().HaveCount(3, "suppression logique : les anciennes périodes sont conservées (règle #6)");
    }

    [Fact]
    public async Task A_Year_With_A_Grade_Is_Refused_And_Left_Untouched()
    {
        await SetSettingsAsync(EvaluationPeriodType.Semester);
        await using (var owner = _db.NewOwnerContext())
        {
            owner.Grades.Add(new Grade
            {
                SchoolId = EcoleA, StudentId = Eleve, SubjectId = Maths, TermId = Trimestre1,
                EvaluationType = EvaluationType.Devoir1, Value = 12
            });
            await owner.SaveChangesAsync(CancellationToken.None);
        }

        var act = () => ApplyAsync(AnneeA);

        await act.Should().ThrowAsync<ValidationException>();
        (await LiveTermsAsync(AnneeA)).Select(t => t.Label)
            .Should().Equal("1er trimestre", "2e trimestre", "3e trimestre");
    }

    [Fact]
    public async Task A_Year_With_Only_A_Report_Card_Remark_Is_Refused()
    {
        await SetSettingsAsync(EvaluationPeriodType.Semester);
        await using (var owner = _db.NewOwnerContext())
        {
            owner.ReportCardRemarks.Add(new ReportCardRemark
            {
                SchoolId = EcoleA, StudentId = Eleve, TermId = Trimestre1, Observations = "Bon travail."
            });
            await owner.SaveChangesAsync(CancellationToken.None);
        }

        var act = () => ApplyAsync(AnneeA);

        await act.Should().ThrowAsync<ValidationException>();
        (await LiveTermsAsync(AnneeA)).Should().HaveCount(3);
    }

    [Fact]
    public async Task A_Closed_Year_Is_Refused()
    {
        await SetSettingsAsync(EvaluationPeriodType.Semester);

        var act = () => ApplyAsync(AnneeTerminee);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task The_Year_Of_Another_School_Is_Not_Found()
    {
        await SetSettingsAsync(EvaluationPeriodType.Semester);

        var act = () => ApplyAsync(AnneeB);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Applying_The_Current_Split_Again_Changes_Nothing()
    {
        await SetSettingsAsync(EvaluationPeriodType.Semester);

        var first = await ApplyAsync(AnneeA);
        var second = await ApplyAsync(AnneeA);

        second.Select(t => t.Id).Should().Equal(first.Select(t => t.Id), "aucune période n'est recréée ni archivée");
    }

    [Fact]
    public async Task Without_Settings_The_Trimester_Split_Is_Restored_On_Historical_Dates()
    {
        // Aucune ligne SchoolSettings : défaut Trimestriel — les dates historiques (91 jours) diffèrent
        // du découpage à parts égales de l'année, donc les périodes sont bien rejouées.
        var result = await ApplyAsync(AnneeA);

        result.Select(t => t.Label).Should().Equal("1er trimestre", "2e trimestre", "3e trimestre");
        result[0].StartDate.Should().Be(Debut);
        result[^1].EndDate.Should().Be(Fin);
    }
}
