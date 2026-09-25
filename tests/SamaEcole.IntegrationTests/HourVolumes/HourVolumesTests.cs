using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Features.Schedules;
using SamaEcole.Application.HourVolumes;
using SamaEcole.Application.Schools;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using SamaEcole.Persistence;
using Xunit;

namespace SamaEcole.IntegrationTests.HourVolumes;

/// <summary>
/// Évolution N°7 — volumes horaires de référence et conformité des emplois du temps contre un vrai PostgreSQL : écarts
/// d'une classe à la grille de son niveau et de sa série, réglages de l'école (série, niveau, retour à la référence,
/// 409), chevauchements de salle, garde à l'écriture d'un créneau, et tenue de weekly_hour_norms en base (RLS, aucun
/// DELETE applicatif, purge par reset_school_data).
/// </summary>
[Trait("Category", "MultiTenant")]
public class HourVolumesTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("61111111-1111-1111-1111-111111111111");
    private static readonly Guid AutreEcole = Guid.Parse("62222222-2222-2222-2222-222222222222");
    private static readonly Guid TleS2 = Guid.Parse("6ccccccc-0000-0000-0000-000000000001");
    private static readonly Guid SixiemeA = Guid.Parse("6ccccccc-0000-0000-0000-000000000002");
    private static readonly Guid ProfSvt = Guid.Parse("6fffffff-0000-0000-0000-000000000001");
    private static readonly Guid ProfMaths = Guid.Parse("6fffffff-0000-0000-0000-000000000002");
    private static readonly Guid MatiereAutreEcole = Guid.Parse("6ddddddd-0000-0000-0000-0000000000ff");

    private static readonly TestCurrentUser Directeur = new(Guid.Parse("6d000000-0000-0000-0000-000000000001"), Role.Directeur);

    // Grille de référence de la Terminale S2 : 28 h.
    private static readonly (string Name, Guid Id)[] LyceeSubjects =
    [
        ("SVT", Guid.NewGuid()), ("Physique-Chimie", Guid.NewGuid()), ("Mathématiques", Guid.NewGuid()),
        ("Français", Guid.NewGuid()), ("Philosophie", Guid.NewGuid()), ("Anglais", Guid.NewGuid()),
        ("Histoire-Géographie", Guid.NewGuid()), ("EPS", Guid.NewGuid())
    ];

    private static readonly Guid MathsCollege = Guid.NewGuid();
    private static readonly Guid FrancaisCollege = Guid.NewGuid();

    private static Guid Lycee(string name) => LyceeSubjects.Single(s => s.Name == name).Id;

    private sealed class StubTenant(Guid schoolId) : ITenantProvider
    {
        public Guid? CurrentSchoolId => schoolId;
    }

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();
        owner.Schools.AddRange(new School { Id = Ecole, Name = "Lycée A" }, new School { Id = AutreEcole, Name = "Lycée B" });
        owner.Classrooms.AddRange(
            new Classroom { Id = TleS2, SchoolId = Ecole, Name = "Tle S2 A", Level = "Lycée", Cycle = CycleType.Lycee, Series = "S2", Capacity = 45 },
            new Classroom { Id = SixiemeA, SchoolId = Ecole, Name = "6e A", Level = "Collège", Cycle = CycleType.College, Capacity = 45 });
        owner.Subjects.AddRange(LyceeSubjects.Select(s => new Subject { Id = s.Id, SchoolId = Ecole, Name = s.Name, Level = "Lycée", Coefficient = 2 }));
        owner.Subjects.AddRange(
            new Subject { Id = MathsCollege, SchoolId = Ecole, Name = "Mathématiques", Level = "Collège", Coefficient = 4 },
            new Subject { Id = FrancaisCollege, SchoolId = Ecole, Name = "Français", Level = "Collège", Coefficient = 4 },
            new Subject { Id = MatiereAutreEcole, SchoolId = AutreEcole, Name = "SVT", Level = "Lycée", Coefficient = 2 });
        owner.Teachers.AddRange(
            new Teacher { Id = ProfSvt, SchoolId = Ecole, Matricule = "ENS-1", FullName = "Awa Ndiaye", Email = "ndiaye@lycee-a.sn", BirthDate = new DateOnly(1980, 1, 1) },
            new Teacher { Id = ProfMaths, SchoolId = Ecole, Matricule = "ENS-2", FullName = "Ibrahima Faye", Email = "faye@lycee-a.sn", BirthDate = new DateOnly(1982, 1, 1) });

        // Écrits par le propriétaire : l'historique d'un établissement peut contenir des chevauchements que la garde
        // d'écriture refuse aujourd'hui — c'est justement ce que le contrôle doit débusquer.
        owner.ScheduleSlots.AddRange(
            new ScheduleSlot { SchoolId = Ecole, TeacherId = ProfSvt, ClassroomId = TleS2, SubjectId = Lycee("SVT"), DayOfWeek = DayOfWeek.Monday, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(12, 0), RoomNumber = "Salle 12" },
            new ScheduleSlot { SchoolId = Ecole, TeacherId = ProfMaths, ClassroomId = TleS2, SubjectId = Lycee("Mathématiques"), DayOfWeek = DayOfWeek.Tuesday, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(13, 0) },
            new ScheduleSlot { SchoolId = Ecole, TeacherId = ProfMaths, ClassroomId = SixiemeA, SubjectId = MathsCollege, DayOfWeek = DayOfWeek.Monday, StartTime = new TimeOnly(10, 0), EndTime = new TimeOnly(12, 0), RoomNumber = "salle-12" });

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private ApplicationDbContext Ctx() => _db.NewAppContext(Ecole);

    private async Task<TimetableComplianceDto> ComplianceAsync(Guid? classroomId)
    {
        await using var ctx = Ctx();
        return await new GetTimetableComplianceQueryHandler(ctx).Handle(new GetTimetableComplianceQuery(classroomId), CancellationToken.None);
    }

    private async Task UpsertAsync(string grade, string? series, Guid subjectId, decimal hours, uint? rowVersion = null)
    {
        await using var ctx = Ctx();
        await new UpsertWeeklyHourNormCommandHandler(ctx, new StubTenant(Ecole))
            .Handle(new UpsertWeeklyHourNormCommand(grade, series, subjectId, hours, rowVersion), CancellationToken.None);
    }

    private async Task<WeeklyHourNormsDto> NormsAsync(string grade, string? series)
    {
        await using var ctx = Ctx();
        return await new GetWeeklyHourNormsQueryHandler(ctx).Handle(new GetWeeklyHourNormsQuery(grade, series), CancellationToken.None);
    }

    [Fact]
    public async Task A_Class_Is_Compared_To_The_Grid_Of_Its_Grade_And_Series()
    {
        var compliance = await ComplianceAsync(TleS2);

        var tle = compliance.Classes.Should().ContainSingle().Subject;
        tle.GradeLevel.Should().Be("Terminale");
        tle.Series.Should().Be("S2");
        tle.Totals.NormHours.Should().Be(28m);
        tle.Totals.PlannedHours.Should().Be(9m);
        tle.Totals.Status.Should().Be(HourComplianceStatus.Under);

        var svt = tle.Subjects.Single(s => s.SubjectName == "SVT");
        svt.PlannedHours.Should().Be(4m);
        svt.NormHours.Should().Be(6m);
        svt.Status.Should().Be(HourComplianceStatus.Under);

        tle.Subjects.Single(s => s.SubjectName == "Mathématiques").Status.Should().Be(HourComplianceStatus.Compliant);
        tle.Subjects.Single(s => s.SubjectName == "Physique-Chimie").PlannedHours.Should().Be(0m);
        tle.Subjects.Should().HaveCount(8, "les huit matières de la grille, planifiées ou non");
    }

    [Fact]
    public async Task A_Shared_Room_Is_Detected_Across_Classes()
    {
        var compliance = await ComplianceAsync(TleS2);

        var conflict = compliance.Conflicts.Should().ContainSingle().Subject;
        conflict.Kind.Should().Be(ScheduleConflictKind.Room);
        conflict.DayOfWeek.Should().Be(DayOfWeek.Monday);
        new[] { conflict.First.ClassroomName, conflict.Second.ClassroomName }.Should().BeEquivalentTo(["Tle S2 A", "6e A"]);
        compliance.Classes[0].ConflictCount.Should().Be(1);
    }

    [Fact]
    public async Task The_Whole_School_Is_Checked_When_No_Class_Is_Given()
    {
        var compliance = await ComplianceAsync(null);

        compliance.Classes.Select(c => c.ClassroomName).Should().BeEquivalentTo(["Tle S2 A", "6e A"]);
        var sixieme = compliance.Classes.Single(c => c.ClassroomId == SixiemeA);
        sixieme.Subjects.Select(s => s.SubjectName).Should().BeEquivalentTo(["Mathématiques", "Français"],
            "seules les matières du COLLÈGE sont confrontées à la grille de Sixième — jamais la Philosophie du lycée");
    }

    [Fact]
    public async Task The_Class_Programme_Restricts_The_Subjects_Checked()
    {
        await using (var owner = _db.NewOwnerContext())
        {
            owner.ClassSubjects.Add(new ClassSubject { SchoolId = Ecole, ClassroomId = SixiemeA, SubjectId = MathsCollege });
            await owner.SaveChangesAsync(CancellationToken.None);
        }

        var sixieme = (await ComplianceAsync(SixiemeA)).Classes.Single();

        sixieme.Subjects.Select(s => s.SubjectName).Should().Equal(["Mathématiques"], "le Français n'est pas au programme saisi de la classe");
    }

    [Fact]
    public async Task A_Series_Setting_Wins_Over_A_Grade_Setting_Which_Wins_Over_The_Grid()
    {
        await UpsertAsync("Terminale", null, Lycee("SVT"), 5m);
        (await ComplianceAsync(TleS2)).Classes[0].Subjects.Single(s => s.SubjectName == "SVT").NormHours.Should().Be(5m);

        await UpsertAsync("Terminale", "S2", Lycee("SVT"), 4m);
        var svt = (await ComplianceAsync(TleS2)).Classes[0].Subjects.Single(s => s.SubjectName == "SVT");
        svt.NormHours.Should().Be(4m);
        svt.Status.Should().Be(HourComplianceStatus.Compliant);

        var norms = await NormsAsync("Terminale", "S2");
        var row = norms.Rows.Single(r => r.SubjectName == "SVT");
        row.TemplateHours.Should().Be(6m);
        row.GradeHours.Should().Be(5m);
        row.SchoolHours.Should().Be(4m);
        row.EffectiveHours.Should().Be(4m);
        row.OverrideId.Should().NotBeNull();
    }

    [Fact]
    public async Task Changing_A_Setting_Requires_Its_Current_RowVersion_And_Resetting_Restores_The_Grid()
    {
        await UpsertAsync("Terminale", "S2", Lycee("SVT"), 4m);

        var withoutVersion = () => UpsertAsync("Terminale", "S2", Lycee("SVT"), 3m);
        await withoutVersion.Should().ThrowAsync<ValidationException>();

        var stale = () => UpsertAsync("Terminale", "S2", Lycee("SVT"), 3m, rowVersion: 1);
        await stale.Should().ThrowAsync<ConcurrencyConflictException>();

        var row = (await NormsAsync("Terminale", "S2")).Rows.Single(r => r.SubjectName == "SVT");
        await UpsertAsync("Terminale", "S2", Lycee("SVT"), 3m, row.RowVersion);

        row = (await NormsAsync("Terminale", "S2")).Rows.Single(r => r.SubjectName == "SVT");
        row.SchoolHours.Should().Be(3m);

        await using (var ctx = Ctx())
        {
            await new ResetWeeklyHourNormCommandHandler(ctx, Directeur)
                .Handle(new ResetWeeklyHourNormCommand(row.OverrideId!.Value, row.RowVersion!.Value), CancellationToken.None);
        }

        (await NormsAsync("Terminale", "S2")).Rows.Single(r => r.SubjectName == "SVT").EffectiveHours.Should().Be(6m);
    }

    [Fact]
    public async Task The_Norms_Screen_Lists_The_Grid_For_The_Cycle_Only()
    {
        var norms = await NormsAsync("Sixième", null);

        norms.HasTemplate.Should().BeTrue();
        norms.Rows.Where(r => r.SubjectId is not null).Select(r => r.SubjectId).Should().BeEquivalentTo([MathsCollege, FrancaisCollege]);
        norms.Rows.Should().Contain(r => r.SubjectId == null && r.SubjectName == "Anglais", "matière de la grille absente de l'établissement");
        norms.TotalHours.Should().Be(11m, "6 h de Français + 5 h de Mathématiques : seules les matières de l'établissement comptent");
    }

    [Theory]
    [InlineData("Sixième", "S2")]
    [InlineData("Terminale", "Z9")]
    [InlineData("Master", null)]
    public async Task An_Invalid_Scope_Is_Refused(string grade, string? series)
    {
        var act = () => UpsertAsync(grade, series, Lycee("SVT"), 4m);
        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Writing_A_Slot_In_An_Occupied_Room_Is_Refused()
    {
        await using var ctx = Ctx();
        var handler = new CreateScheduleSlotCommandHandler(
            ctx, new StubTenant(Ecole), Directeur, new ScheduleOwnershipAuthorizer(ctx, Directeur), new WorkingDayGuard(ctx));

        var act = () => handler.Handle(
            new CreateScheduleSlotCommand(ProfMaths, TleS2, Lycee("Mathématiques"), DayOfWeek.Wednesday, new TimeOnly(8, 0), new TimeOnly(9, 0), "Labo"),
            CancellationToken.None);
        await act.Should().NotThrowAsync();

        var sameRoom = () => handler.Handle(
            new CreateScheduleSlotCommand(ProfSvt, SixiemeA, MathsCollege, DayOfWeek.Wednesday, new TimeOnly(8, 30), new TimeOnly(9, 30), " LABO "),
            CancellationToken.None);
        (await sameRoom.Should().ThrowAsync<FluentValidation.ValidationException>())
            .Which.Errors.Should().ContainSingle(e => e.ErrorMessage.Contains("salle"));
    }

    [Fact]
    public async Task Settings_Of_Another_School_Are_Invisible_And_Unwritable()
    {
        await UpsertAsync("Terminale", "S2", Lycee("SVT"), 4m);

        (await CountAsync(AutreEcole)).Should().Be(0);
        (await CountAsync(schoolId: null)).Should().Be(0, "sans tenant, rien n'est visible");

        var act = () => ExecuteAsync(Ecole, $"""
            INSERT INTO weekly_hour_norms ("Id","SchoolId","GradeLevel","Series","SubjectId","WeeklyHours","CreatedAt","IsDeleted")
            VALUES ('{Guid.NewGuid()}','{AutreEcole}','Terminale','S2','{MatiereAutreEcole}',4,NOW(),FALSE);
            """);
        await act.Should().ThrowAsync<PostgresException>().Where(e => e.SqlState == PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task The_Grade_Wide_Setting_Is_Unique_Even_Without_A_Series()
    {
        await UpsertAsync("Sixième", null, MathsCollege, 4m);

        var duplicate = () => ExecuteAsync(Ecole, $"""
            INSERT INTO weekly_hour_norms ("Id","SchoolId","GradeLevel","Series","SubjectId","WeeklyHours","CreatedAt","IsDeleted")
            VALUES ('{Guid.NewGuid()}','{Ecole}','Sixième',NULL,'{MathsCollege}',6,NOW(),FALSE);
            """);
        await duplicate.Should().ThrowAsync<PostgresException>().Where(e => e.SqlState == PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task The_Application_Role_Cannot_Physically_Delete_A_Setting()
    {
        await UpsertAsync("Sixième", null, MathsCollege, 4m);

        var act = () => ExecuteAsync(Ecole, "DELETE FROM weekly_hour_norms;");

        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.InsufficientPrivilege, "règle #6 : aucune suppression physique");
    }

    [Fact]
    public async Task Resetting_The_School_Data_Purges_The_Settings_Without_A_Foreign_Key_Error()
    {
        await UpsertAsync("Sixième", null, MathsCollege, 4m);

        await ExecuteAsync(Ecole, $"SELECT count(*) FROM reset_school_data('{Ecole}');");

        (await CountAsync(Ecole)).Should().Be(0);
    }

    private async Task ExecuteAsync(Guid schoolId, string sql)
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(schoolId);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private async Task<long> CountAsync(Guid? schoolId)
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(schoolId);
        await using var command = connection.CreateCommand();
        command.CommandText = """SELECT count(*) FROM weekly_hour_norms WHERE NOT "IsDeleted";""";
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
