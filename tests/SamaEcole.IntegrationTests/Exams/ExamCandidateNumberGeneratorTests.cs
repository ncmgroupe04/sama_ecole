using FluentAssertions;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using SamaEcole.Persistence;
using Xunit;

namespace SamaEcole.IntegrationTests.Exams;

/// <summary>
/// Ticket JGK-J03 — numéro de table, généré dans la transaction d'attribution (AGENTS.md règle #3,
/// docs/Volume_1_Cahier_des_Charges.md §22.4). Même démarche que
/// <see cref="Matricules.MatriculeGeneratorTests"/> : l'unicité sous concurrence repose sur un verrou
/// de ligne PostgreSQL (UPDATE ... RETURNING), jamais sur une discipline en mémoire.
/// </summary>
public class ExamCandidateNumberGeneratorTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AnneeA = Guid.Parse("aaaa1111-0000-0000-0000-000000000001");
    private static readonly Guid SessionA = Guid.Parse("55550001-0000-0000-0000-00000000000a");
    private static readonly Guid SessionA2 = Guid.Parse("55550002-0000-0000-0000-00000000000a");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();

        owner.Schools.Add(new School { Id = EcoleA, Name = "École A" });
        owner.SchoolYears.Add(new SchoolYear { Id = AnneeA, SchoolId = EcoleA, Label = "2026-2027", StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2027, 7, 31), IsActive = true });

        owner.ExamSessions.AddRange(
            new ExamSession { Id = SessionA, SchoolId = EcoleA, SchoolYearId = AnneeA, ExamType = ExamType.CFEE },
            new ExamSession { Id = SessionA2, SchoolId = EcoleA, SchoolYearId = AnneeA, ExamType = ExamType.BFEM, Series = "G" });

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task Candidate_Numbers_Should_Be_Sequential_And_Zero_Padded()
    {
        await using var db = _db.NewAppContext(EcoleA);
        var generator = new ExamCandidateNumberGenerator(db, TimeProvider.System);

        var first = await generator.GenerateNextAsync(SessionA, CancellationToken.None);
        var second = await generator.GenerateNextAsync(SessionA, CancellationToken.None);

        first.Should().Be("001");
        second.Should().Be("002");
    }

    [Fact]
    public async Task Each_Session_Should_Have_Its_Own_Sequence()
    {
        await using (var db = _db.NewAppContext(EcoleA))
        {
            var generator = new ExamCandidateNumberGenerator(db, TimeProvider.System);
            await generator.GenerateNextAsync(SessionA, CancellationToken.None);
            await generator.GenerateNextAsync(SessionA, CancellationToken.None);
        }

        // La session BFEM ne doit PAS hériter du compteur de la session CFEE, même école.
        await using var db2 = _db.NewAppContext(EcoleA);
        var firstOfSessionA2 = await new ExamCandidateNumberGenerator(db2, TimeProvider.System)
            .GenerateNextAsync(SessionA2, CancellationToken.None);

        firstOfSessionA2.Should().Be("001");
    }

    [Fact]
    public async Task Concurrent_Generations_Should_Never_Produce_A_Duplicate()
    {
        const int parallelism = 20;

        // Chaque tâche a sa propre connexion : c'est PostgreSQL, pas le code C#, qui doit sérialiser.
        var numbers = await Task.WhenAll(Enumerable.Range(0, parallelism).Select(async _ =>
        {
            await using var db = _db.NewAppContext(EcoleA);
            return await new ExamCandidateNumberGenerator(db, TimeProvider.System)
                .GenerateNextAsync(SessionA, CancellationToken.None);
        }));

        numbers.Should().OnlyHaveUniqueItems();
        numbers.Should().BeEquivalentTo(
            Enumerable.Range(1, parallelism).Select(i => i.ToString("D3")),
            "la numérotation doit être dense : ni doublon, ni trou");
    }

    [Fact]
    public async Task Rolled_Back_Attribution_Should_Not_Consume_A_Candidate_Number()
    {
        await using (var db = _db.NewAppContext(EcoleA))
        {
            // Simule une attribution qui échoue APRÈS la génération du numéro (ex. verrou optimiste
            // périmé sur le dossier) : le rollback doit rembobiner le compteur avec elle.
            await using var transaction = await db.Database.BeginTransactionAsync();
            await new ExamCandidateNumberGenerator(db, TimeProvider.System).GenerateNextAsync(SessionA, CancellationToken.None);
            await transaction.RollbackAsync();
        }

        await using var db2 = _db.NewAppContext(EcoleA);
        var afterRollback = await new ExamCandidateNumberGenerator(db2, TimeProvider.System)
            .GenerateNextAsync(SessionA, CancellationToken.None);

        afterRollback.Should().Be("001");
    }
}
