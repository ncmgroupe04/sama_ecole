using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Xunit;

namespace SamaEcole.FunctionalTests.Quran;

/// <summary>
/// Le module Coran est DÉSACTIVÉ par défaut (SchoolSettingsDefaults.IsCoranModuleEnabled = false) —
/// même patron que InternatModuleGateTests : preuve du refus PAR DÉFAUT, pas seulement après
/// désactivation explicite.
/// </summary>
public class QuranModuleGateTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    public Task InitializeAsync() => factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private record Tokens(string AccessToken, string RefreshToken, int ExpiresIn);
    private record ApiError(string Code, string Message);

    private async Task<Tokens> LoginAsDirecteurAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = AuthApiFactory.DirecteurEmail, password = AuthApiFactory.DirecteurPassword });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!;
    }

    private async Task<HttpResponseMessage> GetAsync(string path, string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await _client.SendAsync(request);
    }

    private static object ValidBody(bool isCoranModuleEnabled) => new
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
        isInternatEnabled = false,
        isCoranModuleEnabled
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
    public async Task Progress_Endpoint_Is_Forbidden_By_Default_With_MODULE_DISABLED()
    {
        var directeur = await LoginAsDirecteurAsync();

        var response = await GetAsync($"/api/v1/quran/progress?studentId={Guid.NewGuid()}", directeur.AccessToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var error = (await response.Content.ReadFromJsonAsync<ApiError>())!;
        error.Code.Should().Be("MODULE_DISABLED");
    }

    [Fact]
    public async Task Enabling_The_Module_Grants_Access()
    {
        var directeur = await LoginAsDirecteurAsync();

        (await PutSettingsAsync(directeur.AccessToken, ValidBody(isCoranModuleEnabled: true)))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await GetAsync($"/api/v1/quran/progress?studentId={Guid.NewGuid()}", directeur.AccessToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
