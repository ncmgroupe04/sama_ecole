using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Boarding.Attendance;
using SamaEcole.Application.Boarding.Attendance.GetAttendanceSheet;
using SamaEcole.Application.Boarding.Attendance.RecordAttendance;
using SamaEcole.Application.Boarding.Attendance.Sessions;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Common.SoftDelete;
using SamaEcole.Application.Internat;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Boarding;

/// <summary>
/// Sessions de pointage configurables (appel de nuit, repas, appel de la Halqa) : configuration par la Direction, feuilles d'appel par
/// audience, portée de l'Oustaz et récapitulatif mensuel — handlers + PostgreSQL réel. Aujourd'hui = 12/10/2026.
/// </summary>
[Trait("Category", "MultiTenant")]
public class AttendanceSessionsTests : LeaveTestBase
{
    private static readonly Guid UserOustaz1 = Guid.Parse("e9d11111-0000-0000-0000-0000000000a2");
    private static readonly Guid UserOustaz2 = Guid.Parse("e9d11111-0000-0000-0000-0000000000a3");
    private static readonly Guid UserDirecteur = Guid.Parse("e9d11111-0000-0000-0000-0000000000a1");
    private static readonly Guid Oustaz1 = Guid.Parse("e9d11111-0000-0000-0000-0000000000f1");
    private static readonly Guid Oustaz2 = Guid.Parse("e9d11111-0000-0000-0000-0000000000f2");

    private static readonly DateOnly Night = new(2026, 10, 11);   // un dimanche
    private static readonly int[] EveryDay = [1, 2, 3, 4, 5, 6, 7];

    private Guid _classe;
    private readonly Dictionary<string, Guid> _students = new();

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await using var owner = Db.NewOwnerContext();

        _classe = (await owner.Classrooms.IgnoreQueryFilters().AsNoTracking().FirstAsync(c => c.SchoolId == EcoleA)).Id;
        foreach (var (key, stay) in Stays)
        {
            _students[key] = (await owner.BoardingEnrollments.IgnoreQueryFilters().AsNoTracking().FirstAsync(b => b.Id == stay)).StudentId;
        }

        owner.Users.AddRange(
            NewUser(UserDirecteur, "directeur@a.sn", Role.Directeur),
            NewUser(UserOustaz1, "oustaz1@a.sn", Role.Enseignant),
            NewUser(UserOustaz2, "oustaz2@a.sn", Role.Enseignant));
        await owner.SaveChangesAsync();

        owner.Instructors.AddRange(
            new Instructor { Id = Oustaz1, SchoolId = EcoleA, FullName = "Oustaz Un", UserId = UserOustaz1 },
            new Instructor { Id = Oustaz2, SchoolId = EcoleA, FullName = "Oustaz Deux", UserId = UserOustaz2 });
        await owner.SaveChangesAsync();

        // Halqa d'Oustaz Un : deux pensionnaires (m1, m2) et un élève de daara sans séjour. Halqa d'Oustaz Deux : m3.
        var daara = new Student
        {
            Id = Guid.NewGuid(), SchoolId = EcoleA, Matricule = "M-daara", FullName = "Élève daara",
            BirthDate = new DateOnly(2014, 1, 1), BirthPlace = "Touba", Gender = "M", ClassroomId = _classe
        };
        owner.Students.Add(daara);
        _students["daara"] = daara.Id;
        await owner.SaveChangesAsync();

        foreach (var (key, instructor) in new[] { ("m1", Oustaz1), ("m2", Oustaz1), ("daara", Oustaz1), ("m3", Oustaz2) })
        {
            var student = await owner.Students.IgnoreQueryFilters().FirstAsync(s => s.Id == _students[key]);
            student.InstructorId = instructor;
        }

