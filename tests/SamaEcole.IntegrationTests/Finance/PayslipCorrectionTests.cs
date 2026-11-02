using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Commands.CancelFichePaie;
using SamaEcole.Application.Finance.Commands.CreateEmployeeContract;
using SamaEcole.Application.Finance.Commands.GenerateFichePaie;
using SamaEcole.Application.Finance.Commands.PayFichePaie;
using SamaEcole.Application.Finance.Commands.RegenerateFichePaie;
using SamaEcole.Application.Finance.Commands.UpdateEmployeeContract;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Finance;

/// <summary>
/// Volume 1 §14.5 à §14.7 — annulation / régénération d'une fiche de paie, règlement en trésorerie et personnel
/// sans compte, sur une vraie base (RLS, verrou xmin, index unique partiel). Une fiche n'est jamais supprimée :
/// elle s'annule, et une fiche payée se contre-passe en trésorerie.
/// </summary>
[Trait("Category", "MultiTenant")]
public class PayslipCorrectionTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("11111111-4444-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("22222222-4444-1111-1111-111111111112");
    private static readonly Guid UtilisateurA = Guid.Parse("aaaaaaaa-4444-0000-0000-00000000000a");
    private static readonly Guid ContratA = Guid.Parse("cccccccc-4444-0000-0000-00000000000a");

    private Guid _actorId;

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();

        owner.Schools.AddRange(
            new School { Id = EcoleA, Name = "École A" },
            new School { Id = EcoleB, Name = "École B" });

        var directeur = new User
        {
            SchoolId = EcoleA, Email = "directeur.a@ecole-a.sn", PasswordHash = "hash",
            FullName = "Directeur A", Role = Role.Directeur
        };
        owner.Users.Add(directeur);
        owner.Users.Add(new User
        {
            Id = UtilisateurA, SchoolId = EcoleA, Email = "employe.a@ecole-a.sn", PasswordHash = "hash",
            FullName = "Fatou Employée", Role = Role.Secretariat
        });
        owner.EmployeeContracts.Add(new EmployeeContract
        {
            Id = ContratA, SchoolId = EcoleA, UserId = UtilisateurA,
            Type = ContractType.Permanent, BaseSalary = 250_000m, HourlyRate = 0m, TransportAllowance = 0m
        });

        await owner.SaveChangesAsync(CancellationToken.None);
        _actorId = directeur.Id;
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    // ------------------------------------------------------------------ aides

    private async Task<Guid> GenerateAsync(Guid contractId = default, int month = 9, int year = 2026)
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new GenerateFichePaieCommandHandler(ctx, new FixedTenantProvider(EcoleA));
        return await handler.Handle(
            new GenerateFichePaieCommand(contractId == default ? ContratA : contractId, month, year),
            CancellationToken.None);
    }

    private async Task<uint> FicheVersionAsync(Guid ficheId, Guid? school = null)
    {
        await using var ctx = _db.NewAppContext(school ?? EcoleA);
        return await ctx.FichePaies.AsNoTracking().Where(f => f.Id == ficheId)
            .Select(f => EF.Property<uint>(f, "xmin")).SingleAsync();
    }

    private async Task PayAsync(Guid ficheId, PaymentMethod method = PaymentMethod.Cash)
    {
        var version = await FicheVersionAsync(ficheId);
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new PayFichePaieCommandHandler(
            ctx, new FixedTenantProvider(EcoleA), new FixedCurrentUser(_actorId), TimeProvider.System, new NoKpiCache());
        await handler.Handle(new PayFichePaieCommand(ficheId, method, new DateOnly(2026, 9, 30), version), CancellationToken.None);
    }

    private async Task<decimal> NetDisbursementsAsync()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        return await ctx.Disbursements.AsNoTracking().SumAsync(d => d.Amount);
    }

    // ------------------------------------------------------------------ annulation

    [Fact]
    public async Task Cancelling_An_Unpaid_Payslip_Should_Keep_The_Row_And_Free_The_Period()
    {
        var ficheId = await GenerateAsync();
        var version = await FicheVersionAsync(ficheId);

        await using (var ctx = _db.NewAppContext(EcoleA))
        {
            var handler = new CancelFichePaieCommandHandler(ctx, new FixedCurrentUser(_actorId), TimeProvider.System, new NoKpiCache());
            var result = await handler.Handle(new CancelFichePaieCommand(ficheId, "Montant erroné sur le contrat.", version), CancellationToken.None);
            result.PaymentReversed.Should().BeFalse();
        }

        await using var check = _db.NewAppContext(EcoleA);
        var fiche = await check.FichePaies.AsNoTracking().SingleAsync(f => f.Id == ficheId);
        fiche.IsCancelled.Should().BeTrue();
        fiche.CancelledByUserId.Should().Be(_actorId);
        fiche.CancellationReason.Should().Be("Montant erroné sur le contrat.");
        fiche.IsDeleted.Should().BeFalse("une fiche annulée est conservée, jamais supprimée");

        // La période est libérée : une nouvelle fiche active peut être générée pour le même mois.
        var again = async () => await GenerateAsync();
        await again.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Cancelling_Twice_Or_Generating_A_Duplicate_Active_Payslip_Should_Be_Refused()
    {
        var ficheId = await GenerateAsync();

        var duplicate = async () => await GenerateAsync();
        await duplicate.Should().ThrowAsync<BusinessRuleException>();

        var version = await FicheVersionAsync(ficheId);
        await using (var ctx = _db.NewAppContext(EcoleA))
        {
            var handler = new CancelFichePaieCommandHandler(ctx, new FixedCurrentUser(_actorId), TimeProvider.System, new NoKpiCache());
            await handler.Handle(new CancelFichePaieCommand(ficheId, "Première annulation.", version), CancellationToken.None);
        }

        await using var ctx2 = _db.NewAppContext(EcoleA);
        var handler2 = new CancelFichePaieCommandHandler(ctx2, new FixedCurrentUser(_actorId), TimeProvider.System, new NoKpiCache());
        var version2 = await FicheVersionAsync(ficheId);
        var twice = async () => await handler2.Handle(new CancelFichePaieCommand(ficheId, "Seconde annulation.", version2), CancellationToken.None);
        await twice.Should().ThrowAsync<BusinessRuleException>().WithMessage("*déjà annulée*");
    }

    [Fact]
    public async Task Cancelling_With_A_Stale_RowVersion_Should_Throw_A_Concurrency_Conflict()
    {
        var ficheId = await GenerateAsync();
        var stale = await FicheVersionAsync(ficheId);
        await PayAsync(ficheId); // fait avancer xmin

        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new CancelFichePaieCommandHandler(ctx, new FixedCurrentUser(_actorId), TimeProvider.System, new NoKpiCache());
        var act = async () => await handler.Handle(new CancelFichePaieCommand(ficheId, "Jeton périmé.", stale), CancellationToken.None);
        await act.Should().ThrowAsync<ConcurrencyConflictException>();
    }

    [Fact]
    public async Task Another_School_Cannot_Cancel_The_Payslip()
    {
        var ficheId = await GenerateAsync();
        var version = await FicheVersionAsync(ficheId);

        await using var ctx = _db.NewAppContext(EcoleB);
        var handler = new CancelFichePaieCommandHandler(ctx, new FixedCurrentUser(_actorId), TimeProvider.System, new NoKpiCache());
        var act = async () => await handler.Handle(new CancelFichePaieCommand(ficheId, "Tentative inter-école.", version), CancellationToken.None);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Cancelling_Should_Be_Refused_Once_A_Tax_Declaration_Exists_For_The_Period()
    {
        var ficheId = await GenerateAsync();
        await using (var owner = _db.NewOwnerContext())
        {
            owner.TaxeDeclarations.Add(new TaxeDeclaration { SchoolId = EcoleA, Month = 9, Year = 2026 });
            await owner.SaveChangesAsync(CancellationToken.None);
        }

        var version = await FicheVersionAsync(ficheId);
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new CancelFichePaieCommandHandler(ctx, new FixedCurrentUser(_actorId), TimeProvider.System, new NoKpiCache());
        var act = async () => await handler.Handle(new CancelFichePaieCommand(ficheId, "Après déclaration.", version), CancellationToken.None);
        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "PAYSLIP_PERIOD_DECLARED");
    }

    // ------------------------------------------------------------------ régénération

    [Fact]
    public async Task Regenerating_After_A_Contract_Correction_Should_Replace_The_Payslip_With_The_Right_Amount()
    {
        var ficheId = await GenerateAsync();

        // Le directeur corrige le montant du contrat (250 000 -> 180 000).
        await using (var ctx = _db.NewAppContext(EcoleA))
        {
            var contractVersion = await ctx.EmployeeContracts.AsNoTracking().Where(c => c.Id == ContratA)
                .Select(c => EF.Property<uint>(c, "xmin")).SingleAsync();
            var update = new UpdateEmployeeContractCommandHandler(
                ctx, new FixedTenantProvider(EcoleA), new FixedCurrentUser(_actorId), TimeProvider.System);
            await update.Handle(
                new UpdateEmployeeContractCommand(ContratA, 180_000m, 0m, 0m, "Erreur de saisie du montant.", contractVersion),
                CancellationToken.None);
        }

        var version = await FicheVersionAsync(ficheId);
        Guid newId;
        await using (var ctx = _db.NewAppContext(EcoleA))
        {
            var handler = new RegenerateFichePaieCommandHandler(
                ctx, new FixedTenantProvider(EcoleA), new FixedCurrentUser(_actorId), TimeProvider.System, new NoKpiCache());
            var result = await handler.Handle(new RegenerateFichePaieCommand(ficheId, "Montant du contrat corrigé.", version), CancellationToken.None);
            newId = result.NewFichePaieId;
        }

        await using var check = _db.NewAppContext(EcoleA);
        var old = await check.FichePaies.AsNoTracking().SingleAsync(f => f.Id == ficheId);
        var replacement = await check.FichePaies.AsNoTracking().SingleAsync(f => f.Id == newId);

        old.IsCancelled.Should().BeTrue();
        old.ReplacedByFichePaieId.Should().Be(newId);
        replacement.IsCancelled.Should().BeFalse();
        replacement.GrossSalary.Should().Be(180_000m);
        replacement.GrossSalary.Should().NotBe(old.GrossSalary);
        (replacement.Month, replacement.Year).Should().Be((old.Month, old.Year));
    }

    // ------------------------------------------------------------------ règlement et trésorerie

    [Fact]
    public async Task Paying_A_Payslip_Should_Create_A_Salaires_Disbursement_For_The_Net_Amount()
    {
        var ficheId = await GenerateAsync();
        await PayAsync(ficheId, PaymentMethod.MobileMoney);

        await using var check = _db.NewAppContext(EcoleA);
        var fiche = await check.FichePaies.AsNoTracking().SingleAsync(f => f.Id == ficheId);
        fiche.IsPaid.Should().BeTrue();
        fiche.PaidWith.Should().Be(PaymentMethod.MobileMoney);

        var disbursement = await check.Disbursements.AsNoTracking().SingleAsync(d => d.Id == fiche.DisbursementId);
        disbursement.Category.Should().Be(DisbursementCategory.Salaires);
        disbursement.Amount.Should().Be(fiche.NetSalary);
        disbursement.Beneficiary.Should().Be("Fatou Employée");
        disbursement.VatAmount.Should().Be(0m);
    }

    [Fact]
    public async Task Paying_Twice_Should_Be_Refused_And_Not_Duplicate_The_Disbursement()
    {
        var ficheId = await GenerateAsync();
        await PayAsync(ficheId);

        var again = async () => await PayAsync(ficheId);
        await again.Should().ThrowAsync<BusinessRuleException>().WithMessage("*déjà été payée*");

        await using var check = _db.NewAppContext(EcoleA);
        (await check.Disbursements.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Cancelling_A_Paid_Payslip_Should_Reverse_The_Disbursement_And_Net_To_Zero()
    {
        var ficheId = await GenerateAsync();
        await PayAsync(ficheId);
        (await NetDisbursementsAsync()).Should().BeGreaterThan(0m);

        var version = await FicheVersionAsync(ficheId);
        await using (var ctx = _db.NewAppContext(EcoleA))
        {
            var handler = new CancelFichePaieCommandHandler(ctx, new FixedCurrentUser(_actorId), TimeProvider.System, new NoKpiCache());
            var result = await handler.Handle(new CancelFichePaieCommand(ficheId, "Payée par erreur.", version), CancellationToken.None);
            result.PaymentReversed.Should().BeTrue();
        }

        (await NetDisbursementsAsync()).Should().Be(0m);

        await using var check = _db.NewAppContext(EcoleA);
        var rows = await check.Disbursements.AsNoTracking().ToListAsync();
        rows.Should().HaveCount(2, "l'écriture d'origine est conservée, une contre-écriture s'y ajoute");
        rows.Single(d => d.ReversalOfId != null).Amount.Should().BeNegative();
    }

    [Fact]
    public async Task Paying_A_Cancelled_Payslip_Should_Be_Refused()
    {
        var ficheId = await GenerateAsync();
        var version = await FicheVersionAsync(ficheId);
        await using (var ctx = _db.NewAppContext(EcoleA))
        {
            var handler = new CancelFichePaieCommandHandler(ctx, new FixedCurrentUser(_actorId), TimeProvider.System, new NoKpiCache());
            await handler.Handle(new CancelFichePaieCommand(ficheId, "Fiche erronée.", version), CancellationToken.None);
        }

        var act = async () => await PayAsync(ficheId);
        await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("*annulée*");
    }

    // ------------------------------------------------------------------ personnel sans compte

    [Fact]
    public async Task A_Staff_Member_Without_Account_Can_Be_Contracted_Paid_And_Appears_In_Treasury()
    {
        Guid contractId;
        await using (var ctx = _db.NewAppContext(EcoleA))
        {
            var handler = new CreateEmployeeContractCommandHandler(ctx, new FixedTenantProvider(EcoleA));
            contractId = await handler.Handle(
                new CreateEmployeeContractCommand(
                    null, null, ContractType.Permanent, 60_000m, 0m, 0m,
                    ExternalFullName: "Moussa Diop", ExternalJobTitle: "Gardien", ExternalPhone: "770000000"),
                CancellationToken.None);
        }

        var ficheId = await GenerateAsync(contractId);
        await PayAsync(ficheId);

        await using var check = _db.NewAppContext(EcoleA);
        var disbursement = await check.Disbursements.AsNoTracking().SingleAsync();
        disbursement.Beneficiary.Should().Be("Moussa Diop");
        disbursement.Category.Should().Be(DisbursementCategory.Salaires);
        disbursement.Reason.Should().Contain("Moussa Diop");

        // Aucun utilisateur n'a été créé pour lui.
        (await check.Users.CountAsync(u => u.FullName == "Moussa Diop")).Should().Be(0);
    }

    [Fact]
    public async Task Creating_Two_Active_Contracts_For_The_Same_Staff_Without_Account_Should_Be_Refused()
    {
        async Task CreateAsync()
        {
            await using var ctx = _db.NewAppContext(EcoleA);
            var handler = new CreateEmployeeContractCommandHandler(ctx, new FixedTenantProvider(EcoleA));
            await handler.Handle(
                new CreateEmployeeContractCommand(null, null, ContractType.Permanent, 60_000m, 0m, 0m, ExternalFullName: "Moussa Diop"),
                CancellationToken.None);
        }

        await CreateAsync();
        var act = async () => await CreateAsync();
        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public void A_Contract_Must_Target_Exactly_One_Kind_Of_Person()
    {
        var validator = new CreateEmployeeContractCommandValidator();

        validator.Validate(new CreateEmployeeContractCommand(null, null, ContractType.Permanent, 1m, 0m, 0m, ExternalFullName: "Moussa Diop"))
            .IsValid.Should().BeTrue();
        validator.Validate(new CreateEmployeeContractCommand(Guid.NewGuid(), null, ContractType.Permanent, 1m, 0m, 0m, ExternalFullName: "Moussa Diop"))
            .IsValid.Should().BeFalse("enseignant ET personne sans compte : jamais les deux");
        validator.Validate(new CreateEmployeeContractCommand(null, null, ContractType.Permanent, 1m, 0m, 0m))
            .IsValid.Should().BeFalse("aucune personne désignée");
    }

    private sealed class NoKpiCache : IKpiCacheService
    {
        public Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken) =>
            factory(cancellationToken);

        public void Invalidate(string key) { }
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
