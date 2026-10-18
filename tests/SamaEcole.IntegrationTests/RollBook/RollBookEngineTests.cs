using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.RollBook;
using SamaEcole.Application.RollBook.Commands.RevertRollBookEntry;
using SamaEcole.Application.RollBook.Commands.UpsertRollBookEntries;
using SamaEcole.Application.RollBook.Queries.GetRollBookGrid;
using SamaEcole.Application.RollBook.Queries.GetRollBookStats;
using SamaEcole.Application.Schools;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using SamaEcole.Persistence;
using Xunit;
using ValidationException = SamaEcole.Application.Common.Exceptions.ValidationException;

namespace SamaEcole.IntegrationTests.RollBook;

file sealed class StubTenant(Guid? schoolId) : ITenantProvider
{
    public Guid? CurrentSchoolId => schoolId;
}

file sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

/// <summary>
/// Cahier de roulement (Franco-Arabe Bloc 5b) sur un VRAI PostgreSQL : fusion des appels par séance et des
/// saisies du cahier, calendrier de fermeture, demi-journées, couverture, portée Enseignant, lot atomique,
/// isolation entre écoles.
///
/// École A (semaine lundi→samedi par défaut), année 2026-10-01 → 2027-07-31. Novembre 2026 : dimanches 1, 8, 15,
/// 22, 29 ; fermeture « Vacances » les mercredi 4 et jeudi 5 → 23 jours de classe. La classe « CM2 A » a trois
/// cours le lundi : Maths 08-10 et Français 10-12 (matin), Arabe 14-16 (après-midi).
/// </summary>
public class RollBookEngineTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("d1111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("d2222222-2222-2222-2222-222222222222");
    private static readonly Guid AnneeA = Guid.Parse("d1111111-0000-0000-0000-000000000001");
    private static readonly Guid AnneeB = Guid.Parse("d2222222-0000-0000-0000-000000000001");

    private static readonly Guid CM2A = Guid.Parse("da000000-0000-0000-0000-0000000000a1");
    private static readonly Guid CM2B = Guid.Parse("da000000-0000-0000-0000-0000000000a2");
    private static readonly Guid CM1A = Guid.Parse("da000000-0000-0000-0000-0000000000a3");
    private static readonly Guid ClasseB = Guid.Parse("da000000-0000-0000-0000-0000000000b1");

    private static readonly Guid Maths = Guid.Parse("dc000000-0000-0000-0000-0000000000c1");
    private static readonly Guid Francais = Guid.Parse("dc000000-0000-0000-0000-0000000000c2");
    private static readonly Guid Arabe = Guid.Parse("dc000000-0000-0000-0000-0000000000c3");

    private static readonly Guid Awa = Guid.Parse("de000000-0000-0000-0000-0000000000e1");
    private static readonly Guid Binta = Guid.Parse("de000000-0000-0000-0000-0000000000e2");
    private static readonly Guid Cheikh = Guid.Parse("de000000-0000-0000-0000-0000000000e3");
    private static readonly Guid Dame = Guid.Parse("de000000-0000-0000-0000-0000000000e4");       // abandon
    private static readonly Guid Eleve2B = Guid.Parse("de000000-0000-0000-0000-0000000000e5");    // CM2 B
    private static readonly Guid Eleve1A = Guid.Parse("de000000-0000-0000-0000-0000000000e6");    // CM1 A
    private static readonly Guid EleveB = Guid.Parse("de000000-0000-0000-0000-0000000000e7");     // école B

    private static readonly Guid CompteProf = Guid.Parse("dd000000-0000-0000-0000-000000000001");
    private static readonly Guid CompteAutreProf = Guid.Parse("dd000000-0000-0000-0000-000000000002");
    private static readonly Guid FicheProf = Guid.Parse("df000000-0000-0000-0000-000000000001");
    private static readonly Guid FicheAutreProf = Guid.Parse("df000000-0000-0000-0000-000000000002");
    private static readonly Guid FicheB = Guid.Parse("df000000-0000-0000-0000-000000000003");

    private static readonly Guid SlotMaths = Guid.Parse("d5000000-0000-0000-0000-000000000001");
    private static readonly Guid SlotFrancais = Guid.Parse("d5000000-0000-0000-0000-000000000002");
    private static readonly Guid SlotArabe = Guid.Parse("d5000000-0000-0000-0000-000000000003");
    private static readonly Guid SlotMatinSeul = Guid.Parse("d5000000-0000-0000-0000-000000000004");
    private static readonly Guid SlotB = Guid.Parse("d5000000-0000-0000-0000-000000000005");

    private static readonly DateOnly Lundi2 = new(2026, 11, 2);
    private static readonly DateOnly Mardi3 = new(2026, 11, 3);
    private static readonly DateOnly Mercredi4 = new(2026, 11, 4);   // fermeture
    private static readonly DateOnly Dimanche8 = new(2026, 11, 8);   // repos
    private static readonly DateOnly Novembre1 = new(2026, 11, 1);
    private static readonly DateOnly Novembre30 = new(2026, 11, 30);

    private static readonly TestCurrentUser Directeur = new(Guid.Parse("dd000000-0000-0000-0000-0000000000f1"), Role.Directeur);
    private static readonly TestCurrentUser ProfAffecte = new(CompteProf, Role.Enseignant);
    private static readonly TestCurrentUser ProfNonAffecte = new(CompteAutreProf, Role.Enseignant);

    private static readonly DateTimeOffset Aujourdhui = new(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();

        owner.Schools.AddRange(new School { Id = EcoleA, Name = "École A" }, new School { Id = EcoleB, Name = "École B" });
        owner.SchoolYears.AddRange(
            new SchoolYear { Id = AnneeA, SchoolId = EcoleA, Label = "2026-2027", StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2027, 7, 31), IsActive = true },
            new SchoolYear { Id = AnneeB, SchoolId = EcoleB, Label = "2026-2027", StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2027, 7, 31), IsActive = true });

        owner.Classrooms.AddRange(
            new Classroom { Id = CM2A, SchoolId = EcoleA, Name = "CM2 A", Level = "Primaire", Cycle = CycleType.Primaire, Capacity = 40 },
            new Classroom { Id = CM2B, SchoolId = EcoleA, Name = "CM2 B", Level = "Primaire", Cycle = CycleType.Primaire, Capacity = 40 },
            new Classroom { Id = CM1A, SchoolId = EcoleA, Name = "CM1 A", Level = "Primaire", Cycle = CycleType.Primaire, Capacity = 40 },
            new Classroom { Id = ClasseB, SchoolId = EcoleB, Name = "CM2 A", Level = "Primaire", Cycle = CycleType.Primaire, Capacity = 40 });

        owner.Subjects.AddRange(
            new Subject { Id = Maths, SchoolId = EcoleA, Name = "Mathématiques", Level = "Primaire", Coefficient = 1 },
            new Subject { Id = Francais, SchoolId = EcoleA, Name = "Français", Level = "Primaire", Coefficient = 1 },
            new Subject { Id = Arabe, SchoolId = EcoleA, Name = "Arabe", Level = "Primaire", Coefficient = 1 });

        owner.Students.AddRange(
            Student(Awa, EcoleA, CM2A, "Awa Fall", "فاطمة فال"),
            Student(Binta, EcoleA, CM2A, "Binta Diop", null),
            Student(Cheikh, EcoleA, CM2A, "Cheikh Ba", null),
            Student(Dame, EcoleA, CM2A, "Dame Sow", null),
            Student(Eleve2B, EcoleA, CM2B, "Eli Gaye", null),
            Student(Eleve1A, EcoleA, CM1A, "Fatou Ndiaye", null),
            Student(EleveB, EcoleB, ClasseB, "Intrus B", null));

        // Dame a abandonné : elle sort du cahier, comme de la feuille d'appel.
        owner.Enrollments.Add(new Enrollment
        {
            SchoolId = EcoleA, StudentId = Dame, SchoolYearId = AnneeA, ClassroomId = CM2A,
            Status = EnrollmentStatus.DroppedOut, ReceiptNumber = "REC-DAME"
        });

        owner.SchoolClosures.Add(new SchoolClosure
        {
            SchoolId = EcoleA, SchoolYearId = AnneeA, Label = "Vacances", LabelAr = "عطلة",
            StartDate = new DateOnly(2026, 11, 4), EndDate = new DateOnly(2026, 11, 5), Kind = SchoolClosureKind.Vacation
        });

        owner.Users.AddRange(
            new User { Id = CompteProf, SchoolId = EcoleA, Email = "prof@a.sn", PasswordHash = "x", FullName = "Prof Affecté", Role = Role.Enseignant },
            new User { Id = CompteAutreProf, SchoolId = EcoleA, Email = "autre@a.sn", PasswordHash = "x", FullName = "Prof Autre", Role = Role.Enseignant });
        owner.Teachers.AddRange(
            new Teacher { Id = FicheProf, SchoolId = EcoleA, Matricule = "ENS-001", FullName = "Prof Affecté", Email = "prof@a.sn", BirthDate = new DateOnly(1985, 1, 1), UserId = CompteProf },
            new Teacher { Id = FicheAutreProf, SchoolId = EcoleA, Matricule = "ENS-002", FullName = "Prof Autre", Email = "autre@a.sn", BirthDate = new DateOnly(1985, 1, 1), UserId = CompteAutreProf },
            new Teacher { Id = FicheB, SchoolId = EcoleB, Matricule = "ENS-001", FullName = "Prof B", Email = "b@b.sn", BirthDate = new DateOnly(1985, 1, 1) });

        // Seul « Prof Affecté » enseigne à CM2 A : le cahier de CM2 A lui est ouvert, celui de CM2 B non.
        owner.TeacherAssignments.Add(new TeacherAssignment
        {
            SchoolId = EcoleA, TeacherId = FicheProf, ClassroomId = CM2A, SubjectId = Maths, SchoolYearId = AnneeA
        });

        owner.ScheduleSlots.AddRange(
            Slot(SlotMaths, EcoleA, FicheProf, CM2A, Maths, 8, 10),
            Slot(SlotFrancais, EcoleA, FicheProf, CM2A, Francais, 10, 12),
            Slot(SlotArabe, EcoleA, FicheProf, CM2A, Arabe, 14, 16),
            Slot(SlotMatinSeul, EcoleA, FicheAutreProf, CM1A, Maths, 8, 10));

        await owner.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    // ------------------------------------------------------------------------------------------------ outils

    private static Student Student(Guid id, Guid school, Guid classroom, string name, string? nameAr) => new()
    {
        Id = id, SchoolId = school, Matricule = $"ELEV-{id.ToString()[^2..]}", FullName = name, FullNameAr = nameAr,
        BirthDate = new DateOnly(2015, 1, 1), BirthPlace = "Dakar", Gender = "F", ClassroomId = classroom
    };

    private static ScheduleSlot Slot(Guid id, Guid school, Guid teacher, Guid classroom, Guid subject, int startHour, int endHour) => new()
    {
        Id = id, SchoolId = school, TeacherId = teacher, ClassroomId = classroom, SubjectId = subject,
        DayOfWeek = DayOfWeek.Monday, StartTime = new TimeOnly(startHour, 0), EndTime = new TimeOnly(endHour, 0)
    };

    /// <summary>Une fiche d'appel avec ses lignes, posée par le propriétaire (exempté de RLS).</summary>
    private async Task SheetAsync(
        Guid school, Guid classroom, Guid subject, Guid year, DateOnly date, Guid? slot, string period,
        params (Guid Student, AttendanceStatus Status, int Minutes)[] lines)
    {
        await using var owner = _db.NewOwnerContext();
        var sheet = new AttendanceSheet
        {
            SchoolId = school, ClassroomId = classroom, SubjectId = subject, SchoolYearId = year, Date = date,
            Period = period, TakenByUserId = Guid.NewGuid(), ScheduleSlotId = slot
        };
        owner.AttendanceSheets.Add(sheet);
        foreach (var (student, status, minutes) in lines)
        {
            owner.StudentAttendances.Add(new StudentAttendance
            {
                SchoolId = school, AttendanceSheetId = sheet.Id, StudentId = student, Status = status, LateMinutes = minutes
            });
        }

        await owner.SaveChangesAsync();
    }

    /// <summary>Le lundi 2 novembre de CM2 A : trois séances, dont une partielle et un retard.</summary>
    private async Task SeedMondayAsync()
    {
        await SheetAsync(EcoleA, CM2A, Maths, AnneeA, Lundi2, SlotMaths, "08:00-10:00",
            (Awa, AttendanceStatus.Present, 0), (Binta, AttendanceStatus.UnjustifiedAbsence, 0), (Cheikh, AttendanceStatus.Late, 10));
        await SheetAsync(EcoleA, CM2A, Francais, AnneeA, Lundi2, SlotFrancais, "10:00-12:00",
            (Awa, AttendanceStatus.Present, 0), (Binta, AttendanceStatus.Present, 0), (Cheikh, AttendanceStatus.Present, 0));
        await SheetAsync(EcoleA, CM2A, Arabe, AnneeA, Lundi2, SlotArabe, "14:00-16:00",
            (Awa, AttendanceStatus.Present, 0), (Binta, AttendanceStatus.Present, 0), (Cheikh, AttendanceStatus.JustifiedAbsence, 0));
    }

    private async Task<ClassRollBook> BuildAsync(
        Guid school, Guid classroom, Guid year, DateOnly from, DateOnly to)
    {
        await using var db = _db.NewAppContext(school);
        var engine = new RollBookEngine(db, new WorkingDayGuard(db));
        var schoolYear = await db.SchoolYears.AsNoTracking().SingleAsync(y => y.Id == year);
        return await engine.BuildAsync(classroom, schoolYear, from, to, CancellationToken.None);
    }

    private static RollBookCell CellOf(ClassRollBook book, Guid student, DateOnly date, HalfDay half) =>
        book.Students.Single(s => s.Student.Id == student).Cells
            .SingleOrDefault(c => c.Date == date && c.Half == half)?.Cell ?? RollBookCell.Empty;

    private RollBookScopeAuthorizer Scope(ApplicationDbContext db, ICurrentUserService user) => new(db, user);

    private UpsertRollBookEntriesCommandHandler UpsertHandler(ApplicationDbContext db, Guid school, ICurrentUserService user) =>
        new(db, new StubTenant(school), user, Scope(db, user), new FixedTime(Aujourdhui),
            NullLogger<UpsertRollBookEntriesCommandHandler>.Instance);

    private static RollBookEntryInput In(Guid student, DateOnly date, HalfDay half, AttendanceStatus? status, int minutes = 0, string? reason = null) =>
        new(student, date, half, status, minutes, reason);

    // ------------------------------------------------------------------------------------- statut dérivé

    [Fact]
    public async Task Derived_Cells_Are_Placed_In_Their_Half_Day_By_The_Slot_Start_Time()
    {
        await SeedMondayAsync();

        var book = await BuildAsync(EcoleA, CM2A, AnneeA, Novembre1, Novembre30);

        CellOf(book, Awa, Lundi2, HalfDay.Morning).Mark.Should().Be(RollBookMark.Present);
        CellOf(book, Awa, Lundi2, HalfDay.Afternoon).Mark.Should().Be(RollBookMark.Present);

        // Binta : absente à Maths mais présente à Français, la MÊME matinée → absence partielle.
        CellOf(book, Binta, Lundi2, HalfDay.Morning).Mark.Should().Be(RollBookMark.PartialAbsence);

        // Cheikh : en retard à Maths (10 min), présent ensuite → retard ; absent justifié à l'unique cours de l'après-midi.
        var late = CellOf(book, Cheikh, Lundi2, HalfDay.Morning);
        late.Mark.Should().Be(RollBookMark.Late);
        late.LateMinutes.Should().Be(10);
        late.Source.Should().Be(RollBookSource.Derived);
        CellOf(book, Cheikh, Lundi2, HalfDay.Afternoon).Mark.Should().Be(RollBookMark.JustifiedAbsence);

        book.UnplacedSessions.Should().Be(0);
    }

    [Fact]
    public async Task A_Sheet_Without_A_Slot_Is_Counted_As_Unplaced_And_Never_Guessed_Into_A_Half_Day()
    {
        await SheetAsync(EcoleA, CM2A, Maths, AnneeA, Mardi3, slot: null, "Matin",
            (Awa, AttendanceStatus.UnjustifiedAbsence, 0), (Binta, AttendanceStatus.Present, 0));

        var book = await BuildAsync(EcoleA, CM2A, AnneeA, Novembre1, Novembre30);

        book.UnplacedSessions.Should().Be(2);
        CellOf(book, Awa, Mardi3, HalfDay.Morning).IsRecorded.Should().BeFalse();
        book.Students.Single(s => s.Student.Id == Awa).Counts.Recorded.Should().Be(0);
    }

    // -------------------------------------------------------------------------- la saisie du cahier prime

    [Fact]
    public async Task A_Roll_Book_Entry_Overrides_The_Derived_Status_And_Feeds_The_Counts()
    {
        await SeedMondayAsync();
        await using (var db = _db.NewAppContext(EcoleA))
        {
            // Binta était « absence partielle » : la direction la déclare absente justifiée (certificat médical).
            await UpsertHandler(db, EcoleA, Directeur).Handle(
                new UpsertRollBookEntriesCommand(CM2A, [In(Binta, Lundi2, HalfDay.Morning, AttendanceStatus.JustifiedAbsence, reason: "Certificat médical")]),
                CancellationToken.None);
        }

        var book = await BuildAsync(EcoleA, CM2A, AnneeA, Novembre1, Novembre30);

        var cell = CellOf(book, Binta, Lundi2, HalfDay.Morning);
        cell.Mark.Should().Be(RollBookMark.JustifiedAbsence);
        cell.Source.Should().Be(RollBookSource.Entry);
        cell.Reason.Should().Be("Certificat médical");

        var counts = book.Students.Single(s => s.Student.Id == Binta).Counts;
        counts.JustifiedAbsence.Should().Be(1);
        counts.PartialAbsence.Should().Be(0, "la saisie a remplacé le constat dérivé");
        counts.Present.Should().Be(1, "l'après-midi reste dérivé : présente");
    }

    [Fact]
    public async Task Reverting_An_Entry_Brings_Back_The_Derived_Status()
    {
        await SeedMondayAsync();
        await using (var db = _db.NewAppContext(EcoleA))
        {
            await UpsertHandler(db, EcoleA, Directeur).Handle(
                new UpsertRollBookEntriesCommand(CM2A, [In(Binta, Lundi2, HalfDay.Morning, AttendanceStatus.Present)]), CancellationToken.None);
        }

        // Retour au calcul par statut nul…
        await using (var db = _db.NewAppContext(EcoleA))
        {
            var result = await UpsertHandler(db, EcoleA, Directeur).Handle(
                new UpsertRollBookEntriesCommand(CM2A, [In(Binta, Lundi2, HalfDay.Morning, null)]), CancellationToken.None);
            result.Should().Be(new UpsertRollBookEntriesResult(0, 0, 1));
        }

        var book = await BuildAsync(EcoleA, CM2A, AnneeA, Novembre1, Novembre30);
        CellOf(book, Binta, Lundi2, HalfDay.Morning).Mark.Should().Be(RollBookMark.PartialAbsence);
        CellOf(book, Binta, Lundi2, HalfDay.Morning).Source.Should().Be(RollBookSource.Derived);

        // … la ligne n'est pas effacée physiquement (règle #6).
        await using var owner = _db.NewOwnerContext();
        (await owner.RollBookEntries.IgnoreQueryFilters().CountAsync(e => e.StudentId == Binta && e.IsDeleted)).Should().Be(1);
    }

    [Fact]
    public async Task Revert_Command_Removes_The_Entry_By_Id()
    {
        Guid entryId;
        await using (var db = _db.NewAppContext(EcoleA))
        {
            await UpsertHandler(db, EcoleA, Directeur).Handle(
                new UpsertRollBookEntriesCommand(CM2A, [In(Awa, Lundi2, HalfDay.Morning, AttendanceStatus.UnjustifiedAbsence)]), CancellationToken.None);
            entryId = (await db.RollBookEntries.SingleAsync()).Id;
        }

        await using (var db = _db.NewAppContext(EcoleA))
        {
            await new RevertRollBookEntryCommandHandler(db, Directeur, Scope(db, Directeur), NullLogger<RevertRollBookEntryCommandHandler>.Instance)
                .Handle(new RevertRollBookEntryCommand(entryId), CancellationToken.None);
        }

        await using var check = _db.NewAppContext(EcoleA);
        (await check.RollBookEntries.CountAsync()).Should().Be(0);

        // Une saisie inconnue (ou d'une autre école) est introuvable.
        var again = async () => await new RevertRollBookEntryCommandHandler(check, Directeur, Scope(check, Directeur), NullLogger<RevertRollBookEntryCommandHandler>.Instance)
            .Handle(new RevertRollBookEntryCommand(entryId), CancellationToken.None);
        await again.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Upserting_An_Existing_Cell_Updates_It_Instead_Of_Duplicating()
    {
        await using var db = _db.NewAppContext(EcoleA);
        var handler = UpsertHandler(db, EcoleA, Directeur);

        (await handler.Handle(new UpsertRollBookEntriesCommand(CM2A, [In(Awa, Lundi2, HalfDay.Morning, AttendanceStatus.Late, 5)]), CancellationToken.None))
            .Should().Be(new UpsertRollBookEntriesResult(1, 0, 0));

        await using var db2 = _db.NewAppContext(EcoleA);
        (await UpsertHandler(db2, EcoleA, Directeur).Handle(
            new UpsertRollBookEntriesCommand(CM2A, [In(Awa, Lundi2, HalfDay.Morning, AttendanceStatus.Late, 20, "Bus")]), CancellationToken.None))
            .Should().Be(new UpsertRollBookEntriesResult(0, 1, 0));

        await using var check = _db.NewAppContext(EcoleA);
        var entry = await check.RollBookEntries.SingleAsync();
        entry.LateMinutes.Should().Be(20);
        entry.Reason.Should().Be("Bus");
    }

    // ---------------------------------------------------------------------- calendrier et demi-journées

    [Fact]
    public async Task The_Month_Counts_Class_Days_Without_Rest_Days_And_Closures()
    {
        var book = await BuildAsync(EcoleA, CM2A, AnneeA, Novembre1, Novembre30);

        book.ClassDayCount.Should().Be(23, "30 jours − 5 dimanches − 2 jours de vacances");
        book.Days.Single(d => d.Date == Mercredi4).OffReason.Should().Be(DayOffReason.Closure);
        book.Days.Single(d => d.Date == Mercredi4).Closure!.LabelAr.Should().Be("عطلة");
        book.Days.Single(d => d.Date == Dimanche8).OffReason.Should().Be(DayOffReason.RestDay);
        book.ActiveHalves.Should().Equal(HalfDay.Morning, HalfDay.Afternoon);
        book.ExpectedHalfDaysPerStudent.Should().Be(46);
    }

    [Fact]
    public async Task An_Entry_On_A_Closed_Day_Is_Shown_But_Does_Not_Weigh_In_The_Counts()
    {
        await using (var db = _db.NewAppContext(EcoleA))
        {
            await UpsertHandler(db, EcoleA, Directeur).Handle(
                new UpsertRollBookEntriesCommand(CM2A, [In(Awa, Mercredi4, HalfDay.Morning, AttendanceStatus.UnjustifiedAbsence)]),
                CancellationToken.None);
        }

        var book = await BuildAsync(EcoleA, CM2A, AnneeA, Novembre1, Novembre30);

        CellOf(book, Awa, Mercredi4, HalfDay.Morning).Mark.Should().Be(RollBookMark.UnjustifiedAbsence);
        book.Students.Single(s => s.Student.Id == Awa).Counts.Recorded.Should().Be(0, "le 4 novembre est fermé");
    }

    [Fact]
    public async Task A_Class_That_Only_Has_Morning_Lessons_Is_Not_Half_Empty()
    {
        var book = await BuildAsync(EcoleA, CM1A, AnneeA, Novembre1, Novembre30);

        book.ActiveHalves.Should().Equal(HalfDay.Morning);
        book.ExpectedHalfDaysPerStudent.Should().Be(23, "une demi-journée par jour de classe, pas deux");
    }

    [Fact]
    public async Task A_Class_Without_Schedule_Or_Data_Expects_Both_Half_Days()
    {
        var book = await BuildAsync(EcoleA, CM2B, AnneeA, Novembre1, Novembre30);

        book.ActiveHalves.Should().Equal(HalfDay.Morning, HalfDay.Afternoon);
    }

    [Fact]
    public async Task Coverage_Reflects_What_Is_Recorded_Against_What_Is_Expected()
    {
        await SeedMondayAsync();

        var book = await BuildAsync(EcoleA, CM2A, AnneeA, Novembre1, Novembre30);
        var counts = RollBookCountsDto.From(book.Students.Single(s => s.Student.Id == Awa).Counts, book.ExpectedHalfDaysPerStudent);

        counts.Recorded.Should().Be(2);
        counts.Coverage.Should().Be(Math.Round(2m / 46m, 4));
        counts.PresenceRate.Should().Be(1m, "rien d'autre n'est renseigné : pas de faux 100 % sans la couverture à côté");
    }

    [Fact]
    public async Task Students_Who_Dropped_Out_Leave_The_Roll_Book()
    {
        var book = await BuildAsync(EcoleA, CM2A, AnneeA, Novembre1, Novembre30);

        book.Students.Select(s => s.Student.FullName).Should().Equal("Awa Fall", "Binta Diop", "Cheikh Ba");
        book.Students[0].Student.FullNameAr.Should().Be("فاطمة فال");
    }

    // ------------------------------------------------------------------------------------- lot et garde-fous

    [Fact]
    public async Task A_Batch_With_One_Invalid_Cell_Writes_Nothing()
    {
        await using var db = _db.NewAppContext(EcoleA);

        var act = async () => await UpsertHandler(db, EcoleA, Directeur).Handle(
            new UpsertRollBookEntriesCommand(CM2A,
            [
                In(Awa, Lundi2, HalfDay.Morning, AttendanceStatus.Present),
                In(Eleve2B, Lundi2, HalfDay.Morning, AttendanceStatus.Present)   // élève d'une AUTRE classe
            ]), CancellationToken.None);

        (await act.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().ContainKey("Entries");

        await using var check = _db.NewAppContext(EcoleA);
        (await check.RollBookEntries.CountAsync()).Should().Be(0, "le lot est atomique : tout passe ou rien");
    }

    [Fact]
    public async Task Future_Dates_And_Dates_Outside_Any_School_Year_Are_Refused()
    {
        await using var db = _db.NewAppContext(EcoleA);
        var handler = UpsertHandler(db, EcoleA, Directeur);

        var future = async () => await handler.Handle(
            new UpsertRollBookEntriesCommand(CM2A, [In(Awa, new DateOnly(2026, 11, 25), HalfDay.Morning, AttendanceStatus.Present)]), CancellationToken.None);
        await future.Should().ThrowAsync<ValidationException>("le « jour 20 » est aujourd'hui : on ne remplit pas le cahier à l'avance");

        var outside = async () => await handler.Handle(
            new UpsertRollBookEntriesCommand(CM2A, [In(Awa, new DateOnly(2026, 8, 12), HalfDay.Morning, AttendanceStatus.Present)]), CancellationToken.None);
        await outside.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Today_Is_Accepted()
    {
        await using var db = _db.NewAppContext(EcoleA);

        var result = await UpsertHandler(db, EcoleA, Directeur).Handle(
            new UpsertRollBookEntriesCommand(CM2A, [In(Awa, new DateOnly(2026, 11, 20), HalfDay.Morning, AttendanceStatus.Present)]), CancellationToken.None);

        result.Created.Should().Be(1);
    }

    [Fact]
    public async Task An_Unknown_Classroom_Is_Refused()
    {
        await using var db = _db.NewAppContext(EcoleA);

        var act = async () => await UpsertHandler(db, EcoleA, Directeur).Handle(
            new UpsertRollBookEntriesCommand(Guid.NewGuid(), [In(Awa, Lundi2, HalfDay.Morning, AttendanceStatus.Present)]), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    // --------------------------------------------------------------------------------- portée Enseignant

    [Fact]
    public async Task A_Teacher_Can_Tend_The_Roll_Book_Of_A_Class_He_Teaches()
    {
        await using var db = _db.NewAppContext(EcoleA);

        var result = await UpsertHandler(db, EcoleA, ProfAffecte).Handle(
            new UpsertRollBookEntriesCommand(CM2A, [In(Awa, Lundi2, HalfDay.Morning, AttendanceStatus.Present)]), CancellationToken.None);

        result.Created.Should().Be(1);

        await using var read = _db.NewAppContext(EcoleA);
        var grid = await new GetRollBookGridQueryHandler(new RollBookEngine(read, new WorkingDayGuard(read)), Scope(read, ProfAffecte))
            .Handle(new GetRollBookGridQuery(CM2A, "2026-11"), CancellationToken.None);
        grid.Students.Should().HaveCount(3);
    }

    [Fact]
    public async Task A_Teacher_Cannot_Read_Or_Write_The_Roll_Book_Of_Another_Class()
    {
        await using var db = _db.NewAppContext(EcoleA);

        var write = async () => await UpsertHandler(db, EcoleA, ProfAffecte).Handle(
            new UpsertRollBookEntriesCommand(CM2B, [In(Eleve2B, Lundi2, HalfDay.Morning, AttendanceStatus.Present)]), CancellationToken.None);
        await write.Should().ThrowAsync<ForbiddenException>();

        await using var read = _db.NewAppContext(EcoleA);
        var grid = async () => await new GetRollBookGridQueryHandler(new RollBookEngine(read, new WorkingDayGuard(read)), Scope(read, ProfAffecte))
            .Handle(new GetRollBookGridQuery(CM2B, "2026-11"), CancellationToken.None);
        await grid.Should().ThrowAsync<ForbiddenException>();

        await using var check = _db.NewAppContext(EcoleA);
        (await check.RollBookEntries.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task A_Teacher_Without_Any_Assignment_Is_Refused()
    {
        await using var db = _db.NewAppContext(EcoleA);

        var act = async () => await UpsertHandler(db, EcoleA, ProfNonAffecte).Handle(
            new UpsertRollBookEntriesCommand(CM2A, [In(Awa, Lundi2, HalfDay.Morning, AttendanceStatus.Present)]), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    // --------------------------------------------------------------------------------------- isolation

    [Fact]
    public async Task Another_School_Cannot_See_Or_Write_This_Roll_Book()
    {
        await SeedMondayAsync();
        await using (var db = _db.NewAppContext(EcoleA))
        {
            await UpsertHandler(db, EcoleA, Directeur).Handle(
                new UpsertRollBookEntriesCommand(CM2A, [In(Awa, Lundi2, HalfDay.Morning, AttendanceStatus.UnjustifiedAbsence)]), CancellationToken.None);
        }

        // Depuis l'école B : la classe de A n'existe pas.
        await using var db2 = _db.NewAppContext(EcoleB);
        var read = async () => await new RollBookEngine(db2, new WorkingDayGuard(db2))
            .BuildAsync(CM2A, await db2.SchoolYears.AsNoTracking().SingleAsync(), Novembre1, Novembre30, CancellationToken.None);
        await read.Should().ThrowAsync<ValidationException>();

        var write = async () => await UpsertHandler(db2, EcoleB, Directeur).Handle(
            new UpsertRollBookEntriesCommand(CM2A, [In(Awa, Lundi2, HalfDay.Morning, AttendanceStatus.Present)]), CancellationToken.None);
        await write.Should().ThrowAsync<ValidationException>();

        // Et la classe homonyme de B reste vide, sans trace des saisies de A.
        var bookB = await BuildAsync(EcoleB, ClasseB, AnneeB, Novembre1, Novembre30);
        bookB.Students.Should().ContainSingle().Which.Cells.Should().BeEmpty();
    }

    // ------------------------------------------------------------------------------------------- grille

    [Fact]
    public async Task The_Grid_Query_Returns_The_Month_The_Cells_And_The_Totals()
    {
        await SeedMondayAsync();

        await using var db = _db.NewAppContext(EcoleA);
        var grid = await new GetRollBookGridQueryHandler(new RollBookEngine(db, new WorkingDayGuard(db)), Scope(db, Directeur))
            .Handle(new GetRollBookGridQuery(CM2A, "2026-11"), CancellationToken.None);

        grid.SchoolYearLabel.Should().Be("2026-2027");
        grid.Days.Should().HaveCount(30);
        grid.ClassDays.Should().Be(23);
        grid.Level.Should().Be("CM2", "le niveau est lu dans le nom de la classe");
        grid.Students.Should().HaveCount(3);

        var cheikh = grid.Students.Single(s => s.StudentId == Cheikh);
        cheikh.Cells.Should().Contain(c => c.Date == Lundi2 && c.HalfDay == "Morning" && c.Mark == "Late" && c.LateMinutes == 10 && c.Source == "Derived");
        cheikh.Counts.Late.Should().Be(1);
        cheikh.Counts.JustifiedAbsence.Should().Be(1);

        grid.Totals.Recorded.Should().Be(6, "3 élèves × 2 demi-journées renseignées le lundi 2");
    }

    [Fact]
    public async Task The_Grid_Refuses_A_Month_Outside_Any_School_Year()
    {
        await using var db = _db.NewAppContext(EcoleA);

        var act = async () => await new GetRollBookGridQueryHandler(new RollBookEngine(db, new WorkingDayGuard(db)), Scope(db, Directeur))
            .Handle(new GetRollBookGridQuery(CM2A, "2025-03"), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    // ------------------------------------------------------------------------------------ statistiques

    [Fact]
    public async Task Class_Stats_Give_Per_Student_Class_And_Level_Rates()
    {
        await SeedMondayAsync();

        await using var db = _db.NewAppContext(EcoleA);
        var stats = await new GetRollBookStatsQueryHandler(db, new RollBookEngine(db, new WorkingDayGuard(db)), Scope(db, Directeur))
            .Handle(new GetRollBookStatsQuery("class", CM2A, "2026-11", null), CancellationToken.None);

        stats.Students.Should().HaveCount(3);
        stats.Students.Single(s => s.StudentId == Binta).Counts.PartialAbsence.Should().Be(1);

        var cheikh = stats.Students.Single(s => s.StudentId == Cheikh).Counts;
        cheikh.Recorded.Should().Be(2);
        cheikh.PresenceRate.Should().Be(0.5m, "retard = présent, absence justifiée = absent : 1/2");
        cheikh.AbsenceRate.Should().Be(0.5m);
        cheikh.AssiduityRate.Should().Be(1m, "l'absence justifiée ne pénalise pas l'assiduité");

        stats.Classes.Should().ContainSingle().Which.Counts.Recorded.Should().Be(6);
        stats.Levels.Should().ContainSingle().Which.Level.Should().Be("CM2");
    }

    [Fact]
    public async Task Level_Stats_Sum_The_Counts_Of_Every_Class_Of_The_Level()
    {
        await SeedMondayAsync();
        // Une classe sœur du même niveau (CM2 B) : 2 demi-journées, dont 1 absence non justifiée.
        await using (var db = _db.NewAppContext(EcoleA))
        {
            await UpsertHandler(db, EcoleA, Directeur).Handle(new UpsertRollBookEntriesCommand(CM2B,
            [
                In(Eleve2B, Lundi2, HalfDay.Morning, AttendanceStatus.Present),
                In(Eleve2B, Lundi2, HalfDay.Afternoon, AttendanceStatus.UnjustifiedAbsence)
            ]), CancellationToken.None);
        }

        await using var read = _db.NewAppContext(EcoleA);
        var stats = await new GetRollBookStatsQueryHandler(read, new RollBookEngine(read, new WorkingDayGuard(read)), Scope(read, Directeur))
            .Handle(new GetRollBookStatsQuery("level", null, "2026-11", null), CancellationToken.None);

        stats.Students.Should().BeEmpty("la portée « level » ne détaille pas les élèves");
        stats.Classes.Select(c => c.Name).Should().BeEquivalentTo("CM2 A", "CM2 B", "CM1 A");

        var cm2 = stats.Levels.Single(l => l.Level == "CM2");
        cm2.ClassCount.Should().Be(2);
        cm2.StudentCount.Should().Be(4);
        cm2.Counts.Recorded.Should().Be(8, "6 de CM2 A + 2 de CM2 B, sommés");
        cm2.Counts.UnjustifiedAbsence.Should().Be(1);

        // Le total de l'école est la somme des niveaux, pas une moyenne.
        stats.Totals.Recorded.Should().Be(stats.Levels.Sum(l => l.Counts.Recorded));
    }

    [Fact]
    public async Task Stats_Over_A_Term_Use_The_Term_Dates()
    {
        await SeedMondayAsync();
        var termId = Guid.NewGuid();
        await using (var owner = _db.NewOwnerContext())
        {
            owner.Terms.Add(new Term
            {
                Id = termId, SchoolId = EcoleA, SchoolYearId = AnneeA, Label = "1er trimestre", Order = 1,
                StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2026, 12, 20)
            });
            await owner.SaveChangesAsync();
        }

        await using var db = _db.NewAppContext(EcoleA);
        var stats = await new GetRollBookStatsQueryHandler(db, new RollBookEngine(db, new WorkingDayGuard(db)), Scope(db, Directeur))
            .Handle(new GetRollBookStatsQuery("class", CM2A, null, termId), CancellationToken.None);

        stats.From.Should().Be(new DateOnly(2026, 10, 1));
        stats.To.Should().Be(new DateOnly(2026, 12, 20));
        stats.Classes.Single().Counts.Recorded.Should().Be(6);
    }

    [Fact]
    public async Task Stats_Of_An_Unknown_Term_Are_Refused()
    {
        await using var db = _db.NewAppContext(EcoleA);

        var act = async () => await new GetRollBookStatsQueryHandler(db, new RollBookEngine(db, new WorkingDayGuard(db)), Scope(db, Directeur))
            .Handle(new GetRollBookStatsQuery("level", null, null, Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }
}
