using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Coefficients;
using SamaEcole.Application.Coefficients.Commands;
using SamaEcole.Application.Coefficients.Queries;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using SamaEcole.Persistence;
using Xunit;
using ValidationException = SamaEcole.Application.Common.Exceptions.ValidationException;

namespace SamaEcole.IntegrationTests.Coefficients;

file sealed class StubTenant(Guid? schoolId) : ITenantProvider
{
    public Guid? CurrentSchoolId => schoolId;
}

/// <summary>
/// Évolution N°4 — les vrais Handlers d'écriture et de lecture des surcharges, contre un PostgreSQL réel
/// sous le rôle applicatif (RLS active) : verrou optimiste, portées, règles du primaire, reconduction,
/// grille et isolation entre écoles.
/// </summary>
[Trait("Category", "MultiTenant")]
public class CoefficientOverrideCommandsTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("91111111-1111-1111-1111-111111111111");
    private static readonly Guid AutreEcole = Guid.Parse("92222222-2222-2222-2222-222222222222");

    private static readonly Guid Annee1 = Guid.Parse("91111111-0000-0000-0000-000000000001"); // active
    private static readonly Guid Annee0 = Guid.Parse("91111111-0000-0000-0000-000000000002"); // précédente
    private static readonly Guid AnneeAutre = Guid.Parse("92222222-0000-0000-0000-000000000001");

    private static readonly Guid ClasseS2 = Guid.Parse("9aaaaaaa-0000-0000-0000-0000000000a2");
    private static readonly Guid ClassePrimaire = Guid.Parse("9aaaaaaa-0000-0000-0000-0000000000a4");

    private static readonly Guid Maths = Guid.Parse("9ccccccc-0000-0000-0000-0000000000c1");
    private static readonly Guid Francais = Guid.Parse("9ccccccc-0000-0000-0000-0000000000c2");
    private static readonly Guid CalculPrimaire = Guid.Parse("9ccccccc-0000-0000-0000-0000000000c3");
    private static readonly Guid MathsAutre = Guid.Parse("9ddddddd-0000-0000-0000-0000000000d1");

    private static readonly TestCurrentUser Directeur = new(Guid.Parse("9d000000-0000-0000-0000-000000000001"), Role.Directeur);

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();

        owner.Schools.AddRange(
            new School { Id = Ecole, Name = "Lycée A" },
            new School { Id = AutreEcole, Name = "Lycée B" });

        // L'école B n'a AUCUNE année active : sert au cas « pas d'année active ».
        owner.SchoolYears.AddRange(
            new SchoolYear { Id = Annee1, SchoolId = Ecole, Label = "2026-2027", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true },
            new SchoolYear { Id = Annee0, SchoolId = Ecole, Label = "2025-2026", StartDate = new DateOnly(2025, 9, 1), EndDate = new DateOnly(2026, 6, 30) },
            new SchoolYear { Id = AnneeAutre, SchoolId = AutreEcole, Label = "2026-2027", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 6, 30) });

        owner.Classrooms.AddRange(
            new Classroom { Id = ClasseS2, SchoolId = Ecole, Name = "Terminale S2 A", Level = "Lycée", Cycle = CycleType.Lycee, Capacity = 40, Series = "S2" },
            new Classroom { Id = ClassePrimaire, SchoolId = Ecole, Name = "CM2 A", Level = "Primaire", Cycle = CycleType.Primaire, Capacity = 40 });

        owner.Subjects.AddRange(
            new Subject { Id = Maths, SchoolId = Ecole, Name = "Mathématiques", Level = "Lycée", Coefficient = 4 },
            new Subject { Id = Francais, SchoolId = Ecole, Name = "Français", Level = "Lycée", Coefficient = 2 },
            new Subject { Id = CalculPrimaire, SchoolId = Ecole, Name = "Calcul", Level = "Primaire", Coefficient = 2 },
            new Subject { Id = MathsAutre, SchoolId = AutreEcole, Name = "Mathématiques", Level = "Lycée", Coefficient = 4 });

        await owner.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    // ------------------------------------------------------------ écriture

    [Fact]
    public async Task A_Series_Override_Is_Created_On_The_Active_Year_And_Returns_Its_Row_Version()
    {
        await using var db = _db.NewAppContext(Ecole);

        var result = await UpsertAsync(db, Ecole, new UpsertCoefficientOverrideCommand
        {
            SubjectId = Maths, Coefficient = 6m, Series = "s2"
        });

        result.Series.Should().Be("S2", "la série est normalisée");
        result.Coefficient.Should().Be(6m);
        result.RowVersion.Should().NotBe(0u);

        await using var reread = _db.NewAppContext(Ecole);
        var row = await reread.SubjectCoefficientOverrides.SingleAsync();
        row.SchoolYearId.Should().Be(Annee1, "l'année est l'année ACTIVE, résolue serveur");
    }

    [Fact]
    public async Task Correcting_An_Override_Needs_The_Row_Version_And_Updates_It()
    {
        await using var db = _db.NewAppContext(Ecole);
        var created = await UpsertAsync(db, Ecole, SeriesCommand(6m));

        await using var db2 = _db.NewAppContext(Ecole);
        var updated = await UpsertAsync(db2, Ecole, SeriesCommand(7m, created.RowVersion));

        updated.Id.Should().Be(created.Id, "même ligne, corrigée");
        updated.Coefficient.Should().Be(7m);
        updated.RowVersion.Should().NotBe(created.RowVersion);
    }

    [Fact]
    public async Task A_Stale_Row_Version_Is_Refused_With_A_Conflict_Not_Overwritten()
    {
        await using var db = _db.NewAppContext(Ecole);
        var created = await UpsertAsync(db, Ecole, SeriesCommand(6m));

        await using var other = _db.NewAppContext(Ecole);
        await UpsertAsync(other, Ecole, SeriesCommand(7m, created.RowVersion)); // quelqu'un d'autre corrige d'abord

        await using var late = _db.NewAppContext(Ecole);
        var act = async () => await UpsertAsync(late, Ecole, SeriesCommand(9m, created.RowVersion)); // jeton périmé

        await act.Should().ThrowAsync<ConcurrencyConflictException>();

        await using var reread = _db.NewAppContext(Ecole);
        (await reread.SubjectCoefficientOverrides.SingleAsync()).Coefficient.Should().Be(7m);
    }

    [Fact]
    public async Task Correcting_Without_A_Row_Version_Is_Refused_Rather_Than_Overwriting_Blindly()
    {
        await using var db = _db.NewAppContext(Ecole);
        await UpsertAsync(db, Ecole, SeriesCommand(6m));

        await using var again = _db.NewAppContext(Ecole);
        var act = async () => await UpsertAsync(again, Ecole, SeriesCommand(9m));

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
    }

    [Fact]
    public async Task Creating_With_A_Row_Version_When_The_Row_Is_Gone_Is_A_Conflict()
    {
        await using var db = _db.NewAppContext(Ecole);

        var act = async () => await UpsertAsync(db, Ecole, SeriesCommand(6m, rowVersion: 12345u));

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
    }

    [Fact]
    public async Task A_Classroom_Override_Is_Accepted_For_A_Secondary_Classroom()
    {
        await using var db = _db.NewAppContext(Ecole);

        var result = await UpsertAsync(db, Ecole, new UpsertCoefficientOverrideCommand
        {
            SubjectId = Maths, Coefficient = 8m, ClassroomId = ClasseS2
        });

        result.ClassroomId.Should().Be(ClasseS2);
        result.Series.Should().BeNull();
    }

    [Fact]
    public async Task A_Primary_Classroom_Or_Subject_Cannot_Be_Overridden()
    {
        await using var db = _db.NewAppContext(Ecole);

        var primaryClassroom = async () => await UpsertAsync(db, Ecole, new UpsertCoefficientOverrideCommand
        {
            SubjectId = Maths, Coefficient = 8m, ClassroomId = ClassePrimaire
        });
        (await primaryClassroom.Should().ThrowAsync<ValidationException>()).Which.Errors
            .Should().ContainKey(nameof(UpsertCoefficientOverrideCommand.ClassroomId));

        var primarySubject = async () => await UpsertAsync(db, Ecole, new UpsertCoefficientOverrideCommand
        {
            SubjectId = CalculPrimaire, Coefficient = 8m, Series = "S2"
        });
        (await primarySubject.Should().ThrowAsync<ValidationException>()).Which.Errors
            .Should().ContainKey(nameof(UpsertCoefficientOverrideCommand.SubjectId));
    }

    [Fact]
    public async Task An_Unknown_Subject_Or_Classroom_Is_Refused_With_A_Readable_Message()
    {
        await using var db = _db.NewAppContext(Ecole);

        var subject = async () => await UpsertAsync(db, Ecole, new UpsertCoefficientOverrideCommand
        {
            SubjectId = Guid.NewGuid(), Coefficient = 6m, Series = "S2"
        });
        await subject.Should().ThrowAsync<ValidationException>();

        var classroom = async () => await UpsertAsync(db, Ecole, new UpsertCoefficientOverrideCommand
        {
            SubjectId = Maths, Coefficient = 6m, ClassroomId = Guid.NewGuid()
        });
        await classroom.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Without_An_Active_Year_Nothing_Can_Be_Written()
    {
        await using var db = _db.NewAppContext(AutreEcole);

        var act = async () => await UpsertAsync(db, AutreEcole, new UpsertCoefficientOverrideCommand
        {
            SubjectId = MathsAutre, Coefficient = 6m, Series = "S2"
        });

        (await act.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().ContainKey("SchoolYear");
    }

    // ------------------------------------------------------------ « Rétablir »

    [Fact]
    public async Task Restoring_Soft_Deletes_The_Override_And_Frees_The_Key()
    {
        await using var db = _db.NewAppContext(Ecole);
        var created = await UpsertAsync(db, Ecole, SeriesCommand(6m));

        await using var db2 = _db.NewAppContext(Ecole);
        await new DeleteCoefficientOverrideCommandHandler(db2, Directeur)
            .Handle(new DeleteCoefficientOverrideCommand(created.Id, created.RowVersion), default);

        await using var reread = _db.NewAppContext(Ecole);
        (await reread.SubjectCoefficientOverrides.CountAsync()).Should().Be(0, "masquée par le filtre global");

        // La clé (année, matière, série) est de nouveau libre.
        await using var db3 = _db.NewAppContext(Ecole);
        (await UpsertAsync(db3, Ecole, SeriesCommand(8m))).Id.Should().NotBe(created.Id);
    }

    [Fact]
    public async Task Restoring_With_A_Stale_Row_Version_Or_An_Unknown_Id_Fails_Cleanly()
    {
        await using var db = _db.NewAppContext(Ecole);
        var created = await UpsertAsync(db, Ecole, SeriesCommand(6m));

        await using var db2 = _db.NewAppContext(Ecole);
        var stale = async () => await new DeleteCoefficientOverrideCommandHandler(db2, Directeur)
            .Handle(new DeleteCoefficientOverrideCommand(created.Id, created.RowVersion + 99), default);
        await stale.Should().ThrowAsync<ConcurrencyConflictException>();

        await using var db3 = _db.NewAppContext(Ecole);
        var unknown = async () => await new DeleteCoefficientOverrideCommandHandler(db3, Directeur)
            .Handle(new DeleteCoefficientOverrideCommand(Guid.NewGuid(), 1u), default);
        await unknown.Should().ThrowAsync<KeyNotFoundException>();
    }

    // ------------------------------------------------------------ reconduction

    [Fact]
    public async Task Carry_Over_Copies_The_Previous_Year_Without_Overwriting_And_Is_Idempotent()
    {
        await SeedOverrideAsync(Annee0, Maths, series: "S2", coefficient: 6m);
        await SeedOverrideAsync(Annee0, Francais, series: "S2", coefficient: 3m);
        await SeedOverrideAsync(Annee0, Maths, classroomId: ClasseS2, coefficient: 8m);
        await SeedOverrideAsync(Annee1, Maths, series: "S2", coefficient: 7m); // déjà posée cette année

        await using var db = _db.NewAppContext(Ecole);
        var first = await new CarryOverCoefficientsCommandHandler(db, new StubTenant(Ecole))
            .Handle(new CarryOverCoefficientsCommand(Annee0), default);

        first.Copied.Should().Be(2, "Français/S2 et Maths/classe ; Maths/S2 existait déjà");
        first.Skipped.Should().Be(1);

        await using var check = _db.NewAppContext(Ecole);
        var thisYear = await check.SubjectCoefficientOverrides.Where(o => o.SchoolYearId == Annee1).ToListAsync();
        thisYear.Should().HaveCount(3);
        thisYear.Single(o => o.SubjectId == Maths && o.Series == "S2").Coefficient.Should().Be(7m, "jamais écrasée");

        await using var db2 = _db.NewAppContext(Ecole);
        var second = await new CarryOverCoefficientsCommandHandler(db2, new StubTenant(Ecole))
            .Handle(new CarryOverCoefficientsCommand(Annee0), default);
        second.Copied.Should().Be(0);
    }

    [Fact]
    public async Task Carry_Over_Refuses_The_Active_Year_And_Unknown_Years()
    {
        await using var db = _db.NewAppContext(Ecole);
        var handler = new CarryOverCoefficientsCommandHandler(db, new StubTenant(Ecole));

        await ((Func<Task>)(async () => await handler.Handle(new CarryOverCoefficientsCommand(Annee1), default)))
            .Should().ThrowAsync<ValidationException>();
        await ((Func<Task>)(async () => await handler.Handle(new CarryOverCoefficientsCommand(Guid.NewGuid()), default)))
            .Should().ThrowAsync<ValidationException>();
    }

    // ------------------------------------------------------------ grille

    [Fact]
    public async Task The_Series_Grid_Lists_Secondary_Subjects_With_Their_Effective_Value_And_Source()
    {
        await SeedOverrideAsync(Annee1, Maths, series: "S2", coefficient: 6m);
        await using var db = _db.NewAppContext(Ecole);

        var grid = await new GetCoefficientGridQueryHandler(db).Handle(new GetCoefficientGridQuery("S2", null), default);

        grid.SchoolYearId.Should().Be(Annee1);
        grid.SchoolYearLabel.Should().Be("2026-2027");
        grid.Rows.Select(r => r.SubjectId).Should().BeEquivalentTo([Maths, Francais], "le primaire n'est pas listé");

        var maths = grid.Rows.Single(r => r.SubjectId == Maths);
        maths.BaseCoefficient.Should().Be(4m);
        maths.OverrideCoefficient.Should().Be(6m);
        maths.EffectiveCoefficient.Should().Be(6m);
        maths.Source.Should().Be("Series");
        maths.OverrideId.Should().NotBeNull();
        maths.RowVersion.Should().NotBeNull();

        var francais = grid.Rows.Single(r => r.SubjectId == Francais);
        francais.OverrideCoefficient.Should().BeNull();
        francais.EffectiveCoefficient.Should().Be(2m);
        francais.Source.Should().Be("Subject");
    }

    [Fact]
    public async Task The_Classroom_Grid_Shows_The_Inherited_Series_Value_And_Its_Own_Override()
    {
        await SeedOverrideAsync(Annee1, Maths, series: "S2", coefficient: 6m);
        await SeedOverrideAsync(Annee1, Francais, series: "S2", coefficient: 3m);
        await SeedOverrideAsync(Annee1, Maths, classroomId: ClasseS2, coefficient: 8m);
        await using var db = _db.NewAppContext(Ecole);

        var grid = await new GetCoefficientGridQueryHandler(db).Handle(new GetCoefficientGridQuery(null, ClasseS2), default);

        var maths = grid.Rows.Single(r => r.SubjectId == Maths);
        maths.OverrideCoefficient.Should().Be(8m);
        maths.InheritedSeriesCoefficient.Should().Be(6m);
        maths.EffectiveCoefficient.Should().Be(8m, "la classe l'emporte sur sa série");
        maths.Source.Should().Be("Classroom");

        var francais = grid.Rows.Single(r => r.SubjectId == Francais);
        francais.OverrideCoefficient.Should().BeNull();
        francais.InheritedSeriesCoefficient.Should().Be(3m);
        francais.EffectiveCoefficient.Should().Be(3m, "sans surcharge propre, la classe hérite de sa série");
        francais.Source.Should().Be("Series");
    }

    [Fact]
    public async Task The_Grid_Refuses_A_Primary_Classroom_And_Flags_A_Year_That_Already_Has_Grades()
    {
        await using var db = _db.NewAppContext(Ecole);
        var handler = new GetCoefficientGridQueryHandler(db);

        var act = async () => await handler.Handle(new GetCoefficientGridQuery(null, ClassePrimaire), default);
        await act.Should().ThrowAsync<ValidationException>();

        (await handler.Handle(new GetCoefficientGridQuery("S2", null), default)).YearHasGrades.Should().BeFalse();

        await SeedGradeAsync();

        await using var db2 = _db.NewAppContext(Ecole);
        (await new GetCoefficientGridQueryHandler(db2).Handle(new GetCoefficientGridQuery("S2", null), default))
            .YearHasGrades.Should().BeTrue("des notes existent : l'écran doit avertir du recalcul rétroactif (A7)");
    }

    // ------------------------------------------------------------ isolation

    [Fact]
    public async Task Another_School_Never_Sees_Nor_Can_Target_These_Overrides_Or_Subjects()
    {
        await SeedOverrideAsync(Annee1, Maths, series: "S2", coefficient: 6m);

        await using var autre = _db.NewAppContext(AutreEcole);
        (await autre.SubjectCoefficientOverrides.CountAsync()).Should().Be(0);

        // Cibler la matière d'une autre école : introuvable (RLS + filtre global), jamais acceptée.
        var act = async () => await UpsertAsync(autre, AutreEcole, new UpsertCoefficientOverrideCommand
        {
            SubjectId = Maths, Coefficient = 9m, Series = "S2"
        });
        await act.Should().ThrowAsync<ValidationException>();
    }

    // ------------------------------------------------------------ helpers

    private static UpsertCoefficientOverrideCommand SeriesCommand(decimal coefficient, uint? rowVersion = null) => new()
    {
        SubjectId = Maths, Coefficient = coefficient, Series = "S2", RowVersion = rowVersion
    };

    private static Task<CoefficientOverrideResult> UpsertAsync(
        ApplicationDbContext db, Guid schoolId, UpsertCoefficientOverrideCommand command)
        => new UpsertCoefficientOverrideCommandHandler(db, new StubTenant(schoolId)).Handle(command, default);

    private async Task SeedOverrideAsync(
        Guid yearId, Guid subjectId, string? series = null, Guid? classroomId = null, decimal coefficient = 1m)
    {
        await using var owner = _db.NewOwnerContext();
        owner.SubjectCoefficientOverrides.Add(new SubjectCoefficientOverride
        {
            SchoolId = Ecole, SchoolYearId = yearId, SubjectId = subjectId,
            Series = series, ClassroomId = classroomId, Coefficient = coefficient
        });
        await owner.SaveChangesAsync();
    }

    private async Task SeedGradeAsync()
    {
        var term = Guid.Parse("91111111-0000-0000-0000-0000000000d1");
        var student = Guid.Parse("91111111-0000-0000-0000-0000000000e1");

        await using var owner = _db.NewOwnerContext();
        owner.Terms.Add(new Term { Id = term, SchoolId = Ecole, SchoolYearId = Annee1, Label = "1er trimestre", Order = 1, StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 12, 20) });
        owner.Students.Add(new Student
        {
            Id = student, SchoolId = Ecole, Matricule = "ELEV-0001", FullName = "Awa", BirthDate = new DateOnly(2008, 1, 1),
            BirthPlace = "Dakar", Gender = "F", ClassroomId = ClasseS2
        });
        owner.Grades.Add(new Grade
        {
            SchoolId = Ecole, StudentId = student, SubjectId = Maths, TermId = term,
            EvaluationType = EvaluationType.Composition, Value = 12
        });
        await owner.SaveChangesAsync();
    }
}
