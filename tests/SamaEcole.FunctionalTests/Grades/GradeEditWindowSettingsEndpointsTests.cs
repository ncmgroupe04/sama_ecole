using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Xunit;

namespace SamaEcole.FunctionalTests.Grades;

/// <summary>
/// Évolution N°1 — le Directeur règle la fenêtre de correction des notes (GradeEditWindowDays).
/// Défaut 7 jours, borné à 1–365, écriture réservée au Directeur comme tous les paramètres.
/// </summary>
public class GradeEditWindowSettingsEndpointsTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    public Task InitializeAsync() => factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private record Tokens(string AccessToken, string RefreshToken, int ExpiresIn);

    private async Task<string> LoginAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!.AccessToken;
    }

    private Task<string> DirecteurAsync() => LoginAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);

    private static object Body(int? gradeEditWindowDays) => new
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
        gradeEditWindowDays
    };

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, "/api/v1/schools/current/settings");
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(request);
    }

    private static async Task<int> WindowOfAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("gradeEditWindowDays").GetInt32();

    [Fact]
    public async Task A_School_Without_Any_Setting_Defaults_To_Seven_Days()
    {
        var response = await SendAsync(HttpMethod.Get, await DirecteurAsync());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await WindowOfAsync(response)).Should().Be(7);
    }

    [Fact]
    public async Task The_Directeur_Can_Change_The_Window_And_Read_It_Back()
    {
        var directeur = await DirecteurAsync();

        var put = await SendAsync(HttpMethod.Put, directeur, Body(gradeEditWindowDays: 3));
        put.StatusCode.Should().Be(HttpStatusCode.OK);
        (await WindowOfAsync(put)).Should().Be(3);

        (await WindowOfAsync(await SendAsync(HttpMethod.Get, directeur))).Should().Be(3);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(366)]
    public async Task A_Window_Outside_One_To_365_Days_Is_Refused_With_422(int days)
    {
        var response = await SendAsync(HttpMethod.Put, await DirecteurAsync(), Body(days));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task The_Secretariat_Cannot_Change_The_Window()
    {
        var secretaire = await LoginAsync(AuthApiFactory.SecretaireEmail, AuthApiFactory.SecretairePassword);

        var response = await SendAsync(HttpMethod.Put, secretaire, Body(gradeEditWindowDays: 30));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
