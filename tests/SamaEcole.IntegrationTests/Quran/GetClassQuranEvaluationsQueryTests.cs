using FluentAssertions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Quran.Commands.CreateQuranEvaluation;
using SamaEcole.Application.Quran.Queries.GetClassQuranEvaluations;
using SamaEcole.Domain.Entities;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Quran;

public class GetClassQuranEvaluationsQueryTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ClasseA = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid ClasseB = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");
    private static readonly Guid EleveA = Guid.Parse("cccccccc-0000-0000-0000-00000000000c");

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
        owner.Students.Add(
            new Student { Id = EleveA, SchoolId = EcoleA, Matricule = "ELEV-0001", FullName = "Awa Fall", BirthDate = new DateOnly(2015, 1, 1), BirthPlace = "Dakar", Gender = "F", ClassroomId = ClasseA });
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task Returns_One_Row_Per_Student_With_Their_Evaluations()
    {
        await using var seed = _db.NewAppContext(EcoleA);
        await new CreateQuranEvaluationCommandHandler(seed, new StubTenantProvider(EcoleA)).Handle(
            new CreateQuranEvaluationCommand(EleveA, new DateOnly(2026, 9, 1), 1, 1, 1, 15), CancellationToken.None);

        await using var db = _db.NewAppContext(EcoleA);
        var handler = new GetClassQuranEvaluationsQueryHandler(db);

        var result = await handler.Handle(new GetClassQuranEvaluationsQuery(ClasseA), CancellationToken.None);

        result.Should().ContainSingle();
        result[0].FullName.Should().Be("Awa Fall");
        result[0].Entries.Should().ContainSingle();
    }

    [Fact]
    public async Task Never_Includes_A_Classroom_From_Another_School()
    {
        await using var db = _db.NewAppContext(EcoleA);
        var handler = new GetClassQuranEvaluationsQueryHandler(db);

        var act = async () => await handler.Handle(new GetClassQuranEvaluationsQuery(ClasseB), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    private sealed class StubTenantProvider(Guid? schoolId) : ITenantProvider
    {
        public Guid? CurrentSchoolId => schoolId;
    }
}
