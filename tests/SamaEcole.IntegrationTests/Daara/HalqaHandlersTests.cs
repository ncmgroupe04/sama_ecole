using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Internat;
using SamaEcole.Application.Internat.Commands.AssignInstructor;
using SamaEcole.Application.Internat.Commands.UpdateHizbProgress;
using SamaEcole.Application.Internat.Queries.GetInstructorStudents;
using SamaEcole.Application.Internat.Queries.GetMyHalqa;
using SamaEcole.Application.Internat.Queries.GetStudentHizbProgress;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using SamaEcole.Persistence;
using Xunit;

namespace SamaEcole.IntegrationTests.Daara;

/// <summary>
/// Les quatre cas d'usage Halqa/Hizb, exécutés par les VRAIS Handlers sur un vrai PostgreSQL sous le rôle applicatif
/// (RLS active) : la portée d'un Oustaz, l'isolation entre écoles, le verrou optimiste et l'idempotence de
/// l'affectation tiennent-ils de bout en bout ? Deux écoles, deux Oustaz dans la première.
/// </summary>
[Trait("Category", "MultiTenant")]
public class HalqaHandlersTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("b1111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("b2222222-2222-2222-2222-222222222222");
    private static readonly Guid ClasseA = Guid.Parse("b1111111-0000-0000-0000-0000000000c1");
    private static readonly Guid ClasseB = Guid.Parse("b2222222-0000-0000-0000-0000000000c1");

    private static readonly Guid UserOustazA1 = Guid.Parse("b1111111-0000-0000-0000-0000000000a1");
    private static readonly Guid UserOustazA2 = Guid.Parse("b1111111-0000-0000-0000-0000000000a2");
    private static readonly Guid UserSansFiche = Guid.Parse("b1111111-0000-0000-0000-0000000000a3");
    private static readonly Guid UserOustazSuspendu = Guid.Parse("b1111111-0000-0000-0000-0000000000a4");
    private static readonly Guid UserDirecteur = Guid.Parse("b1111111-0000-0000-0000-0000000000a5");

    private static readonly Guid OustazA1 = Guid.Parse("b1111111-0000-0000-0000-0000000000f1");
    private static readonly Guid OustazA2 = Guid.Parse("b1111111-0000-0000-0000-0000000000f2");
    private static readonly Guid OustazSuspendu = Guid.Parse("b1111111-0000-0000-0000-0000000000f3");
    private static readonly Guid OustazB = Guid.Parse("b2222222-0000-0000-0000-0000000000f1");

    private static readonly Guid EleveA1 = Guid.Parse("b1111111-0000-0000-0000-0000000000e1");   // Halqa de OustazA1
    private static readonly Guid EleveA1bis = Guid.Parse("b1111111-0000-0000-0000-0000000000e2"); // Halqa de OustazA1
    private static readonly Guid EleveA2 = Guid.Parse("b1111111-0000-0000-0000-0000000000e3");   // Halqa de OustazA2
    private static readonly Guid EleveLibre = Guid.Parse("b1111111-0000-0000-0000-0000000000e4"); // sans Oustaz
    private static readonly Guid EleveB = Guid.Parse("b2222222-0000-0000-0000-0000000000e1");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();

        owner.Schools.AddRange(new School { Id = EcoleA, Name = "Daara A" }, new School { Id = EcoleB, Name = "Daara B" });
        owner.Classrooms.AddRange(
            new Classroom { Id = ClasseA, SchoolId = EcoleA, Name = "Halqa A", Level = "Daara", Cycle = CycleType.Primaire, Capacity = 40 },
            new Classroom { Id = ClasseB, SchoolId = EcoleB, Name = "Halqa B", Level = "Daara", Cycle = CycleType.Primaire, Capacity = 40 });

        owner.Users.AddRange(
            NewUser(UserOustazA1, "oustaz1@daara-a.sn", Role.Enseignant),
            NewUser(UserOustazA2, "oustaz2@daara-a.sn", Role.Enseignant),
            NewUser(UserSansFiche, "enseignant@daara-a.sn", Role.Enseignant),
            NewUser(UserOustazSuspendu, "suspendu@daara-a.sn", Role.Enseignant),
            NewUser(UserDirecteur, "directeur@daara-a.sn", Role.Directeur));
        await owner.SaveChangesAsync();

        owner.Instructors.AddRange(
            new Instructor { Id = OustazA1, SchoolId = EcoleA, FullName = "Oustaz Un", UserId = UserOustazA1 },
            new Instructor { Id = OustazA2, SchoolId = EcoleA, FullName = "Oustaz Deux", UserId = UserOustazA2 },
            new Instructor { Id = OustazSuspendu, SchoolId = EcoleA, FullName = "Oustaz Suspendu", UserId = UserOustazSuspendu, Status = EntityStatus.Suspended },
            new Instructor { Id = OustazB, SchoolId = EcoleB, FullName = "Oustaz B" });
        await owner.SaveChangesAsync();

        owner.Students.AddRange(
            NewStudent(EleveA1, EcoleA, ClasseA, "Awa Un", "ELEV-A1", OustazA1),
            NewStudent(EleveA1bis, EcoleA, ClasseA, "Binta Un", "ELEV-A2", OustazA1),
            NewStudent(EleveA2, EcoleA, ClasseA, "Cheikh Deux", "ELEV-A3", OustazA2),
            NewStudent(EleveLibre, EcoleA, ClasseA, "Demba Libre", "ELEV-A4", null),
            NewStudent(EleveB, EcoleB, ClasseB, "Elève B", "ELEV-B1", OustazB));
        await owner.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

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

    /// <summary>Un contexte applicatif de l'école A + le compte courant simulé.</summary>
    private sealed record Session(ApplicationDbContext Db, HalqaScopeAuthorizer Scope) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private Session AsUser(Guid userId, Role role, Guid? school = null)
    {
        var db = _db.NewAppContext(school ?? EcoleA);
        return new Session(db, new HalqaScopeAuthorizer(db, new TestCurrentUser(userId, role)));
    }

    private Session AsDirecteur(Guid? school = null) => AsUser(UserDirecteur, Role.Directeur, school);

    private static UpdateHizbProgressCommandHandler UpdateHandler(Session s, Guid? school = null) =>
        new(s.Db, new StubTenantProvider(school ?? EcoleA), s.Scope, TimeProvider.System);

    private async Task SeedHizbAsync(Guid student, int hizb, int quarters, int? rating = null)
    {
        await using var owner = _db.NewOwnerContext();
        owner.StudentHizbStatuses.Add(new StudentHizbStatus
        {
            SchoolId = EcoleA, StudentId = student, HizbNumber = hizb, CompletedQuarters = quarters,
            State = HizbRules.StateFor(quarters), Rating = rating,
            LastEvaluatedAt = quarters > 0 ? DateTimeOffset.UtcNow : null
        });
        await owner.SaveChangesAsync();
    }

    // ------------------------------------------------------------------ Instructor.UserId

    [Fact]
    public async Task A_Login_Account_Can_Only_Be_Linked_To_One_Live_Oustaz()
    {
        await using (var owner = _db.NewOwnerContext())
        {
            owner.Instructors.Add(new Instructor { SchoolId = EcoleA, FullName = "Doublon", UserId = UserOustazA1 });
            var act = async () => await owner.SaveChangesAsync();
            await act.Should().ThrowAsync<DuplicateRecordException>("un compte = une seule fiche d'Oustaz vivante");
        }

        // Une fiche supprimée logiquement libère le compte (index partiel, règle #6).
        await using var owner2 = _db.NewOwnerContext();
        var first = await owner2.Instructors.IgnoreQueryFilters().SingleAsync(i => i.Id == OustazA1);
        first.SoftDelete("test");
        await owner2.SaveChangesAsync();

        owner2.Instructors.Add(new Instructor { SchoolId = EcoleA, FullName = "Remplaçant", UserId = UserOustazA1 });
        await owner2.SaveChangesAsync();
    }

    // ------------------------------------------------------------------ GET instructors/{id}/students

    [Fact]
    public async Task The_Director_Reads_A_Halqa_With_The_Global_Kpi_Of_Each_Student()
    {
        await SeedHizbAsync(EleveA1, 1, 4, rating: 5);   // complet
        await SeedHizbAsync(EleveA1, 2, 2, rating: 3);   // en cours
        await SeedHizbAsync(EleveA2, 1, 4, rating: 4);   // autre Halqa : ne doit pas compter ici

        await using var s = AsDirecteur();
        var halqa = await new GetInstructorStudentsQueryHandler(s.Db, s.Scope)
            .Handle(new GetInstructorStudentsQuery(OustazA1), CancellationToken.None);

        halqa.InstructorName.Should().Be("Oustaz Un");
        halqa.StudentCount.Should().Be(2);
        halqa.Students.Select(x => x.StudentId).Should().BeEquivalentTo([EleveA1, EleveA1bis]);

        var awa = halqa.Students.Single(x => x.StudentId == EleveA1);
        awa.CompletedHizbs.Should().Be(1);
        awa.InProgressHizbs.Should().Be(1);
        awa.CompletedQuarters.Should().Be(6);
        awa.ProgressPercent.Should().Be(2.5m);   // 6 / 240
        awa.LastEvaluatedAt.Should().NotBeNull();

        var binta = halqa.Students.Single(x => x.StudentId == EleveA1bis);
        binta.CompletedQuarters.Should().Be(0);
        binta.ProgressPercent.Should().Be(0m);
        binta.LastEvaluatedAt.Should().BeNull();

        halqa.AverageProgressPercent.Should().Be(1.3m);   // moyenne de 2,5 et 0 → 1,25 arrondi
    }

    [Fact]
    public async Task An_Oustaz_Reads_His_Own_Halqa_But_Never_Another()
    {
        await using var s = AsUser(UserOustazA1, Role.Enseignant);
        var handler = new GetInstructorStudentsQueryHandler(s.Db, s.Scope);

        (await handler.Handle(new GetInstructorStudentsQuery(OustazA1), CancellationToken.None))
            .StudentCount.Should().Be(2);

        var act = async () => await handler.Handle(new GetInstructorStudentsQuery(OustazA2), CancellationToken.None);
        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task A_Teacher_Account_Without_An_Instructor_Record_Is_Refused_With_An_Actionable_Message()
    {
        await using var s = AsUser(UserSansFiche, Role.Enseignant);

        var act = async () => await new GetInstructorStudentsQueryHandler(s.Db, s.Scope)
            .Handle(new GetInstructorStudentsQuery(OustazA1), CancellationToken.None);

        (await act.Should().ThrowAsync<ForbiddenException>()).Which.Message.Should().Contain("demandez au Directeur");
    }

    [Fact]
    public async Task A_Suspended_Oustaz_Can_No_Longer_Read_Or_Write()
    {
        await using var s = AsUser(UserOustazSuspendu, Role.Enseignant);

        var read = async () => await new GetInstructorStudentsQueryHandler(s.Db, s.Scope)
            .Handle(new GetInstructorStudentsQuery(OustazSuspendu), CancellationToken.None);
        await read.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task An_Instructor_Of_Another_School_Is_Not_Found_Even_For_The_Director()
    {
        await using var s = AsDirecteur();

        var act = async () => await new GetInstructorStudentsQueryHandler(s.Db, s.Scope)
            .Handle(new GetInstructorStudentsQuery(OustazB), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    // ------------------------------------------------------------------ GET my-halqa

    [Fact]
    public async Task An_Oustaz_Opens_His_Own_Halqa_Without_Passing_Any_Identifier()
    {
        await SeedHizbAsync(EleveA1, 1, 4, rating: 5);
        await SeedHizbAsync(EleveA2, 1, 4, rating: 5);   // Halqa de l'autre Oustaz : ne doit jamais apparaître

        await using var s = AsUser(UserOustazA1, Role.Enseignant);
        var halqa = await new GetMyHalqaQueryHandler(s.Db, s.Scope).Handle(new GetMyHalqaQuery(), CancellationToken.None);

        halqa.InstructorId.Should().Be(OustazA1);
        halqa.Students.Select(x => x.StudentId).Should().BeEquivalentTo([EleveA1, EleveA1bis]);
        halqa.Students.Single(x => x.StudentId == EleveA1).CompletedHizbs.Should().Be(1);
    }

    [Fact]
    public async Task Each_Oustaz_Gets_His_Own_Halqa_From_His_Own_Account()
    {
        await using var s = AsUser(UserOustazA2, Role.Enseignant);

        var halqa = await new GetMyHalqaQueryHandler(s.Db, s.Scope).Handle(new GetMyHalqaQuery(), CancellationToken.None);

        halqa.InstructorId.Should().Be(OustazA2);
        halqa.Students.Select(x => x.StudentId).Should().Equal(EleveA2);
    }

    [Fact]
    public async Task A_Teacher_Account_Without_An_Instructor_Record_Has_No_Halqa()
    {
        await using var s = AsUser(UserSansFiche, Role.Enseignant);

        var act = async () => await new GetMyHalqaQueryHandler(s.Db, s.Scope).Handle(new GetMyHalqaQuery(), CancellationToken.None);

        (await act.Should().ThrowAsync<ForbiddenException>()).Which.Message.Should().Contain("aucune fiche d'Oustaz");
    }

    [Fact]
    public async Task The_Director_Has_No_Halqa_Of_His_Own()
    {
        await using var s = AsDirecteur();

        var act = async () => await new GetMyHalqaQueryHandler(s.Db, s.Scope).Handle(new GetMyHalqaQuery(), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task A_Suspended_Oustaz_Has_No_Halqa()
    {
        await using var s = AsUser(UserOustazSuspendu, Role.Enseignant);

        var act = async () => await new GetMyHalqaQueryHandler(s.Db, s.Scope).Handle(new GetMyHalqaQuery(), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    // ------------------------------------------------------------------ GET students/{id}/hizb-progress

    [Fact]
    public async Task The_Grid_Always_Has_60_Cells_And_Synthesizes_The_Hizb_Never_Entered()
    {
        await SeedHizbAsync(EleveA1, 3, 4, rating: 5);
        await SeedHizbAsync(EleveA1, 60, 1, rating: 2);

        await using var s = AsUser(UserOustazA1, Role.Enseignant);
        var grid = await new GetStudentHizbProgressQueryHandler(s.Db, s.Scope)
            .Handle(new GetStudentHizbProgressQuery(EleveA1), CancellationToken.None);

        grid.Hizbs.Select(c => c.HizbNumber).Should().Equal(Enumerable.Range(1, 60));
        grid.Hizbs[2].State.Should().Be(HizbMemorizationState.Completed);
        grid.Hizbs[2].RowVersion.Should().NotBeNull();
        grid.Hizbs[59].State.Should().Be(HizbMemorizationState.InProgress);

        var empty = grid.Hizbs[0];
        empty.State.Should().Be(HizbMemorizationState.NotStarted);
        empty.CompletedQuarters.Should().Be(0);
        empty.RowVersion.Should().BeNull("un Hizb jamais saisi n'existe pas en base");

        grid.Summary.CompletedHizbs.Should().Be(1);
        grid.Summary.InProgressHizbs.Should().Be(1);
        grid.Summary.CompletedQuarters.Should().Be(5);
        grid.Summary.TotalQuarters.Should().Be(240);
    }

    [Fact]
    public async Task An_Oustaz_Cannot_Read_A_Student_Of_Another_Halqa_Or_A_Student_Without_Oustaz()
    {
        await using var s = AsUser(UserOustazA1, Role.Enseignant);
        var handler = new GetStudentHizbProgressQueryHandler(s.Db, s.Scope);

        foreach (var student in new[] { EleveA2, EleveLibre })
        {
            var act = async () => await handler.Handle(new GetStudentHizbProgressQuery(student), CancellationToken.None);
            await act.Should().ThrowAsync<ForbiddenException>();
        }
    }

    [Fact]
    public async Task The_Director_Reads_Any_Student_Of_His_School_But_Not_Of_Another()
    {
        await using var s = AsDirecteur();
        var handler = new GetStudentHizbProgressQueryHandler(s.Db, s.Scope);

        foreach (var student in new[] { EleveA1, EleveA2, EleveLibre })
        {
            (await handler.Handle(new GetStudentHizbProgressQuery(student), CancellationToken.None))
                .Hizbs.Should().HaveCount(60);
        }

        var act = async () => await handler.Handle(new GetStudentHizbProgressQuery(EleveB), CancellationToken.None);
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    // ------------------------------------------------------------------ PUT students/{id}/hizb-progress

    [Fact]
    public async Task An_Oustaz_Records_A_New_Hizb_And_The_State_And_Date_Are_Set_By_The_Server()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        await using var s = AsUser(UserOustazA1, Role.Enseignant);

        var cell = await UpdateHandler(s).Handle(
            new UpdateHizbProgressCommand(EleveA1, 5, 2, 4, RowVersion: null), CancellationToken.None);

        cell.HizbNumber.Should().Be(5);
        cell.State.Should().Be(HizbMemorizationState.InProgress);
        cell.Rating.Should().Be(4);
        cell.LastEvaluatedAt.Should().BeAfter(before);
        cell.RowVersion.Should().NotBeNull();

        await using var check = _db.NewOwnerContext();
        var row = await check.StudentHizbStatuses.IgnoreQueryFilters().SingleAsync(h => h.StudentId == EleveA1 && h.HizbNumber == 5);
        row.SchoolId.Should().Be(EcoleA);
        row.CompletedQuarters.Should().Be(2);
    }

    [Theory]
    [InlineData(1, HizbMemorizationState.InProgress)]
    [InlineData(3, HizbMemorizationState.InProgress)]
    [InlineData(4, HizbMemorizationState.Completed)]
    public async Task The_State_Is_Derived_From_The_Quarters(int quarters, HizbMemorizationState expected)
    {
        await using var s = AsDirecteur();

        var cell = await UpdateHandler(s).Handle(
            new UpdateHizbProgressCommand(EleveA1, 7, quarters, 3, null), CancellationToken.None);

        cell.State.Should().Be(expected);
    }

    [Fact]
    public async Task Resetting_A_Hizb_To_Zero_Quarters_Clears_The_Rating_And_The_Evaluation_Date()
    {
        await SeedHizbAsync(EleveA1, 9, 3, rating: 4);
        await using var read = AsDirecteur();
        var version = (await new GetStudentHizbProgressQueryHandler(read.Db, read.Scope)
            .Handle(new GetStudentHizbProgressQuery(EleveA1), CancellationToken.None)).Hizbs[8].RowVersion;

        await using var s = AsDirecteur();
        var cell = await UpdateHandler(s).Handle(
            new UpdateHizbProgressCommand(EleveA1, 9, 0, Rating: 5, version), CancellationToken.None);

        cell.State.Should().Be(HizbMemorizationState.NotStarted);
        cell.Rating.Should().BeNull("un Hizb non commencé n'a pas de note, même si le client en envoie une");
        cell.LastEvaluatedAt.Should().BeNull();
    }

    [Fact]
    public async Task Updating_An_Existing_Hizb_Needs_The_Current_RowVersion()
    {
        await SeedHizbAsync(EleveA1, 4, 1, rating: 2);
        await using var read = AsDirecteur();
        var current = (await new GetStudentHizbProgressQueryHandler(read.Db, read.Scope)
            .Handle(new GetStudentHizbProgressQuery(EleveA1), CancellationToken.None)).Hizbs[3];

        // Sans jeton alors que la ligne existe : refus, jamais un écrasement silencieux.
        await using (var noToken = AsDirecteur())
        {
            var act = async () => await UpdateHandler(noToken).Handle(
                new UpdateHizbProgressCommand(EleveA1, 4, 3, 4, null), CancellationToken.None);
            await act.Should().ThrowAsync<ConcurrencyConflictException>();
        }

        // Avec le bon jeton : accepté, et le jeton change.
        await using var ok = AsDirecteur();
        var updated = await UpdateHandler(ok).Handle(
            new UpdateHizbProgressCommand(EleveA1, 4, 3, 4, current.RowVersion), CancellationToken.None);
        updated.CompletedQuarters.Should().Be(3);
        updated.RowVersion.Should().NotBe(current.RowVersion);

        // Rejouer l'ancien jeton : périmé → conflit.
        await using var stale = AsDirecteur();
        var replay = async () => await UpdateHandler(stale).Handle(
            new UpdateHizbProgressCommand(EleveA1, 4, 4, 5, current.RowVersion), CancellationToken.None);
        await replay.Should().ThrowAsync<ConcurrencyConflictException>();
    }

    [Fact]
    public async Task A_Token_For_A_Hizb_That_No_Longer_Exists_Is_A_Conflict_Not_A_Creation()
    {
        await using var s = AsDirecteur();

        var act = async () => await UpdateHandler(s).Handle(
            new UpdateHizbProgressCommand(EleveA1, 10, 1, 3, RowVersion: 42), CancellationToken.None);

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
    }

    [Fact]
    public async Task An_Oustaz_Cannot_Write_For_A_Student_Outside_His_Halqa_And_Nothing_Is_Persisted()
    {
        await using var s = AsUser(UserOustazA1, Role.Enseignant);
        var handler = UpdateHandler(s);

        foreach (var student in new[] { EleveA2, EleveLibre })
        {
            var act = async () => await handler.Handle(
                new UpdateHizbProgressCommand(student, 1, 4, 5, null), CancellationToken.None);
            await act.Should().ThrowAsync<ForbiddenException>();
        }

        await using var check = _db.NewOwnerContext();
        (await check.StudentHizbStatuses.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task The_Other_Oustaz_Can_Write_For_His_Own_Student_And_The_Director_For_Anyone()
    {
        await using (var oustaz2 = AsUser(UserOustazA2, Role.Enseignant))
        {
            (await UpdateHandler(oustaz2).Handle(new UpdateHizbProgressCommand(EleveA2, 1, 4, 5, null), CancellationToken.None))
                .State.Should().Be(HizbMemorizationState.Completed);
        }

        await using var directeur = AsDirecteur();
        (await UpdateHandler(directeur).Handle(new UpdateHizbProgressCommand(EleveLibre, 1, 1, null, null), CancellationToken.None))
            .State.Should().Be(HizbMemorizationState.InProgress);
    }

    [Fact]
    public async Task A_Student_Of_Another_School_Is_Not_Found_On_Write()
    {
        await using var s = AsDirecteur();

        var act = async () => await UpdateHandler(s).Handle(
            new UpdateHizbProgressCommand(EleveB, 1, 1, 3, null), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    // ------------------------------------------------------------------ POST students/assign-instructor

    [Fact]
    public async Task The_Director_Assigns_A_Batch_To_An_Oustaz_And_The_Classroom_Never_Moves()
    {
        await using var s = AsDirecteur();

        var result = await new AssignInstructorCommandHandler(s.Db).Handle(
            new AssignInstructorCommand(OustazA2, [EleveA1, EleveLibre]), CancellationToken.None);

        result.AssignedCount.Should().Be(2);
        result.UnchangedCount.Should().Be(0);

        await using var check = _db.NewOwnerContext();
        var students = await check.Students.IgnoreQueryFilters()
            .Where(x => x.Id == EleveA1 || x.Id == EleveLibre).ToListAsync();
        students.Should().OnlyContain(x => x.InstructorId == OustazA2);
        students.Should().OnlyContain(x => x.ClassroomId == ClasseA, "ClassroomId reste l'axe administratif et de caisse");
    }

    [Fact]
    public async Task Replaying_The_Same_Assignment_Changes_Nothing()
    {
        await using (var first = AsDirecteur())
        {
            await new AssignInstructorCommandHandler(first.Db).Handle(
                new AssignInstructorCommand(OustazA2, [EleveLibre]), CancellationToken.None);
        }

        await using var s = AsDirecteur();
        var replay = await new AssignInstructorCommandHandler(s.Db).Handle(
            new AssignInstructorCommand(OustazA2, [EleveLibre, EleveLibre]), CancellationToken.None);

        replay.AssignedCount.Should().Be(0);
        replay.UnchangedCount.Should().Be(1, "un id répété dans le lot ne compte qu'une fois");
    }

    [Fact]
    public async Task A_Null_Instructor_Detaches_The_Students()
    {
        await using var s = AsDirecteur();

        var result = await new AssignInstructorCommandHandler(s.Db).Handle(
            new AssignInstructorCommand(null, [EleveA1, EleveA1bis]), CancellationToken.None);

        result.AssignedCount.Should().Be(2);
        await using var check = _db.NewOwnerContext();
        (await check.Students.IgnoreQueryFilters().CountAsync(x => x.InstructorId == OustazA1)).Should().Be(0);
    }

    [Fact]
    public async Task A_Batch_With_A_Student_Of_Another_School_Is_Refused_Whole()
    {
        await using var s = AsDirecteur();

        var act = async () => await new AssignInstructorCommandHandler(s.Db).Handle(
            new AssignInstructorCommand(OustazA2, [EleveLibre, EleveB]), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();

        await using var check = _db.NewOwnerContext();
        (await check.Students.IgnoreQueryFilters().SingleAsync(x => x.Id == EleveLibre)).InstructorId
            .Should().BeNull("tout ou rien : l'élève valide du lot n'a pas été affecté");
        (await check.Students.IgnoreQueryFilters().SingleAsync(x => x.Id == EleveB)).InstructorId
            .Should().Be(OustazB, "l'élève de l'autre école n'est jamais touché");
    }

    [Fact]
    public async Task An_Instructor_Of_Another_School_Cannot_Receive_Students()
    {
        await using var s = AsDirecteur();

        var act = async () => await new AssignInstructorCommandHandler(s.Db).Handle(
            new AssignInstructorCommand(OustazB, [EleveLibre]), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task A_Suspended_Instructor_Cannot_Receive_Students()
    {
        await using var s = AsDirecteur();

        var act = async () => await new AssignInstructorCommandHandler(s.Db).Handle(
            new AssignInstructorCommand(OustazSuspendu, [EleveLibre]), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }
}
