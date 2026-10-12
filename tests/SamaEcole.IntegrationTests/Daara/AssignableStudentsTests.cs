using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Internat.Queries.ListAssignableStudents;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Daara;

/// <summary>
/// La liste des élèves affectables à une Halqa (écran d'affectation de /oustaz), exécutée par le VRAI Handler sur un vrai
/// PostgreSQL sous le rôle applicatif (RLS active) : périmètre (sans Oustaz / tous), recherche, pagination, classe supprimée
/// et isolation entre écoles.
/// </summary>
[Trait("Category", "MultiTenant")]
public class AssignableStudentsTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("c1111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("c2222222-2222-2222-2222-222222222222");
    private static readonly Guid ClasseA = Guid.Parse("c1111111-0000-0000-0000-0000000000c1");
    private static readonly Guid ClasseSupprimee = Guid.Parse("c1111111-0000-0000-0000-0000000000c2");
    private static readonly Guid ClasseB = Guid.Parse("c2222222-0000-0000-0000-0000000000c1");

    private static readonly Guid OustazA = Guid.Parse("c1111111-0000-0000-0000-0000000000f1");
    private static readonly Guid OustazB = Guid.Parse("c2222222-0000-0000-0000-0000000000f1");

    private static readonly Guid Awa = Guid.Parse("c1111111-0000-0000-0000-0000000000e1");      // dans la Halqa de OustazA
    private static readonly Guid Binta = Guid.Parse("c1111111-0000-0000-0000-0000000000e2");    // sans Oustaz
    private static readonly Guid Cheikh = Guid.Parse("c1111111-0000-0000-0000-0000000000e3");   // sans Oustaz, classe supprimée
    private static readonly Guid Demba = Guid.Parse("c1111111-0000-0000-0000-0000000000e4");    // sans Oustaz
    private static readonly Guid EleveB = Guid.Parse("c2222222-0000-0000-0000-0000000000e1");   // autre école

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();

        owner.Schools.AddRange(new School { Id = EcoleA, Name = "Daara A" }, new School { Id = EcoleB, Name = "Daara B" });
        owner.Classrooms.AddRange(
            new Classroom { Id = ClasseA, SchoolId = EcoleA, Name = "CM1 Daara", Level = "Daara", Cycle = CycleType.Primaire, Capacity = 40 },
            new Classroom { Id = ClasseSupprimee, SchoolId = EcoleA, Name = "Ancienne classe", Level = "Daara", Cycle = CycleType.Primaire, Capacity = 40 },
            new Classroom { Id = ClasseB, SchoolId = EcoleB, Name = "Classe B", Level = "Daara", Cycle = CycleType.Primaire, Capacity = 40 });
        owner.Instructors.AddRange(
            new Instructor { Id = OustazA, SchoolId = EcoleA, FullName = "Oustaz Un" },
            new Instructor { Id = OustazB, SchoolId = EcoleB, FullName = "Oustaz B" });
        await owner.SaveChangesAsync();

        owner.Students.AddRange(
            NewStudent(Awa, EcoleA, ClasseA, "Awa Un", "ELEV-A1", OustazA, "عائشة"),
            NewStudent(Binta, EcoleA, ClasseA, "Binta Libre", "ELEV-A2", null),
            NewStudent(Cheikh, EcoleA, ClasseSupprimee, "Cheikh Libre", "ELEV-A3", null),
            NewStudent(Demba, EcoleA, ClasseA, "Demba Libre", "ELEV-A4", null),
            NewStudent(EleveB, EcoleB, ClasseB, "Élève B", "ELEV-B1", null));
        await owner.SaveChangesAsync();

        (await owner.Classrooms.IgnoreQueryFilters().SingleAsync(c => c.Id == ClasseSupprimee)).SoftDelete("test");
        await owner.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private static Student NewStudent(Guid id, Guid school, Guid classroom, string name, string matricule, Guid? instructor, string? nameAr = null) =>
        new()
        {
            Id = id, SchoolId = school, Matricule = matricule, FullName = name, FullNameAr = nameAr, BirthDate = new DateOnly(2012, 1, 1),
            BirthPlace = "Touba", Gender = "M", ClassroomId = classroom, InstructorId = instructor
        };

    private async Task<PaginatedAssignableStudents> ListAsync(ListAssignableStudentsQuery query, Guid? school = null)
    {
        await using var db = _db.NewAppContext(school ?? EcoleA);
        return await new ListAssignableStudentsQueryHandler(db).Handle(query, CancellationToken.None);
    }

    [Fact]
    public async Task By_Default_Only_The_Students_Without_An_Oustaz_Are_Listed_By_Name()
    {
        var result = await ListAsync(new ListAssignableStudentsQuery());

        result.TotalCount.Should().Be(3);
        result.Items.Select(i => i.StudentId).Should().Equal(Binta, Cheikh, Demba);
        result.Items.Should().OnlyContain(i => i.InstructorId == null && i.InstructorName == null);
    }

    [Fact]
    public async Task The_All_Scope_Adds_The_Assigned_Students_With_Their_Current_Oustaz()
    {
        var result = await ListAsync(new ListAssignableStudentsQuery { Scope = "all" });

        result.TotalCount.Should().Be(4);
        var awa = result.Items.Single(i => i.StudentId == Awa);
        awa.InstructorId.Should().Be(OustazA);
        awa.InstructorName.Should().Be("Oustaz Un");
        awa.FullNameAr.Should().Be("عائشة");
        awa.ClassroomName.Should().Be("CM1 Daara");
    }

    [Fact]
    public async Task The_Search_Matches_The_French_Name_The_Matricule_And_The_Arabic_Name()
    {
        (await ListAsync(new ListAssignableStudentsQuery { Search = "BINTA" })).Items.Select(i => i.StudentId)
            .Should().Equal(Binta);
        (await ListAsync(new ListAssignableStudentsQuery { Search = "elev-a3" })).Items.Select(i => i.StudentId)
            .Should().Equal(Cheikh);
        (await ListAsync(new ListAssignableStudentsQuery { Search = "عائ", Scope = "all" })).Items.Select(i => i.StudentId)
            .Should().Equal(Awa);
    }

    [Fact]
    public async Task A_One_Character_Search_Does_Not_Filter()
    {
        var result = await ListAsync(new ListAssignableStudentsQuery { Search = "z" });

        result.TotalCount.Should().Be(3, "un seul caractère est trop large pour être utile : la liste n'est pas filtrée");
    }

    [Fact]
    public async Task The_List_Is_Paginated_And_The_Page_Size_Is_Bounded()
    {
        var first = await ListAsync(new ListAssignableStudentsQuery { Page = 1, PageSize = 2 });
        var second = await ListAsync(new ListAssignableStudentsQuery { Page = 2, PageSize = 2 });

        first.Items.Select(i => i.StudentId).Should().Equal(Binta, Cheikh);
        second.Items.Select(i => i.StudentId).Should().Equal(Demba);
        first.TotalCount.Should().Be(3);
        second.TotalCount.Should().Be(3);

        (await ListAsync(new ListAssignableStudentsQuery { PageSize = 5000 })).PageSize.Should().Be(100);
        (await ListAsync(new ListAssignableStudentsQuery { Page = 0, PageSize = 0 })).Should().Match<PaginatedAssignableStudents>(
            r => r.Page == 1 && r.PageSize == 1);
    }

    [Fact]
    public async Task A_Student_Of_A_Deleted_Classroom_Stays_Listed_With_The_Classroom_Name()
    {
        var result = await ListAsync(new ListAssignableStudentsQuery());

        // La classe est supprimée (soft delete) : l'élève reste dans la liste et garde le nom de sa classe.
        result.Items.Single(i => i.StudentId == Cheikh).ClassroomName.Should().Be("Ancienne classe");
        result.Items.Single(i => i.StudentId == Binta).ClassroomName.Should().Be("CM1 Daara");
    }

    [Fact]
    public async Task Each_School_Only_Sees_Its_Own_Students()
    {
        var b = await ListAsync(new ListAssignableStudentsQuery { Scope = "all" }, EcoleB);

        b.Items.Select(i => i.StudentId).Should().Equal(EleveB);
        b.Items.Single().ClassroomName.Should().Be("Classe B");

        var a = await ListAsync(new ListAssignableStudentsQuery { Scope = "all" });
        a.Items.Select(i => i.StudentId).Should().NotContain(EleveB);
    }
}
