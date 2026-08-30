using FluentAssertions;
using SamaEcole.Application.StateIntegration;
using SamaEcole.Domain.Entities;
using SamaEcole.IntegrationTests.Common;
using SamaEcole.Persistence;
using Xunit;

namespace SamaEcole.IntegrationTests.StateIntegration;

/// <summary>
/// Séquence de secours de l'IEN provisoire (<see cref="NationalIenGenerator"/>, ticket JGK-M01,
/// Volume 1 §23.1) — même démarche que
/// <see cref="Exams.ExamCandidateNumberGeneratorTests"/> : l'unicité sous concurrence repose sur un
/// verrou de ligne PostgreSQL (le même compteur que <see cref="MatriculeGenerator"/>), jamais sur une
/// discipline en mémoire. Comble le manque consigné dans ACTIVE_CONTEXT.md (« Ce qui reste : ...
/// concurrence sur la séquence IEN, refus 409 code absent »).
/// </summary>
public class IenProvisionalSequenceTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();
        owner.Schools.Add(new School { Id = EcoleA, Name = "École A", NationalSchoolCode = "012347" });
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private NationalIenGenerator NewGenerator(ApplicationDbContext ctx) =>
        new(ctx, _db.NewGenerator(ctx), TimeProvider.System);

    [Fact]
    public async Task Concurrent_Generations_Should_Never_Produce_A_Duplicate()
    {
        const int parallelism = 20;

        // Chaque tâche a sa propre connexion : c'est PostgreSQL, pas le code C#, qui doit sérialiser
        // (même remarque que ExamCandidateNumberGeneratorTests).
        var numbers = await Task.WhenAll(Enumerable.Range(0, parallelism).Select(async _ =>
        {
            await using var ctx = _db.NewAppContext(EcoleA);
            return await NewGenerator(ctx).GenerateProvisionalIenAsync(EcoleA, CancellationToken.None);
        }));

        numbers.Should().OnlyHaveUniqueItems(
            "deux élèves de la même école ne doivent jamais recevoir le même IEN provisoire");
        numbers.Should().OnlyContain(
            n => IenNumberFormat.IsWellFormed(n),
            "un numéro produit sous forte concurrence doit rester bien formé (préfixe P, clé de contrôle Luhn)");
    }

    [Fact]
    public async Task Rolled_Back_Generation_Should_Not_Consume_A_Sequence_Number()
    {
        string first;
        await using (var ctx = _db.NewAppContext(EcoleA))
        {
            // Simule une écriture qui échoue APRÈS la génération du numéro (ex. conflit de version sur
            // la fiche élève) : le rollback doit rembobiner le compteur avec elle, aucun trou.
            await using var transaction = await ctx.Database.BeginTransactionAsync();
            first = await NewGenerator(ctx).GenerateProvisionalIenAsync(EcoleA, CancellationToken.None);
            await transaction.RollbackAsync();
        }

        await using var ctx2 = _db.NewAppContext(EcoleA);
        var afterRollback = await NewGenerator(ctx2).GenerateProvisionalIenAsync(EcoleA, CancellationToken.None);

        afterRollback.Should().Be(first, "le rollback doit rembobiner le compteur : aucun numéro perdu");
    }

    [Fact]
    public async Task Each_School_Has_Its_Own_Sequence()
    {
        var ecoleB = Guid.NewGuid();
        await using (var owner = _db.NewOwnerContext())
        {
            owner.Schools.Add(new School { Id = ecoleB, Name = "École B", NationalSchoolCode = "987654" });
            await owner.SaveChangesAsync(CancellationToken.None);
        }

        await using (var ctxA = _db.NewAppContext(EcoleA))
        {
            await NewGenerator(ctxA).GenerateProvisionalIenAsync(EcoleA, CancellationToken.None);
        }

        // La séquence de l'école B ne doit PAS hériter du compteur de l'école A : son premier numéro
        // porte la séquence "00001" (les 5 chiffres qui précèdent le chiffre de contrôle final).
        await using var ctxB = _db.NewAppContext(ecoleB);
        var firstOfB = await NewGenerator(ctxB).GenerateProvisionalIenAsync(ecoleB, CancellationToken.None);

        firstOfB[9..14].Should().Be("00001");
    }
}
