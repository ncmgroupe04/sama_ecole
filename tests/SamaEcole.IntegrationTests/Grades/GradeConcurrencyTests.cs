using FluentAssertions;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace SamaEcole.IntegrationTests.Grades;

/// <summary>
/// Ticket JGK-G01, AGENTS.md règle #5 — le verrou optimiste (xmin) tient-il RÉELLEMENT sur les notes ?
///
/// Scénario du critère du ticket : deux enseignants corrigent la même note en même temps. On le
/// reproduit avec deux DbContext branchés sur le rôle applicatif, chacun ayant lu la note à son
/// propre instant — exactement comme ClassFeeConcurrencyTests.
/// </summary>
[Trait("Category", "MultiTenant")]
public class GradeConcurrencyTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Classe = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid Eleve = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");
    private static readonly Guid Annee = Guid.Parse("cccccccc-0000-0000-0000-00000000000c");
    private static readonly Guid Trimestre = Guid.Parse("dddddddd-0000-0000-0000-00000000000d");
    private static readonly Guid Matiere = Guid.Parse("eeeeeeee-0000-0000-0000-00000000000e");

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
        owner.SchoolYears.Add(new SchoolYear
        {
            Id = Annee, SchoolId = Ecole, Label = "2026-2027",
            StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true
        });
        owner.Terms.Add(new Term
        {
            Id = Trimestre, SchoolId = Ecole, SchoolYearId = Annee, Label = "1er trimestre", Order = 1,
            StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2027, 1, 15)
        });
        owner.Subjects.Add(new Subject { Id = Matiere, SchoolId = Ecole, Name = "Mathématiques", Level = "Primaire", Coefficient = 4 });
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task Two_Concurrent_Corrections_The_Second_One_Should_Be_Refused_With_A_Conflict()
    {
        Guid gradeId;
        await using (var seed = _db.NewAppContext(Ecole))
        {
            var grade = new Grade
            {
                SchoolId = Ecole, StudentId = Eleve, SubjectId = Matiere, TermId = Trimestre,
                EvaluationType = EvaluationType.Devoir, Value = 12
            };
            seed.Grades.Add(grade);
            await seed.SaveChangesAsync(CancellationToken.None);
            gradeId = grade.Id;
        }

        // Deux sessions ont chacune LU la note : elles détiennent le même jeton xmin.
        await using var ctxA = _db.NewAppContext(Ecole);
        await using var ctxB = _db.NewAppContext(Ecole);

        var gradeA = await ctxA.Grades.FirstAsync(g => g.Id == gradeId);
        var gradeB = await ctxB.Grades.FirstAsync(g => g.Id == gradeId);

        // A écrit en premier : xmin bascule en base.
        gradeA.Value = 14;
        await ctxA.SaveChangesAsync(CancellationToken.None);

        // B écrit avec le jeton qu'il détenait — désormais périmé.
        gradeB.Value = 18;
        var act = async () => await ctxB.SaveChangesAsync(CancellationToken.None);

        await act.Should().ThrowAsync<ConcurrencyConflictException>(
            "deux corrections concurrentes sur la même note ne doivent jamais s'écraser en silence (règle #5)");

        await using var check = _db.NewAppContext(Ecole);
        var finalValue = await check.Grades.Where(g => g.Id == gradeId).Select(g => g.Value).FirstAsync();
        finalValue.Should().Be(14);
    }

    [Fact]
    public async Task A_Sequential_Correction_With_The_Fresh_Token_Should_Succeed()
    {
        // Contre-épreuve : sans conflit, l'écriture passe normalement.
        Guid gradeId;
        await using (var seed = _db.NewAppContext(Ecole))
        {
            var grade = new Grade
            {
                SchoolId = Ecole, StudentId = Eleve, SubjectId = Matiere, TermId = Trimestre,
                EvaluationType = EvaluationType.Composition, Value = 10
            };
            seed.Grades.Add(grade);
            await seed.SaveChangesAsync(CancellationToken.None);
            gradeId = grade.Id;
        }

        await using var ctx = _db.NewAppContext(Ecole);
        var grade2 = await ctx.Grades.FirstAsync(g => g.Id == gradeId);
        grade2.Value = 16;

        var act = async () => await ctx.SaveChangesAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task A_Concurrent_First_Entry_On_The_Same_Key_Should_Be_Refused_With_A_Conflict()
    {
        // Aucune note n'existe encore : deux enseignants saisissent chacun la PREMIÈRE note de la même
        // clé (élève/matière/trimestre/type) en même temps. Le second doit se heurter à l'index unique
        // UX_grades_single_entry, traduit en 409 par ApplicationDbContext.SaveChangesAsync — jamais un
        // doublon silencieux (AGENTS.md règle #5).
        await using var ctxA = _db.NewAppContext(Ecole);
        await using var ctxB = _db.NewAppContext(Ecole);

        ctxA.Grades.Add(new Grade
        {
            SchoolId = Ecole, StudentId = Eleve, SubjectId = Matiere, TermId = Trimestre,
            EvaluationType = EvaluationType.Devoir, Value = 12
        });
        await ctxA.SaveChangesAsync(CancellationToken.None);

        ctxB.Grades.Add(new Grade
        {
            SchoolId = Ecole, StudentId = Eleve, SubjectId = Matiere, TermId = Trimestre,
            EvaluationType = EvaluationType.Devoir, Value = 15
        });
        var act = async () => await ctxB.SaveChangesAsync(CancellationToken.None);

        await act.Should().ThrowAsync<ConcurrencyConflictException>(
            "deux premières saisies concurrentes sur la même clé ne doivent jamais créer de doublon (règle #5)");
    }
}
