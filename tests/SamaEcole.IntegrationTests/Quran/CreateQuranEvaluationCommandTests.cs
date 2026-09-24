using FluentAssertions;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Quran.Commands.CreateQuranEvaluation;
using SamaEcole.Domain.Entities;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Quran;

public class CreateQuranEvaluationCommandTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ClasseA = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid EleveA = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");
    private static readonly Guid ClasseB = Guid.Parse("cccccccc-0000-0000-0000-00000000000c");
    private static readonly Guid EleveB = Guid.Parse("dddddddd-0000-0000-0000-00000000000d");

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
            new Student { Id = EleveA, SchoolId = EcoleA, Matricule = "ELEV-0001", FullName = "Élève A", BirthDate = new DateOnly(2015, 1, 1), BirthPlace = "Dakar", Gender = "M", ClassroomId = ClasseA },
            new Student { Id = EleveB, SchoolId = EcoleB, Matricule = "ELEV-0001", FullName = "Élève B", BirthDate = new DateOnly(2015, 1, 1), BirthPlace = "Thiès", Gender = "F", ClassroomId = ClasseB });
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task Creates_An_Evaluation_For_A_Student_Of_The_Current_School()
    {
        await using var db = _db.NewAppContext(EcoleA);
        var handler = new CreateQuranEvaluationCommandHandler(db, new StubTenantProvider(EcoleA));

        var result = await handler.Handle(
            new CreateQuranEvaluationCommand(EleveA, new DateOnly(2026, 9, 20), 1, 2, 0, 17),
            CancellationToken.None);

        result.StudentId.Should().Be(EleveA);
        result.FinalScore.Should().Be(17);
        result.RowVersion.Should().NotBe((uint)0);
    }

    [Fact]
    public async Task Rejects_A_Student_From_Another_School()
    {
        await using var db = _db.NewAppContext(EcoleA);
        var handler = new CreateQuranEvaluationCommandHandler(db, new StubTenantProvider(EcoleA));

        var act = async () => await handler.Handle(
            new CreateQuranEvaluationCommand(EleveB, new DateOnly(2026, 9, 20), 0, 0, 0, 20),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    private sealed class StubTenantProvider(Guid? schoolId) : ITenantProvider
    {
        public Guid? CurrentSchoolId => schoolId;
    }
}
