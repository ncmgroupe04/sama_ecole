using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Attendance;
using SamaEcole.Application.Attendance.Commands.CreateTeacherAttendance;
using SamaEcole.Application.Attendance.Commands.SubmitAttendanceSheet;
using SamaEcole.Application.Attendance.Queries.InitializeAttendanceSheet;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Schools;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

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
/// Évolution N°3 — l'appel des élèves et le pointage des enseignants sont verrouillés les jours de
/// repos de l'établissement (SchoolSettings.WorkingDays). Exerce les vrais Handlers contre un PostgreSQL
/// réel sous le rôle applicatif : le réglage se lit sous RLS, donc par école.
///
/// École A : repos jeudi/vendredi. École B : aucune ligne de réglages → semaine par défaut (lundi → samedi).
/// Dates fixes : 2026-09-24 = jeudi, 2026-09-25 = vendredi, 2026-09-26 = samedi, 2026-09-27 = dimanche.
/// </summary>
public class WorkingDayLockTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("41111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("42222222-2222-2222-2222-222222222222");

    private static readonly Guid ClasseA = Guid.Parse("4aaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid ClasseB = Guid.Parse("4bbbbbbb-0000-0000-0000-00000000000b");
    private static readonly Guid MatiereA = Guid.Parse("4ccccccc-0000-0000-0000-00000000000c");
    private static readonly Guid MatiereB = Guid.Parse("4ddddddd-0000-0000-0000-00000000000d");
    private static readonly Guid AnneeA = Guid.Parse("41111111-0000-0000-0000-000000000001");
    private static readonly Guid AnneeB = Guid.Parse("42222222-0000-0000-0000-000000000002");
    private static readonly Guid EleveA = Guid.Parse("4eeeeeee-0000-0000-0000-00000000000e");
    private static readonly Guid EleveB = Guid.Parse("4fffffff-0000-0000-0000-00000000000f");
    private static readonly Guid FicheEnseignantA = Guid.Parse("41111111-0000-0000-0000-0000000000e1");
    private static readonly Guid FicheEnseignantB = Guid.Parse("42222222-0000-0000-0000-0000000000e2");

    private static readonly DateOnly Jeudi = new(2026, 9, 24);
    private static readonly DateOnly Samedi = new(2026, 9, 26);

    private static readonly TestCurrentUser Directeur = new(Guid.Parse("4d000000-0000-0000-0000-000000000001"), Role.Directeur);

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();

        owner.Schools.AddRange(
            new School { Id = EcoleA, Name = "École A" },
            new School { Id = EcoleB, Name = "École B" });

        // Seule l'école A a une ligne de réglages : B prouve que l'absence de ligne = semaine par défaut.
        owner.SchoolSettings.Add(new SchoolSettings { SchoolId = EcoleA, WorkingDays = "Monday,Tuesday,Wednesday,Saturday,Sunday" });

        owner.Classrooms.AddRange(
            new Classroom { Id = ClasseA, SchoolId = EcoleA, Name = "CM2 A", Level = "Primaire", Capacity = 40 },
            new Classroom { Id = ClasseB, SchoolId = EcoleB, Name = "6e B", Level = "Collège", Capacity = 45 });

        owner.Subjects.AddRange(
            new Subject { Id = MatiereA, SchoolId = EcoleA, Name = "Mathématiques", Level = "Primaire", Coefficient = 4 },
            new Subject { Id = MatiereB, SchoolId = EcoleB, Name = "Français", Level = "Collège", Coefficient = 4 });

        owner.SchoolYears.AddRange(
            new SchoolYear { Id = AnneeA, SchoolId = EcoleA, Label = "2026-2027", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true },
            new SchoolYear { Id = AnneeB, SchoolId = EcoleB, Label = "2026-2027", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true });

        owner.Students.AddRange(
            new Student { Id = EleveA, SchoolId = EcoleA, Matricule = "ELEV-2026-0001", FullName = "Awa Fall", BirthDate = new DateOnly(2015, 3, 12), BirthPlace = "Dakar", Gender = "F", ClassroomId = ClasseA },
            new Student { Id = EleveB, SchoolId = EcoleB, Matricule = "ELEV-2026-0001", FullName = "Modou Diop", BirthDate = new DateOnly(2014, 8, 2), BirthPlace = "Dakar", Gender = "M", ClassroomId = ClasseB });

        owner.Teachers.AddRange(
            new Teacher { Id = FicheEnseignantA, SchoolId = EcoleA, Matricule = "ENS-2026-001", FullName = "Awa Sow", Email = "awa@ecole-a.sn", BirthDate = new DateOnly(1990, 4, 3) },
            new Teacher { Id = FicheEnseignantB, SchoolId = EcoleB, Matricule = "ENS-2026-001", FullName = "Ibra Ndiaye", Email = "ibra@ecole-b.sn", BirthDate = new DateOnly(1988, 11, 20) });

        await owner.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    // 1 — un jeudi (repos) : refusé, et rien n'est écrit.
    [Fact]
    public async Task Submitting_An_Attendance_Sheet_On_A_Rest_Day_Is_Refused_And_Writes_Nothing()
    {
        await using var context = _db.NewAppContext(EcoleA);

        var act = async () => await SubmitAsync(context, EcoleA, ClasseA, MatiereA, EleveA, Jeudi);

        var error = (await act.Should().ThrowAsync<ValidationException>()).Which;
        error.Errors[nameof(SubmitAttendanceSheetCommand.Date)].Single()
            .Should().Contain("jeudi").And.Contain("jour de repos");

        await using var relecture = _db.NewAppContext(EcoleA);
        (await relecture.AttendanceSheets.CountAsync()).Should().Be(0);
    }

    // 2 — un samedi (ouvré) : passe.
    [Fact]
    public async Task Submitting_An_Attendance_Sheet_On_A_Working_Day_Succeeds()
    {
        await using var context = _db.NewAppContext(EcoleA);

        await SubmitAsync(context, EcoleA, ClasseA, MatiereA, EleveA, Samedi);

        await using var relecture = _db.NewAppContext(EcoleA);
        (await relecture.AttendanceSheets.CountAsync()).Should().Be(1);
    }

    // 3 — l'ouverture de la feuille d'appel est verrouillée comme la soumission.
    [Fact]
    public async Task Opening_The_Roster_On_A_Rest_Day_Is_Refused_But_Not_On_A_Working_Day()
    {
        await using var context = _db.NewAppContext(EcoleA);
        var handler = new InitializeAttendanceSheetQueryHandler(
            context, new AttendanceScopeAuthorizer(context, Directeur), new WorkingDayGuard(context));

        var onRestDay = async () => await handler.Handle(RosterQuery(ClasseA, MatiereA, Jeudi), default);
        await onRestDay.Should().ThrowAsync<ValidationException>();

        var roster = await handler.Handle(RosterQuery(ClasseA, MatiereA, Samedi), default);
        roster.Students.Should().ContainSingle().Which.StudentId.Should().Be(EleveA);
    }

    // 4 — pointage enseignant : un vendredi (repos) refusé, un dimanche (ouvré chez A) accepté.
    [Fact]
    public async Task Teacher_Attendance_Follows_The_Configured_Week()
    {
        await using var context = _db.NewAppContext(EcoleA);
        var handler = new CreateTeacherAttendanceCommandHandler(context, new StubTenant(EcoleA), new WorkingDayGuard(context));

        var onRestDay = async () => await handler.Handle(TeacherAttendance(FicheEnseignantA, Utc(25)), default);
        await onRestDay.Should().ThrowAsync<ValidationException>();

        var id = await handler.Handle(TeacherAttendance(FicheEnseignantA, Utc(27)), default);
        id.Should().NotBeEmpty();
    }

    // 5 — école sans ligne de réglages : défaut lundi → samedi.
    [Fact]
    public async Task A_School_Without_Settings_Keeps_The_Default_Monday_To_Saturday_Week()
    {
        await using var context = _db.NewAppContext(EcoleB);
        var handler = new CreateTeacherAttendanceCommandHandler(context, new StubTenant(EcoleB), new WorkingDayGuard(context));

        var id = await handler.Handle(TeacherAttendance(FicheEnseignantB, Utc(26)), default); // samedi
        id.Should().NotBeEmpty();

        var onSunday = async () => await handler.Handle(TeacherAttendance(FicheEnseignantB, Utc(27)), default);
        await onSunday.Should().ThrowAsync<ValidationException>();
    }

    // 6 — le réglage d'une école ne déborde pas sur l'autre : même jeudi, A refuse et B accepte.
    [Fact]
    [Trait("Category", "MultiTenant")]
    public async Task Rest_Days_Are_Per_School()
    {
        await using var contextB = _db.NewAppContext(EcoleB);
        await SubmitAsync(contextB, EcoleB, ClasseB, MatiereB, EleveB, Jeudi);

        await using var contextA = _db.NewAppContext(EcoleA);
        var act = async () => await SubmitAsync(contextA, EcoleA, ClasseA, MatiereA, EleveA, Jeudi);
        await act.Should().ThrowAsync<ValidationException>();

        await using var relectureB = _db.NewAppContext(EcoleB);
        (await relectureB.AttendanceSheets.CountAsync()).Should().Be(1);
    }

    private static Task<SubmitAttendanceSheetResult> SubmitAsync(
        SamaEcole.Persistence.ApplicationDbContext context, Guid schoolId, Guid classroomId, Guid subjectId, Guid studentId, DateOnly date)
    {
        var handler = new SubmitAttendanceSheetCommandHandler(
            context,
            new StubTenant(schoolId),
            Directeur,
            new AttendanceScopeAuthorizer(context, Directeur),
            new WorkingDayGuard(context),
            new NoOpPublish(),
            new NoOpKpiCache());

        return handler.Handle(
            new SubmitAttendanceSheetCommand
            {
                ClassroomId = classroomId,
                SubjectId = subjectId,
                Date = date,
                Period = "Matin",
                Entries = [new AttendanceEntry(studentId, AttendanceStatus.Present, 0)]
            },
            default);
    }

    private static InitializeAttendanceSheetQuery RosterQuery(Guid classroomId, Guid subjectId, DateOnly date)
        => new() { ClassroomId = classroomId, SubjectId = subjectId, Date = date, Period = "Matin" };

    // timestamptz : Npgsql n'accepte qu'un DateTime UTC.
    private static DateTime Utc(int day) => new(2026, 9, day, 0, 0, 0, DateTimeKind.Utc);

    private static CreateTeacherAttendanceCommand TeacherAttendance(Guid teacherId, DateTime date)
        => new() { TeacherId = teacherId, Date = date, Status = AttendanceStatus.Present };
}
