using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Xunit;

namespace SamaEcole.FunctionalTests.Finance;

/// <summary>
/// Volume 1 §14.5 — le Directeur peut retirer à la Finance le droit d'annuler ou de corriger une fiche de paie
/// (SchoolSettings.AllowFinanceToCancelPayslips, ouvert par défaut). Retiré : la Finance reçoit 403 sur l'annulation et la
/// régénération, mais peut toujours payer ; le Directeur garde la main dans tous les cas. Le serveur est l'arbitre, pas
/// l'écran.
/// </summary>
public class PayslipCancellationDelegationTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    public Task InitializeAsync() => factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private record Tokens(string AccessToken, int ExpiresIn);
    private record ContractDto(Guid Id, Guid? UserId, uint RowVersion);
    private record PayslipDto(Guid Id, Guid EmployeeContractId, int Month, int Year, string Status, uint RowVersion);

    private async Task<string> TokenAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!.AccessToken;
    }

    private Task<string> DirecteurTokenAsync() => TokenAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);
    private Task<string> FinanceTokenAsync() => TokenAsync(AuthApiFactory.FinanceEmail, AuthApiFactory.FinancePassword);

    private async Task<HttpResponseMessage> SendAsync(string token, HttpMethod method, string path, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await _client.SendAsync(request);
    }

    private async Task SetFinanceMayCancelPayslipsAsync(string directeurToken, bool allowed)
    {
        var response = await SendAsync(directeurToken, HttpMethod.Put, "/api/v1/schools/current/settings", new
        {
            gradingScale = "20",
            studentMatriculeFormat = "ELEV-{YEAR}-{SEQ:4}",
            teacherMatriculeFormat = "ENS-{YEAR}-{SEQ:3}",
            autoLogoutMinutes = 10,
            dateFormat = "dd/MM/yyyy",
            tuitionMonthsPerYear = 9,
            allowSecretaryToManageGrading = false,
            allowFinanceToModifyFees = false,
            allowFinanceToDeleteFees = false,
            allowFinanceToCancelPayslips = allowed
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>Contrat sur le compte Secrétariat semé, puis une fiche pour le mois donné ; renvoie la fiche telle que listée.</summary>
    private async Task<PayslipDto> CreatePayslipAsync(string directeurToken, int month)
    {
        var contractResponse = await SendAsync(directeurToken, HttpMethod.Post, "/api/v1/finance/employee-contracts", new
        {
            userId = AuthApiFactory.SecretaireId,
            type = "Permanent",
            baseSalary = 250_000m,
            hourlyRate = 0m,
            transportAllowance = 0m
        });
        if (contractResponse.StatusCode != HttpStatusCode.Created)
        {
            // Le contrat actif existe déjà d'un test précédent de la même classe : on le réutilise.
            contractResponse.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        }

        var contracts = (await (await SendAsync(directeurToken, HttpMethod.Get, "/api/v1/finance/employee-contracts"))
            .Content.ReadFromJsonAsync<List<ContractDto>>())!;
        var contract = contracts.First(c => c.UserId == AuthApiFactory.SecretaireId);

        var generate = await SendAsync(directeurToken, HttpMethod.Post, "/api/v1/finance/payroll",
            new { employeeContractId = contract.Id, month, year = 2026, hoursWorked = 0m, transportAllowance = 0m });
        generate.StatusCode.Should().Be(HttpStatusCode.Created);

        var payslips = (await (await SendAsync(directeurToken, HttpMethod.Get, $"/api/v1/finance/payroll?month={month}&year=2026"))
            .Content.ReadFromJsonAsync<List<PayslipDto>>())!;
        return payslips.Single(p => p.Status == "Generated");
    }

    [Fact]
    public async Task Finance_Can_Cancel_A_Payslip_By_Default()
    {
        var directeur = await DirecteurTokenAsync();
        var payslip = await CreatePayslipAsync(directeur, 3);

        var finance = await FinanceTokenAsync();
        var response = await SendAsync(finance, HttpMethod.Post, $"/api/v1/finance/payroll/{payslip.Id}/cancel",
            new { reason = "Montant erroné sur le contrat.", rowVersion = payslip.RowVersion });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Finance_Cannot_Cancel_Or_Regenerate_Once_The_Director_Withdraws_The_Right_But_Can_Still_Pay()
    {
        var directeur = await DirecteurTokenAsync();
        var payslip = await CreatePayslipAsync(directeur, 4);
        await SetFinanceMayCancelPayslipsAsync(directeur, allowed: false);

        var finance = await FinanceTokenAsync();

        var cancel = await SendAsync(finance, HttpMethod.Post, $"/api/v1/finance/payroll/{payslip.Id}/cancel",
            new { reason = "Tentative de la Finance.", rowVersion = payslip.RowVersion });
        cancel.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var regenerate = await SendAsync(finance, HttpMethod.Post, $"/api/v1/finance/payroll/{payslip.Id}/regenerate",
            new { reason = "Tentative de la Finance.", rowVersion = payslip.RowVersion });
        regenerate.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Payer n'est pas concerné par le retrait : la Finance garde ce geste.
        var pay = await SendAsync(finance, HttpMethod.Post, $"/api/v1/finance/payroll/{payslip.Id}/pay",
            new { paymentMethod = "Cash", date = "2026-04-30", rowVersion = payslip.RowVersion });
        pay.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_Director_Can_Always_Cancel_Even_When_The_Right_Is_Withdrawn_From_Finance()
    {
        var directeur = await DirecteurTokenAsync();
        var payslip = await CreatePayslipAsync(directeur, 5);
        await SetFinanceMayCancelPayslipsAsync(directeur, allowed: false);

        var response = await SendAsync(directeur, HttpMethod.Post, $"/api/v1/finance/payroll/{payslip.Id}/cancel",
            new { reason = "Fiche générée par erreur.", rowVersion = payslip.RowVersion });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
