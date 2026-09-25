using SamaEcole.Application.ClassSubjects;
using FluentAssertions;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Grades;
using SamaEcole.Application.Grades.Commands.CreateGrade;
using SamaEcole.Application.Grades.Commands.ImportGradeSheet;
using SamaEcole.Application.Grades.Commands.UpdateGrade;
using SamaEcole.Application.Grades.Queries.GetClassGrades;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using SamaEcole.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace SamaEcole.IntegrationTests.Grades;

/// <summary>
/// Évolution N°1 — la correction d'une note par l'Enseignant est bornée par la fenêtre de l'école
/// (GradeEditWindowDays) ET par la propriété : auteur de la note, ou affecté à la classe/matière. Le
/// Directeur et le Secrétariat corrigent sans limite de délai. Testé en intégration : la règle repose
/// sur des remontées de base (compte → fiche → affectation) et sur l'heure, ici simulée.
/// </summary>
public class GradeCorrectionTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Annee = Guid.Parse("aaaa1111-0000-0000-0000-000000000001");
    private static readonly Guid Trimestre = Guid.Parse("aaaa1111-0000-0000-0000-000000000002");
    private static readonly Guid Classe = Guid.Parse("cccccccc-0000-0000-0000-000000000001");
    private static readonly Guid Maths = Guid.Parse("dddddddd-0000-0000-0000-00000000000d");
    private static readonly Guid Francais = Guid.Parse("dddddddd-0000-0000-0000-00000000000e");
    private static readonly Guid Eleve = Guid.Parse("eeeeeeee-0000-0000-0000-000000000001");
    private static readonly Guid Eleve2 = Guid.Parse("eeeeeeee-0000-0000-0000-000000000002");

    // Trois enseignants : A (auteur, affecté Maths), C (collègue affecté Maths), B (affecté Français seul).
    private static readonly Guid CompteA = Guid.Parse("ffffffff-0000-0000-0000-0000000000a1");
    private static readonly Guid CompteB = Guid.Parse("ffffffff-0000-0000-0000-0000000000b1");
    private static readonly Guid CompteC = Guid.Parse("ffffffff-0000-0000-0000-0000000000c1");
    private static readonly Guid CompteSansFiche = Guid.Parse("ffffffff-0000-0000-0000-0000000000d1");
    private static readonly Guid CompteDirecteur = Guid.Parse("ffffffff-0000-0000-0000-0000000000e1");
    private static readonly Guid CompteSecretaire = Guid.Parse("ffffffff-0000-0000-0000-0000000000f1");

    private sealed class StubTenantProvider(Guid? schoolId) : ITenantProvider
    {
        public Guid? CurrentSchoolId => schoolId;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>Parseur simulé : ce test porte sur la règle de correction, pas sur la lecture du fichier.</summary>
    private sealed class StubParser(params GradeSheetRow[] rows) : IGradeSheetImportParser
    {
        public IReadOnlyList<GradeSheetRow> Parse(byte[] fileContent, string fileName) => rows;
    }

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();

        owner.Schools.Add(new School { Id = Ecole, Name = "École A" });
        owner.SchoolYears.Add(new SchoolYear
        {
            Id = Annee, SchoolId = Ecole, Label = "2026-2027",
            StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2027, 7, 31), IsActive = true
        });
        owner.Terms.Add(new Term
        {
            Id = Trimestre, SchoolId = Ecole, SchoolYearId = Annee, Label = "1er trimestre", Order = 1,
            StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2027, 1, 15)
        });
        owner.Classrooms.Add(new Classroom { Id = Classe, SchoolId = Ecole, Name = "CM2 A", Level = "Primaire", Cycle = CycleType.Primaire, Capacity = 40 });
        owner.Subjects.AddRange(
            new Subject { Id = Maths, SchoolId = Ecole, Name = "Mathématiques", Level = "Primaire", Coefficient = 4 },
            new Subject { Id = Francais, SchoolId = Ecole, Name = "Français", Level = "Primaire", Coefficient = 4 });
        owner.Students.AddRange(
            new Student { Id = Eleve, SchoolId = Ecole, Matricule = "ELEV-0001", FullName = "Awa Fall", BirthDate = new DateOnly(2015, 1, 1), BirthPlace = "Dakar", Gender = "F", ClassroomId = Classe },
            new Student { Id = Eleve2, SchoolId = Ecole, Matricule = "ELEV-0002", FullName = "Modou Diop", BirthDate = new DateOnly(2015, 1, 1), BirthPlace = "Dakar", Gender = "M", ClassroomId = Classe });

        owner.Users.AddRange(
            Account(CompteA, "a", Role.Enseignant), Account(CompteB, "b", Role.Enseignant),
            Account(CompteC, "c", Role.Enseignant), Account(CompteSansFiche, "d", Role.Enseignant),
            Account(CompteDirecteur, "dir", Role.Directeur), Account(CompteSecretaire, "sec", Role.Secretariat));

        var ficheA = Guid.NewGuid();
        var ficheB = Guid.NewGuid();
        var ficheC = Guid.NewGuid();
        owner.Teachers.AddRange(
            Fiche(ficheA, CompteA, "ENS-001"), Fiche(ficheB, CompteB, "ENS-002"), Fiche(ficheC, CompteC, "ENS-003"));
        owner.TeacherAssignments.AddRange(
            Assign(ficheA, Maths), Assign(ficheC, Maths), Assign(ficheB, Francais));

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private static User Account(Guid id, string tag, Role role) => new()
    {
        Id = id, SchoolId = Ecole, Email = $"{tag}@ecole-a.sn", PasswordHash = "hash-de-test",
        FullName = $"Compte {tag}", Role = role
    };

    private static Teacher Fiche(Guid id, Guid userId, string matricule) => new()
    {
        Id = id, SchoolId = Ecole, Matricule = matricule, FullName = $"Fiche {matricule}",
        Email = $"{matricule}@ecole-a.sn", BirthDate = new DateOnly(1990, 1, 1), UserId = userId
    };

    private static TeacherAssignment Assign(Guid teacherId, Guid subjectId) => new()
    {
        SchoolId = Ecole, TeacherId = teacherId, ClassroomId = Classe, SubjectId = subjectId, SchoolYearId = Annee
    };

    /// <summary>Saisit une note de Maths (barème /10) au nom de <paramref name="author"/>.</summary>
    private async Task<GradeResult> SeedGradeAsync(
        Guid author, Role role, Guid? studentId = null, EvaluationType type = EvaluationType.Devoir1, decimal value = 5)
    {
        await using var ctx = _db.NewAppContext(Ecole);
        return await new CreateGradeCommandHandler(ctx, new StubTenantProvider(Ecole), new TestCurrentUser(author, role), new SubjectFollowScope(ctx))
            .Handle(new CreateGradeCommand(studentId ?? Eleve, Maths, Trimestre, type, value), CancellationToken.None);
    }

    private Task<GradeResult> CorrectAsync(
        Guid actor, Role role, GradeResult grade, decimal newValue, TimeSpan after)
    {
        var ctx = _db.NewAppContext(Ecole);
        var user = new TestCurrentUser(actor, role);
        var clock = new FixedTimeProvider(DateTimeOffset.UtcNow + after);

        return new UpdateGradeCommandHandler(ctx, new GradeCorrectionAuthorizer(ctx, user, clock))
            .Handle(new UpdateGradeCommand(grade.Id, newValue, grade.RowVersion), CancellationToken.None);
    }

    [Fact]
    public async Task Creating_A_Grade_Stamps_Its_Author()
    {
        var grade = await SeedGradeAsync(CompteA, Role.Enseignant);

        await using var ctx = _db.NewAppContext(Ecole);
        (await ctx.Grades.AsNoTracking().SingleAsync(g => g.Id == grade.Id)).CreatedBy.Should().Be(CompteA.ToString());
    }

    [Fact]
    public async Task The_Author_Corrects_Inside_The_Default_Window()
    {
        var grade = await SeedGradeAsync(CompteA, Role.Enseignant);

        var result = await CorrectAsync(CompteA, Role.Enseignant, grade, 8, after: TimeSpan.FromDays(3));

        result.Value.Should().Be(8);
    }

    [Fact]
    public async Task The_Author_Is_Refused_Once_The_Default_Window_Has_Elapsed()
    {
        var grade = await SeedGradeAsync(CompteA, Role.Enseignant);

        var act = () => CorrectAsync(CompteA, Role.Enseignant, grade, 8, after: TimeSpan.FromDays(8));

        (await act.Should().ThrowAsync<ForbiddenException>()).Which.Message.Should().Contain("délai");
    }

    [Fact]
    public async Task The_Window_Comes_From_The_Schools_Settings()
    {
        await using (var owner = _db.NewOwnerContext())
        {
            owner.SchoolSettings.Add(new SchoolSettings { SchoolId = Ecole, GradeEditWindowDays = 2 });
            await owner.SaveChangesAsync(CancellationToken.None);
        }

        var grade = await SeedGradeAsync(CompteA, Role.Enseignant);

        // 3 jours : dans le défaut de 7, mais hors des 2 jours réglés par le Directeur.
        var act = () => CorrectAsync(CompteA, Role.Enseignant, grade, 8, after: TimeSpan.FromDays(3));

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task A_Colleague_Assigned_To_The_Class_And_Subject_Corrects_Inside_The_Window()
    {
        var grade = await SeedGradeAsync(CompteA, Role.Enseignant);

        var result = await CorrectAsync(CompteC, Role.Enseignant, grade, 9, after: TimeSpan.FromDays(1));

        result.Value.Should().Be(9);
    }

    [Fact]
    public async Task A_Teacher_Of_Another_Subject_Cannot_Correct_A_Colleagues_Grade()
    {
        var grade = await SeedGradeAsync(CompteA, Role.Enseignant);

        // B est affecté à Français seulement : ni auteur, ni affecté à Mathématiques.
        var act = () => CorrectAsync(CompteB, Role.Enseignant, grade, 9, after: TimeSpan.FromDays(1));

        (await act.Should().ThrowAsync<ForbiddenException>()).Which.Message.Should().Contain("affecté");
    }

    [Fact]
    public async Task A_Teacher_Without_A_Teacher_Record_Can_Still_Correct_His_Own_Grade()
    {
        var grade = await SeedGradeAsync(CompteSansFiche, Role.Enseignant);

        var result = await CorrectAsync(CompteSansFiche, Role.Enseignant, grade, 7, after: TimeSpan.FromDays(1));

        result.Value.Should().Be(7);
    }

    [Fact]
    public async Task The_Directeur_Corrects_Long_After_The_Window_Without_Any_Assignment()
    {
        var grade = await SeedGradeAsync(CompteA, Role.Enseignant);

        var result = await CorrectAsync(CompteDirecteur, Role.Directeur, grade, 6, after: TimeSpan.FromDays(400));

        result.Value.Should().Be(6);
    }

    [Fact]
    public async Task The_Secretariat_Corrects_Long_After_The_Window_Without_Any_Assignment()
    {
        var grade = await SeedGradeAsync(CompteA, Role.Enseignant);

        var result = await CorrectAsync(CompteSecretaire, Role.Secretariat, grade, 6, after: TimeSpan.FromDays(400));

        result.Value.Should().Be(6);
    }

    [Fact]
    public async Task The_Class_Grid_Tells_Each_Role_Whether_A_Cell_Is_Editable()
    {
        await SeedGradeAsync(CompteA, Role.Enseignant);

        async Task<bool> CanEditAsync(Guid actor, Role role, TimeSpan after)
        {
            await using var ctx = _db.NewAppContext(Ecole);
            var user = new TestCurrentUser(actor, role);
            var handler = new GetClassGradesQueryHandler(
                ctx, new GradeCorrectionAuthorizer(ctx, user, new FixedTimeProvider(DateTimeOffset.UtcNow + after)),
                new SubjectFollowScope(ctx));

            var rows = await handler.Handle(new GetClassGradesQuery(Classe, Maths, Trimestre), CancellationToken.None);

            return rows.Single(r => r.StudentId == Eleve).Devoir1!.CanEdit;
        }

        (await CanEditAsync(CompteA, Role.Enseignant, TimeSpan.FromDays(1))).Should().BeTrue("l'auteur, dans la fenêtre");
        (await CanEditAsync(CompteA, Role.Enseignant, TimeSpan.FromDays(9))).Should().BeFalse("l'auteur, hors fenêtre");
        (await CanEditAsync(CompteB, Role.Enseignant, TimeSpan.FromDays(1))).Should().BeFalse("ni auteur ni affecté");
        (await CanEditAsync(CompteDirecteur, Role.Directeur, TimeSpan.FromDays(400))).Should().BeTrue("le Directeur, sans limite");
        (await CanEditAsync(CompteSecretaire, Role.Secretariat, TimeSpan.FromDays(400))).Should().BeTrue("le Secrétariat, sans limite");
    }

    private ImportGradeSheetCommandHandler NewImportHandler(
        ApplicationDbContext ctx, Guid actor, Role role, TimeSpan after, params GradeSheetRow[] rows)
    {
        var user = new TestCurrentUser(actor, role);
        var clock = new FixedTimeProvider(DateTimeOffset.UtcNow + after);

        return new ImportGradeSheetCommandHandler(
            ctx, new StubTenantProvider(Ecole), new StubParser(rows), user, new GradeCorrectionAuthorizer(ctx, user, clock), new SubjectFollowScope(ctx));
    }

    private static ImportGradeSheetCommand Import(bool dryRun) =>
        new(Classe, Maths, Trimestre, dryRun, [0x1], "notes.xlsx");

    [Fact]
    public async Task An_Import_Cannot_Bypass_The_Window_By_Rewriting_An_Existing_Grade()
    {
        await SeedGradeAsync(CompteA, Role.Enseignant, Eleve, EvaluationType.Devoir1, 5);

        await using var ctx = _db.NewAppContext(Ecole);
        var handler = NewImportHandler(
            ctx, CompteA, Role.Enseignant, TimeSpan.FromDays(9),
            new GradeSheetRow(2, "ELEV-0001", "8", "", ""));

        // La valeur change (5 → 8) hors fenêtre : refusé, y compris en aperçu.
        var act = () => handler.Handle(Import(dryRun: true), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task An_Import_Still_Creates_New_Grades_And_Ignores_Unchanged_Ones_Outside_The_Window()
    {
        await SeedGradeAsync(CompteA, Role.Enseignant, Eleve, EvaluationType.Devoir1, 5);

        await using var ctx = _db.NewAppContext(Ecole);
        var handler = NewImportHandler(
            ctx, CompteA, Role.Enseignant, TimeSpan.FromDays(9),
            new GradeSheetRow(2, "ELEV-0001", "5", "6", ""),   // Devoir1 inchangé, Devoir2 nouveau
            new GradeSheetRow(3, "ELEV-0002", "7", "", ""));   // nouvel élève

        var result = await handler.Handle(Import(dryRun: false), CancellationToken.None);

        result.Created.Should().Be(2);
        result.Updated.Should().Be(0);
        result.Unchanged.Should().Be(1);
    }

    [Fact]
    public async Task A_Directeur_Import_Rewrites_Existing_Grades_Long_After_The_Window()
    {
        await SeedGradeAsync(CompteA, Role.Enseignant, Eleve, EvaluationType.Devoir1, 5);

        await using var ctx = _db.NewAppContext(Ecole);
        var handler = NewImportHandler(
            ctx, CompteDirecteur, Role.Directeur, TimeSpan.FromDays(400),
            new GradeSheetRow(2, "ELEV-0001", "8", "", ""));

        var result = await handler.Handle(Import(dryRun: false), CancellationToken.None);

        result.Updated.Should().Be(1);
    }
}
