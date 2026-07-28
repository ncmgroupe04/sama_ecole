using FluentAssertions;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Domain.Entities;
using SamaEcole.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace SamaEcole.IntegrationTests.Finance;

/// <summary>
/// Ticket JGK-F01, AGENTS.md règle #5 — le verrou optimiste (xmin) tient-il RÉELLEMENT ?
///
/// Le scénario est celui de deux directeurs qui éditent le même montant en même temps. On le
/// reproduit avec deux DbContext branchés sur le rôle applicatif, chacun ayant lu la ligne à son
/// propre instant. Le premier qui écrit gagne ; le second doit se heurter à un 409, jamais écraser
/// en silence le travail du premier.
/// </summary>
[Trait("Category", "MultiTenant")]
public class ClassFeeConcurrencyTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Classe = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid Categorie = Guid.Parse("cccccccc-0000-0000-0000-00000000000a");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();
        owner.Schools.Add(new School { Id = Ecole, Name = "École A" });
        owner.Classrooms.Add(new Classroom { Id = Classe, SchoolId = Ecole, Name = "CM2", Level = "Primaire", Capacity = 40 });
        owner.FeeCategories.Add(new FeeCategory { Id = Categorie, SchoolId = Ecole, Name = "Mensualité", IsRecurring = true });
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task Two_Concurrent_Edits_The_Second_One_Should_Be_Refused_With_A_Conflict()
    {
        Guid feeId;
        await using (var seed = _db.NewAppContext(Ecole))
        {
            var fee = new ClassFee { SchoolId = Ecole, FeeCategoryId = Categorie, ClassroomId = Classe, Amount = 15000 };
            seed.ClassFees.Add(fee);
            await seed.SaveChangesAsync(CancellationToken.None);
            feeId = fee.Id;
        }

        // Deux sessions ont chacune LU la ligne : elles détiennent le même jeton xmin.
        await using var ctxA = _db.NewAppContext(Ecole);
        await using var ctxB = _db.NewAppContext(Ecole);

        var feeA = await ctxA.ClassFees.FirstAsync(f => f.Id == feeId);
        var feeB = await ctxB.ClassFees.FirstAsync(f => f.Id == feeId);

        // A écrit en premier : xmin bascule en base.
        feeA.Amount = 12000;
        await ctxA.SaveChangesAsync(CancellationToken.None);

        // B écrit avec le jeton qu'il détenait — désormais périmé. Son UPDATE « WHERE xmin = <ancien> »
        // ne touche aucune ligne : SaveChanges refuse plutôt que d'écraser le montant fixé par A.
        feeB.Amount = 18000;
        var act = async () => await ctxB.SaveChangesAsync(CancellationToken.None);

        await act.Should().ThrowAsync<ConcurrencyConflictException>(
            "deux écritures concurrentes sur le barème ne doivent jamais s'écraser en silence (règle #5)");

        // Le montant en base est bien celui de A, intact.
        await using var check = _db.NewAppContext(Ecole);
        var finalAmount = await check.ClassFees.Where(f => f.Id == feeId).Select(f => f.Amount).FirstAsync();
        finalAmount.Should().Be(12000);
    }

    [Fact]
    public async Task A_Sequential_Edit_With_The_Fresh_Token_Should_Succeed()
    {
        // Contre-épreuve : sans conflit, l'écriture passe normalement. Sinon le test précédent
        // pourrait être vert pour une mauvaise raison (une écriture qui échoue TOUJOURS).
        Guid feeId;
        await using (var seed = _db.NewAppContext(Ecole))
        {
            var fee = new ClassFee { SchoolId = Ecole, FeeCategoryId = Categorie, ClassroomId = Classe, Amount = 15000 };
            seed.ClassFees.Add(fee);
            await seed.SaveChangesAsync(CancellationToken.None);
            feeId = fee.Id;
        }

        await using var ctx = _db.NewAppContext(Ecole);
        var fee2 = await ctx.ClassFees.FirstAsync(f => f.Id == feeId);
        fee2.Amount = 20000;

        var act = async () => await ctx.SaveChangesAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}
