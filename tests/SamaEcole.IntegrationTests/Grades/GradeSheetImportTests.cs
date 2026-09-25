using SamaEcole.Application.ClassSubjects;
using ClosedXML.Excel;
using FluentAssertions;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Grades;
using SamaEcole.Application.Grades.Commands.ImportGradeSheet;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using SamaEcole.Infrastructure.Files;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace SamaEcole.IntegrationTests.Grades;

/// <summary>
/// Le test central du ticket (harmonisation import/export Excel des notes) : un fichier dont les
/// LIGNES (élèves) ET les COLONNES (épreuves) sont volontairement mélangées par rapport à l'ordre
/// "naturel" doit produire EXACTEMENT les mêmes notes, sur les bons élèves et les bonnes épreuves —
/// contre un vrai PostgreSQL, à travers le vrai <see cref="ImportGradeSheetCommandHandler"/> et le vrai
/// <see cref="GradeSheetImportParser"/> (aucun double, comme GradeConcurrencyTests pour le verrou
/// optimiste).
/// </summary>
[Trait("Category", "MultiTenant")]
public class GradeSheetImportTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Classe = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid Annee = Guid.Parse("cccccccc-0000-0000-0000-00000000000c");
    private static readonly Guid Trimestre = Guid.Parse("dddddddd-0000-0000-0000-00000000000d");
    private static readonly Guid Matiere = Guid.Parse("eeeeeeee-0000-0000-0000-00000000000e");

    private static readonly Guid Eleve1 = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
    private static readonly Guid Eleve2 = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid Eleve3 = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000003");

    private const string Matricule1 = "ELEV-2026-0001";
    private const string Matricule2 = "ELEV-2026-0002";
    private const string Matricule3 = "ELEV-2026-0003";

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();
        owner.Schools.Add(new School { Id = Ecole, Name = "École A" });
        owner.Classrooms.Add(new Classroom { Id = Classe, SchoolId = Ecole, Name = "3e A", Level = "Collège", Capacity = 40 });
        owner.Students.AddRange(
            new Student
            {
                Id = Eleve1, SchoolId = Ecole, Matricule = Matricule1, FullName = "Premier Élève",
                BirthDate = new DateOnly(2012, 1, 1), BirthPlace = "Dakar", Gender = "M", ClassroomId = Classe
            },
            new Student
            {
                Id = Eleve2, SchoolId = Ecole, Matricule = Matricule2, FullName = "Second Élève",
                BirthDate = new DateOnly(2012, 2, 1), BirthPlace = "Dakar", Gender = "F", ClassroomId = Classe
            },
            new Student
            {
                Id = Eleve3, SchoolId = Ecole, Matricule = Matricule3, FullName = "Troisième Élève",
                BirthDate = new DateOnly(2012, 3, 1), BirthPlace = "Dakar", Gender = "M", ClassroomId = Classe
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
        owner.Subjects.Add(new Subject { Id = Matiere, SchoolId = Ecole, Name = "Mathématiques", Level = "Collège", Coefficient = 4 });
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private static ImportGradeSheetCommandHandler NewHandler(IApplicationDbContext db)
    {
        // Un Directeur : ces tests portent sur le format et la validation du fichier, pas sur la règle
        // de correction (GradeEditPolicy, testée à part) — il corrige sans limite de délai.
        var director = new TestCurrentUser(Guid.NewGuid(), Role.Directeur);

        return new(db, new StubTenantProvider(Ecole), new GradeSheetImportParser(), director,
            new GradeCorrectionAuthorizer(db, director, TimeProvider.System), new SubjectFollowScope(db));
    }

    private static byte[] BuildXlsx(string[] headers, IEnumerable<string?[]> rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Notes");

        for (var col = 0; col < headers.Length; col++)
        {
            sheet.Cell(1, col + 1).Value = headers[col];
        }

        var rowNumber = 2;
        foreach (var row in rows)
        {
            for (var col = 0; col < row.Length; col++)
            {
                if (row[col] is { } value) sheet.Cell(rowNumber, col + 1).Value = value;
            }
            rowNumber++;
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    [Fact]
    public async Task Rows_And_Columns_Reordered_Still_Land_On_The_Right_Student_And_The_Right_Evaluation()
    {
        // En-têtes dans un ordre inhabituel (Composition avant les deux Devoirs, Matricule en dernier),
        // ET lignes dans un ordre différent de celui des élèves en base (3, 1, 2) : rien dans le fichier
        // ne suit l'ordre "naturel" — seul le CONTENU des en-têtes doit gouverner le résultat.
        var headers = new[] { "Composition", "Devoir 2", "Matricule", "Devoir 1" };
        var file = BuildXlsx(headers,
        [
            ["7", "9", Matricule3, "8"],
            ["16", "14", Matricule1, "15"],
            [null, "20", Matricule2, "18"]
        ]);

        await using var db = _db.NewAppContext(Ecole);
        var handler = NewHandler(db);

        var result = await handler.Handle(
            new ImportGradeSheetCommand(Classe, Matiere, Trimestre, DryRun: false, file, "notes.xlsx"), CancellationToken.None);

        result.StudentsMatched.Should().Be(3);
        result.Created.Should().Be(8); // élève 1 : 3 notes, élève 3 : 3 notes, élève 2 : 2 notes (Composition vide)

        var grades = await db.Grades.Where(g => g.SubjectId == Matiere && g.TermId == Trimestre).ToListAsync(CancellationToken.None);

        Value(Eleve1, EvaluationType.Devoir1).Should().Be(15);
        Value(Eleve1, EvaluationType.Devoir2).Should().Be(14);
        Value(Eleve1, EvaluationType.Composition).Should().Be(16);

        Value(Eleve2, EvaluationType.Devoir1).Should().Be(18);
        Value(Eleve2, EvaluationType.Devoir2).Should().Be(20);
        grades.Should().NotContain(g => g.StudentId == Eleve2 && g.EvaluationType == EvaluationType.Composition,
            "la cellule Composition de l'élève 2 était vide dans le fichier : aucune note ne doit être créée");

        Value(Eleve3, EvaluationType.Devoir1).Should().Be(8);
        Value(Eleve3, EvaluationType.Devoir2).Should().Be(9);
        Value(Eleve3, EvaluationType.Composition).Should().Be(7);

        decimal? Value(Guid studentId, EvaluationType type) => grades
            .Where(g => g.StudentId == studentId && g.EvaluationType == type)
            .Select(g => (decimal?)g.Value)
            .FirstOrDefault();
    }

    [Fact]
    public async Task A_Dry_Run_Reports_The_Same_Counts_As_A_Confirm_But_Writes_Nothing()
    {
        var file = BuildXlsx(["Matricule", "Devoir 1", "Composition"],
        [
            [Matricule1, "15", "16"],
            [Matricule2, "12", "14"]
        ]);

        await using (var previewDb = _db.NewAppContext(Ecole))
        {
            var preview = await NewHandler(previewDb).Handle(
                new ImportGradeSheetCommand(Classe, Matiere, Trimestre, DryRun: true, file, "notes.xlsx"), CancellationToken.None);

            preview.StudentsMatched.Should().Be(2);
            preview.Created.Should().Be(4);
        }

        await using (var checkDb = _db.NewAppContext(Ecole))
        {
            var gradesAfterPreview = await checkDb.Grades.Where(g => g.SubjectId == Matiere && g.TermId == Trimestre).ToListAsync(CancellationToken.None);
            gradesAfterPreview.Should().BeEmpty("un aperçu ne doit rien écrire, même quand tout le fichier est valide");
        }

        await using var confirmDb = _db.NewAppContext(Ecole);
        var confirm = await NewHandler(confirmDb).Handle(
            new ImportGradeSheetCommand(Classe, Matiere, Trimestre, DryRun: false, file, "notes.xlsx"), CancellationToken.None);

        confirm.Created.Should().Be(4);
    }

    [Fact]
    public async Task A_Matricule_From_Another_Classroom_Rejects_The_Whole_File_And_Writes_Nothing()
    {
        var otherClassroom = Guid.NewGuid();
        var otherStudent = Guid.NewGuid();
        await using (var owner = _db.NewOwnerContext())
        {
            owner.Classrooms.Add(new Classroom { Id = otherClassroom, SchoolId = Ecole, Name = "3e B", Level = "Collège", Capacity = 40 });
            owner.Students.Add(new Student
            {
                Id = otherStudent, SchoolId = Ecole, Matricule = "ELEV-2026-9999", FullName = "Élève d'une autre classe",
                BirthDate = new DateOnly(2012, 4, 1), BirthPlace = "Dakar", Gender = "M", ClassroomId = otherClassroom
            });
            await owner.SaveChangesAsync(CancellationToken.None);
        }

        var file = BuildXlsx(["Matricule", "Devoir 1"],
        [
            [Matricule1, "15"],
            ["ELEV-2026-9999", "12"]
        ]);

        await using var db = _db.NewAppContext(Ecole);
        var handler = NewHandler(db);

        var act = async () => await handler.Handle(
            new ImportGradeSheetCommand(Classe, Matiere, Trimestre, DryRun: false, file, "notes.xlsx"), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();

        var grades = await db.Grades.Where(g => g.SubjectId == Matiere && g.TermId == Trimestre).ToListAsync(CancellationToken.None);
        grades.Should().BeEmpty("la ligne valide (élève 1) ne doit pas être enregistrée tant qu'une autre ligne du fichier est en erreur");
    }

    [Fact]
    public async Task A_Grade_Above_The_Classroom_Cycle_Scale_Is_Rejected()
    {
        var file = BuildXlsx(["Matricule", "Devoir 1"], [[Matricule1, "25"]]); // classe Collège -> barème /20

        await using var db = _db.NewAppContext(Ecole);
        var handler = NewHandler(db);

        var act = async () => await handler.Handle(
            new ImportGradeSheetCommand(Classe, Matiere, Trimestre, DryRun: false, file, "notes.xlsx"), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    private sealed class StubTenantProvider(Guid? schoolId) : ITenantProvider
    {
        public Guid? CurrentSchoolId => schoolId;
    }
}
