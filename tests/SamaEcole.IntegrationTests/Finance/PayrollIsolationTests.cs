using FluentAssertions;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Finance.Commands.CreateEmployeeContract;
using SamaEcole.Application.Finance.Queries.GetEmployeeContracts;
using SamaEcole.Application.Finance.Queries.GetFichePaies;
using SamaEcole.Application.Finance.Queries.GetTaxDeclarations;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace SamaEcole.IntegrationTests.Finance;

/// <summary>
/// Module Comptabilité & Fiscalité (JGK) — CRUD EmployeeContract et lectures FichePaie/TaxeDeclaration.
///
/// Deux garanties critiques, exercées contre un vrai PostgreSQL (RLS active) :
/// - Un contrat lié à un enseignant/utilisateur d'une AUTRE école ne doit jamais pouvoir être créé
///   (le filtre EF + RLS le rend invisible, donc introuvable — <see cref="NotFoundException"/>).
/// - Aucune fuite entre écoles sur les listes de contrats, fiches de paie ou déclarations fiscales.
/// </summary>
[Trait("Category", "MultiTenant")]
public class PayrollIsolationTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid EnseignantA = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid EnseignantB = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");
    private static readonly Guid ContratA = Guid.Parse("cccccccc-0000-0000-0000-00000000000a");
    private static readonly Guid ContratB = Guid.Parse("cccccccc-0000-0000-0000-00000000000b");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();

        owner.Schools.AddRange(
            new School { Id = EcoleA, Name = "École A" },
            new School { Id = EcoleB, Name = "École B" });

        owner.Teachers.AddRange(
            new Teacher { Id = EnseignantA, SchoolId = EcoleA, Matricule = "ENS-A-0001", FullName = "Awa Fall", Email = "awa@ecole-a.sn", BirthDate = new DateOnly(1985, 4, 12) },
            new Teacher { Id = EnseignantB, SchoolId = EcoleB, Matricule = "ENS-B-0001", FullName = "Moussa Diop", Email = "moussa@ecole-b.sn", BirthDate = new DateOnly(1980, 1, 5) });

        owner.EmployeeContracts.AddRange(
            new EmployeeContract { Id = ContratA, SchoolId = EcoleA, TeacherId = EnseignantA, Type = ContractType.Permanent, BaseSalary = 250_000m, HourlyRate = 0m, TransportAllowance = 15_000m },
            new EmployeeContract { Id = ContratB, SchoolId = EcoleB, TeacherId = EnseignantB, Type = ContractType.Permanent, BaseSalary = 400_000m, HourlyRate = 0m, TransportAllowance = 20_000m });

        owner.FichePaies.AddRange(
            PayrollCalculator(ContratA, EcoleA, 3, 2026, 250_000m),
            PayrollCalculator(ContratB, EcoleB, 3, 2026, 400_000m));

        owner.TaxeDeclarations.AddRange(
            new TaxeDeclaration { SchoolId = EcoleA, Month = 3, Year = 2026, TotalIpres = 1_000m, TotalCss = 500m, TotalVrs = 300m, TotalBrs = 200m, TvaCollected = 0m, TvaDeductible = 0m, NetTva = 0m, TotalDueToState = 500m },
            new TaxeDeclaration { SchoolId = EcoleB, Month = 3, Year = 2026, TotalIpres = 9_000m, TotalCss = 4_500m, TotalVrs = 2_700m, TotalBrs = 1_800m, TvaCollected = 0m, TvaDeductible = 0m, NetTva = 0m, TotalDueToState = 4_500m });

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private static FichePaie PayrollCalculator(Guid contractId, Guid schoolId, int month, int year, decimal baseSalary) =>
        Application.Finance.Services.PayrollCalculator.CalculateFichePaie(schoolId, contractId, month, year, baseSalary, 0m, 0m, 0m);

    [Fact]
    public async Task Creating_A_Contract_For_Another_Schools_Teacher_Is_Rejected_As_Not_Found()
    {
        // EnseignantB appartient à l'École B : sous le rôle applicatif de l'École A, RLS + filtre EF le
        // rendent invisible — la commande doit échouer par NotFoundException, jamais créer le contrat.
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new CreateEmployeeContractCommandHandler(ctx, new FixedTenantProvider(EcoleA));

        var command = new CreateEmployeeContractCommand(EnseignantB, null, ContractType.Permanent, 200_000m, 0m, 0m);
        var act = async () => await handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Employee_Contracts_List_Never_Leaks_Another_Schools_Contract()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new GetEmployeeContractsQueryHandler(ctx);

        var result = await handler.Handle(new GetEmployeeContractsQuery(), CancellationToken.None);

        result.Should().ContainSingle().Which.EmployeeFullName.Should().Be("Awa Fall");
        result.Should().NotContain(c => c.Id == ContratB, "le contrat de l'École B ne doit jamais apparaître");
    }

    [Fact]
    public async Task FichePaies_List_Never_Leaks_Another_Schools_Payslip()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new GetFichePaiesQueryHandler(ctx);

        var result = await handler.Handle(new GetFichePaiesQuery(Month: 3, Year: 2026), CancellationToken.None);

        result.Should().ContainSingle().Which.EmployeeFullName.Should().Be("Awa Fall");
        result.Single().GrossSalary.Should().Be(250_000m);
    }

    [Fact]
    public async Task Tax_Declarations_List_Never_Leaks_Another_Schools_Declaration()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new GetTaxDeclarationsQueryHandler(ctx);

        var result = await handler.Handle(new GetTaxDeclarationsQuery(Year: 2026), CancellationToken.None);

        result.Should().ContainSingle().Which.TotalDueToState.Should().Be(500m,
            "500 est le total de l'École A ; 4 500 (École B) ne doit jamais apparaître");
    }
}

/// <summary>Fournit un SchoolId fixe, sans passer par le contexte HTTP — suffisant pour un handler appelé directement en test.</summary>
file sealed class FixedTenantProvider(Guid schoolId) : Application.Common.Interfaces.ITenantProvider
{
    public Guid? CurrentSchoolId => schoolId;
}
