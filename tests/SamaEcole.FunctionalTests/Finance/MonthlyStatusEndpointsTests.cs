using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace SamaEcole.FunctionalTests.Finance;

/// <summary>
/// Situation des mensualités — permissions de bout en bout : Directeur toujours, Finance seulement si le Directeur a
/// ouvert l'interrupteur (fermé par défaut), retrait immédiat, Secrétariat/Enseignant jamais, et seul le Directeur
/// peut modifier l'interrupteur.
/// </summary>
public class MonthlyStatusEndpointsTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public MonthlyStatusEndpointsTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public Task InitializeAsync() => _factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private record Tokens(string AccessToken, int ExpiresIn);

    private async Task<string> TokenAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!.AccessToken;
    }

    private Task<string> DirecteurAsync() => TokenAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);
    private Task<string> FinanceAsync() => TokenAsync(AuthApiFactory.FinanceEmail, AuthApiFactory.FinancePassword);
    private Task<string> SecretaireAsync() => TokenAsync(AuthApiFactory.SecretaireEmail, AuthApiFactory.SecretairePassword);
    private Task<string> EnseignantAsync() => TokenAsync(AuthApiFactory.EnseignantEmail, AuthApiFactory.EnseignantPassword);

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await _client.SendAsync(request);
    }

    private async Task<Guid> CreateYearAsync(string directeur)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var response = await SendAsync(HttpMethod.Post, "/api/v1/school-years", directeur, new
        {
            label = $"{today.Year}-{today.Year + 1}",
            startDate = today.AddDays(-30).ToString("yyyy-MM-dd"),
            endDate = today.AddDays(+300).ToString("yyyy-MM-dd")
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private Task<HttpResponseMessage> SetSwitchAsync(string token, bool allow) =>
        SendAsync(HttpMethod.Put, "/api/v1/schools/current/settings", token, new
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
            allowFinanceToViewMonthlyStatus = allow
        });

    private static string Url(Guid yearId, int month = 10) =>
        $"/api/v1/finance/monthly-status?schoolYearId={yearId}&month={month}";

    [Fact]
    public async Task The_Director_Always_Has_Access_And_Gets_The_Summary_Shape()
    {
        var directeur = await DirecteurAsync();
        var yearId = await CreateYearAsync(directeur);

        var response = await SendAsync(HttpMethod.Get, Url(yearId), directeur);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("summary").GetProperty("totalStudents").GetInt32().Should().Be(0);
        body.GetProperty("paid").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Finance_Is_Refused_By_Default_Then_Allowed_When_The_Director_Opens_It_And_Refused_Again_At_Once_When_Closed()
    {
        var directeur = await DirecteurAsync();
        var finance = await FinanceAsync();
        var yearId = await CreateYearAsync(directeur);

        (await SendAsync(HttpMethod.Get, Url(yearId), finance)).StatusCode
            .Should().Be(HttpStatusCode.Forbidden, "l'interrupteur est fermé par défaut");

        (await SetSwitchAsync(directeur, true)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Get, Url(yearId), finance)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Get, Url(yearId) + "&classroomId=" + Guid.NewGuid(), finance)).StatusCode
            .Should().Be(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Get, $"/api/v1/finance/monthly-status/excel?schoolYearId={yearId}&month=10", finance))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Même jeton Finance, déjà émis : l'accès tombe dès que le Directeur ferme l'interrupteur.
        (await SetSwitchAsync(directeur, false)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Get, Url(yearId), finance)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Get, $"/api/v1/finance/monthly-status/excel?schoolYearId={yearId}&month=10", finance))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Secretariat_And_Teacher_Are_Refused_Even_When_The_Switch_Is_On()
    {
        var directeur = await DirecteurAsync();
        var yearId = await CreateYearAsync(directeur);
        (await SetSwitchAsync(directeur, true)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await SendAsync(HttpMethod.Get, Url(yearId), await SecretaireAsync())).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Get, Url(yearId), await EnseignantAsync())).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Finance_Cannot_Change_The_Switch_Itself_And_Other_Finance_Rights_Are_Untouched()
    {
        var directeur = await DirecteurAsync();
        var finance = await FinanceAsync();

        (await SetSwitchAsync(finance, true)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Activer l'interrupteur ne change pas les autres délégations de la Finance.
        (await SetSwitchAsync(directeur, true)).StatusCode.Should().Be(HttpStatusCode.OK);
        var settings = await (await SendAsync(HttpMethod.Get, "/api/v1/schools/current/settings", directeur))
            .Content.ReadFromJsonAsync<JsonElement>();
        settings.GetProperty("allowFinanceToViewMonthlyStatus").GetBoolean().Should().BeTrue();
        settings.GetProperty("allowFinanceToModifyFees").GetBoolean().Should().BeFalse();
        settings.GetProperty("allowFinanceToDeleteFees").GetBoolean().Should().BeFalse();
        settings.GetProperty("allowFinanceToCancelPayments").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task The_Switch_Is_Off_By_Default_And_An_Old_Client_Omitting_It_Does_Not_Reset_It()
    {
        var directeur = await DirecteurAsync();

        var before = await (await SendAsync(HttpMethod.Get, "/api/v1/schools/current/settings", directeur))
            .Content.ReadFromJsonAsync<JsonElement>();
        before.GetProperty("allowFinanceToViewMonthlyStatus").GetBoolean().Should().BeFalse();

        (await SetSwitchAsync(directeur, true)).StatusCode.Should().Be(HttpStatusCode.OK);

        var oldClient = await SendAsync(HttpMethod.Put, "/api/v1/schools/current/settings", directeur, new
        {
            gradingScale = "20",
            studentMatriculeFormat = "ELEV-{YEAR}-{SEQ:4}",
            teacherMatriculeFormat = "ENS-{YEAR}-{SEQ:3}",
            autoLogoutMinutes = 10,
            dateFormat = "dd/MM/yyyy",
            tuitionMonthsPerYear = 9,
            allowSecretaryToManageGrading = false,
            allowFinanceToModifyFees = false,
            allowFinanceToDeleteFees = false
        });
        oldClient.StatusCode.Should().Be(HttpStatusCode.OK);

        var after = await (await SendAsync(HttpMethod.Get, "/api/v1/schools/current/settings", directeur))
            .Content.ReadFromJsonAsync<JsonElement>();
        after.GetProperty("allowFinanceToViewMonthlyStatus").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task The_First_Month_Covers_Last_Month_Setting_Is_Off_By_Default_Director_Only_And_Preserved_By_Old_Clients()
    {
        var directeur = await DirecteurAsync();
        var finance = await FinanceAsync();

        object Body(bool? first) => first is null
            ? new
            {
                gradingScale = "20", studentMatriculeFormat = "ELEV-{YEAR}-{SEQ:4}", teacherMatriculeFormat = "ENS-{YEAR}-{SEQ:3}",
                autoLogoutMinutes = 10, dateFormat = "dd/MM/yyyy", tuitionMonthsPerYear = 9,
                allowSecretaryToManageGrading = false, allowFinanceToModifyFees = false, allowFinanceToDeleteFees = false
            }
            : new
            {
                gradingScale = "20", studentMatriculeFormat = "ELEV-{YEAR}-{SEQ:4}", teacherMatriculeFormat = "ENS-{YEAR}-{SEQ:3}",
                autoLogoutMinutes = 10, dateFormat = "dd/MM/yyyy", tuitionMonthsPerYear = 9,
                allowSecretaryToManageGrading = false, allowFinanceToModifyFees = false, allowFinanceToDeleteFees = false,
                firstMonthlyPaymentCoversLastMonth = first.Value
            };

        async Task<bool> ReadAsync() => (await (await SendAsync(HttpMethod.Get, "/api/v1/schools/current/settings", directeur))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("firstMonthlyPaymentCoversLastMonth").GetBoolean();

        (await ReadAsync()).Should().BeFalse();
        (await SendAsync(HttpMethod.Put, "/api/v1/schools/current/settings", finance, Body(true))).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Put, "/api/v1/schools/current/settings", directeur, Body(true))).StatusCode
            .Should().Be(HttpStatusCode.OK);
        (await ReadAsync()).Should().BeTrue();
        (await SendAsync(HttpMethod.Put, "/api/v1/schools/current/settings", directeur, Body(null))).StatusCode
            .Should().Be(HttpStatusCode.OK);
        (await ReadAsync()).Should().BeTrue("un ancien client qui n'envoie pas le champ ne le réinitialise pas");
    }

    [Fact]
    public async Task An_Invalid_Month_Is_Rejected_And_An_Unknown_Year_Is_Not_Found()
    {
        var directeur = await DirecteurAsync();
        var yearId = await CreateYearAsync(directeur);

        (await SendAsync(HttpMethod.Get, Url(yearId, month: 13), directeur)).StatusCode
            .Should().Be(HttpStatusCode.UnprocessableEntity);
        (await SendAsync(HttpMethod.Get, Url(Guid.NewGuid()), directeur)).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }
}
