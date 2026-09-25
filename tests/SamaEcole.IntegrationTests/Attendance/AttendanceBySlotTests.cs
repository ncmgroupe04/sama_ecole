using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Attendance;
using SamaEcole.Application.Attendance.Commands.SubmitAttendanceSheet;
using SamaEcole.Application.Attendance.Queries.GetAttendanceSlots;
using SamaEcole.Application.Attendance.Queries.InitializeAttendanceSheet;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Schools;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using SamaEcole.Persistence;
using Xunit;
using ValidationException = SamaEcole.Application.Common.Exceptions.ValidationException;

namespace SamaEcole.IntegrationTests.Attendance;

file sealed class NoOpKpiCache : IKpiCacheService
{
    public Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken) =>
        factory(cancellationToken);

    public void Invalidate(string key) { }
}

file sealed class NoOpPublish : IPublisher
{
    public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification => Task.CompletedTask;
}

file sealed class StubTenant(Guid? schoolId) : ITenantProvider
{
    public Guid? CurrentSchoolId => schoolId;
}

/// <summary>
/// Évolution N°5 — l'appel par créneau d'emploi du temps : lien fiche ↔ cours, libellé du créneau DÉRIVÉ du
/// cours, contrôle du titulaire, liste des cours du jour. École A : repos jeudi/vendredi ; le samedi 2026-09-26
/// porte deux cours (08:00-10:00 Maths par Awa, 10:00-12:00 Français par Modou).
/// </summary>
public class AttendanceBySlotTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("c1111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("c2222222-2222-2222-2222-222222222222");

    private static readonly Guid ClasseA = Guid.Parse("caaaaaaa-0000-0000-0000-0000000000a1");
    private static readonly Guid ClasseA2 = Guid.Parse("caaaaaaa-0000-0000-0000-0000000000a2");
    private static readonly Guid ClasseB = Guid.Parse("caaaaaaa-0000-0000-0000-0000000000b1");
    private static readonly Guid Maths = Guid.Parse("cccccccc-0000-0000-0000-0000000000c1");
    private static readonly Guid Francais = Guid.Parse("cccccccc-0000-0000-0000-0000000000c2");
    private static readonly Guid MathsB = Guid.Parse("cccccccc-0000-0000-0000-0000000000c3");
    private static readonly Guid AnneeA = Guid.Parse("c1111111-0000-0000-0000-000000000001");
    private static readonly Guid AnneeB = Guid.Parse("c2222222-0000-0000-0000-000000000001");
    private static readonly Guid Eleve = Guid.Parse("ceeeeeee-0000-0000-0000-0000000000e1");

    private static readonly Guid CompteAwa = Guid.Parse("cd000000-0000-0000-0000-0000000000d1");
    private static readonly Guid CompteModou = Guid.Parse("cd000000-0000-0000-0000-0000000000d2");
    private static readonly Guid FicheAwa = Guid.Parse("ce000000-0000-0000-0000-0000000000e1");
    private static readonly Guid FicheModou = Guid.Parse("ce000000-0000-0000-0000-0000000000e2");
    private static readonly Guid FicheB = Guid.Parse("ce000000-0000-0000-0000-0000000000e3");

    private static readonly Guid CreneauMaths = Guid.Parse("c5555555-0000-0000-0000-000000000001");
    private static readonly Guid CreneauFrancais = Guid.Parse("c5555555-0000-0000-0000-000000000002");
    private static readonly Guid CreneauAutreClasse = Guid.Parse("c5555555-0000-0000-0000-000000000003");
    private static readonly Guid CreneauB = Guid.Parse("c5555555-0000-0000-0000-000000000004");

    private static readonly DateOnly Samedi = new(2026, 9, 26);
    private static readonly DateOnly Dimanche = new(2026, 9, 27);
    private static readonly DateOnly Jeudi = new(2026, 9, 24);

    private static readonly TestCurrentUser Directeur = new(Guid.Parse("cd000000-0000-0000-0000-000000000001"), Role.Directeur);
    private static readonly TestCurrentUser Surveillant = new(Guid.Parse("cd000000-0000-0000-0000-000000000002"), Role.Surveillant);
    private static readonly TestCurrentUser Awa = new(CompteAwa, Role.Enseignant);
    private static readonly TestCurrentUser Modou = new(CompteModou, Role.Enseignant);

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();

        owner.Schools.AddRange(
            new School { Id = EcoleA, Name = "École A" },
            new School { Id = EcoleB, Name = "École B" });
        // Repos jeudi/vendredi chez A ; B garde la semaine par défaut.
        owner.SchoolSettings.Add(new SchoolSettings { SchoolId = EcoleA, WorkingDays = "Monday,Tuesday,Wednesday,Saturday,Sunday" });

        owner.Classrooms.AddRange(
            new Classroom { Id = ClasseA, SchoolId = EcoleA, Name = "CM2 A", Level = "Primaire", Capacity = 40 },
            new Classroom { Id = ClasseA2, SchoolId = EcoleA, Name = "CM2 B", Level = "Primaire", Capacity = 40 },
            new Classroom { Id = ClasseB, SchoolId = EcoleB, Name = "6e B", Level = "Collège", Capacity = 40 });
        owner.Subjects.AddRange(
            new Subject { Id = Maths, SchoolId = EcoleA, Name = "Mathématiques", Level = "Primaire", Coefficient = 1 },
            new Subject { Id = Francais, SchoolId = EcoleA, Name = "Français", Level = "Primaire", Coefficient = 1 },
            new Subject { Id = MathsB, SchoolId = EcoleB, Name = "Mathématiques", Level = "Collège", Coefficient = 1 });
        owner.SchoolYears.AddRange(
            new SchoolYear { Id = AnneeA, SchoolId = EcoleA, Label = "2026-2027", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true },
            new SchoolYear { Id = AnneeB, SchoolId = EcoleB, Label = "2026-2027", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true });
        owner.Students.Add(new Student
        {
            Id = Eleve, SchoolId = EcoleA, Matricule = "ELEV-0001", FullName = "Awa Fall", BirthDate = new DateOnly(2015, 1, 1),
            BirthPlace = "Dakar", Gender = "F", ClassroomId = ClasseA
        });

        owner.Users.AddRange(
            new User { Id = CompteAwa, SchoolId = EcoleA, Email = "awa@a.sn", PasswordHash = "x", FullName = "Awa Sow", Role = Role.Enseignant },
            new User { Id = CompteModou, SchoolId = EcoleA, Email = "modou@a.sn", PasswordHash = "x", FullName = "Modou Ba", Role = Role.Enseignant });
        owner.Teachers.AddRange(
            new Teacher { Id = FicheAwa, SchoolId = EcoleA, Matricule = "ENS-001", FullName = "Awa Sow", Email = "awa@a.sn", BirthDate = new DateOnly(1985, 1, 1), UserId = CompteAwa },
            new Teacher { Id = FicheModou, SchoolId = EcoleA, Matricule = "ENS-002", FullName = "Modou Ba", Email = "modou@a.sn", BirthDate = new DateOnly(1985, 1, 1), UserId = CompteModou },
            new Teacher { Id = FicheB, SchoolId = EcoleB, Matricule = "ENS-001", FullName = "Prof B", Email = "b@b.sn", BirthDate = new DateOnly(1985, 1, 1) });

        // Les DEUX enseignants sont affectés à Maths pour cette classe : seule la propriété du CRÉNEAU distingue
        // Awa (titulaire) de Modou.
        owner.TeacherAssignments.AddRange(
            new TeacherAssignment { SchoolId = EcoleA, TeacherId = FicheAwa, ClassroomId = ClasseA, SubjectId = Maths, SchoolYearId = AnneeA },
            new TeacherAssignment { SchoolId = EcoleA, TeacherId = FicheModou, ClassroomId = ClasseA, SubjectId = Maths, SchoolYearId = AnneeA },
            new TeacherAssignment { SchoolId = EcoleA, TeacherId = FicheModou, ClassroomId = ClasseA, SubjectId = Francais, SchoolYearId = AnneeA });

        owner.ScheduleSlots.AddRange(
            Slot(CreneauMaths, EcoleA, FicheAwa, ClasseA, Maths, DayOfWeek.Saturday, 8, 10),
            Slot(CreneauFrancais, EcoleA, FicheModou, ClasseA, Francais, DayOfWeek.Saturday, 10, 12),
            Slot(CreneauAutreClasse, EcoleA, FicheAwa, ClasseA2, Maths, DayOfWeek.Saturday, 8, 10),
            Slot(CreneauB, EcoleB, FicheB, ClasseB, MathsB, DayOfWeek.Saturday, 8, 10));

        await owner.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    // 1 — le libellé du créneau est DÉRIVÉ du cours, quoi que le client envoie.
    [Fact]
    public async Task A_Sheet_On_A_Slot_Stores_The_Link_And_Derives_The_Period_Whatever_The_Client_Sends()
    {
        await using var db = _db.NewAppContext(EcoleA);

        await SubmitAsync(db, Directeur, Maths, Samedi, slot: CreneauMaths, period: "n'importe quoi");

        await using var relecture = _db.NewAppContext(EcoleA);
        var sheet = await relecture.AttendanceSheets.SingleAsync();
        sheet.ScheduleSlotId.Should().Be(CreneauMaths);
        sheet.Period.Should().Be("08:00-10:00");
    }

    [Fact]
    public async Task A_Slot_Sheet_Does_Not_Need_A_Period_From_The_Client()
    {
        await using var db = _db.NewAppContext(EcoleA);

        await SubmitAsync(db, Directeur, Maths, Samedi, slot: CreneauMaths, period: "");

        await using var relecture = _db.NewAppContext(EcoleA);
        (await relecture.AttendanceSheets.SingleAsync()).Period.Should().Be("08:00-10:00");
    }

    // 2 — un créneau qui ne colle pas à (classe, matière, jour) est refusé, rien n'est écrit.
    [Fact]
    public async Task A_Slot_That_Does_Not_Match_Class_Subject_Or_Weekday_Is_Refused_And_Nothing_Is_Written()
    {
        await using var db = _db.NewAppContext(EcoleA);

        var otherClass = async () => await SubmitAsync(db, Directeur, Maths, Samedi, slot: CreneauAutreClasse);
        (await otherClass.Should().ThrowAsync<ValidationException>()).Which.Errors
            .Should().ContainKey(nameof(SubmitAttendanceSheetCommand.ScheduleSlotId));

        var otherSubject = async () => await SubmitAsync(db, Directeur, Francais, Samedi, slot: CreneauMaths);
        await otherSubject.Should().ThrowAsync<ValidationException>();

        var otherWeekday = async () => await SubmitAsync(db, Directeur, Maths, Dimanche, slot: CreneauMaths);
        (await otherWeekday.Should().ThrowAsync<ValidationException>()).Which.Errors[nameof(SubmitAttendanceSheetCommand.ScheduleSlotId)]
            .Single().Should().Contain("samedi");

        await using var relecture = _db.NewAppContext(EcoleA);
        (await relecture.AttendanceSheets.CountAsync()).Should().Be(0);
    }

    // 3 — l'Enseignant ne fait l'appel que de SES créneaux ; la Vie Scolaire, de n'importe lequel (remplaçant).
    [Fact]
    public async Task A_Teacher_Takes_The_Roll_Only_Of_His_Own_Slots_While_The_School_Life_Can_Replace_Him()
    {
        await using var modou = _db.NewAppContext(EcoleA);
        var notMine = async () => await SubmitAsync(modou, Modou, Maths, Samedi, slot: CreneauMaths);
        (await notMine.Should().ThrowAsync<ForbiddenException>()).Which.Message.Should().Contain("autre enseignant");

        await using var awa = _db.NewAppContext(EcoleA);
        await SubmitAsync(awa, Awa, Maths, Samedi, slot: CreneauMaths);

        // Le même créneau, un autre jour de classe, fait par le Surveillant en remplacement d'Awa.
        await using var surveillant = _db.NewAppContext(EcoleA);
        await SubmitAsync(surveillant, Surveillant, Maths, Samedi.AddDays(-7), slot: CreneauMaths);

        await using var relecture = _db.NewAppContext(EcoleA);
        (await relecture.AttendanceSheets.CountAsync()).Should().Be(2);
    }

    // 4 — même créneau, même date : une seule fiche.
    [Fact]
    public async Task The_Same_Slot_On_The_Same_Date_Cannot_Be_Recorded_Twice()
    {
        await using var db = _db.NewAppContext(EcoleA);
        await SubmitAsync(db, Directeur, Maths, Samedi, slot: CreneauMaths);

        await using var again = _db.NewAppContext(EcoleA);
        var act = async () => await SubmitAsync(again, Directeur, Maths, Samedi, slot: CreneauMaths);

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
    }

    // 5 — sans créneau : le mode libre est strictement celui d'avant.
    [Fact]
    public async Task Without_A_Slot_The_Free_Mode_Is_Unchanged()
    {
        await using var db = _db.NewAppContext(EcoleA);

        await SubmitAsync(db, Directeur, Maths, Samedi, slot: null, period: "Matin");

        await using var relecture = _db.NewAppContext(EcoleA);
        var sheet = await relecture.AttendanceSheets.SingleAsync();
        sheet.ScheduleSlotId.Should().BeNull();
        sheet.Period.Should().Be("Matin");
    }

    // Complément N°5 bis (C8/C9) — un retard n'a plus d'autre source qu'un billet d'entrée.
    [Fact]
    public async Task A_Late_Line_Without_An_Entry_Ticket_Is_Refused_And_Nothing_Is_Written()
    {
        await using var db = _db.NewAppContext(EcoleA);

        var act = async () => await SubmitAsync(db, Directeur, Maths, Samedi, slot: CreneauMaths, status: AttendanceStatus.Late, lateMinutes: 10);

        (await act.Should().ThrowAsync<ValidationException>()).Which.Errors[nameof(SubmitAttendanceSheetCommand.Entries)]
            .Single().Should().Contain("billet d'entrée");

        await using var relecture = _db.NewAppContext(EcoleA);
        (await relecture.AttendanceSheets.CountAsync()).Should().Be(0);
        (await relecture.StudentAttendances.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task A_Late_Line_Is_Also_Refused_In_Free_Mode_Where_No_Ticket_Can_Be_Attached()
    {
        await using var db = _db.NewAppContext(EcoleA);

        var act = async () => await SubmitAsync(db, Directeur, Maths, Samedi, slot: null, period: "Matin", status: AttendanceStatus.Late, lateMinutes: 10);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task The_Three_Remaining_Statuses_Are_Still_Accepted_Without_Any_Ticket()
    {
        foreach (var (status, day) in new[]
                 {
                     (AttendanceStatus.Present, Samedi),
                     (AttendanceStatus.JustifiedAbsence, Samedi.AddDays(-7)),
                     (AttendanceStatus.UnjustifiedAbsence, Samedi.AddDays(-14))
                 })
        {
            await using var db = _db.NewAppContext(EcoleA);
            await SubmitAsync(db, Directeur, Maths, day, slot: CreneauMaths, status: status);
        }

        await using var relecture = _db.NewAppContext(EcoleA);
        (await relecture.StudentAttendances.Select(sa => sa.Status).ToListAsync()).Should().BeEquivalentTo(
            [AttendanceStatus.Present, AttendanceStatus.JustifiedAbsence, AttendanceStatus.UnjustifiedAbsence]);
    }

    // 6 — la liste des cours du jour.
    [Fact]
    public async Task The_Slots_Of_The_Day_Follow_The_Working_Week_And_The_Caller()
    {
        await using var db = _db.NewAppContext(EcoleA);

        var rest = await SlotsAsync(db, Directeur, Jeudi);
        rest.Should().BeEmpty("jeudi est un jour de repos de l'établissement");

        var all = await SlotsAsync(db, Directeur, Samedi);
        all.Select(s => s.SlotId).Should().Equal(CreneauMaths, CreneauFrancais);
        all.Select(s => s.Label).Should().Equal("08:00-10:00", "10:00-12:00");
        all.Should().OnlyContain(s => s.SheetId == null);

        var mine = await SlotsAsync(db, Awa, Samedi);
        mine.Select(s => s.SlotId).Should().Equal(new[] { CreneauMaths }, "un enseignant ne voit que ses cours");
    }

    [Fact]
    public async Task A_Slot_Shows_Its_Sheet_Once_The_Roll_Is_Taken()
    {
        await using var db = _db.NewAppContext(EcoleA);
        await SubmitAsync(db, Directeur, Maths, Samedi, slot: CreneauMaths);

        await using var db2 = _db.NewAppContext(EcoleA);
        var slots = await SlotsAsync(db2, Directeur, Samedi);

        slots.Single(s => s.SlotId == CreneauMaths).SheetId.Should().NotBeNull();
        slots.Single(s => s.SlotId == CreneauFrancais).SheetId.Should().BeNull();
    }

    // 7 — l'ouverture de la feuille suit les mêmes règles que la soumission.
    [Fact]
    public async Task Opening_The_Roster_By_Slot_Uses_The_Same_Checks_And_Reports_The_Derived_Period()
    {
        await using var db = _db.NewAppContext(EcoleA);
        var handler = new InitializeAttendanceSheetQueryHandler(
            db, new AttendanceScopeAuthorizer(db, Directeur), new WorkingDayGuard(db));

        var roster = await handler.Handle(new InitializeAttendanceSheetQuery
        {
            ClassroomId = ClasseA, SubjectId = Maths, Date = Samedi, ScheduleSlotId = CreneauMaths
        }, default);
        roster.Period.Should().Be("08:00-10:00");
        roster.AlreadySubmitted.Should().BeFalse();

        var mismatch = async () => await handler.Handle(new InitializeAttendanceSheetQuery
        {
            ClassroomId = ClasseA, SubjectId = Francais, Date = Samedi, ScheduleSlotId = CreneauMaths
        }, default);
        await mismatch.Should().ThrowAsync<ValidationException>();

        await using var db2 = _db.NewAppContext(EcoleA);
        await SubmitAsync(db2, Directeur, Maths, Samedi, slot: CreneauMaths);

        await using var db3 = _db.NewAppContext(EcoleA);
        var again = await new InitializeAttendanceSheetQueryHandler(
                db3, new AttendanceScopeAuthorizer(db3, Directeur), new WorkingDayGuard(db3))
            .Handle(new InitializeAttendanceSheetQuery
            {
                ClassroomId = ClasseA, SubjectId = Maths, Date = Samedi, ScheduleSlotId = CreneauMaths
            }, default);
        again.AlreadySubmitted.Should().BeTrue("la fiche du créneau existe : la grille reprend l'appel saisi");
    }

    // 8 — isolation : le créneau d'une autre école est introuvable.
    [Fact]
    [Trait("Category", "MultiTenant")]
    public async Task A_Slot_Of_Another_School_Is_Never_Accepted()
    {
        await using var db = _db.NewAppContext(EcoleA);

        var act = async () => await SubmitAsync(db, Directeur, Maths, Samedi, slot: CreneauB);

        (await act.Should().ThrowAsync<ValidationException>()).Which.Errors
            .Should().ContainKey(nameof(SubmitAttendanceSheetCommand.ScheduleSlotId));
    }

    private static ScheduleSlot Slot(Guid id, Guid school, Guid teacher, Guid classroom, Guid subject, DayOfWeek day, int from, int to)
        => new()
        {
            Id = id, SchoolId = school, TeacherId = teacher, ClassroomId = classroom, SubjectId = subject,
            DayOfWeek = day, StartTime = new TimeOnly(from, 0), EndTime = new TimeOnly(to, 0)
        };

    private static Task<SubmitAttendanceSheetResult> SubmitAsync(
        ApplicationDbContext db, ICurrentUserService user, Guid subject, DateOnly date, Guid? slot, string period = "Matin",
        AttendanceStatus status = AttendanceStatus.Present, int lateMinutes = 0)
    {
        var handler = new SubmitAttendanceSheetCommandHandler(
            db, new StubTenant(EcoleA), user, new AttendanceScopeAuthorizer(db, user),
            new WorkingDayGuard(db), new NoOpPublish(), new NoOpKpiCache());

        return handler.Handle(
            new SubmitAttendanceSheetCommand
            {
                ClassroomId = ClasseA,
                SubjectId = subject,
                Date = date,
                Period = period,
                ScheduleSlotId = slot,
                Entries = [new AttendanceEntry(Eleve, status, lateMinutes)]
            },
            default);
    }

    private static Task<IReadOnlyList<AttendanceSlotDto>> SlotsAsync(ApplicationDbContext db, ICurrentUserService user, DateOnly date)
        => new GetAttendanceSlotsQueryHandler(db, user, new WorkingDayGuard(db))
            .Handle(new GetAttendanceSlotsQuery(ClasseA, date), default);
}
