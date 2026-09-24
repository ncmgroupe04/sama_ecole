using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Xunit;

namespace SamaEcole.FunctionalTests.Quran;

public class QuranEndpointsTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>, IAsyncLifetime
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

    private static object ValidSettingsBody() => new
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
        isCoranModuleEnabled = true
    };

    private async Task EnableModuleAsync(string directeurToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/schools/current/settings")
        {
            Content = JsonContent.Create(ValidSettingsBody())
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", directeurToken);
        (await _client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string accessToken, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await _client.SendAsync(request);
    }

    [Fact]
    public async Task Secretariat_Cannot_Create_A_Progress_Entry()
    {
        var directeur = await LoginAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);
        await EnableModuleAsync(directeur);
        var secretariat = await LoginAsync(AuthApiFactory.SecretaireEmail, AuthApiFactory.SecretairePassword);

        var response = await SendAsync(HttpMethod.Post, "/api/v1/quran/progress", secretariat, new
        {
            studentId = Guid.NewGuid(), juzNumber = 1, hizbNumber = 1, surahNumber = 1,
            status = "InProcess", evaluationDate = (DateOnly?)null, notes = (string?)null
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Secretariat_Can_Read_The_Progress_List()
    {
        var directeur = await LoginAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);
        await EnableModuleAsync(directeur);
        var secretariat = await LoginAsync(AuthApiFactory.SecretaireEmail, AuthApiFactory.SecretairePassword);

        var response = await SendAsync(HttpMethod.Get, $"/api/v1/quran/progress?studentId={Guid.NewGuid()}", secretariat);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Enseignant_Can_Create_An_Evaluation()
    {
        var directeur = await LoginAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);
        await EnableModuleAsync(directeur);
        var enseignant = await LoginAsync(AuthApiFactory.EnseignantEmail, AuthApiFactory.EnseignantPassword);

        // AuthApiFactory ne crée pas d'élève par défaut : un StudentId inconnu doit être refusé en
        // 422 (élève introuvable), preuve que le rôle Enseignant a bien franchi la garde 403 pour
        // atteindre le Handler.
        var response = await SendAsync(HttpMethod.Post, "/api/v1/quran/evaluations", enseignant, new
        {
            studentId = Guid.NewGuid(), evaluationDate = DateOnly.FromDateTime(DateTime.UtcNow),
            memoryMistakes = 0, tajwidMistakes = 0, hesitations = 0, finalScore = 18
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }
}
