using FluentAssertions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Quran.Commands.CreateQuranProgress;
using SamaEcole.Application.Quran.Queries.GetStudentQuranProgress;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Quran;

public class GetStudentQuranProgressQueryTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Classe = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid EleveA = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");
    private static readonly Guid EleveB = Guid.Parse("cccccccc-0000-0000-0000-00000000000c");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();
        owner.Schools.Add(new School { Id = Ecole, Name = "École A" });
        owner.Classrooms.Add(new Classroom { Id = Classe, SchoolId = Ecole, Name = "CM2", Level = "Primaire", Capacity = 40 });
        owner.Students.AddRange(
            new Student { Id = EleveA, SchoolId = Ecole, Matricule = "ELEV-0001", FullName = "Élève A", BirthDate = new DateOnly(2015, 1, 1), BirthPlace = "Dakar", Gender = "M", ClassroomId = Classe },
            new Student { Id = EleveB, SchoolId = Ecole, Matricule = "ELEV-0002", FullName = "Élève B", BirthDate = new DateOnly(2015, 1, 1), BirthPlace = "Dakar", Gender = "F", ClassroomId = Classe });
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task Returns_Only_The_Requested_Students_Entries()
    {
        await using var seed = _db.NewAppContext(Ecole);
        var createHandler = new CreateQuranProgressCommandHandler(seed, new StubTenantProvider(Ecole));
        await createHandler.Handle(new CreateQuranProgressCommand(EleveA, 1, 1, 1, QuranMemorizationStatus.InProcess, null, null), CancellationToken.None);
        await createHandler.Handle(new CreateQuranProgressCommand(EleveA, 2, 3, 10, QuranMemorizationStatus.Memorized, null, null), CancellationToken.None);
        await createHandler.Handle(new CreateQuranProgressCommand(EleveB, 1, 1, 1, QuranMemorizationStatus.InProcess, null, null), CancellationToken.None);

        await using var db = _db.NewAppContext(Ecole);
        var handler = new GetStudentQuranProgressQueryHandler(db);

        var result = await handler.Handle(new GetStudentQuranProgressQuery(EleveA), CancellationToken.None);

        result.Should().HaveCount(2);
        result.Should().OnlyContain(p => p.StudentId == EleveA);
    }

    [Fact]
    public async Task Returns_Empty_List_When_No_Entry_Exists()
    {
        await using var db = _db.NewAppContext(Ecole);
        var handler = new GetStudentQuranProgressQueryHandler(db);

        var result = await handler.Handle(new GetStudentQuranProgressQuery(EleveB), CancellationToken.None);

        result.Should().BeEmpty();
    }

    private sealed class StubTenantProvider(Guid? schoolId) : ITenantProvider
    {
        public Guid? CurrentSchoolId => schoolId;
    }
}
