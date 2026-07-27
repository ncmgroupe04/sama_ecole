using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Xunit;

namespace SamaEcole.FunctionalTests.Finance;

/// <summary>
/// Module Comptabilité &amp; Fiscalité — Paie (Volume 1 §14). Surface HTTP jusqu'ici testée seulement en
/// dessous (PayrollCalculator en unitaire, isolation multi-tenant en intégration) : aucun test
/// fonctionnel n'exerçait le contrôle des rôles, le rejet d'un doublon mensuel, ni le cycle de vie d'un
/// contrat (modification, clôture) via l'API réelle. Critères couverts : rôles Directeur/Finance
/// uniquement ; anti-doublon de fiche par mois (verrou DB) ; verrou optimiste sur la modification d'un
/// contrat ; un contrat clôturé ne génère plus jamais de fiche ; la clôture n'est pas une suppression
/// (§14.1) et libère l'index unique pour un nouveau contrat sur la même personne.
/// </summary>
public class PayrollEndpointsTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    public Task InitializeAsync() => factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private record Tokens(string AccessToken, int ExpiresIn);
    private record ContractDto(
        Guid Id, Guid? TeacherId, Guid? UserId, string EmployeeFullName, string EmployeeRole,
        string Type, decimal BaseSalary, decimal HourlyRate, decimal TransportAllowance,
        DateOnly? EndDate, uint RowVersion);

    private async Task<string> TokenAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!.AccessToken;
    }

    private Task<string> DirecteurTokenAsync() => TokenAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);
    private Task<string> SecretariatTokenAsync() => TokenAsync(AuthApiFactory.SecretaireEmail, AuthApiFactory.SecretairePassword);

    private async Task<HttpResponseMessage> SendAsync(string? token, HttpMethod method, string path, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return await _client.SendAsync(request);
    }

    /// <summary>Contrat Permanent sur le compte Secrétariat semé (JGK-A05) : évite de dépendre du module Teachers.</summary>
    private async Task<ContractDto> CreatePermanentContractAsync(string directorToken, decimal baseSalary = 250_000m)
    {
        var response = await SendAsync(directorToken, HttpMethod.Post, "/api/v1/finance/employee-contracts", new
        {
            teacherId = (Guid?)null,
            userId = AuthApiFactory.SecretaireId,
            type = "Permanent",
            baseSalary,
            hourlyRate = 0m,
            transportAllowance = 15_000m
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var list = await SendAsync(directorToken, HttpMethod.Get, "/api/v1/finance/employee-contracts");
        var contracts = (await list.Content.ReadFromJsonAsync<List<ContractDto>>())!;
        return contracts.Single(c => c.UserId == AuthApiFactory.SecretaireId);
    }

    // ------------------------------------------------------------ Rôles

    [Fact]
    public async Task A_Secretariat_Must_Not_Be_Allowed_To_Read_Employee_Contracts()
    {
        var secretariat = await SecretariatTokenAsync();

        var response = await SendAsync(secretariat, HttpMethod.Get, "/api/v1/finance/employee-contracts");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_Secretariat_Must_Not_Be_Allowed_To_Generate_A_Payslip()
    {
        var secretariat = await SecretariatTokenAsync();

        var response = await SendAsync(secretariat, HttpMethod.Post, "/api/v1/finance/payroll", new
        {
            employeeContractId = Guid.NewGuid(),
            month = 3,
            year = 2026
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ------------------------------------------------------------ Cycle contrat -> fiche de paie

    [Fact]
    public async Task Directeur_Should_Create_A_Contract_And_Generate_Its_First_Payslip()
    {
        var director = await DirecteurTokenAsync();
        var contract = await CreatePermanentContractAsync(director);

        contract.Type.Should().Be("Permanent");
        contract.EndDate.Should().BeNull();

        var generate = await SendAsync(director, HttpMethod.Post, "/api/v1/finance/payroll", new
        {
            employeeContractId = contract.Id,
            month = 3,
            year = 2026,
            hoursWorked = 0,
            transportAllowance = 0
        });

        generate.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Generating_A_Second_Payslip_For_The_Same_Month_Should_Be_Rejected()
    {
        var director = await DirecteurTokenAsync();
        var contract = await CreatePermanentContractAsync(director);
        var payload = new { employeeContractId = contract.Id, month = 4, year = 2026, hoursWorked = 0, transportAllowance = 0 };

        var first = await SendAsync(director, HttpMethod.Post, "/api/v1/finance/payroll", payload);
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        var second = await SendAsync(director, HttpMethod.Post, "/api/v1/finance/payroll", payload);

        second.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "une seule fiche de paie par employé et par mois — verrouillé par un index unique en base");
    }

    // ------------------------------------------------------------ Modification (Volume 1 §14.1)

    [Fact]
    public async Task Updating_A_Contract_Should_Change_Its_Terms()
    {
        var director = await DirecteurTokenAsync();
        var contract = await CreatePermanentContractAsync(director, baseSalary: 250_000m);

        var response = await SendAsync(director, HttpMethod.Patch, $"/api/v1/finance/employee-contracts/{contract.Id}", new
        {
            baseSalary = 300_000m,
            hourlyRate = 0m,
            transportAllowance = 20_000m,
            reason = "Augmentation annuelle actée en conseil.",
            rowVersion = contract.RowVersion
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var list = await SendAsync(director, HttpMethod.Get, "/api/v1/finance/employee-contracts");
        var updated = (await list.Content.ReadFromJsonAsync<List<ContractDto>>())!.Single(c => c.Id == contract.Id);
        updated.BaseSalary.Should().Be(300_000m);
        updated.TransportAllowance.Should().Be(20_000m);
    }

    [Fact]
    public async Task Updating_A_Contract_With_A_Stale_RowVersion_Should_Return_409()
    {
        var director = await DirecteurTokenAsync();
        var contract = await CreatePermanentContractAsync(director);

        // Première modification, réussie : le jeton xmin change en base.
        await SendAsync(director, HttpMethod.Patch, $"/api/v1/finance/employee-contracts/{contract.Id}", new
        {
            baseSalary = 260_000m, hourlyRate = 0m, transportAllowance = 15_000m,
            reason = "Première révision.", rowVersion = contract.RowVersion
        });

        // Seconde modification avec le jeton PÉRIMÉ (celui lu avant la première modification).
        var stale = await SendAsync(director, HttpMethod.Patch, $"/api/v1/finance/employee-contracts/{contract.Id}", new
        {
            baseSalary = 999_999m, hourlyRate = 0m, transportAllowance = 15_000m,
            reason = "Seconde révision, jeton périmé.", rowVersion = contract.RowVersion
        });

        stale.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "un jeton xmin périmé doit être refusé plutôt que d'écraser la première modification");
    }

    [Fact]
    public async Task Updating_A_Contract_Without_A_Reason_Should_Return_422()
    {
        var director = await DirecteurTokenAsync();
        var contract = await CreatePermanentContractAsync(director);

        var response = await SendAsync(director, HttpMethod.Patch, $"/api/v1/finance/employee-contracts/{contract.Id}", new
        {
            baseSalary = 300_000m, hourlyRate = 0m, transportAllowance = 15_000m,
            reason = "", rowVersion = contract.RowVersion
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    // ------------------------------------------------------------ Clôture (Volume 1 §14.1)

    [Fact]
    public async Task Closing_A_Contract_Should_Prevent_Any_New_Payslip_Generation()
    {
        var director = await DirecteurTokenAsync();
        var contract = await CreatePermanentContractAsync(director);

        var close = await SendAsync(director, HttpMethod.Post, $"/api/v1/finance/employee-contracts/{contract.Id}/close", new
        {
            endDate = "2026-03-31",
            reason = "Fin de contrat — départ de l'établissement.",
            rowVersion = contract.RowVersion
        });
        close.StatusCode.Should().Be(HttpStatusCode.OK);

        var generate = await SendAsync(director, HttpMethod.Post, "/api/v1/finance/payroll", new
        {
            employeeContractId = contract.Id, month = 4, year = 2026, hoursWorked = 0, transportAllowance = 0
        });

        generate.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "un contrat clôturé ne doit plus jamais générer de nouvelle fiche de paie");
    }

    [Fact]
    public async Task Closing_A_Contract_Should_Not_Delete_It()
    {
        var director = await DirecteurTokenAsync();
        var contract = await CreatePermanentContractAsync(director);

        await SendAsync(director, HttpMethod.Post, $"/api/v1/finance/employee-contracts/{contract.Id}/close", new
        {
            endDate = "2026-03-31", reason = "Fin de contrat.", rowVersion = contract.RowVersion
        });

        var list = await SendAsync(director, HttpMethod.Get, "/api/v1/finance/employee-contracts");
        var contracts = (await list.Content.ReadFromJsonAsync<List<ContractDto>>())!;

        contracts.Should().Contain(c => c.Id == contract.Id,
            "AGENTS.md règle #6 : aucune suppression physique — un contrat clôturé reste consultable");
        contracts.Single(c => c.Id == contract.Id).EndDate.Should().Be(new DateOnly(2026, 3, 31));
    }

    [Fact]
    public async Task Closing_An_Already_Closed_Contract_Should_Be_Rejected()
    {
        var director = await DirecteurTokenAsync();
        var contract = await CreatePermanentContractAsync(director);

        var firstClose = await SendAsync(director, HttpMethod.Post, $"/api/v1/finance/employee-contracts/{contract.Id}/close", new
        {
            endDate = "2026-03-31", reason = "Fin de contrat.", rowVersion = contract.RowVersion
        });
        firstClose.StatusCode.Should().Be(HttpStatusCode.OK);

        var secondClose = await SendAsync(director, HttpMethod.Post, $"/api/v1/finance/employee-contracts/{contract.Id}/close", new
        {
            endDate = "2026-04-30", reason = "Nouvelle tentative.", rowVersion = contract.RowVersion
        });

        secondClose.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Updating_A_Closed_Contract_Should_Be_Rejected()
    {
        var director = await DirecteurTokenAsync();
        var contract = await CreatePermanentContractAsync(director);

        await SendAsync(director, HttpMethod.Post, $"/api/v1/finance/employee-contracts/{contract.Id}/close", new
        {
            endDate = "2026-03-31", reason = "Fin de contrat.", rowVersion = contract.RowVersion
        });

        var update = await SendAsync(director, HttpMethod.Patch, $"/api/v1/finance/employee-contracts/{contract.Id}", new
        {
            baseSalary = 500_000m, hourlyRate = 0m, transportAllowance = 0m,
            reason = "Tentative sur contrat clôturé.", rowVersion = contract.RowVersion
        });

        update.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Closing_A_Contract_Should_Free_Its_Slot_For_A_New_Contract_On_The_Same_Person()
    {
        // Volume 1 §14.1 : un départ suivi d'une reprise doit rester possible — l'index unique sur
        // UserId ne doit plus considérer un contrat clôturé comme actif.
        var director = await DirecteurTokenAsync();
        var contract = await CreatePermanentContractAsync(director);

        await SendAsync(director, HttpMethod.Post, $"/api/v1/finance/employee-contracts/{contract.Id}/close", new
        {
            endDate = "2026-03-31", reason = "Fin de contrat.", rowVersion = contract.RowVersion
        });

        var rehire = await SendAsync(director, HttpMethod.Post, "/api/v1/finance/employee-contracts", new
        {
            teacherId = (Guid?)null,
            userId = AuthApiFactory.SecretaireId,
            type = "Vacataire",
            baseSalary = 0m,
            hourlyRate = 5_000m,
            transportAllowance = 0m
        });

        rehire.StatusCode.Should().Be(HttpStatusCode.Created);
    }
}
