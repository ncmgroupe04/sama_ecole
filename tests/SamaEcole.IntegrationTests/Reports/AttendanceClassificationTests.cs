using FluentAssertions;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Reports;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using SamaEcole.Persistence;
using Xunit;
using ValidationException = SamaEcole.Application.Common.Exceptions.ValidationException;
using S = SamaEcole.Domain.Enums.AttendanceStatus;

namespace SamaEcole.IntegrationTests.Reports;

/// <summary>
/// Évolution N°5 (arbitrage B4) — la JOURNÉE d'un élève est classée absence complète, partielle, retard ou
/// présence d'après les séances APPELÉES ; le rapport expose aussi une vue par matière. Le taux de
/// présence, lui, n'a PAS changé : (Présents + Retards) ÷ lignes d'appel saisies.
///
/// Trois jours d'appels, deux élèves, deux matières (voir les fiches ci-dessous) :
///   sam. 19 : Maths P1, Français P2, Maths P3   dim. 20 : idem   lun. 21 : Maths P1, Français P2
///   Awa   19 [Abs. non just., Abs. justifiée, Abs. non just.]  20 [Prés., Prés., Prés.]  21 [Abs., Retard]
///   Modou 19 [Prés., Abs., Prés.]                              20 [Retard, Prés., Prés.]
/// </summary>
[Trait("Category", "MultiTenant")]
public class AttendanceClassificationTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("d1111111-1111-1111-1111-111111111111");
    private static readonly Guid AutreEcole = Guid.Parse("d2222222-2222-2222-2222-222222222222");
    private static readonly Guid Classe = Guid.Parse("daaaaaaa-0000-0000-0000-0000000000a1");
    private static readonly Guid ClasseAutre = Guid.Parse("daaaaaaa-0000-0000-0000-0000000000b1");
    private static readonly Guid Maths = Guid.Parse("dccccccc-0000-0000-0000-0000000000c1");
    private static readonly Guid Francais = Guid.Parse("dccccccc-0000-0000-0000-0000000000c2");
    private static readonly Guid Annee = Guid.Parse("d1111111-0000-0000-0000-000000000001");
    private static readonly Guid Awa = Guid.Parse("deeeeeee-0000-0000-0000-0000000000e1");
    private static readonly Guid Modou = Guid.Parse("deeeeeee-0000-0000-0000-0000000000e2");

    private static readonly DateOnly Debut = new(2026, 9, 19);
    private static readonly DateOnly Fin = new(2026, 9, 27);

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();

        owner.Schools.AddRange(
            new School { Id = Ecole, Name = "École A" },
            new School { Id = AutreEcole, Name = "École B" });
        owner.Classrooms.AddRange(
            new Classroom { Id = Classe, SchoolId = Ecole, Name = "CM2 A", Level = "Primaire", Capacity = 40 },
            new Classroom { Id = ClasseAutre, SchoolId = AutreEcole, Name = "CM2 B", Level = "Primaire", Capacity = 40 });
        owner.Subjects.AddRange(
            new Subject { Id = Maths, SchoolId = Ecole, Name = "Mathématiques", Level = "Primaire", Coefficient = 1 },
            new Subject { Id = Francais, SchoolId = Ecole, Name = "Français", Level = "Primaire", Coefficient = 1 });
        owner.SchoolYears.Add(new SchoolYear
        {
            Id = Annee, SchoolId = Ecole, Label = "2026-2027", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true
        });
        owner.Students.AddRange(
            new Student { Id = Awa, SchoolId = Ecole, Matricule = "ELEV-0001", FullName = "Awa Fall", BirthDate = new DateOnly(2015, 1, 1), BirthPlace = "Dakar", Gender = "F", ClassroomId = Classe },
            new Student { Id = Modou, SchoolId = Ecole, Matricule = "ELEV-0002", FullName = "Modou Diop", BirthDate = new DateOnly(2015, 1, 1), BirthPlace = "Dakar", Gender = "M", ClassroomId = Classe });

        // sam. 19
        Sheet(owner, new DateOnly(2026, 9, 19), Maths, "P1", (Awa, S.UnjustifiedAbsence, 0), (Modou, S.Present, 0));
        Sheet(owner, new DateOnly(2026, 9, 19), Francais, "P2", (Awa, S.JustifiedAbsence, 0), (Modou, S.UnjustifiedAbsence, 0));
        Sheet(owner, new DateOnly(2026, 9, 19), Maths, "P3", (Awa, S.UnjustifiedAbsence, 0), (Modou, S.Present, 0));
        // dim. 20
        Sheet(owner, new DateOnly(2026, 9, 20), Maths, "P1", (Awa, S.Present, 0), (Modou, S.Late, 10));
        Sheet(owner, new DateOnly(2026, 9, 20), Francais, "P2", (Awa, S.Present, 0), (Modou, S.Present, 0));
        Sheet(owner, new DateOnly(2026, 9, 20), Maths, "P3", (Awa, S.Present, 0), (Modou, S.Present, 0));
        // lun. 21 (Modou n'y figure pas)
        Sheet(owner, new DateOnly(2026, 9, 21), Maths, "P1", (Awa, S.UnjustifiedAbsence, 0));
        Sheet(owner, new DateOnly(2026, 9, 21), Francais, "P2", (Awa, S.Late, 5));

        await owner.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task Each_Day_Is_Classified_From_The_Sessions_That_Were_Called()
    {
        await using var db = _db.NewAppContext(Ecole);

        var aggregate = await new AttendanceReportAggregator(db).ComputeAsync(Debut, Fin, null, default);

        var awa = aggregate.Students.Single(s => s.StudentId == Awa);
        awa.FullAbsenceDays.Should().Be(1, "le 19 : absent aux trois séances");
        awa.PartialAbsenceDays.Should().Be(1, "le 21 : absent puis en retard — il est bien venu");
        awa.LateOnlyDays.Should().Be(0);
        awa.DaysRecorded.Should().Be(3);

        var modou = aggregate.Students.Single(s => s.StudentId == Modou);
        modou.FullAbsenceDays.Should().Be(0);
        modou.PartialAbsenceDays.Should().Be(1, "le 19 : absent à une séance sur trois");
        modou.LateOnlyDays.Should().Be(1, "le 20 : un retard, aucune absence");
        modou.DaysRecorded.Should().Be(2);
    }

    [Fact]
    public async Task The_Attendance_Rate_Has_Not_Changed()
    {
        await using var db = _db.NewAppContext(Ecole);

        var aggregate = await new AttendanceReportAggregator(db).ComputeAsync(Debut, Fin, null, default);

        // Présents + retards ÷ lignes : Awa 4/8, Modou 5/6 → 9/14. Aucune notion de jour calendaire.
        aggregate.AverageAttendanceRate.Should().Be(Math.Round(9m / 14m, 4));
        aggregate.Students.Single(s => s.StudentId == Awa).AttendanceRate.Should().Be(0.5m);
        aggregate.Students.Single(s => s.StudentId == Awa).TotalCalls.Should().Be(8);
    }

    [Fact]
    public async Task The_By_Subject_View_Counts_Sessions_And_Statuses_And_Adds_Up_To_The_Student_Report()
    {
        await using var db = _db.NewAppContext(Ecole);
        var aggregator = new AttendanceReportAggregator(db);

        var bySubject = await aggregator.ComputeBySubjectAsync(Debut, Fin, null, default);

        var maths = bySubject.Single(r => r.SubjectId == Maths);
        maths.Sessions.Should().Be(5, "cinq fiches de mathématiques");
        maths.Lines.Should().Be(9);
        maths.Presents.Should().Be(5);
        maths.Lates.Should().Be(1);
        maths.UnjustifiedAbsences.Should().Be(3);
        maths.JustifiedAbsences.Should().Be(0);
        maths.AttendanceRate.Should().Be(Math.Round(6m / 9m, 4));

        var francais = bySubject.Single(r => r.SubjectId == Francais);
        francais.Sessions.Should().Be(3);
        francais.Lines.Should().Be(5);
        francais.Presents.Should().Be(2);
        francais.Lates.Should().Be(1);
        francais.JustifiedAbsences.Should().Be(1);
        francais.UnjustifiedAbsences.Should().Be(1);
        francais.AttendanceRate.Should().Be(0.6m);

        var students = await aggregator.ComputeAsync(Debut, Fin, null, default);
        bySubject.Sum(r => r.Lines).Should().Be(students.Students.Sum(s => s.TotalCalls),
            "le total des lignes par matière égale celui du rapport par élève");
    }

    [Fact]
    public async Task A_Period_Without_Any_Call_Gives_An_Empty_By_Subject_View()
    {
        await using var db = _db.NewAppContext(Ecole);

        var bySubject = await new AttendanceReportAggregator(db)
            .ComputeBySubjectAsync(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), null, default);

        bySubject.Should().BeEmpty();
    }

    [Fact]
    public async Task A_Class_Filter_Outside_The_School_Is_Refused_For_Both_Views()
    {
        await using var db = _db.NewAppContext(Ecole);
        var aggregator = new AttendanceReportAggregator(db);

        var students = async () => await aggregator.ComputeAsync(Debut, Fin, ClasseAutre, default);
        await students.Should().ThrowAsync<ValidationException>();

        var bySubject = async () => await aggregator.ComputeBySubjectAsync(Debut, Fin, ClasseAutre, default);
        await bySubject.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Another_School_Sees_Neither_View()
    {
        await using var autre = _db.NewAppContext(AutreEcole);
        var aggregator = new AttendanceReportAggregator(autre);

        (await aggregator.ComputeBySubjectAsync(Debut, Fin, null, default)).Should().BeEmpty();
        (await aggregator.ComputeAsync(Debut, Fin, null, default)).Students.Should().BeEmpty();
    }

    private static void Sheet(
        ApplicationDbContext owner, DateOnly date, Guid subject, string period,
        params (Guid Student, AttendanceStatus Status, int Minutes)[] lines)
    {
        var sheet = new AttendanceSheet
        {
            SchoolId = Ecole, ClassroomId = Classe, SubjectId = subject, SchoolYearId = Annee,
            Date = date, Period = period, TakenByUserId = Guid.NewGuid()
        };
        owner.AttendanceSheets.Add(sheet);

        foreach (var (student, status, minutes) in lines)
        {
            owner.StudentAttendances.Add(new StudentAttendance
            {
                SchoolId = Ecole, AttendanceSheetId = sheet.Id, StudentId = student, Status = status, LateMinutes = minutes
            });
        }
    }
}
