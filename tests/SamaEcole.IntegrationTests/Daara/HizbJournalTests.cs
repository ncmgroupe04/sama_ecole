using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Internat;
using SamaEcole.Application.Internat.Commands.UpdateHizbProgress;
using SamaEcole.Application.Internat.Queries.GetProgressHistory;
using SamaEcole.Application.Internat.Queries.GetStudentHizbHistory;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using SamaEcole.Persistence;
using Xunit;

namespace SamaEcole.IntegrationTests.Daara;

/// <summary>
/// Journal des évaluations Hizb sur un vrai PostgreSQL sous le rôle applicatif (RLS active) : chaque saisie qui change quelque chose
/// écrit une ligne dans la MÊME transaction, le journal est append-only (le rôle applicatif ne peut ni modifier ni supprimer), les
/// courbes se lisent dans le journal, la portée d'un Oustaz tient, et la migration reprend les états existants (puis se défait).
/// </summary>
[Trait("Category", "MultiTenant")]
public class HizbJournalTests : IAsyncLifetime
{
    private const string PreviousMigration = "20261007090741_ReconcileBoardingWithLegacyModel";

    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("e1111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("e2222222-2222-2222-2222-222222222222");
    private static readonly Guid ClasseA = Guid.Parse("e1111111-0000-0000-0000-0000000000c1");
    private static readonly Guid ClasseB = Guid.Parse("e2222222-0000-0000-0000-0000000000c1");

    private static readonly Guid UserDirecteur = Guid.Parse("e1111111-0000-0000-0000-0000000000a1");
    private static readonly Guid UserOustaz1 = Guid.Parse("e1111111-0000-0000-0000-0000000000a2");
    private static readonly Guid UserOustaz2 = Guid.Parse("e1111111-0000-0000-0000-0000000000a3");

    private static readonly Guid Oustaz1 = Guid.Parse("e1111111-0000-0000-0000-0000000000f1");
    private static readonly Guid Oustaz2 = Guid.Parse("e1111111-0000-0000-0000-0000000000f2");
    private static readonly Guid OustazB = Guid.Parse("e2222222-0000-0000-0000-0000000000f1");

    private static readonly Guid Eleve1 = Guid.Parse("e1111111-0000-0000-0000-0000000000e1");   // Halqa de Oustaz1
    private static readonly Guid Eleve2 = Guid.Parse("e1111111-0000-0000-0000-0000000000e2");   // Halqa de Oustaz1
    private static readonly Guid Eleve3 = Guid.Parse("e1111111-0000-0000-0000-0000000000e3");   // Halqa de Oustaz2
    private static readonly Guid EleveLibre = Guid.Parse("e1111111-0000-0000-0000-0000000000e4"); // sans Oustaz
    private static readonly Guid EleveB = Guid.Parse("e2222222-0000-0000-0000-0000000000e1");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await SeedAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private async Task SeedAsync()
    {
        await using var owner = _db.NewOwnerContext();
        owner.Schools.AddRange(new School { Id = EcoleA, Name = "Daara A" }, new School { Id = EcoleB, Name = "Daara B" });
        owner.Classrooms.AddRange(
            new Classroom { Id = ClasseA, SchoolId = EcoleA, Name = "Halqa A", Level = "Daara", Cycle = CycleType.Primaire, Capacity = 40 },
            new Classroom { Id = ClasseB, SchoolId = EcoleB, Name = "Halqa B", Level = "Daara", Cycle = CycleType.Primaire, Capacity = 40 });
        owner.Users.AddRange(
            NewUser(UserDirecteur, "directeur@daara-a.sn", Role.Directeur),
            NewUser(UserOustaz1, "oustaz1@daara-a.sn", Role.Enseignant),
            NewUser(UserOustaz2, "oustaz2@daara-a.sn", Role.Enseignant));
        await owner.SaveChangesAsync();

        owner.Instructors.AddRange(
            new Instructor { Id = Oustaz1, SchoolId = EcoleA, FullName = "Oustaz Un", UserId = UserOustaz1 },
            new Instructor { Id = Oustaz2, SchoolId = EcoleA, FullName = "Oustaz Deux", UserId = UserOustaz2 },
            new Instructor { Id = OustazB, SchoolId = EcoleB, FullName = "Oustaz B" });
        await owner.SaveChangesAsync();

        owner.Students.AddRange(
            NewStudent(Eleve1, EcoleA, ClasseA, "Awa Un", "ELEV-E1", Oustaz1),
            NewStudent(Eleve2, EcoleA, ClasseA, "Binta Un", "ELEV-E2", Oustaz1),
            NewStudent(Eleve3, EcoleA, ClasseA, "Cheikh Deux", "ELEV-E3", Oustaz2),
            NewStudent(EleveLibre, EcoleA, ClasseA, "Demba Libre", "ELEV-E4", null),
            NewStudent(EleveB, EcoleB, ClasseB, "Élève B", "ELEV-B1", OustazB));
        await owner.SaveChangesAsync();
    }

    private static User NewUser(Guid id, string email, Role role) =>
        new() { Id = id, SchoolId = EcoleA, Email = email, PasswordHash = "x", FullName = email, Role = role };

    private static Student NewStudent(Guid id, Guid school, Guid classroom, string name, string matricule, Guid? instructor) =>
        new()
        {
            Id = id, SchoolId = school, Matricule = matricule, FullName = name, BirthDate = new DateOnly(2012, 1, 1),
            BirthPlace = "Touba", Gender = "M", ClassroomId = classroom, InstructorId = instructor
        };

    // ------------------------------------------------------------------ outillage

    private sealed class StubTenantProvider(Guid? schoolId) : ITenantProvider
    {
        public Guid? CurrentSchoolId => schoolId;
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static DateTimeOffset At(int year, int month, int day, int hour = 10) => new(year, month, day, hour, 0, 0, TimeSpan.Zero);

    /// <summary>Saisit un Hizb comme le ferait l'écran (jeton xmin relu à chaque fois) et renvoie la case enregistrée.</summary>
    private async Task<HizbCellDto> SaveAsync(
        Guid student, int hizb, int quarters, int? rating, uint? rowVersion, Guid userId, Role role, DateTimeOffset now)
    {
        await using var db = _db.NewAppContext(EcoleA);
        var user = new TestCurrentUser(userId, role);
        var handler = new UpdateHizbProgressCommandHandler(
            db, new StubTenantProvider(EcoleA), new HalqaScopeAuthorizer(db, user), new FixedTime(now), user);
        return await handler.Handle(new UpdateHizbProgressCommand(student, hizb, quarters, rating, rowVersion), CancellationToken.None);
    }

    private Task<HizbCellDto> AsDirecteurAsync(Guid student, int hizb, int quarters, int? rating, uint? rowVersion, DateTimeOffset now) =>
        SaveAsync(student, hizb, quarters, rating, rowVersion, UserDirecteur, Role.Directeur, now);

    private async Task<List<StudentHizbEvaluation>> JournalAsync(Guid student)
    {
        await using var db = _db.NewAppContext(EcoleA);
        return await db.StudentHizbEvaluations.AsNoTracking().Where(e => e.StudentId == student).OrderBy(e => e.EvaluatedAt).ToListAsync();
    }

    // ------------------------------------------------------------------ écriture du journal

    [Fact]
    public async Task A_First_Save_Writes_One_Journal_Row_With_Who_When_And_The_Students_Total()
    {
        await SaveAsync(Eleve1, 1, 3, 4, null, UserOustaz1, Role.Enseignant, At(2026, 10, 1));

        var rows = await JournalAsync(Eleve1);

        var row = rows.Should().ContainSingle().Subject;
        row.HizbNumber.Should().Be(1);
        row.QuartersBefore.Should().Be(0);
        row.QuartersAfter.Should().Be(3);
        row.Rating.Should().Be(4);
        row.TotalQuartersAfter.Should().Be(3);
        row.EvaluatedAt.Should().Be(At(2026, 10, 1));
        row.EvaluatedByUserId.Should().Be(UserOustaz1);
        row.InstructorId.Should().Be(Oustaz1, "l'Oustaz de l'élève AU MOMENT de la saisie");
        row.IsBaseline.Should().BeFalse();
    }

    [Fact]
    public async Task The_Total_Accumulates_Across_Hizb_And_Follows_A_Correction_Downwards()
    {
        var h1 = await AsDirecteurAsync(Eleve1, 1, 4, 5, null, At(2026, 10, 1));
        var h2 = await AsDirecteurAsync(Eleve1, 2, 2, 3, null, At(2026, 10, 2));
        await AsDirecteurAsync(Eleve1, 1, 1, 2, h1.RowVersion, At(2026, 10, 3));   // correction : 4 → 1 quart

        var rows = await JournalAsync(Eleve1);

        rows.Select(r => r.TotalQuartersAfter).Should().Equal(4, 6, 3);
        rows.Select(r => (r.QuartersBefore, r.QuartersAfter)).Should().Equal((0, 4), (0, 2), (4, 1));
        h2.RowVersion.Should().NotBeNull();
    }

    [Fact]
    public async Task Saving_The_Same_Values_Again_Writes_Nothing_But_Changing_Only_The_Rating_Does()
    {
        var first = await AsDirecteurAsync(Eleve1, 1, 2, 3, null, At(2026, 10, 1));

        var same = await AsDirecteurAsync(Eleve1, 1, 2, 3, first.RowVersion, At(2026, 10, 2));
        (await JournalAsync(Eleve1)).Should().HaveCount(1, "rien n'a changé : pas de doublon dans le journal");

        await AsDirecteurAsync(Eleve1, 1, 2, 5, same.RowVersion, At(2026, 10, 3));
        var rows = await JournalAsync(Eleve1);

        rows.Should().HaveCount(2);
        rows[1].Rating.Should().Be(5);
        rows[1].QuartersBefore.Should().Be(2);
        rows[1].QuartersAfter.Should().Be(2);
    }

    [Fact]
    public async Task Bringing_A_Hizb_Back_To_Zero_Is_Journalized_Without_A_Rating()
    {
        var first = await AsDirecteurAsync(Eleve1, 1, 3, 4, null, At(2026, 10, 1));

        await AsDirecteurAsync(Eleve1, 1, 0, null, first.RowVersion, At(2026, 10, 2));

        var rows = await JournalAsync(Eleve1);
        rows[1].QuartersAfter.Should().Be(0);
        rows[1].Rating.Should().BeNull();
        rows[1].TotalQuartersAfter.Should().Be(0);
    }

    [Fact]
    public async Task A_Rejected_Save_Leaves_No_Journal_Row()
    {
        var first = await AsDirecteurAsync(Eleve1, 1, 2, 3, null, At(2026, 10, 1));

        // Jeton périmé : la saisie est refusée en 409 et ne laisse AUCUNE ligne (même transaction que l'état courant).
        var stale = () => AsDirecteurAsync(Eleve1, 1, 4, 5, first.RowVersion + 1, At(2026, 10, 2));

        await stale.Should().ThrowAsync<ConcurrencyConflictException>();
        (await JournalAsync(Eleve1)).Should().HaveCount(1);
    }

    // ------------------------------------------------------------------ append-only et isolation

    [Theory]
    [InlineData("UPDATE student_hizb_evaluations SET \"Rating\" = 1")]
    [InlineData("DELETE FROM student_hizb_evaluations")]
    public async Task The_Application_Role_Can_Neither_Rewrite_Nor_Erase_The_Journal(string sql)
    {
        await AsDirecteurAsync(Eleve1, 1, 2, 3, null, At(2026, 10, 1));
        await using var db = _db.NewAppContext(EcoleA);

        var rewrite = () => db.Database.ExecuteSqlRawAsync(sql);

        var ex = await rewrite.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be("42501", "permission refusée : le journal est append-only");
        (await JournalAsync(Eleve1)).Should().HaveCount(1);
    }

    [Fact]
    public async Task Each_School_Only_Sees_Its_Own_Journal_And_Cannot_Write_In_Anothers()
    {
        await AsDirecteurAsync(Eleve1, 1, 2, 3, null, At(2026, 10, 1));

        await using var other = _db.NewAppContext(EcoleB);
        (await other.StudentHizbEvaluations.CountAsync()).Should().Be(0, "la RLS isole le journal par école");

        var forged = new StudentHizbEvaluation
        {
            SchoolId = EcoleA, StudentId = Eleve1, HizbNumber = 1, QuartersBefore = 0, QuartersAfter = 1, TotalQuartersAfter = 1,
            EvaluatedAt = At(2026, 10, 2)
        };
        other.StudentHizbEvaluations.Add(forged);
        var write = () => other.SaveChangesAsync();
        await write.Should().ThrowAsync<Exception>("WITH CHECK refuse une ligne d'une autre école");
    }

    [Fact]
    public async Task The_Database_Itself_Refuses_Out_Of_Range_Values()
    {
        await using var owner = _db.NewOwnerContext();
        owner.StudentHizbEvaluations.Add(new StudentHizbEvaluation
        {
            SchoolId = EcoleA, StudentId = Eleve1, HizbNumber = 1, QuartersBefore = 0, QuartersAfter = 4, TotalQuartersAfter = 300,
            EvaluatedAt = At(2026, 10, 1)
        });

        var write = () => owner.SaveChangesAsync();

        await write.Should().ThrowAsync<DbUpdateException>("un total de 300 quarts n'existe pas (240 au plus)");
    }

    // ------------------------------------------------------------------ historique d'un élève

    private async Task<StudentHizbHistoryDto> HistoryAsync(
        Guid student, Guid userId, Role role, DateOnly? from = null, DateOnly? to = null)
    {
        await using var db = _db.NewAppContext(EcoleA);
        var handler = new GetStudentHizbHistoryQueryHandler(db, new HalqaScopeAuthorizer(db, new TestCurrentUser(userId, role)));
        return await handler.Handle(new GetStudentHizbHistoryQuery(student, from, to), CancellationToken.None);
    }

    [Fact]
    public async Task The_History_Has_One_Point_Per_Day_The_Last_Save_Of_The_Day_Wins()
    {
        var h1 = await AsDirecteurAsync(Eleve1, 1, 2, 3, null, At(2026, 10, 1, 9));
        await AsDirecteurAsync(Eleve1, 2, 3, 4, null, At(2026, 10, 1, 15));           // même jour : total 5
        await AsDirecteurAsync(Eleve1, 1, 4, 5, h1.RowVersion, At(2026, 10, 4));      // total 7

        var history = await HistoryAsync(Eleve1, UserDirecteur, Role.Directeur);

        history.Points.Select(p => (p.Date, p.TotalQuarters)).Should().Equal(
            (new DateOnly(2026, 10, 1), 5), (new DateOnly(2026, 10, 4), 7));
        history.Points[1].ProgressPercent.Should().Be(2.9m);   // 7 / 240
        history.CurrentTotalQuarters.Should().Be(7);
        history.FirstEvaluationAt.Should().Be(At(2026, 10, 1, 9));
        history.OpeningTotalQuarters.Should().BeNull("période ouverte vers le passé");
    }

    [Fact]
    public async Task A_Period_Starts_At_The_Level_The_Student_Had_Before_It()
    {
        var h1 = await AsDirecteurAsync(Eleve1, 1, 4, 5, null, At(2026, 9, 10));
        await AsDirecteurAsync(Eleve1, 2, 2, 3, null, At(2026, 10, 5));
        

        var history = await HistoryAsync(Eleve1, UserDirecteur, Role.Directeur, from: new DateOnly(2026, 10, 1), to: new DateOnly(2026, 10, 31));

        history.OpeningTotalQuarters.Should().Be(4, "la dernière saisie avant le 1er octobre");
        history.Points.Should().ContainSingle().Which.TotalQuarters.Should().Be(6);
    }

    [Fact]
    public async Task A_Period_Before_The_Journal_Began_Has_No_Opening_Level_Rather_Than_An_Invented_Zero()
    {
        await AsDirecteurAsync(Eleve1, 1, 2, 3, null, At(2026, 10, 5));

        var history = await HistoryAsync(Eleve1, UserDirecteur, Role.Directeur, from: new DateOnly(2026, 1, 1), to: new DateOnly(2026, 3, 31));

        history.OpeningTotalQuarters.Should().BeNull();
        history.Points.Should().BeEmpty();
        history.FirstEvaluationAt.Should().Be(At(2026, 10, 5), "l'écran dit d'où part l'historique");
    }

    [Fact]
    public async Task The_Recent_List_Is_Newest_First_Named_And_Bounded()
    {
        await SaveAsync(Eleve1, 1, 1, 2, null, UserOustaz1, Role.Enseignant, At(2026, 10, 1));
        for (var hizb = 2; hizb <= 25; hizb++)
        {
            await AsDirecteurAsync(Eleve1, hizb, 1, 2, null, At(2026, 10, 2).AddMinutes(hizb));
        }

        var history = await HistoryAsync(Eleve1, UserDirecteur, Role.Directeur);

        history.Recent.Should().HaveCount(GetStudentHizbHistoryQueryHandler.RecentCount);
        history.Recent[0].HizbNumber.Should().Be(25, "la plus récente d'abord");
        history.Recent[0].EvaluatedBy.Should().Be("directeur@daara-a.sn");
    }

    [Fact]
    public async Task An_Oustaz_Reads_The_History_Of_His_Own_Students_Only()
    {
        await AsDirecteurAsync(Eleve1, 1, 2, 3, null, At(2026, 10, 1));
        await AsDirecteurAsync(Eleve3, 1, 2, 3, null, At(2026, 10, 1));

        (await HistoryAsync(Eleve1, UserOustaz1, Role.Enseignant)).Points.Should().HaveCount(1);

        var other = () => HistoryAsync(Eleve3, UserOustaz1, Role.Enseignant);
        await other.Should().ThrowAsync<ForbiddenException>();
        var free = () => HistoryAsync(EleveLibre, UserOustaz1, Role.Enseignant);
        await free.Should().ThrowAsync<ForbiddenException>("un élève sans Oustaz n'est dans la Halqa de personne");
    }

    [Fact]
    public async Task The_History_Of_A_Student_Of_Another_School_Is_Not_Found()
    {
        var other = () => HistoryAsync(EleveB, UserDirecteur, Role.Directeur);

        await other.Should().ThrowAsync<KeyNotFoundException>();
    }

    // ------------------------------------------------------------------ progression mensuelle

    private async Task SeedJournalAsync(Guid student, Guid? instructor, int hizb, int total, DateTimeOffset at)
    {
        await using var owner = _db.NewOwnerContext();
        owner.StudentHizbEvaluations.Add(new StudentHizbEvaluation
        {
            SchoolId = EcoleA, StudentId = student, HizbNumber = hizb, QuartersBefore = 0, QuartersAfter = 4, TotalQuartersAfter = total,
            EvaluatedAt = at, InstructorId = instructor
        });
        await owner.SaveChangesAsync();
    }

    private async Task<ProgressHistoryDto> ProgressAsync(
        Guid userId, Role role, Guid? instructorId = null, DateOnly? from = null, DateOnly? to = null)
    {
        await using var db = _db.NewAppContext(EcoleA);
        var handler = new GetProgressHistoryQueryHandler(
            db, new HalqaScopeAuthorizer(db, new TestCurrentUser(userId, role)), new FixedTime(At(2026, 10, 15)));
        return await handler.Handle(new GetProgressHistoryQuery(from, to, instructorId), CancellationToken.None);
    }

    private async Task SeedProgressAsync()
    {
        // Eleve1 : 40 fin août, 80 fin septembre. Eleve2 : 20 en septembre, 60 en octobre. Eleve3 (autre Halqa) : jamais évalué.
        await SeedJournalAsync(Eleve1, Oustaz1, 1, 40, At(2026, 8, 10));
        await SeedJournalAsync(Eleve1, Oustaz1, 2, 80, At(2026, 9, 20));
        await SeedJournalAsync(Eleve2, Oustaz1, 1, 20, At(2026, 9, 5));
        await SeedJournalAsync(Eleve2, Oustaz1, 2, 60, At(2026, 10, 3));
    }

    [Fact]
    public async Task The_Monthly_Average_Starts_At_The_First_Journal_Entry_And_Counts_Unevaluated_Students_As_Zero()
    {
        await SeedProgressAsync();

        var result = await ProgressAsync(UserDirecteur, Role.Directeur);

        result.StudentsInScope.Should().Be(3, "les trois élèves rattachés à un Oustaz");
        result.FirstEvaluationAt.Should().Be(At(2026, 8, 10));
        result.Months.Select(m => m.MonthStart).Should().Equal(
            new DateOnly(2026, 8, 1), new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1));   // pas de mai-juillet : pas d'historique
        result.Months.Select(m => m.AverageProgressPercent).Should().Equal(5.6m, 13.9m, 19.4m);
        result.Months.Select(m => m.EvaluationCount).Should().Equal(1, 2, 1);
        result.Months.Select(m => m.StudentsEvaluated).Should().Equal(1, 2, 1);
    }

    [Fact]
    public async Task A_Halqa_Scope_Only_Averages_Its_Own_Students()
    {
        await SeedProgressAsync();

        var result = await ProgressAsync(UserDirecteur, Role.Directeur, instructorId: Oustaz1);

        result.StudentsInScope.Should().Be(2);
        result.Months.Select(m => m.AverageProgressPercent).Should().Equal(8.3m, 20.8m, 29.2m);   // /480 : 40, 100, 140
    }

    [Fact]
    public async Task An_Oustaz_Gets_His_Own_Halqa_Even_Without_Asking_And_Cannot_Ask_For_Another()
    {
        await SeedProgressAsync();

        var own = await ProgressAsync(UserOustaz1, Role.Enseignant);
        own.InstructorId.Should().Be(Oustaz1);
        own.StudentsInScope.Should().Be(2);

        var other = () => ProgressAsync(UserOustaz1, Role.Enseignant, instructorId: Oustaz2);
        await other.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task An_Empty_Journal_Gives_No_Months_And_The_Window_Is_Bounded_To_Two_Years()
    {
        var none = await ProgressAsync(UserDirecteur, Role.Directeur);
        none.Months.Should().BeEmpty();
        none.FirstEvaluationAt.Should().BeNull();

        await SeedJournalAsync(Eleve1, Oustaz1, 1, 40, At(2023, 1, 10));
        var wide = await ProgressAsync(UserDirecteur, Role.Directeur, from: new DateOnly(2020, 1, 1), to: new DateOnly(2026, 10, 15));
        wide.Months.Count.Should().BeLessThanOrEqualTo(GetProgressHistoryQueryHandler.MaxMonths);
        wide.Months.Last().MonthStart.Should().Be(new DateOnly(2026, 10, 1));
    }

    // ------------------------------------------------------------------ migration : reprise et retour arrière

    private async Task MigrateToAsync(string? target)
    {
        await using var owner = _db.NewOwnerContext();
        await owner.GetService<IMigrator>().MigrateAsync(target);
    }

    private async Task<string> ResetFunctionAsync()
    {
        await using var owner = _db.NewOwnerContext();
        var connection = owner.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT pg_get_functiondef('reset_school_data(uuid)'::regprocedure)";
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private async Task<bool> JournalTableExistsAsync()
    {
        await using var owner = _db.NewOwnerContext();
        var connection = owner.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT to_regclass('public.student_hizb_evaluations') IS NOT NULL";
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task The_Migration_Seeds_One_Baseline_Per_Existing_State_With_A_Cumulative_Total_And_Can_Be_Reverted()
    {
        await MigrateToAsync(PreviousMigration);
        (await JournalTableExistsAsync()).Should().BeFalse("la migration est reculée");

        // États courants existants : Eleve1 a deux Hizb évalués (dates distinctes), un troisième à 0 quart ; Eleve2 un seul.
        await using (var owner = _db.NewOwnerContext())
        {
            owner.StudentHizbStatuses.AddRange(
                Status(Eleve1, 1, 4, 5, At(2026, 9, 1)),
                Status(Eleve1, 2, 2, 3, At(2026, 9, 15)),
                Status(Eleve1, 3, 0, null, null),
                Status(Eleve2, 1, 3, 4, At(2026, 9, 10)));
            await owner.SaveChangesAsync();
        }

        await MigrateToAsync(null);

        await using (var owner = _db.NewOwnerContext())
        {
            var baseline = await owner.StudentHizbEvaluations.IgnoreQueryFilters().AsNoTracking()
                .OrderBy(e => e.StudentId).ThenBy(e => e.EvaluatedAt).ToListAsync();

            baseline.Should().HaveCount(3, "un état à 0 quart n'a rien à reprendre");
            baseline.Should().OnlyContain(e => e.IsBaseline && e.QuartersBefore == 0);
            var awa = baseline.Where(e => e.StudentId == Eleve1).ToList();
            // Cumul dans l'ordre des dates d'évaluation : 4, puis 4 + 2.
            awa.Select(e => e.TotalQuartersAfter).Should().Equal(4, 6);
            awa.Select(e => e.EvaluatedAt).Should().Equal(At(2026, 9, 1), At(2026, 9, 15));
            awa.Should().OnlyContain(e => e.InstructorId == Oustaz1);
            baseline.Single(e => e.StudentId == Eleve2).TotalQuartersAfter.Should().Be(3);
        }

        (await ResetFunctionAsync()).Should().Contain("student_hizb_evaluations", "la purge connaît le journal");

        // Retour arrière : la table, sa policy et la ligne de purge disparaissent.
        await MigrateToAsync(PreviousMigration);
        (await JournalTableExistsAsync()).Should().BeFalse();
        (await ResetFunctionAsync()).Should().NotContain("student_hizb_evaluations");

        // Et de nouveau en avant : la reprise se refait sans erreur.
        await MigrateToAsync(null);
        (await JournalTableExistsAsync()).Should().BeTrue();
    }

    private static StudentHizbStatus Status(Guid student, int hizb, int quarters, int? rating, DateTimeOffset? evaluatedAt) =>
        new()
        {
            SchoolId = EcoleA, StudentId = student, HizbNumber = hizb, CompletedQuarters = quarters,
            State = HizbRules.StateFor(quarters), Rating = rating, LastEvaluatedAt = evaluatedAt
        };
}
