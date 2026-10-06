using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Internat;
using SamaEcole.Application.Internat.Commands.AssignInstructor;
using SamaEcole.Application.Internat.Commands.CreateInstructor;
using SamaEcole.Application.Internat.Commands.UpdateInstructor;
using SamaEcole.Application.Internat.Queries.GetInstructorStudents;
using SamaEcole.Application.Internat.Queries.ListInstructors;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using SamaEcole.Persistence;
using Xunit;

namespace SamaEcole.IntegrationTests.Daara;

/// <summary>
/// Gestion des fiches d'Oustaz par la Direction (création, liste, modification, suspension, liaison au compte), par les
/// VRAIS Handlers sur un vrai PostgreSQL sous le rôle applicatif : la liaison au compte est-elle étanche entre écoles,
/// la suspension coupe-t-elle bien l'accès de l'Oustaz, le verrou optimiste tient-il ?
/// </summary>
[Trait("Category", "MultiTenant")]
public class InstructorManagementTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("c1111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("c2222222-2222-2222-2222-222222222222");
    private static readonly Guid ClasseA = Guid.Parse("c1111111-0000-0000-0000-0000000000c1");
    private static readonly Guid ClasseB = Guid.Parse("c2222222-0000-0000-0000-0000000000c1");

    private static readonly Guid UserEns1 = Guid.Parse("c1111111-0000-0000-0000-0000000000a1");
    private static readonly Guid UserEns2 = Guid.Parse("c1111111-0000-0000-0000-0000000000a2");
    private static readonly Guid UserDirecteur = Guid.Parse("c1111111-0000-0000-0000-0000000000a3");
    private static readonly Guid UserEnsB = Guid.Parse("c2222222-0000-0000-0000-0000000000a1");

    private static readonly Guid EleveA1 = Guid.Parse("c1111111-0000-0000-0000-0000000000e1");
    private static readonly Guid EleveA2 = Guid.Parse("c1111111-0000-0000-0000-0000000000e2");
    private static readonly Guid EleveA3 = Guid.Parse("c1111111-0000-0000-0000-0000000000e3");
    private static readonly Guid EleveB = Guid.Parse("c2222222-0000-0000-0000-0000000000e1");
    private static readonly Guid OustazB = Guid.Parse("c2222222-0000-0000-0000-0000000000f1");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();

        owner.Schools.AddRange(new School { Id = EcoleA, Name = "Daara A" }, new School { Id = EcoleB, Name = "Daara B" });
        owner.Classrooms.AddRange(
            new Classroom { Id = ClasseA, SchoolId = EcoleA, Name = "Halqa A", Level = "Daara", Cycle = CycleType.Primaire, Capacity = 40 },
            new Classroom { Id = ClasseB, SchoolId = EcoleB, Name = "Halqa B", Level = "Daara", Cycle = CycleType.Primaire, Capacity = 40 });
        owner.Users.AddRange(
            NewUser(UserEns1, EcoleA, "ens1@daara-a.sn", Role.Enseignant),
            NewUser(UserEns2, EcoleA, "ens2@daara-a.sn", Role.Enseignant),
            NewUser(UserDirecteur, EcoleA, "directeur@daara-a.sn", Role.Directeur),
            NewUser(UserEnsB, EcoleB, "ens@daara-b.sn", Role.Enseignant));
        await owner.SaveChangesAsync();

        owner.Instructors.Add(new Instructor { Id = OustazB, SchoolId = EcoleB, FullName = "Oustaz B", UserId = UserEnsB });
        await owner.SaveChangesAsync();

        owner.Students.AddRange(
            NewStudent(EleveA1, EcoleA, ClasseA, "ELEV-A1"),
            NewStudent(EleveA2, EcoleA, ClasseA, "ELEV-A2"),
            NewStudent(EleveA3, EcoleA, ClasseA, "ELEV-A3"),
            NewStudent(EleveB, EcoleB, ClasseB, "ELEV-B1"));
        await owner.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private static User NewUser(Guid id, Guid school, string email, Role role) =>
        new() { Id = id, SchoolId = school, Email = email, PasswordHash = "x", FullName = email, Role = role };

    private static Student NewStudent(Guid id, Guid school, Guid classroom, string matricule) =>
        new()
        {
            Id = id, SchoolId = school, Matricule = matricule, FullName = matricule, BirthDate = new DateOnly(2012, 1, 1),
            BirthPlace = "Touba", Gender = "M", ClassroomId = classroom
        };

    private sealed class StubTenantProvider(Guid? schoolId) : ITenantProvider
    {
        public Guid? CurrentSchoolId => schoolId;
    }

    private async Task<InstructorDto> CreateAsync(string name = "serigne modou", Guid? userId = null, string? nameAr = null, string? phone = null)
    {
        await using var db = _db.NewAppContext(EcoleA);
        return await new CreateInstructorCommandHandler(db, new StubTenantProvider(EcoleA))
            .Handle(new CreateInstructorCommand(name, nameAr, phone, userId), CancellationToken.None);
    }

    private async Task<InstructorDto> UpdateAsync(
        InstructorDto current, string? name = null, Guid? userId = null, EntityStatus? status = null, uint? rowVersion = null, bool keepUser = false)
    {
        await using var db = _db.NewAppContext(EcoleA);
        return await new UpdateInstructorCommandHandler(db, new StubTenantProvider(EcoleA)).Handle(
            new UpdateInstructorCommand(
                current.Id, name ?? current.FullName, current.FullNameAr, current.Phone,
                keepUser ? current.UserId : userId, status ?? current.Status, rowVersion ?? current.RowVersion),
            CancellationToken.None);
    }

    // ------------------------------------------------------------------ création

    [Fact]
    public async Task Creating_An_Oustaz_Without_Account_Yields_An_Active_Empty_Halqa()
    {
        var created = await CreateAsync("serigne modou", nameAr: "  سيرين مودو  ", phone: " ");

        created.FullName.Should().Be("Serigne Modou");
        created.FullNameAr.Should().Be("سيرين مودو", "l'arabe est rogné mais jamais traduit ni modifié");
        created.Phone.Should().BeNull("un téléphone vide n'est pas stocké");
        created.Status.Should().Be(EntityStatus.Active);
        created.UserId.Should().BeNull();
        created.UserEmail.Should().BeNull();
        created.StudentCount.Should().Be(0);
        created.RowVersion.Should().NotBe(0u);

        await using var check = _db.NewOwnerContext();
        (await check.Instructors.IgnoreQueryFilters().SingleAsync(i => i.Id == created.Id)).SchoolId.Should().Be(EcoleA);
    }

    [Fact]
    public async Task Creating_An_Oustaz_With_A_Teacher_Account_Links_It_And_Shows_The_Login()
    {
        var created = await CreateAsync(userId: UserEns1);

        created.UserId.Should().Be(UserEns1);
        created.UserEmail.Should().Be("ens1@daara-a.sn");
    }

    [Fact]
    public async Task An_Unknown_Account_And_An_Account_Of_Another_School_Get_The_Same_Neutral_404()
    {
        var unknown = async () => await CreateAsync(userId: Guid.NewGuid());
        var otherSchool = async () => await CreateAsync(userId: UserEnsB);

        var first = (await unknown.Should().ThrowAsync<KeyNotFoundException>()).Which;
        var second = (await otherSchool.Should().ThrowAsync<KeyNotFoundException>()).Which;

        second.Message.Should().Be(first.Message, "aucune différence observable : on ne peut pas énumérer les comptes d'une autre école");

        await using var check = _db.NewOwnerContext();
        (await check.Instructors.IgnoreQueryFilters().CountAsync(i => i.SchoolId == EcoleA)).Should().Be(0, "rien n'est créé");
    }

    [Fact]
    public async Task Only_An_Enseignant_Account_Can_Be_Linked()
    {
        var act = async () => await CreateAsync(userId: UserDirecteur);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task An_Account_Already_Linked_To_Another_Oustaz_Is_Refused()
    {
        await CreateAsync("premier", userId: UserEns1);

        var act = async () => await CreateAsync("second", userId: UserEns1);

        await act.Should().ThrowAsync<ValidationException>();
        await using var check = _db.NewOwnerContext();
        (await check.Instructors.IgnoreQueryFilters().CountAsync(i => i.SchoolId == EcoleA)).Should().Be(1);
    }

    // ------------------------------------------------------------------ liste

    [Fact]
    public async Task The_List_Shows_Every_Status_With_The_Halqa_Size_And_Never_Another_School()
    {
        var a = await CreateAsync("amadou", userId: UserEns1);
        var b = await CreateAsync("bamba");
        await UpdateAsync(b, status: EntityStatus.Suspended, keepUser: true);

        await using (var assign = _db.NewAppContext(EcoleA))
        {
            await new AssignInstructorCommandHandler(assign).Handle(
                new AssignInstructorCommand(a.Id, [EleveA1, EleveA2]), CancellationToken.None);
        }

        await using var db = _db.NewAppContext(EcoleA);
        var list = await new ListInstructorsQueryHandler(db).Handle(new ListInstructorsQuery(), CancellationToken.None);

        list.Select(i => i.FullName).Should().Equal("Amadou", "Bamba");
        list.Single(i => i.Id == a.Id).StudentCount.Should().Be(2);
        list.Single(i => i.Id == a.Id).UserEmail.Should().Be("ens1@daara-a.sn");
        list.Single(i => i.Id == b.Id).Status.Should().Be(EntityStatus.Suspended);
        list.Should().NotContain(i => i.Id == OustazB);
    }

    // ------------------------------------------------------------------ modification

    [Fact]
    public async Task Renaming_An_Oustaz_Changes_The_RowVersion()
    {
        var created = await CreateAsync();

        var updated = await UpdateAsync(created, name: "cheikh tidiane", keepUser: true);

        updated.FullName.Should().Be("Cheikh Tidiane");
        updated.RowVersion.Should().NotBe(created.RowVersion);
    }

    [Fact]
    public async Task A_Stale_RowVersion_Is_Refused_With_A_Conflict()
    {
        var created = await CreateAsync();
        await UpdateAsync(created, name: "premier", keepUser: true);

        var replay = async () => await UpdateAsync(created, name: "second", keepUser: true);

        await replay.Should().ThrowAsync<ConcurrencyConflictException>();
    }

    [Fact]
    public async Task Suspending_An_Oustaz_Cuts_His_Access_At_Once_And_Blocks_New_Assignments()
    {
        var oustaz = await CreateAsync(userId: UserEns1);
        await using (var assign = _db.NewAppContext(EcoleA))
        {
            await new AssignInstructorCommandHandler(assign).Handle(
                new AssignInstructorCommand(oustaz.Id, [EleveA1]), CancellationToken.None);
        }

        // Avant : l'Oustaz ouvre sa Halqa.
        await using (var before = _db.NewAppContext(EcoleA))
        {
            var scope = new HalqaScopeAuthorizer(before, new TestCurrentUser(UserEns1, Role.Enseignant));
            (await new GetInstructorStudentsQueryHandler(before, scope).Handle(new GetInstructorStudentsQuery(oustaz.Id), CancellationToken.None))
                .StudentCount.Should().Be(1);
        }

        var suspended = await UpdateAsync(oustaz, status: EntityStatus.Suspended, keepUser: true);
        suspended.Status.Should().Be(EntityStatus.Suspended);
        suspended.StudentCount.Should().Be(1, "ses élèves ne sont pas détachés en silence");

        // Après : plus d'accès, et plus d'élève à lui confier.
        await using (var after = _db.NewAppContext(EcoleA))
        {
            var scope = new HalqaScopeAuthorizer(after, new TestCurrentUser(UserEns1, Role.Enseignant));
            var read = async () => await new GetInstructorStudentsQueryHandler(after, scope)
                .Handle(new GetInstructorStudentsQuery(oustaz.Id), CancellationToken.None);
            await read.Should().ThrowAsync<ForbiddenException>();
        }

        await using var assignAgain = _db.NewAppContext(EcoleA);
        var assign2 = async () => await new AssignInstructorCommandHandler(assignAgain).Handle(
            new AssignInstructorCommand(oustaz.Id, [EleveA2]), CancellationToken.None);
        await assign2.Should().ThrowAsync<ValidationException>();

        // Réactivé : l'accès revient.
        await UpdateAsync(suspended, status: EntityStatus.Active, keepUser: true);
        await using var reactivated = _db.NewAppContext(EcoleA);
        var scope3 = new HalqaScopeAuthorizer(reactivated, new TestCurrentUser(UserEns1, Role.Enseignant));
        (await new GetInstructorStudentsQueryHandler(reactivated, scope3).Handle(new GetInstructorStudentsQuery(oustaz.Id), CancellationToken.None))
            .StudentCount.Should().Be(1);
    }

    [Fact]
    public async Task Unlinking_The_Account_Removes_The_Oustaz_Access_And_Frees_The_Account()
    {
        var oustaz = await CreateAsync(userId: UserEns1);

        var unlinked = await UpdateAsync(oustaz, userId: null);

        unlinked.UserId.Should().BeNull();
        unlinked.UserEmail.Should().BeNull();

        await using (var db = _db.NewAppContext(EcoleA))
        {
            var scope = new HalqaScopeAuthorizer(db, new TestCurrentUser(UserEns1, Role.Enseignant));
            var read = async () => await new GetInstructorStudentsQueryHandler(db, scope)
                .Handle(new GetInstructorStudentsQuery(oustaz.Id), CancellationToken.None);
            (await read.Should().ThrowAsync<ForbiddenException>()).Which.Message.Should().Contain("aucune fiche d'Oustaz");
        }

        // Le compte est libre : un autre Oustaz peut s'en servir.
        (await CreateAsync("autre", userId: UserEns1)).UserId.Should().Be(UserEns1);
    }

    [Fact]
    public async Task Switching_To_An_Account_Used_By_Another_Oustaz_Is_Refused()
    {
        await CreateAsync("premier", userId: UserEns1);
        var second = await CreateAsync("second", userId: UserEns2);

        var act = async () => await UpdateAsync(second, userId: UserEns1);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task The_Account_Is_Revalidated_Only_When_It_Changes()
    {
        var oustaz = await CreateAsync(userId: UserEns1);

        // Le compte change de rôle après coup : renommer l'Oustaz ne doit pas être bloqué par l'ancien rattachement.
        await using (var owner = _db.NewOwnerContext())
        {
            (await owner.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == UserEns1)).Role = Role.Secretariat;
            await owner.SaveChangesAsync();
        }

        var renamed = await UpdateAsync(oustaz, name: "renommé", keepUser: true);

        renamed.FullName.Should().Be("Renommé");
        renamed.UserId.Should().Be(UserEns1);
    }

    [Fact]
    public async Task Updating_An_Oustaz_Of_Another_School_Is_Not_Found()
    {
        await using var db = _db.NewAppContext(EcoleA);

        var act = async () => await new UpdateInstructorCommandHandler(db, new StubTenantProvider(EcoleA)).Handle(
            new UpdateInstructorCommand(OustazB, "Pirate", null, null, null, EntityStatus.Active, 1), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Linking_To_An_Account_Of_Another_School_On_Update_Is_The_Same_Neutral_404()
    {
        var oustaz = await CreateAsync();

        var act = async () => await UpdateAsync(oustaz, userId: UserEnsB);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
