using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Xunit;

namespace SamaEcole.FunctionalTests.Finance;

/// <summary>
/// Paie « sans retenues » (structure qui ne cotise pas, ex. un Daara) via l'API réelle : le régime se règle par contrat, les
/// deux régimes coexistent dans une même école, chaque fiche garde un instantané du régime utilisé, et ne pas fournir le champ
/// ne change jamais le régime d'un contrat existant.
/// </summary>
public class PayrollWithoutDeductionsEndpointsTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    public Task InitializeAsync() => factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private record Tokens(string AccessToken, int ExpiresIn);
    private record ContractDto(
        Guid Id, Guid? UserId, string Type, decimal BaseSalary, decimal TransportAllowance,
        DateOnly? EndDate, uint RowVersion, bool ApplyStatutoryDeductions);
    private record FicheDto(
        Guid Id, Guid EmployeeContractId, decimal GrossSalary, decimal TransportAllowance, decimal IpresEmployee,
        decimal IpresEmployer, decimal CssEmployer, decimal Vrs, decimal Brs, decimal NetSalary, bool DeductionsApplied);

    private async Task<string> DirecteurTokenAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = AuthApiFactory.DirecteurEmail, password = AuthApiFactory.DirecteurPassword });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!.AccessToken;
    }

    private async Task<HttpResponseMessage> SendAsync(string token, HttpMethod method, string path, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(request);
    }

    /// <summary>Contrat Permanent sur le compte Secrétariat semé ; <paramref name="applyDeductions"/> null = champ omis (défaut serveur).</summary>
    private async Task<ContractDto> CreateContractAsync(string token, decimal baseSalary, decimal transport, bool? applyDeductions)
    {
        var body = new Dictionary<string, object?>
        {
            ["userId"] = AuthApiFactory.SecretaireId,
            ["type"] = "Permanent",
            ["baseSalary"] = baseSalary,
            ["hourlyRate"] = 0m,
            ["transportAllowance"] = transport
        };
        if (applyDeductions is { } apply) body["applyStatutoryDeductions"] = apply;

        var created = await SendAsync(token, HttpMethod.Post, "/api/v1/finance/employee-contracts", body);
        created.StatusCode.Should().Be(HttpStatusCode.Created);

        return await GetContractAsync(token);
    }

    private async Task<ContractDto> GetContractAsync(string token)
    {
        var list = await SendAsync(token, HttpMethod.Get, "/api/v1/finance/employee-contracts");
        return (await list.Content.ReadFromJsonAsync<List<ContractDto>>())!.Single(c => c.UserId == AuthApiFactory.SecretaireId);
    }

    private async Task<FicheDto> GenerateAndReadAsync(string token, Guid contractId, int month)
    {
        var generate = await SendAsync(token, HttpMethod.Post, "/api/v1/finance/payroll",
            new { employeeContractId = contractId, month, year = 2026, hoursWorked = 0, transportAllowance = 5_000 });
        generate.StatusCode.Should().Be(HttpStatusCode.Created);

        var list = await SendAsync(token, HttpMethod.Get, $"/api/v1/finance/payroll?month={month}&year=2026");
        return (await list.Content.ReadFromJsonAsync<List<FicheDto>>())!.Single(f => f.EmployeeContractId == contractId);
    }

    [Fact]
    public async Task A_Contract_Without_Deductions_Pays_The_Gross_Salary_Plus_Transport_With_Nothing_Withheld()
    {
        var director = await DirecteurTokenAsync();
        var contract = await CreateContractAsync(director, baseSalary: 40_000m, transport: 0m, applyDeductions: false);
        contract.ApplyStatutoryDeductions.Should().BeFalse();

        var fiche = await GenerateAndReadAsync(director, contract.Id, month: 3);

        fiche.DeductionsApplied.Should().BeFalse();
        fiche.GrossSalary.Should().Be(40_000m);
        fiche.NetSalary.Should().Be(45_000m, "40 000 de salaire + 5 000 de prime de transport, aucune retenue");
        fiche.IpresEmployee.Should().Be(0m);
        fiche.IpresEmployer.Should().Be(0m);
        fiche.CssEmployer.Should().Be(0m);
        fiche.Vrs.Should().Be(0m);
        fiche.Brs.Should().Be(0m);
    }

    [Fact]
    public async Task A_Contract_Created_Without_The_Field_Keeps_The_Standard_Deductions()
    {
        // Le comportement existant ne doit pas changer pour les écoles qui n'utilisent pas ce réglage.
        var director = await DirecteurTokenAsync();
        var contract = await CreateContractAsync(director, baseSalary: 250_000m, transport: 0m, applyDeductions: null);
        contract.ApplyStatutoryDeductions.Should().BeTrue();

        var fiche = await GenerateAndReadAsync(director, contract.Id, month: 4);

        fiche.DeductionsApplied.Should().BeTrue();
        fiche.IpresEmployee.Should().BeGreaterThan(0m);
        fiche.Brs.Should().BeGreaterThan(0m);
        fiche.NetSalary.Should().BeLessThan(fiche.GrossSalary + fiche.TransportAllowance);
    }

    [Fact]
    public async Task Switching_The_Regime_Applies_To_New_Payslips_But_Never_Rewrites_An_Existing_One()
    {
        var director = await DirecteurTokenAsync();
        var contract = await CreateContractAsync(director, baseSalary: 250_000m, transport: 0m, applyDeductions: true);
        var before = await GenerateAndReadAsync(director, contract.Id, month: 5);
        before.DeductionsApplied.Should().BeTrue();

        var update = await SendAsync(director, HttpMethod.Patch, $"/api/v1/finance/employee-contracts/{contract.Id}", new
        {
            baseSalary = 250_000m, hourlyRate = 0m, transportAllowance = 0m,
            reason = "Passage en paie sans retenues : l'établissement ne cotise pas.",
            rowVersion = contract.RowVersion, applyStatutoryDeductions = false
        });
        update.StatusCode.Should().Be(HttpStatusCode.OK);

        (await GetContractAsync(director)).ApplyStatutoryDeductions.Should().BeFalse();

        var stillBefore = (await (await SendAsync(director, HttpMethod.Get, "/api/v1/finance/payroll?month=5&year=2026"))
            .Content.ReadFromJsonAsync<List<FicheDto>>())!.Single();
        stillBefore.DeductionsApplied.Should().BeTrue("une fiche déjà éditée garde son régime");
        stillBefore.NetSalary.Should().Be(before.NetSalary);

        var after = await GenerateAndReadAsync(director, contract.Id, month: 6);
        after.DeductionsApplied.Should().BeFalse();
        after.NetSalary.Should().Be(255_000m);
    }

    [Fact]
    public async Task Updating_A_Contract_Without_The_Field_Must_Not_Change_Its_Regime()
    {
        // Un client qui ne connaît pas encore ce champ ne doit jamais rebasculer un contrat « sans retenues » vers « avec ».
        var director = await DirecteurTokenAsync();
        var contract = await CreateContractAsync(director, baseSalary: 40_000m, transport: 0m, applyDeductions: false);

        var update = await SendAsync(director, HttpMethod.Patch, $"/api/v1/finance/employee-contracts/{contract.Id}", new
        {
            baseSalary = 45_000m, hourlyRate = 0m, transportAllowance = 0m,
            reason = "Augmentation annuelle actée en conseil.", rowVersion = contract.RowVersion
        });
        update.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await GetContractAsync(director);
        updated.BaseSalary.Should().Be(45_000m);
        updated.ApplyStatutoryDeductions.Should().BeFalse();
    }

    [Fact]
    public async Task Regenerating_A_Payslip_Keeps_The_Regime_Of_The_Contract()
    {
        var director = await DirecteurTokenAsync();
        var contract = await CreateContractAsync(director, baseSalary: 40_000m, transport: 5_000m, applyDeductions: false);
        var fiche = await GenerateAndReadAsync(director, contract.Id, month: 7);

        // Se relit avec son jeton de version pour la régénération.
        var raw = await SendAsync(director, HttpMethod.Get, "/api/v1/finance/payroll?month=7&year=2026");
        var rowVersion = (await raw.Content.ReadFromJsonAsync<List<System.Text.Json.JsonElement>>())!
            .Single().GetProperty("rowVersion").GetUInt32();

        var regenerate = await SendAsync(director, HttpMethod.Post, $"/api/v1/finance/payroll/{fiche.Id}/regenerate", new
        {
            reason = "Heures corrigées après vérification.", rowVersion
        });
        regenerate.StatusCode.Should().Be(HttpStatusCode.OK, await regenerate.Content.ReadAsStringAsync());

        var current = (await (await SendAsync(director, HttpMethod.Get, "/api/v1/finance/payroll?month=7&year=2026"))
            .Content.ReadFromJsonAsync<List<System.Text.Json.JsonElement>>())!
            .Single(f => f.GetProperty("status").GetString() != "Cancelled");
        current.GetProperty("deductionsApplied").GetBoolean().Should().BeFalse();
        current.GetProperty("netSalary").GetDecimal().Should().Be(45_000m);
    }
}