        await owner.SaveChangesAsync();
    }

    private static User NewUser(Guid id, string email, Role role) =>
        new() { Id = id, SchoolId = EcoleA, Email = email, PasswordHash = "x", FullName = email, Role = role };

    // ------------------------------------------------------------------ outillage

    private sealed class StubTenantProvider(Guid? schoolId) : ITenantProvider
    {
        public Guid? CurrentSchoolId => schoolId;
    }

    private static TestCurrentUser Direction => new(UserDirecteur, Role.Directeur);

    private static TestCurrentUser Oustaz(Guid userId) => new(userId, Role.Enseignant);

    private async Task<AttendanceSessionDto> CreateAsync(
        string name, AttendanceAudience audience, int[]? days = null, bool active = true, string? timeLabel = null, Guid? school = null)
    {
        await using var ctx = Db.NewAppContext(school ?? EcoleA);
        return await new CreateAttendanceSessionCommandHandler(ctx, new StubTenantProvider(school ?? EcoleA))
            .Handle(new CreateAttendanceSessionCommand(name, audience, days ?? EveryDay, timeLabel, active), CancellationToken.None);
    }

    private async Task<AttendanceSessionDto> UpdateAsync(
        Guid id, string name, AttendanceAudience audience, int[]? days = null, bool active = true, Guid? school = null)
    {
        await using var ctx = Db.NewAppContext(school ?? EcoleA);
        return await new UpdateAttendanceSessionCommandHandler(ctx)
            .Handle(new UpdateAttendanceSessionCommand(id, name, audience, days ?? EveryDay, null, active), CancellationToken.None);
    }

    private async Task<IReadOnlyList<AttendanceSessionDto>> ListAsync(Guid? school = null, bool activeOnly = false, AttendanceAudience? audience = null)
    {
        await using var ctx = Db.NewAppContext(school ?? EcoleA);
        return await new ListAttendanceSessionsQueryHandler(ctx)
            .Handle(new ListAttendanceSessionsQuery(activeOnly, audience), CancellationToken.None);
    }

    private async Task<SessionSheetDto> GroupSheetAsync(Guid session, Guid? group, DateOnly date, TestCurrentUser? user = null, Guid? school = null)
    {
        await using var ctx = Db.NewAppContext(school ?? EcoleA);
        return await new GetSessionSheetQueryHandler(ctx, new HalqaScopeAuthorizer(ctx, user ?? Direction), Clock)
            .Handle(new GetSessionSheetQuery(session, group, date), CancellationToken.None);
    }

    private async Task<RecordSessionAttendanceResult> RecordGroupAsync(
        Guid session, Guid? group, DateOnly date, SessionEntryInput[] entries, TestCurrentUser? user = null)
    {
        await using var ctx = Db.NewAppContext(EcoleA);
        return await new RecordSessionAttendanceCommandHandler(ctx, new HalqaScopeAuthorizer(ctx, user ?? Direction), Clock)
            .Handle(new RecordSessionAttendanceCommand(session, group, date, entries), CancellationToken.None);
    }

    private async Task<MonthlySummaryDto> SummaryAsync(Guid session, Guid? group, DateOnly month, TestCurrentUser? user = null)
    {
        await using var ctx = Db.NewAppContext(EcoleA);
        return await new GetMonthlyAttendanceSummaryQueryHandler(ctx, new HalqaScopeAuthorizer(ctx, user ?? Direction))
            .Handle(new GetMonthlyAttendanceSummaryQuery(month, session, group), CancellationToken.None);
    }

    private async Task<RecordAttendanceResult> RecordNightAsync(DateOnly date, Guid dormitory, Guid? session, params string[] present)
    {
        await using var ctx = Db.NewAppContext(EcoleA);
        return await new RecordAttendanceCommandHandler(ctx, Clock).Handle(
            new RecordAttendanceCommand(date, dormitory, present.Select(k => new AttendanceEntryInput(Stays[k], true, null)).ToList(), session),
            CancellationToken.None);
    }

    private SessionEntryInput In(string key, bool present, string? note = null) => new(_students[key], present, note);

    private async Task<List<BoardingAttendance>> RowsAsync(Guid? session = null)
    {
        await using var owner = Db.NewOwnerContext();
        return await owner.BoardingAttendances.IgnoreQueryFilters().AsNoTracking()
            .Where(a => session == null || a.SessionId == session).ToListAsync();
    }

    private async Task AddLeaveAsync(string key, DateOnly leave, DateOnly? returned = null)
    {
        await using var owner = Db.NewOwnerContext();
        owner.BoardingLeaves.Add(new BoardingLeave
        {
            SchoolId = EcoleA, BoardingEnrollmentId = Stays[key], LeaveDate = leave, ExpectedReturnDate = leave.AddDays(3),
            ActualReturnDate = returned, Reason = BoardingLeaveReason.Famille, AccompaniedBy = "Parent"
        });
        await owner.SaveChangesAsync();
    }

    // ------------------------------------------------------------------ configuration des sessions

    [Fact]
    public async Task Sessions_Are_Created_Listed_In_Creation_Order_And_Named_Once_Per_School_Ignoring_Case()
    {
        var night = await CreateAsync("Appel de nuit", AttendanceAudience.Interns, timeLabel: "  21 h 00 ");
        var lunch = await CreateAsync("Déjeuner", AttendanceAudience.AllBoarders);

        night.SortOrder.Should().Be(0);
        lunch.SortOrder.Should().Be(1);
        night.TimeLabel.Should().Be("21 h 00", "le repère horaire est nettoyé");
        (await ListAsync()).Select(s => s.Name).Should().Equal("Appel de nuit", "Déjeuner");

        var twice = async () => await CreateAsync("DÉJEUNER", AttendanceAudience.HalfBoarders);
        await twice.Should().ThrowAsync<ValidationException>("un nom par école, sans tenir compte de la casse");

        // L'école B peut réutiliser le même nom : l'unicité est par école.
        var other = await CreateAsync("Déjeuner", AttendanceAudience.AllBoarders, school: EcoleB);
        other.Name.Should().Be("Déjeuner");
    }

    [Fact]
    public async Task Days_Are_Kept_As_Iso_Days_And_Only_Active_Sessions_Of_An_Audience_Can_Be_Listed()
    {
        await CreateAsync("Appel de nuit", AttendanceAudience.Interns, days: [1, 2, 3, 4, 7]);
        await CreateAsync("Halqa du vendredi", AttendanceAudience.Halqa, days: [5]);
        await CreateAsync("Halqa du soir", AttendanceAudience.Halqa, active: false);

        var all = await ListAsync();
        all.Single(s => s.Name == "Appel de nuit").Days.Should().Equal(1, 2, 3, 4, 7);
        all.Single(s => s.Name == "Halqa du vendredi").Days.Should().Equal(5);

        (await ListAsync(activeOnly: true)).Select(s => s.Name).Should().Equal("Appel de nuit", "Halqa du vendredi");
        (await ListAsync(audience: AttendanceAudience.Halqa)).Select(s => s.Name).Should().Equal("Halqa du vendredi", "Halqa du soir");
        (await ListAsync(activeOnly: true, audience: AttendanceAudience.Halqa)).Select(s => s.Name).Should().Equal("Halqa du vendredi");
    }

    [Fact]
    public async Task A_Session_That_Holds_Records_Can_Be_Renamed_And_Deactivated_But_Not_Redirected_To_Another_Audience()
    {
        var meal = await CreateAsync("Déjeuner", AttendanceAudience.AllBoarders);
        (await RecordGroupAsync(meal.Id, _classe, Night, [In("m1", true)])).Saved.Should().Be(1);

        (await ListAsync()).Single().HasRecords.Should().BeTrue();

        var audience = async () => await UpdateAsync(meal.Id, "Déjeuner", AttendanceAudience.Halqa);
        (await audience.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("ATTENDANCE_SESSION_AUDIENCE_LOCKED");

        var renamed = await UpdateAsync(meal.Id, "Repas de midi", AttendanceAudience.AllBoarders, days: [1, 2, 3, 4, 5], active: false);
        renamed.Should().Match<AttendanceSessionDto>(s => s.Name == "Repas de midi" && !s.IsActive && s.Days.Count == 5);
    }

    [Fact]
    public async Task An_Unused_Session_Is_Deleted_And_Frees_Its_Name_While_A_Used_One_Is_Refused()
    {
        var unused = await CreateAsync("Goûter", AttendanceAudience.AllBoarders);
        var used = await CreateAsync("Déjeuner", AttendanceAudience.AllBoarders);
        await RecordGroupAsync(used.Id, _classe, Night, [In("m1", true)]);

        await using (var ctx = Db.NewAppContext(EcoleA))
        {
            var handler = new DeleteAttendanceSessionCommandHandler(ctx, Direction);
            var refused = async () => await handler.Handle(new DeleteAttendanceSessionCommand(used.Id), CancellationToken.None);
            (await refused.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be(SoftDeleteLifecycle.ResourceInUse);

            await handler.Handle(new DeleteAttendanceSessionCommand(unused.Id), CancellationToken.None);
        }

        (await ListAsync()).Select(s => s.Name).Should().Equal("Déjeuner");
        (await CreateAsync("Goûter", AttendanceAudience.AllBoarders)).Name.Should().Be("Goûter", "le nom d'une session supprimée est libre");
    }

    [Fact]
    public async Task Reordering_Needs_The_Complete_List_Of_The_School_Sessions()
    {
        var a = await CreateAsync("A", AttendanceAudience.Interns);
        var b = await CreateAsync("B", AttendanceAudience.AllBoarders);
        var c = await CreateAsync("C", AttendanceAudience.Halqa);

        await using (var ctx = Db.NewAppContext(EcoleA))
        {
            var handler = new ReorderAttendanceSessionsCommandHandler(ctx);
            await handler.Handle(new ReorderAttendanceSessionsCommand([c.Id, a.Id, b.Id]), CancellationToken.None);

            var partial = async () => await handler.Handle(new ReorderAttendanceSessionsCommand([c.Id, a.Id]), CancellationToken.None);
            await partial.Should().ThrowAsync<ValidationException>("une liste incomplète laisserait des positions en double");

            var foreign = async () => await handler.Handle(new ReorderAttendanceSessionsCommand([c.Id, a.Id, Guid.NewGuid()]), CancellationToken.None);
            await foreign.Should().ThrowAsync<ValidationException>();
        }

        (await ListAsync()).Select(s => s.Name).Should().Equal("C", "A", "B");
    }

    [Fact]
    public async Task Another_School_Neither_Sees_Nor_Changes_A_Session()
    {
        var mine = await CreateAsync("Déjeuner", AttendanceAudience.AllBoarders);

        (await ListAsync(EcoleB)).Should().BeEmpty();
        var update = async () => await UpdateAsync(mine.Id, "Piraté", AttendanceAudience.AllBoarders, school: EcoleB);
        await update.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public void The_Configuration_Validators_Refuse_Empty_Or_Out_Of_Range_Values()
    {
        var validator = new CreateAttendanceSessionCommandValidator();

        validator.Validate(new CreateAttendanceSessionCommand("Dîner", AttendanceAudience.AllBoarders, EveryDay, "19 h 30")).IsValid.Should().BeTrue();
        validator.Validate(new CreateAttendanceSessionCommand(" ", AttendanceAudience.AllBoarders, EveryDay, null)).IsValid.Should().BeFalse("nom vide");
        validator.Validate(new CreateAttendanceSessionCommand(new string('x', 61), AttendanceAudience.AllBoarders, EveryDay, null)).IsValid.Should().BeFalse("nom trop long");
        validator.Validate(new CreateAttendanceSessionCommand("X", AttendanceAudience.AllBoarders, [], null)).IsValid.Should().BeFalse("aucun jour");
        validator.Validate(new CreateAttendanceSessionCommand("X", AttendanceAudience.AllBoarders, [0, 8], null)).IsValid.Should().BeFalse("jours hors 1 à 7");
        validator.Validate(new CreateAttendanceSessionCommand("X", AttendanceAudience.AllBoarders, EveryDay, new string('h', 41))).IsValid.Should().BeFalse("repère trop long");
        validator.Validate(new CreateAttendanceSessionCommand("X", (AttendanceAudience)99, EveryDay, null)).IsValid.Should().BeFalse("public inconnu");
    }

    [Fact]
    public void The_Offered_Templates_Cover_The_Six_Usual_Rolls_And_Are_Valid_Sessions()
    {
        AttendanceSessionTemplates.All.Select(t => t.Name).Should().Equal(
            "Appel de nuit", "Appel du matin", "Petit-déjeuner", "Déjeuner", "Dîner", "Appel de la Halqa");

        var validator = new CreateAttendanceSessionCommandValidator();
        foreach (var template in AttendanceSessionTemplates.All)
        {
            validator.Validate(new CreateAttendanceSessionCommand(template.Name, template.Audience, EveryDay, template.TimeLabel)).IsValid.Should().BeTrue(template.Name);
        }

        AttendanceSessionTemplates.All.Single(t => t.Code == "halqa").Audience.Should().Be(AttendanceAudience.Halqa);
    }

    // ------------------------------------------------------------------ appel de nuit d'un pavillon (routes d'origine et sessions d'internes)

    [Fact]
    public async Task The_Night_Roll_Without_Session_Creates_The_Default_One_And_Stores_Student_And_Session()
    {
        (await ListAsync()).Should().BeEmpty();

        var result = await RecordNightAsync(Night, PavA, null, "m1", "m2");

        result.Saved.Should().Be(2);
        var created = (await ListAsync()).Single();
        created.Should().Match<AttendanceSessionDto>(s => s.Name == "Appel de nuit" && s.Audience == AttendanceAudience.Interns && s.Days.Count == 7);
        result.SessionId.Should().Be(created.Id);

        var rows = await RowsAsync();
        rows.Should().OnlyContain(r => r.SessionId == created.Id);
        rows.Select(r => r.StudentId).Should().BeEquivalentTo([_students["m1"], _students["m2"]]);
        rows.Select(r => r.BoardingEnrollmentId).Should().BeEquivalentTo([Stays["m1"], Stays["m2"]]);

        // La même session sert ensuite : pas de doublon « Appel de nuit ».
        await RecordNightAsync(Night.AddDays(-1), PavA, null, "m1");
        (await ListAsync()).Should().HaveCount(1);
    }

    [Fact]
    public async Task Two_Intern_Sessions_Hold_Independent_Rolls_For_The_Same_Night()
    {
        var night = await CreateAsync("Appel de nuit", AttendanceAudience.Interns);
        var morning = await CreateAsync("Appel du matin", AttendanceAudience.Interns);

        await RecordNightAsync(Night, PavA, night.Id, "m1", "m2");
        await RecordNightAsync(Night, PavA, morning.Id, "m1");

        (await RowsAsync(night.Id)).Should().HaveCount(2);
        (await RowsAsync(morning.Id)).Should().HaveCount(1);

        await using var ctx = Db.NewAppContext(EcoleA);
        var sheet = await new GetAttendanceSheetQueryHandler(ctx, Clock).Handle(new GetAttendanceSheetQuery(PavA, Night, morning.Id), CancellationToken.None);
        sheet.SessionName.Should().Be("Appel du matin");
        sheet.Lines.Single(l => l.BoarderId == Stays["m1"]).Recorded.Should().BeTrue();
        sheet.Lines.Single(l => l.BoarderId == Stays["m2"]).Recorded.Should().BeFalse("m2 n'a pas été pointé le matin");

        // Sans session : la première session des internes (l'appel de nuit) fait foi.
        var defaultSheet = await new GetAttendanceSheetQueryHandler(ctx, Clock).Handle(new GetAttendanceSheetQuery(PavA, Night), CancellationToken.None);
        defaultSheet.SessionName.Should().Be("Appel de nuit");
        defaultSheet.Lines.Count(l => l.Recorded).Should().Be(2);
    }

    [Fact]
    public async Task A_Pavilion_Sheet_Refuses_A_Group_Session_And_A_Group_Sheet_Refuses_An_Intern_Session()
    {
        var meal = await CreateAsync("Déjeuner", AttendanceAudience.AllBoarders);
        var night = await CreateAsync("Appel de nuit", AttendanceAudience.Interns);

        var pavilionWithMeal = async () => await RecordNightAsync(Night, PavA, meal.Id, "m1");
        await pavilionWithMeal.Should().ThrowAsync<ValidationException>();

        var groupWithNight = async () => await GroupSheetAsync(night.Id, _classe, Night);
        await groupWithNight.Should().ThrowAsync<ValidationException>();

        var unknown = async () => await GroupSheetAsync(Guid.NewGuid(), _classe, Night);
        await unknown.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task A_Deactivated_Session_Stays_Readable_But_Takes_No_New_Roll()
    {
        var night = await CreateAsync("Appel de nuit", AttendanceAudience.Interns, active: false);

        var record = async () => await RecordNightAsync(Night, PavA, night.Id, "m1");
        (await record.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("ATTENDANCE_SESSION_INACTIVE");

        var meal = await CreateAsync("Déjeuner", AttendanceAudience.AllBoarders, active: false);
        var recordMeal = async () => await RecordGroupAsync(meal.Id, _classe, Night, [In("m1", true)]);
        (await recordMeal.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("ATTENDANCE_SESSION_INACTIVE");

        (await GroupSheetAsync(meal.Id, _classe, Night)).Lines.Should().NotBeEmpty("la feuille reste lisible");
        (await RowsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task A_Day_Outside_The_Session_Days_Is_Flagged_But_Is_Still_Pointable()
    {
        var weekdays = await CreateAsync("Déjeuner", AttendanceAudience.AllBoarders, days: [1, 2, 3, 4, 5]);

        var sunday = await GroupSheetAsync(weekdays.Id, _classe, Night);                   // dimanche 11/10
        var monday = await GroupSheetAsync(weekdays.Id, _classe, new DateOnly(2026, 10, 12)); // lundi 12/10

        sunday.OutsideSessionDays.Should().BeTrue();
        monday.OutsideSessionDays.Should().BeFalse();

        (await RecordGroupAsync(weekdays.Id, _classe, Night, [In("m1", true)])).Saved.Should().Be(1, "le jour hors sélection n'est pas bloquant");
    }

    // ------------------------------------------------------------------ feuilles de groupe : classe

    [Fact]
    public async Task A_Meal_For_All_Boarders_Lists_The_Active_Interns_And_Half_Boarders_Of_The_Class()
    {
        var meal = await CreateAsync("Déjeuner", AttendanceAudience.AllBoarders);

        var sheet = await GroupSheetAsync(meal.Id, _classe, Night);

        sheet.GroupName.Should().Be("CM2");
        sheet.Lines.Select(l => l.Matricule).Should().BeEquivalentTo(["M-m1", "M-m2", "M-m3", "M-m4", "M-attente", "M-demi"],
            "le séjour clos et l'élève de l'autre école n'y sont pas");
        sheet.Lines.Single(l => l.Matricule == "M-demi").Detail.Should().Be("Demi-pensionnaire");
        sheet.Lines.Single(l => l.Matricule == "M-m1").Detail.Should().Be("Interne");
        sheet.Lines.Should().OnlyContain(l => !l.Recorded && l.IsPresent == null);
    }

    [Fact]
    public async Task A_Half_Boarders_Session_Lists_Only_The_Half_Boarders_Without_A_Regime_Label()
    {
        var lunch = await CreateAsync("Déjeuner demi-pension", AttendanceAudience.HalfBoarders);

        var sheet = await GroupSheetAsync(lunch.Id, _classe, Night);

        sheet.Lines.Select(l => l.Matricule).Should().Equal("M-demi");
        sheet.Lines.Single().Detail.Should().BeNull();
    }

    [Fact]
    public async Task A_Boarder_On_Leave_Is_Prefilled_Absent_And_Cannot_Be_Pointed_Present_At_A_Meal()
    {
        var meal = await CreateAsync("Déjeuner", AttendanceAudience.AllBoarders);
        await AddLeaveAsync("m1", new DateOnly(2026, 10, 10));

        var sheet = await GroupSheetAsync(meal.Id, _classe, Night);
        sheet.Lines.Single(l => l.Matricule == "M-m1").Should().Match<SessionSheetLineDto>(l =>
            l.OnLeave && !l.Recorded && l.IsPresent == false && l.Note == "En permission");

        var present = await RecordGroupAsync(meal.Id, _classe, Night, [In("m1", true)]);
        present.Lines.Single().Should().Match<SessionLineResult>(l => l.Outcome == "Rejected" && l.Code == "BOARDER_ON_LEAVE");

        var absent = await RecordGroupAsync(meal.Id, _classe, Night, [In("m1", false)]);
        absent.Saved.Should().Be(1);
        (await RowsAsync(meal.Id)).Single().Note.Should().Be("En permission");
    }

    [Fact]
    public async Task Recording_A_Class_Upserts_On_Replay_And_Rejects_Strangers_And_Duplicates_Line_By_Line()
    {
        var meal = await CreateAsync("Déjeuner", AttendanceAudience.AllBoarders);

        var first = await RecordGroupAsync(meal.Id, _classe, Night, [In("m1", true), In("m2", false, "Malade")]);
        first.Should().Match<RecordSessionAttendanceResult>(r => r.Saved == 2 && r.Rejected == 0);

        var replay = await RecordGroupAsync(meal.Id, _classe, Night,
        [
            In("m1", false, "Parti tard"),
            In("m2", true),
            In("m2", false),                                          // doublon : la 1ʳᵉ occurrence l'emporte
            In("clos", true),                                         // séjour clos : hors du groupe
            new SessionEntryInput(Guid.NewGuid(), true, null),        // élève inconnu
            In("autre", true)                                         // élève de l'école B
        ]);

        (replay.Saved, replay.Rejected).Should().Be((2, 4));
        replay.Lines.Where(l => l.Outcome == "Rejected").Select(l => l.Code)
            .Should().BeEquivalentTo(["DUPLICATE_ENTRY", "NOT_IN_GROUP", "NOT_IN_GROUP", "NOT_IN_GROUP"]);

        var rows = await RowsAsync(meal.Id);
        rows.Should().HaveCount(2, "un pointage par élève, session et jour : la saisie corrigée met à jour la ligne");
        rows.Single(r => r.StudentId == _students["m1"]).Should().Match<BoardingAttendance>(r => !r.IsPresent && r.Note == "Parti tard");
        rows.Single(r => r.StudentId == _students["m2"]).IsPresent.Should().BeTrue();
    }

    [Fact]
    public async Task The_Same_Student_Is_Pointed_Independently_For_Two_Sessions_The_Same_Day()
    {
        var lunch = await CreateAsync("Déjeuner", AttendanceAudience.AllBoarders);
        var dinner = await CreateAsync("Dîner", AttendanceAudience.AllBoarders);

        await RecordGroupAsync(lunch.Id, _classe, Night, [In("m1", true)]);
        await RecordGroupAsync(dinner.Id, _classe, Night, [In("m1", false, "Absent au dîner")]);

        (await RowsAsync(lunch.Id)).Single().IsPresent.Should().BeTrue();
        (await RowsAsync(dinner.Id)).Single().IsPresent.Should().BeFalse();
        (await GroupSheetAsync(lunch.Id, _classe, Night)).Lines.Count(l => l.Recorded).Should().Be(1);
    }

    [Fact]
    public async Task A_Group_Roll_Refuses_A_Future_Date_And_A_Date_Outside_The_Active_Year()
    {
        var meal = await CreateAsync("Déjeuner", AttendanceAudience.AllBoarders);

        var future = async () => await RecordGroupAsync(meal.Id, _classe, new DateOnly(2026, 10, 13), [In("m1", true)]);
        var beforeYear = async () => await GroupSheetAsync(meal.Id, _classe, new DateOnly(2026, 8, 31));
        var unknownClass = async () => await GroupSheetAsync(meal.Id, Guid.NewGuid(), Night);
        var noGroup = async () => await GroupSheetAsync(meal.Id, null, Night);

        await future.Should().ThrowAsync<ValidationException>();
        await beforeYear.Should().ThrowAsync<ValidationException>();
        await unknownClass.Should().ThrowAsync<KeyNotFoundException>();
        await noGroup.Should().ThrowAsync<ValidationException>("la Direction doit désigner la classe");
        (await RowsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task A_Group_Sheet_Of_Another_School_Does_Not_Exist()
    {
        var meal = await CreateAsync("Déjeuner", AttendanceAudience.AllBoarders);

        var foreign = async () => await GroupSheetAsync(meal.Id, _classe, Night, school: EcoleB);

        await foreign.Should().ThrowAsync<KeyNotFoundException>("la session de l'école A n'existe pas pour l'école B");
    }

    // ------------------------------------------------------------------ appel de la Halqa

    [Fact]
    public async Task The_Halqa_Roll_Lists_The_Students_Of_The_Oustaz_Pointed_Or_Not_Boarders_And_Without_A_Stay()
    {
        var halqa = await CreateAsync("Appel de la Halqa", AttendanceAudience.Halqa);

        var sheet = await GroupSheetAsync(halqa.Id, Oustaz1, Night);

        sheet.GroupName.Should().Be("Oustaz Un");
        sheet.Lines.Select(l => l.Matricule).Should().BeEquivalentTo(["M-m1", "M-m2", "M-daara"]);

        var result = await RecordGroupAsync(halqa.Id, Oustaz1, Night, [In("m1", true), In("daara", false, "Absent")]);

        result.Saved.Should().Be(2);
        var rows = await RowsAsync(halqa.Id);
        rows.Should().OnlyContain(r => r.BoardingEnrollmentId == null, "une session de Halqa ne passe pas par un séjour, même pour un pensionnaire");
        (await RecordGroupAsync(halqa.Id, Oustaz1, Night, [In("m3", true)])).Lines.Single().Code.Should().Be("NOT_IN_GROUP", "m3 est dans l'autre Halqa");
    }

    [Fact]
    public async Task An_Oustaz_Rolls_Only_His_Own_Halqa_And_Never_A_Class_Session()
    {
        var halqa = await CreateAsync("Appel de la Halqa", AttendanceAudience.Halqa);
        var meal = await CreateAsync("Déjeuner", AttendanceAudience.AllBoarders);
        var ahmad = Oustaz(UserOustaz1);

        // Groupe omis : sa propre Halqa.
        (await GroupSheetAsync(halqa.Id, null, Night, ahmad)).GroupName.Should().Be("Oustaz Un");
        (await RecordGroupAsync(halqa.Id, null, Night, [In("m1", true)], ahmad)).Saved.Should().Be(1);
        (await RecordGroupAsync(halqa.Id, Oustaz1, Night, [In("m2", true)], ahmad)).Saved.Should().Be(1);

        var other = async () => await GroupSheetAsync(halqa.Id, Oustaz2, Night, ahmad);
        await other.Should().ThrowAsync<ForbiddenException>("une autre Halqa que la sienne");
        var otherWrite = async () => await RecordGroupAsync(halqa.Id, Oustaz2, Night, [In("m3", true)], ahmad);
        await otherWrite.Should().ThrowAsync<ForbiddenException>();
        var classSession = async () => await GroupSheetAsync(meal.Id, _classe, Night, ahmad);
        await classSession.Should().ThrowAsync<ForbiddenException>("seule la Direction pointe une session de classe");

        // La Direction, elle, désigne l'Oustaz.
        var noGroup = async () => await GroupSheetAsync(halqa.Id, null, Night);
        await noGroup.Should().ThrowAsync<ValidationException>();
        (await GroupSheetAsync(halqa.Id, Oustaz2, Night)).Lines.Select(l => l.Matricule).Should().Equal("M-m3");
    }

    [Fact]
    public async Task An_Oustaz_Without_A_Profile_Or_Suspended_Cannot_Roll()
    {
        var halqa = await CreateAsync("Appel de la Halqa", AttendanceAudience.Halqa);

        var noProfile = async () => await GroupSheetAsync(halqa.Id, null, Night, Oustaz(Guid.NewGuid()));
        await noProfile.Should().ThrowAsync<ForbiddenException>();

        await using (var owner = Db.NewOwnerContext())
        {
            var instructor = await owner.Instructors.IgnoreQueryFilters().FirstAsync(i => i.Id == Oustaz1);
            instructor.Status = EntityStatus.Suspended;
            await owner.SaveChangesAsync();
        }

        var suspended = async () => await RecordGroupAsync(halqa.Id, null, Night, [In("m1", true)], Oustaz(UserOustaz1));
        await suspended.Should().ThrowAsync<ForbiddenException>();
    }

    // ------------------------------------------------------------------ groupes proposés par session

    private async Task<IReadOnlyList<AttendanceGroupDto>> GroupsAsync(Guid session, Guid? school = null)
    {
        await using var ctx = Db.NewAppContext(school ?? EcoleA);
        return await new ListAttendanceGroupsQueryHandler(ctx).Handle(new ListAttendanceGroupsQuery(session), CancellationToken.None);
    }

    [Fact]
    public async Task The_Groups_Offered_Follow_The_Audience_Of_The_Session_With_Their_Student_Counts()
    {
        var night = await CreateAsync("Appel de nuit", AttendanceAudience.Interns);
        var meal = await CreateAsync("Déjeuner", AttendanceAudience.AllBoarders);
        var half = await CreateAsync("Déjeuner demi-pension", AttendanceAudience.HalfBoarders);
        var halqa = await CreateAsync("Appel de la Halqa", AttendanceAudience.Halqa);

        (await GroupsAsync(night.Id)).Select(g => (g.Name, g.StudentCount)).Should().Equal(
            ("Pavillon Oustaz Ahmad", 3), ("Pavillon Oustaz Bamba", 1));
        (await GroupsAsync(meal.Id)).Select(g => (g.Id, g.Name, g.StudentCount)).Should().Equal(
            (_classe, "CM2", 6));
        (await GroupsAsync(half.Id)).Select(g => (g.Name, g.StudentCount)).Should().Equal(("CM2", 1));
        (await GroupsAsync(halqa.Id)).Select(g => (g.Id, g.StudentCount)).Should().Equal((Oustaz2, 1), (Oustaz1, 3));
    }

    [Fact]
    public async Task A_Suspended_Oustaz_And_A_Class_Without_Boarders_Are_Not_Offered_And_Other_Schools_Stay_Out()
    {
        var halqa = await CreateAsync("Appel de la Halqa", AttendanceAudience.Halqa);
        var half = await CreateAsync("Déjeuner demi-pension", AttendanceAudience.HalfBoarders);

        await using (var owner = Db.NewOwnerContext())
        {
            (await owner.Instructors.IgnoreQueryFilters().FirstAsync(i => i.Id == Oustaz2)).Status = EntityStatus.Suspended;
            // Le seul demi-pensionnaire quitte l'internat : la classe n'a plus rien à pointer pour cette session.
            (await owner.BoardingEnrollments.IgnoreQueryFilters().FirstAsync(b => b.Id == Stays["demi"])).IsActive = false;
            (await owner.BoardingEnrollments.IgnoreQueryFilters().FirstAsync(b => b.Id == Stays["demi"])).EndDate = new DateOnly(2026, 10, 1);
            await owner.SaveChangesAsync();
        }

        (await GroupsAsync(halqa.Id)).Select(g => g.Id).Should().Equal(Oustaz1);
        (await GroupsAsync(half.Id)).Should().BeEmpty();

        var foreign = async () => await GroupsAsync(halqa.Id, EcoleB);
        await foreign.Should().ThrowAsync<KeyNotFoundException>("la session de l'école A n'existe pas pour l'école B");
    }

    // ------------------------------------------------------------------ récapitulatif mensuel

    private static readonly DateOnly October = new(2026, 10, 1);

    [Fact]
    public async Task The_Monthly_Summary_Counts_Present_Absent_On_Leave_And_Not_Recorded_Per_Student()
    {
        var meal = await CreateAsync("Déjeuner", AttendanceAudience.AllBoarders);
        await AddLeaveAsync("m2", new DateOnly(2026, 10, 5), returned: new DateOnly(2026, 10, 7));   // en sortie les nuits du 5 et du 6

        await RecordGroupAsync(meal.Id, _classe, new DateOnly(2026, 10, 5), [In("m1", true), In("m2", false)]);
        await RecordGroupAsync(meal.Id, _classe, new DateOnly(2026, 10, 6), [In("m1", false, "Malade"), In("m2", false, "En permission — famille")]);
        await RecordGroupAsync(meal.Id, _classe, new DateOnly(2026, 10, 7), [In("m1", true)]);

        var summary = await SummaryAsync(meal.Id, _classe, October);

        summary.Days.Should().Equal(new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 6), new DateOnly(2026, 10, 7));
        summary.GroupName.Should().Be("CM2");
        var m1 = summary.Rows.Single(r => r.Matricule == "M-m1");
        (m1.Present, m1.Absent, m1.OnLeave, m1.NotRecorded).Should().Be((2, 1, 0, 0));
        var m2 = summary.Rows.Single(r => r.Matricule == "M-m2");
        (m2.Present, m2.Absent, m2.OnLeave, m2.NotRecorded).Should().Be((0, 0, 2, 1), "deux jours en permission, un jour non pointé");
        var m3 = summary.Rows.Single(r => r.Matricule == "M-m3");
        (m3.Present, m3.Absent, m3.OnLeave, m3.NotRecorded).Should().Be((0, 0, 0, 3), "jamais pointé : trois jours non pointés");

        summary.Rows.Should().OnlyContain(r => r.Present + r.Absent + r.OnLeave + r.NotRecorded == summary.Days.Count);
    }

    [Fact]
    public async Task The_Summary_Ignores_Other_Months_Other_Sessions_And_Other_Groups()
    {
        var lunch = await CreateAsync("Déjeuner", AttendanceAudience.AllBoarders);
        var dinner = await CreateAsync("Dîner", AttendanceAudience.AllBoarders);

        await RecordGroupAsync(lunch.Id, _classe, new DateOnly(2026, 10, 5), [In("m1", true)]);
        await RecordGroupAsync(lunch.Id, _classe, new DateOnly(2026, 9, 28), [In("m1", true)]);   // septembre
        await RecordGroupAsync(dinner.Id, _classe, new DateOnly(2026, 10, 5), [In("m1", false)]); // autre session

        var october = await SummaryAsync(lunch.Id, _classe, October);
        october.Days.Should().Equal(new DateOnly(2026, 10, 5));
        october.Rows.Single(r => r.Matricule == "M-m1").Present.Should().Be(1);

        var september = await SummaryAsync(lunch.Id, _classe, new DateOnly(2026, 9, 1));
        september.Days.Should().Equal(new DateOnly(2026, 9, 28));

        var empty = await SummaryAsync(lunch.Id, _classe, new DateOnly(2026, 11, 1));
        empty.Days.Should().BeEmpty();
        empty.Rows.Should().OnlyContain(r => r.Present == 0 && r.Absent == 0 && r.OnLeave == 0 && r.NotRecorded == 0);
    }

    [Fact]
    public async Task The_Interns_Summary_Is_Per_Dormitory_With_Room_And_Bed()
    {
        var night = await CreateAsync("Appel de nuit", AttendanceAudience.Interns);
        await RecordNightAsync(new DateOnly(2026, 10, 5), PavA, night.Id, "m1", "m2");
        await RecordNightAsync(new DateOnly(2026, 10, 6), PavA, night.Id, "m1");

        var summary = await SummaryAsync(night.Id, PavA, October);

        summary.GroupName.Should().Be("Pavillon Oustaz Ahmad");
        summary.Rows.Select(r => r.Matricule).Should().Equal("M-m1", "M-m2", "M-m3");
        summary.Rows.First().Detail.Should().Be("Chambre 101 · lit 1");
        summary.Rows.Single(r => r.Matricule == "M-m1").Present.Should().Be(2);
        summary.Rows.Single(r => r.Matricule == "M-m2").NotRecorded.Should().Be(1);
        (await SummaryAsync(night.Id, PavA2, October)).Rows.Select(r => r.Matricule).Should().Equal("M-m4");
    }

    [Fact]
    public async Task An_Oustaz_Reads_The_Summary_Of_His_Halqa_Only()
    {
        var halqa = await CreateAsync("Appel de la Halqa", AttendanceAudience.Halqa);
        var meal = await CreateAsync("Déjeuner", AttendanceAudience.AllBoarders);
        await RecordGroupAsync(halqa.Id, Oustaz1, new DateOnly(2026, 10, 5), [In("m1", true), In("daara", false)]);
        var ahmad = Oustaz(UserOustaz1);

        var own = await SummaryAsync(halqa.Id, null, October, ahmad);
        own.Rows.Select(r => r.Matricule).Should().BeEquivalentTo(["M-m1", "M-m2", "M-daara"]);
        own.Rows.Single(r => r.Matricule == "M-daara").Absent.Should().Be(1);

        var other = async () => await SummaryAsync(halqa.Id, Oustaz2, October, ahmad);
        await other.Should().ThrowAsync<ForbiddenException>();
        var classSession = async () => await SummaryAsync(meal.Id, _classe, October, ahmad);
        await classSession.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public void The_Month_Parameter_Is_Read_As_Year_Month_Or_Refused()
    {
        AttendanceMonth.Parse("2026-10").Should().Be(new DateOnly(2026, 10, 1));
        AttendanceMonth.Parse("2027-02").Should().Be(new DateOnly(2027, 2, 1));

        foreach (var bad in new[] { null, "", "2026", "2026-13", "octobre", "2026-10-05", "26-10" })
        {
            var parse = () => AttendanceMonth.Parse(bad);
            parse.Should().Throw<ValidationException>(bad ?? "null");
        }

        var validator = new GetMonthlyAttendanceSummaryQueryValidator();
        validator.Validate(new GetMonthlyAttendanceSummaryQuery(new DateOnly(2026, 10, 1), Guid.NewGuid(), null)).IsValid.Should().BeTrue();
        validator.Validate(new GetMonthlyAttendanceSummaryQuery(new DateOnly(2026, 10, 5), Guid.NewGuid(), null)).IsValid.Should().BeFalse();
    }
}
