using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Xunit;

namespace SamaEcole.FunctionalTests.Internat;

/// <summary>
/// Le module Internat est DÉSACTIVÉ par défaut (SchoolSettingsDefaults.IsInternatEnabled = false,
/// contrairement à Pédagogie/Finance) : contrairement à SchoolModuleTogglesEndpointsTests, ce test
/// prouve le refus PAR DÉFAUT, pas seulement après désactivation explicite.
/// </summary>
public class InternatModuleGateTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    public Task InitializeAsync() => factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private record Tokens(string AccessToken, string RefreshToken, int ExpiresIn);

    private record ApiError(string Code, string Message);

    private async Task<Tokens> LoginAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!;
    }

    private Task<Tokens> LoginAsDirecteurAsync() =>
        LoginAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string accessToken)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await _client.SendAsync(request);
    }

    private Task<HttpResponseMessage> GetDashboardAsync(string accessToken) =>
        SendAsync(HttpMethod.Get, "/api/v1/internat/dashboard", accessToken);

    /// <summary>Corps PUT complet — même convention que SchoolModuleTogglesEndpointsTests : le
    /// formulaire envoie toujours l'objet entier, jamais un correctif partiel.</summary>
    private static object ValidBody(bool isInternatEnabled) => new
    {
        gradingScale = "20",
        studentMatriculeFormat = "ELEV-{YEAR}-{SEQ:4}",
        teacherMatriculeFormat = "ENS-{YEAR}-{SEQ:3}",
        autoLogoutMinutes = 10,
        dateFormat = "dd/MM/yyyy",
        tuitionMonthsPerYear = 9,
        allowSecretaryToManageGrading = false,
        isPedagogyEnabled = true,
        isFinanceEnabled = true,
        isInternatEnabled,
        isCoranModuleEnabled = false
    };

    private async Task<HttpResponseMessage> PutSettingsAsync(string accessToken, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/schools/current/settings")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await _client.SendAsync(request);
    }

    [Fact]
    public async Task Dashboard_Is_Forbidden_By_Default_With_MODULE_DISABLED()
    {
        var directeur = await LoginAsDirecteurAsync();

        var response = await GetDashboardAsync(directeur.AccessToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var error = (await response.Content.ReadFromJsonAsync<ApiError>())!;
        error.Code.Should().Be("MODULE_DISABLED");
    }

    [Fact]
    public async Task Enabling_Internat_Grants_Access()
    {
        var directeur = await LoginAsDirecteurAsync();

        (await PutSettingsAsync(directeur.AccessToken, ValidBody(isInternatEnabled: true)))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await GetDashboardAsync(directeur.AccessToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
