using FluentAssertions;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Quran;
using SamaEcole.Application.Quran.Commands.CreateQuranEvaluation;
using SamaEcole.Application.Quran.Commands.UpdateQuranEvaluation;
using SamaEcole.Domain.Entities;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Quran;

public class UpdateQuranEvaluationCommandTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Classe = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid Eleve = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();
        owner.Schools.Add(new School { Id = Ecole, Name = "École A" });
        owner.Classrooms.Add(new Classroom { Id = Classe, SchoolId = Ecole, Name = "CM2", Level = "Primaire", Capacity = 40 });
        owner.Students.Add(new Student { Id = Eleve, SchoolId = Ecole, Matricule = "ELEV-0001", FullName = "Élève de test", BirthDate = new DateOnly(2015, 1, 1), BirthPlace = "Dakar", Gender = "M", ClassroomId = Classe });
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private static async Task<QuranEvaluationDto> SeedEvaluationAsync(SamaEcole.Persistence.ApplicationDbContext db) =>
        await new CreateQuranEvaluationCommandHandler(db, new StubTenantProvider(Ecole)).Handle(
            new CreateQuranEvaluationCommand(Eleve, new DateOnly(2026, 9, 20), 1, 1, 1, 15),
            CancellationToken.None);

    [Fact]
    public async Task Corrects_The_Final_Score()
    {
        await using var seed = _db.NewAppContext(Ecole);
        var created = await SeedEvaluationAsync(seed);

        await using var db = _db.NewAppContext(Ecole);
        var handler = new UpdateQuranEvaluationCommandHandler(db);

        var result = await handler.Handle(
            new UpdateQuranEvaluationCommand(created.Id, created.EvaluationDate, 0, 0, 0, 19, created.RowVersion),
            CancellationToken.None);

        result.FinalScore.Should().Be(19);
        result.StudentId.Should().Be(Eleve);
    }

    [Fact]
    public async Task Unknown_Id_Is_Not_Found()
    {
        await using var db = _db.NewAppContext(Ecole);
        var handler = new UpdateQuranEvaluationCommandHandler(db);

        var act = async () => await handler.Handle(
            new UpdateQuranEvaluationCommand(Guid.NewGuid(), new DateOnly(2026, 9, 20), 0, 0, 0, 15, RowVersion: 1),
            CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Stale_RowVersion_Is_Refused_With_A_Conflict()
    {
        await using var seed = _db.NewAppContext(Ecole);
        var created = await SeedEvaluationAsync(seed);

        await using var db1 = _db.NewAppContext(Ecole);
        await new UpdateQuranEvaluationCommandHandler(db1).Handle(
            new UpdateQuranEvaluationCommand(created.Id, created.EvaluationDate, 0, 0, 0, 19, created.RowVersion),
            CancellationToken.None);

        await using var db2 = _db.NewAppContext(Ecole);
        var handler2 = new UpdateQuranEvaluationCommandHandler(db2);

        var act = async () => await handler2.Handle(
            new UpdateQuranEvaluationCommand(created.Id, created.EvaluationDate, 2, 2, 2, 12, created.RowVersion),
            CancellationToken.None);

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
    }

    private sealed class StubTenantProvider(Guid? schoolId) : ITenantProvider
    {
        public Guid? CurrentSchoolId => schoolId;
    }
}
