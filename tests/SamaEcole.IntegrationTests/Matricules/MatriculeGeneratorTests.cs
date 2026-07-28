using FluentAssertions;
using SamaEcole.Domain.Common;
using SamaEcole.Domain.Entities;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Matricules;

/// <summary>
/// Ticket JGK-D01 — numérotation des matricules (docs/Volume_1_Cahier_des_Charges.md §2.2).
///
/// Tout passe par le RÔLE APPLICATIF, RLS active : le générateur écrit dans matricule_sequences,
/// qui est une table tenant. S'il ne respectait pas la policy (WITH CHECK sur SchoolId), il
/// fonctionnerait en test avec le propriétaire et échouerait en production. On teste donc dans les
/// conditions réelles.
///
/// L'unicité sous concurrence repose sur un verrou de ligne PostgreSQL
/// (INSERT ... ON CONFLICT DO UPDATE) : aucun double en mémoire ne la reproduirait.
/// </summary>
public class MatriculeGeneratorTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    /// <summary>Millésime de l'année SCOLAIRE (bascule en octobre), pas l'année civile.</summary>
    private readonly int _year = AcademicYear.ForDate(DateTimeOffset.UtcNow);

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();
        owner.Schools.AddRange(
            new School { Id = EcoleA, Name = "École A" },
            new School { Id = EcoleB, Name = "École B" });
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task Student_Matricules_Should_Be_Sequential_And_Zero_Padded()
    {
        await using var db = _db.NewAppContext(EcoleA);
        var generator = _db.NewGenerator(db);

        var first = await generator.GenerateNextStudentMatriculeAsync(EcoleA, CancellationToken.None);
        var second = await generator.GenerateNextStudentMatriculeAsync(EcoleA, CancellationToken.None);

        first.Should().Be($"ELEV-{_year}-0001");
        second.Should().Be($"ELEV-{_year}-0002");
    }

    [Fact]
    public async Task Teacher_Matricules_Should_Use_Their_Own_Prefix_And_Counter()
    {
        await using var db = _db.NewAppContext(EcoleA);
        var generator = _db.NewGenerator(db);

        // Un élève a déjà consommé le numéro 1 : le compteur enseignant doit rester indépendant.
        await generator.GenerateNextStudentMatriculeAsync(EcoleA, CancellationToken.None);

        var teacher = await generator.GenerateNextTeacherMatriculeAsync(EcoleA, CancellationToken.None);

        teacher.Should().Be($"ENS-{_year}-001");
    }

    [Fact]
    public async Task Each_School_Should_Have_Its_Own_Sequence()
    {
        await using (var dbA = _db.NewAppContext(EcoleA))
        {
            await _db.NewGenerator(dbA).GenerateNextStudentMatriculeAsync(EcoleA, CancellationToken.None);
            await _db.NewGenerator(dbA).GenerateNextStudentMatriculeAsync(EcoleA, CancellationToken.None);
        }

        // L'École B ne doit PAS hériter du compteur de l'École A (isolation multi-tenant).
        await using var dbB = _db.NewAppContext(EcoleB);
        var firstOfB = await _db.NewGenerator(dbB).GenerateNextStudentMatriculeAsync(EcoleB, CancellationToken.None);

        firstOfB.Should().Be($"ELEV-{_year}-0001");
    }

    [Fact]
    public async Task Concurrent_Generations_Should_Never_Produce_A_Duplicate()
    {
        const int parallelism = 20;

        // Chaque tâche a sa propre connexion : c'est PostgreSQL, pas le code C#, qui doit sérialiser.
        var matricules = await Task.WhenAll(Enumerable.Range(0, parallelism).Select(async _ =>
        {
            await using var db = _db.NewAppContext(EcoleA);
            return await _db.NewGenerator(db).GenerateNextStudentMatriculeAsync(EcoleA, CancellationToken.None);
        }));

        matricules.Should().OnlyHaveUniqueItems();
        matricules.Should().BeEquivalentTo(
            Enumerable.Range(1, parallelism).Select(i => $"ELEV-{_year}-{i:D4}"),
            "la numérotation doit être dense : ni doublon, ni trou");
    }

    [Fact]
    public async Task Rolled_Back_Registration_Should_Not_Consume_A_Matricule()
    {
        await using (var db = _db.NewAppContext(EcoleA))
        {
            // Simule un enregistrement qui échoue APRÈS la génération du matricule.
            await using var transaction = await db.Database.BeginTransactionAsync();
            await _db.NewGenerator(db).GenerateNextStudentMatriculeAsync(EcoleA, CancellationToken.None);
            await transaction.RollbackAsync();
        }

        // Le numéro 1 doit être encore libre : un formulaire abandonné ne laisse pas de trou
        // (docs/Volume_1_Cahier_des_Charges.md §2.1).
        await using var db2 = _db.NewAppContext(EcoleA);
        var afterRollback = await _db.NewGenerator(db2).GenerateNextStudentMatriculeAsync(EcoleA, CancellationToken.None);

        afterRollback.Should().Be($"ELEV-{_year}-0001");
    }
}