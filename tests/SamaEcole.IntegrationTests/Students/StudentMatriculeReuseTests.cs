using FluentAssertions;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Domain.Entities;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Students;

/// <summary>
/// Le matricule (et l'IEN) d'une fiche supprimée logiquement redevient libre (spec 2026-10-17, §2.2), mais deux élèves
/// ACTIFS ne partagent jamais le même : l'unicité ne porte que sur les lignes non supprimées.
/// </summary>
[Trait("Category", "MultiTenant")]
public class StudentMatriculeReuseTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("11111111-bbbb-1111-1111-111111111111");
    private static readonly Guid Classe = Guid.Parse("aaaaaaaa-bbbb-0000-0000-00000000000a");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();
        owner.Schools.Add(new School { Id = Ecole, Name = "École A" });
        owner.Classrooms.Add(new Classroom { Id = Classe, SchoolId = Ecole, Name = "CM2", Level = "Primaire", Capacity = 40 });
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private static Student NewStudent(string matricule, string name, string? ien = null) => new()
    {
        SchoolId = Ecole, Matricule = matricule, FullName = name, IenNumber = ien,
        BirthDate = new DateOnly(2015, 5, 20), BirthPlace = "Dakar", Gender = "F", ClassroomId = Classe
    };

    private async Task AddAndDeleteAsync(Student student)
    {
        await using var owner = _db.NewOwnerContext();
        owner.Students.Add(student);
        await owner.SaveChangesAsync(CancellationToken.None);
        student.SoftDelete("director");
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    [Fact]
    public async Task The_Matricule_Of_A_Deleted_Student_Can_Be_Used_Again()
    {
        await AddAndDeleteAsync(NewStudent("ELEV-2026-0001", "Awa Fall"));

        await using var owner = _db.NewOwnerContext();
        owner.Students.Add(NewStudent("ELEV-2026-0001", "Moussa Diop"));

        var reuse = async () => await owner.SaveChangesAsync(CancellationToken.None);

        await reuse.Should().NotThrowAsync("le matricule d'une fiche supprimée est libre");
    }

    [Fact]
    public async Task Two_Active_Students_Never_Share_A_Matricule()
    {
        await using (var first = _db.NewOwnerContext())
        {
            first.Students.Add(NewStudent("ELEV-2026-0001", "Awa Fall"));
            await first.SaveChangesAsync(CancellationToken.None);
        }

        await using var owner = _db.NewOwnerContext();
        owner.Students.Add(NewStudent("ELEV-2026-0001", "Doublon"));

        var duplicate = async () => await owner.SaveChangesAsync(CancellationToken.None);

        await duplicate.Should().ThrowAsync<DuplicateRecordException>();
    }

    [Fact]
    public async Task The_Ien_Of_A_Deleted_Student_Can_Be_Used_Again_But_Not_Twice_Among_Active_Students()
    {
        await AddAndDeleteAsync(NewStudent("ELEV-2026-0001", "Awa Fall", ien: "SN2015000000001"));

        await using (var reuse = _db.NewOwnerContext())
        {
            reuse.Students.Add(NewStudent("ELEV-2026-0002", "Moussa Diop", ien: "SN2015000000001"));
            var act = async () => await reuse.SaveChangesAsync(CancellationToken.None);
            await act.Should().NotThrowAsync("l'IEN d'une fiche supprimée est libre");
        }

        await using var duplicate = _db.NewOwnerContext();
        duplicate.Students.Add(NewStudent("ELEV-2026-0003", "Doublon", ien: "SN2015000000001"));
        var dup = async () => await duplicate.SaveChangesAsync(CancellationToken.None);
        await dup.Should().ThrowAsync<DuplicateRecordException>();
    }
}
