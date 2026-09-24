using FluentAssertions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Quran.Commands.CreateQuranProgress;
using SamaEcole.Application.Quran.Queries.GetClassQuranProgress;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Quran;

public class GetClassQuranProgressQueryTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ClasseA = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid ClasseB = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");
    private static readonly Guid EleveA = Guid.Parse("cccccccc-0000-0000-0000-00000000000c");
    private static readonly Guid EleveB = Guid.Parse("dddddddd-0000-0000-0000-00000000000d");
    private static readonly Guid EleveAutreEcole = Guid.Parse("eeeeeeee-0000-0000-0000-00000000000e");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();
        owner.Schools.AddRange(
            new School { Id = EcoleA, Name = "École A" },
            new School { Id = EcoleB, Name = "École B" });
        owner.Classrooms.AddRange(
            new Classroom { Id = ClasseA, SchoolId = EcoleA, Name = "CM2", Level = "Primaire", Capacity = 40 },
            new Classroom { Id = ClasseB, SchoolId = EcoleB, Name = "CM2", Level = "Primaire", Capacity = 40 });
        owner.Students.AddRange(
            new Student { Id = EleveA, SchoolId = EcoleA, Matricule = "ELEV-0001", FullName = "Awa Fall", BirthDate = new DateOnly(2015, 1, 1), BirthPlace = "Dakar", Gender = "F", ClassroomId = ClasseA },
            new Student { Id = EleveB, SchoolId = EcoleA, Matricule = "ELEV-0002", FullName = "Modou Diop", BirthDate = new DateOnly(2015, 1, 1), BirthPlace = "Dakar", Gender = "M", ClassroomId = ClasseA },
            new Student { Id = EleveAutreEcole, SchoolId = EcoleB, Matricule = "ELEV-0001", FullName = "Élève B", BirthDate = new DateOnly(2015, 1, 1), BirthPlace = "Thiès", Gender = "M", ClassroomId = ClasseB });
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task Returns_One_Row_Per_Student_With_Their_Entries_Sorted_By_FullName()
    {
        await using var seed = _db.NewAppContext(EcoleA);
        var createHandler = new CreateQuranProgressCommandHandler(seed, new StubTenantProvider(EcoleA));
        await createHandler.Handle(new CreateQuranProgressCommand(EleveA, 1, 1, 1, QuranMemorizationStatus.InProcess, null, null), CancellationToken.None);
        await createHandler.Handle(new CreateQuranProgressCommand(EleveB, 30, 60, 114, QuranMemorizationStatus.Memorized, null, null), CancellationToken.None);

        await using var db = _db.NewAppContext(EcoleA);
        var handler = new GetClassQuranProgressQueryHandler(db);

        var result = await handler.Handle(new GetClassQuranProgressQuery(ClasseA), CancellationToken.None);

        result.Should().HaveCount(2);
        result[0].FullName.Should().Be("Awa Fall");
        result[0].Entries.Should().ContainSingle();
        result[1].FullName.Should().Be("Modou Diop");
        result[1].Entries.Should().ContainSingle();
    }

    [Fact]
    public async Task Never_Includes_A_Student_From_Another_School()
    {
        await using var db = _db.NewAppContext(EcoleA);
        var handler = new GetClassQuranProgressQueryHandler(db);

        var act = async () => await handler.Handle(new GetClassQuranProgressQuery(ClasseB), CancellationToken.None);

        // ClasseB appartient à l'École B : le Global Query Filter la rend introuvable pour l'École A.
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    private sealed class StubTenantProvider(Guid? schoolId) : ITenantProvider
    {
        public Guid? CurrentSchoolId => schoolId;
    }
}
