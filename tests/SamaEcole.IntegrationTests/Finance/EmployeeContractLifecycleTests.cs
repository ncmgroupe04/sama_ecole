using FluentAssertions;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Commands.CloseEmployeeContract;
using SamaEcole.Application.Finance.Commands.GenerateFichePaie;
using SamaEcole.Application.Finance.Commands.UpdateEmployeeContract;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace SamaEcole.IntegrationTests.Finance;

/// <summary>
/// Volume 1 §14.1 — modification et clôture d'un contrat, côté base réelle (verrou optimiste xmin,
/// RLS). Un contrat n'est jamais supprimé physiquement : ces tests prouvent que la clôture pose
/// seulement <see cref="EmployeeContract.EndDate"/>, jamais un DELETE, et que l'historique
/// (<see cref="EmployeeContractHistory"/>) trace fidèlement chaque changement.
/// </summary>
[Trait("Category", "MultiTenant")]
public class EmployeeContractLifecycleTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("11111111-3333-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("22222222-3333-1111-1111-111111111112");
    private static readonly Guid UtilisateurA = Guid.Parse("aaaaaaaa-3333-0000-0000-00000000000a");
    private static readonly Guid ContratA = Guid.Parse("cccccccc-3333-0000-0000-00000000000a");

    private Guid _actorId;

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();

        owner.Schools.AddRange(
            new School { Id = EcoleA, Name = "École A" },
            new School { Id = EcoleB, Name = "École B" });

        var directeurA = new User
        {
            SchoolId = EcoleA, Email = "directeur.a@ecole-a.sn", PasswordHash = "hash",
            FullName = "Directeur A", Role = Role.Directeur
        };
        owner.Users.Add(directeurA);

        owner.Users.Add(new User
        {
            Id = UtilisateurA, SchoolId = EcoleA, Email = "employe.a@ecole-a.sn", PasswordHash = "hash",
            FullName = "Fatou Employée", Role = Role.Secretariat
        });

        owner.EmployeeContracts.Add(new EmployeeContract
        {
            Id = ContratA, SchoolId = EcoleA, UserId = UtilisateurA,
            Type = ContractType.Permanent, BaseSalary = 250_000m, HourlyRate = 0m, TransportAllowance = 15_000m
        });

        await owner.SaveChangesAsync(CancellationToken.None);
        _actorId = directeurA.Id;
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private async Task<uint> ReadRowVersionAsync(IApplicationDbContext ctx) =>
        await ctx.EmployeeContracts.AsNoTracking()
            .Where(c => c.Id == ContratA)
            .Select(c => EF.Property<uint>(c, "xmin"))
            .SingleAsync();

    // ------------------------------------------------------------ Modification

    [Fact]
    public async Task Updating_A_Contract_Should_Change_Terms_And_Record_History()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var rowVersion = await ReadRowVersionAsync(ctx);

        var handler = new UpdateEmployeeContractCommandHandler(
            ctx, new FixedTenantProvider(EcoleA), new FixedCurrentUser(_actorId), TimeProvider.System);

        var result = await handler.Handle(
            new UpdateEmployeeContractCommand(ContratA, 300_000m, 0m, 20_000m, "Augmentation annuelle actée en conseil.", rowVersion),
            CancellationToken.None);

        result.BaseSalary.Should().Be(300_000m);

        await using var check = _db.NewAppContext(EcoleA);
        var contract = await check.EmployeeContracts.AsNoTracking().SingleAsync(c => c.Id == ContratA);
        contract.BaseSalary.Should().Be(300_000m);
        contract.TransportAllowance.Should().Be(20_000m);

        var history = await check.EmployeeContractHistories.AsNoTracking().SingleAsync(h => h.EmployeeContractId == ContratA);
        history.ChangeType.Should().Be(EmployeeContractChangeType.Amended);
        history.PreviousBaseSalary.Should().Be(250_000m);
        history.NewBaseSalary.Should().Be(300_000m);
        history.Reason.Should().Be("Augmentation annuelle actée en conseil.");
    }

    [Fact]
    public async Task Updating_A_Contract_With_A_Stale_RowVersion_Should_Throw_A_Concurrency_Conflict()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var staleRowVersion = await ReadRowVersionAsync(ctx);

        // Une première modification, hors scope du test, fait avancer xmin en base.
        await using (var firstEdit = _db.NewAppContext(EcoleA))
        {
            var firstHandler = new UpdateEmployeeContractCommandHandler(
                firstEdit, new FixedTenantProvider(EcoleA), new FixedCurrentUser(_actorId), TimeProvider.System);
            await firstHandler.Handle(
                new UpdateEmployeeContractCommand(ContratA, 260_000m, 0m, 15_000m, "Première révision.", staleRowVersion),
                CancellationToken.None);
        }

        var handler = new UpdateEmployeeContractCommandHandler(
            ctx, new FixedTenantProvider(EcoleA), new FixedCurrentUser(_actorId), TimeProvider.System);

        var act = async () => await handler.Handle(
            new UpdateEmployeeContractCommand(ContratA, 999_999m, 0m, 15_000m, "Seconde révision, jeton périmé.", staleRowVersion),
            CancellationToken.None);

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
    }

    [Fact]
    public async Task Updating_Only_PayoutMethod_Should_Not_Create_A_History_Entry()
    {
        // Ticket JGK-K02 : EmployeeContractHistory historise les MONTANTS (AGENTS.md règle #4), pas
        // les coordonnées de règlement. Un changement de PayoutMethod seul ne doit jamais produire
        // une ligne Previous==New qui affirmerait un changement de rémunération inexistant.
        await using var ctx = _db.NewAppContext(EcoleA);
        var rowVersion = await ReadRowVersionAsync(ctx);

        var handler = new UpdateEmployeeContractCommandHandler(
            ctx, new FixedTenantProvider(EcoleA), new FixedCurrentUser(_actorId), TimeProvider.System);

        var result = await handler.Handle(
            new UpdateEmployeeContractCommand(
                ContratA, 250_000m, 0m, 15_000m, "Passage au virement bancaire.", rowVersion,
                PayoutMethod.BankTransfer, "SN08 SN01 0152 0000 0000 1234 5678"),
            CancellationToken.None);

        result.PayoutMethod.Should().Be(PayoutMethod.BankTransfer);

        await using var check = _db.NewAppContext(EcoleA);
        var contract = await check.EmployeeContracts.AsNoTracking().SingleAsync(c => c.Id == ContratA);
        contract.PayoutMethod.Should().Be(PayoutMethod.BankTransfer);
        contract.PayoutAccountReference.Should().Be("SN08 SN01 0152 0000 0000 1234 5678");

        (await check.EmployeeContractHistories.AsNoTracking().AnyAsync(h => h.EmployeeContractId == ContratA))
            .Should().BeFalse("aucun montant n'a changé, l'historique ne doit pas s'en trouver rempli");
    }

    [Fact]
    public async Task Updating_A_Contract_From_Another_School_Should_Be_Not_Found()
    {
        await using var ctx = _db.NewAppContext(EcoleB);

        var handler = new UpdateEmployeeContractCommandHandler(
            ctx, new FixedTenantProvider(EcoleB), new FixedCurrentUser(_actorId), TimeProvider.System);

        var act = async () => await handler.Handle(
            new UpdateEmployeeContractCommand(ContratA, 300_000m, 0m, 15_000m, "Tentative depuis une autre école.", 0),
            CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>("la RLS + le filtre EF rendent le contrat de l'École A invisible à l'École B");
    }

    // ------------------------------------------------------------ Clôture

    [Fact]
    public async Task Closing_A_Contract_Should_Set_EndDate_Without_Deleting_It()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var rowVersion = await ReadRowVersionAsync(ctx);

        var handler = new CloseEmployeeContractCommandHandler(
            ctx, new FixedTenantProvider(EcoleA), new FixedCurrentUser(_actorId), TimeProvider.System);

        await handler.Handle(
            new CloseEmployeeContractCommand(ContratA, new DateOnly(2026, 3, 31), "Fin de contrat.", rowVersion),
            CancellationToken.None);

        await using var check = _db.NewAppContext(EcoleA);
        var contract = await check.EmployeeContracts.AsNoTracking().SingleAsync(c => c.Id == ContratA);

        contract.Should().NotBeNull("AGENTS.md règle #6 : aucune suppression physique");
        contract.EndDate.Should().Be(new DateOnly(2026, 3, 31));
        contract.IsDeleted.Should().BeFalse();

        var history = await check.EmployeeContractHistories.AsNoTracking().SingleAsync(h => h.EmployeeContractId == ContratA);
        history.ChangeType.Should().Be(EmployeeContractChangeType.Closed);
        history.EndDate.Should().Be(new DateOnly(2026, 3, 31));
        history.PreviousBaseSalary.Should().Be(history.NewBaseSalary, "la clôture ne change pas les termes du contrat");
    }

    [Fact]
    public async Task Closing_An_Already_Closed_Contract_Should_Throw_A_Business_Rule_Exception()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var rowVersion = await ReadRowVersionAsync(ctx);

        var handler = new CloseEmployeeContractCommandHandler(
            ctx, new FixedTenantProvider(EcoleA), new FixedCurrentUser(_actorId), TimeProvider.System);

        await handler.Handle(
            new CloseEmployeeContractCommand(ContratA, new DateOnly(2026, 3, 31), "Fin de contrat.", rowVersion),
            CancellationToken.None);

        await using var secondCtx = _db.NewAppContext(EcoleA);
        var secondHandler = new CloseEmployeeContractCommandHandler(
            secondCtx, new FixedTenantProvider(EcoleA), new FixedCurrentUser(_actorId), TimeProvider.System);

        var act = async () => await secondHandler.Handle(
            new CloseEmployeeContractCommand(ContratA, new DateOnly(2026, 4, 30), "Nouvelle tentative.", rowVersion),
            CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task A_Closed_Contract_Should_No_Longer_Generate_A_Payslip()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var rowVersion = await ReadRowVersionAsync(ctx);

        var closeHandler = new CloseEmployeeContractCommandHandler(
            ctx, new FixedTenantProvider(EcoleA), new FixedCurrentUser(_actorId), TimeProvider.System);
        await closeHandler.Handle(
            new CloseEmployeeContractCommand(ContratA, new DateOnly(2026, 3, 31), "Fin de contrat.", rowVersion),
            CancellationToken.None);

        await using var payrollCtx = _db.NewAppContext(EcoleA);
        var payrollHandler = new GenerateFichePaieCommandHandler(payrollCtx, new FixedTenantProvider(EcoleA));

        var act = async () => await payrollHandler.Handle(
            new GenerateFichePaieCommand(ContratA, 4, 2026), CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task Closing_A_Contract_Should_Allow_A_New_Contract_For_The_Same_Person()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var rowVersion = await ReadRowVersionAsync(ctx);

        var closeHandler = new CloseEmployeeContractCommandHandler(
            ctx, new FixedTenantProvider(EcoleA), new FixedCurrentUser(_actorId), TimeProvider.System);
        await closeHandler.Handle(
            new CloseEmployeeContractCommand(ContratA, new DateOnly(2026, 3, 31), "Fin de contrat.", rowVersion),
            CancellationToken.None);

        await using var createCtx = _db.NewAppContext(EcoleA);
        createCtx.EmployeeContracts.Add(new EmployeeContract
        {
            SchoolId = EcoleA, UserId = UtilisateurA,
            Type = ContractType.Vacataire, BaseSalary = 0m, HourlyRate = 5_000m, TransportAllowance = 0m
        });

        // Ne doit PAS lever de violation d'index unique : le contrat précédent est clôturé, l'index
        // "TeacherId/UserId IS NOT NULL AND EndDate IS NULL" ne le considère plus.
        var act = async () => await createCtx.SaveChangesAsync(CancellationToken.None);
        await act.Should().NotThrowAsync();
    }
}

file sealed class FixedTenantProvider(Guid schoolId) : ITenantProvider
{
    public Guid? CurrentSchoolId => schoolId;
}

file sealed class FixedCurrentUser(Guid userId) : ICurrentUserService
{
    public Guid? UserId => userId;
    public Role? Role => SamaEcole.Domain.Enums.Role.Directeur;
    public string? IpAddress => null;
}
