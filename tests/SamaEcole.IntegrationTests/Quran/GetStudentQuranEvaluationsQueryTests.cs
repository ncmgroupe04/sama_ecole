using FluentAssertions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Quran.Commands.CreateQuranEvaluation;
using SamaEcole.Application.Quran.Queries.GetStudentQuranEvaluations;
using SamaEcole.Domain.Entities;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Quran;

public class GetStudentQuranEvaluationsQueryTests : IAsyncLifetime
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
    public async Task Returns_Only_The_Requested_Students_Evaluations()
    {
        await using var seed = _db.NewAppContext(Ecole);
        var createHandler = new CreateQuranEvaluationCommandHandler(seed, new StubTenantProvider(Ecole));
        await createHandler.Handle(new CreateQuranEvaluationCommand(EleveA, new DateOnly(2026, 9, 1), 1, 1, 1, 15), CancellationToken.None);
        await createHandler.Handle(new CreateQuranEvaluationCommand(EleveA, new DateOnly(2026, 9, 15), 0, 0, 0, 18), CancellationToken.None);
        await createHandler.Handle(new CreateQuranEvaluationCommand(EleveB, new DateOnly(2026, 9, 1), 2, 2, 2, 12), CancellationToken.None);

        await using var db = _db.NewAppContext(Ecole);
        var handler = new GetStudentQuranEvaluationsQueryHandler(db);

        var result = await handler.Handle(new GetStudentQuranEvaluationsQuery(EleveA), CancellationToken.None);

        result.Should().HaveCount(2);
        result.Should().OnlyContain(e => e.StudentId == EleveA);
    }

    private sealed class StubTenantProvider(Guid? schoolId) : ITenantProvider
    {
        public Guid? CurrentSchoolId => schoolId;
    }
}
