using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using FluentAssertions;
using Xunit;

namespace SamaEcole.IntegrationTests.Quran;

/// <summary>
/// Socle Franco-Arabe/Daara (docs/superpowers/specs/2026-09-20-franco-arabic-core-design.md) :
/// les valeurs par défaut résistent-elles à un aller-retour PAR LA BASE (pas seulement en mémoire),
/// et le verrou optimiste xmin tient-il RÉELLEMENT sur les deux nouvelles tables ?
/// </summary>
[Trait("Category", "MultiTenant")]
public class QuranCoreSchemaTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Classe = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid Eleve = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();
        owner.Schools.Add(new School { Id = Ecole, Name = "École A" });
        owner.Classrooms.Add(new Classroom { Id = Classe, SchoolId = Ecole, Name = "CM2", Level = "Primaire", Capacity = 40 });
        owner.Students.Add(new Student
        {
            Id = Eleve,
            SchoolId = Ecole,
            Matricule = "ELEV-2026-0001",
            FullName = "Élève de test",
            BirthDate = new DateOnly(2015, 1, 1),
            BirthPlace = "Dakar",
            Gender = "M",
            ClassroomId = Classe
        });
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task A_Subject_Inserted_Without_SectionType_Reads_Back_As_French()
    {
        await using var ctx = _db.NewAppContext(Ecole);
        var subject = new Subject { SchoolId = Ecole, Name = "Mathématiques", Level = "Primaire", Coefficient = 4 };
        ctx.Subjects.Add(subject);
        await ctx.SaveChangesAsync(CancellationToken.None);

        await using var reload = _db.NewAppContext(Ecole);
        var reloaded = await reload.Subjects.FirstAsync(s => s.Id == subject.Id);

        reloaded.SectionType.Should().Be(SectionType.French);
    }

    [Fact]
    public async Task School_Settings_Inserted_Without_SchoolType_Reads_Back_As_Standard()
    {
        await using var ctx = _db.NewAppContext(Ecole);
        var settings = new SchoolSettings { SchoolId = Ecole };
        ctx.SchoolSettings.Add(settings);
        await ctx.SaveChangesAsync(CancellationToken.None);

        await using var reload = _db.NewAppContext(Ecole);
        var reloaded = await reload.SchoolSettings.FirstAsync(s => s.SchoolId == Ecole);

        reloaded.SchoolType.Should().Be(SchoolType.Standard);
    }

    [Fact]
    public async Task Two_Concurrent_Corrections_On_The_Same_Progress_Entry_The_Second_Is_Refused()
    {
        Guid entryId;
        await using (var seed = _db.NewAppContext(Ecole))
        {
            var entry = new QuranProgress
            {
                SchoolId = Ecole, StudentId = Eleve, JuzNumber = 1, HizbNumber = 1, SurahNumber = 1
            };
            seed.QuranProgresses.Add(entry);
            await seed.SaveChangesAsync(CancellationToken.None);
            entryId = entry.Id;
        }

        await using var ctxA = _db.NewAppContext(Ecole);
        await using var ctxB = _db.NewAppContext(Ecole);

        var entryA = await ctxA.QuranProgresses.FirstAsync(p => p.Id == entryId);
        var entryB = await ctxB.QuranProgresses.FirstAsync(p => p.Id == entryId);

        entryA.Status = QuranMemorizationStatus.Memorized;
        await ctxA.SaveChangesAsync(CancellationToken.None);

        entryB.Status = QuranMemorizationStatus.Revised;
        var act = async () => await ctxB.SaveChangesAsync(CancellationToken.None);

        await act.Should().ThrowAsync<ConcurrencyConflictException>(
            "deux corrections concurrentes sur le même suivi ne doivent jamais s'écraser en silence (règle #5)");
    }

    [Fact]
    public async Task Two_Concurrent_Corrections_On_The_Same_Evaluation_The_Second_Is_Refused()
    {
        Guid evaluationId;
        await using (var seed = _db.NewAppContext(Ecole))
        {
            var evaluation = new QuranEvaluation
            {
                SchoolId = Ecole, StudentId = Eleve, EvaluationDate = new DateOnly(2026, 9, 20), FinalScore = 15
            };
            seed.QuranEvaluations.Add(evaluation);
            await seed.SaveChangesAsync(CancellationToken.None);
            evaluationId = evaluation.Id;
        }

        await using var ctxA = _db.NewAppContext(Ecole);
        await using var ctxB = _db.NewAppContext(Ecole);

        var evalA = await ctxA.QuranEvaluations.FirstAsync(e => e.Id == evaluationId);
        var evalB = await ctxB.QuranEvaluations.FirstAsync(e => e.Id == evaluationId);

        evalA.FinalScore = 17;
        await ctxA.SaveChangesAsync(CancellationToken.None);

        evalB.FinalScore = 12;
        var act = async () => await ctxB.SaveChangesAsync(CancellationToken.None);

        await act.Should().ThrowAsync<ConcurrencyConflictException>(
            "deux corrections concurrentes sur la même évaluation ne doivent jamais s'écraser en silence (règle #5)");
    }
}
