using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Xunit;

namespace SamaEcole.FunctionalTests.Exports;

/// <summary>POST /api/v1/exports/school-data — Directeur seul, mot de passe exigé dans le corps, archive ZIP en réponse.</summary>
public class SchoolDataExportEndpointTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private const string Route = "/api/v1/exports/school-data";
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

    private async Task<HttpResponseMessage> ExportAsync(string? token, object? body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Route) { Content = JsonContent.Create(body ?? new { }) };
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(request);
    }

    [Fact]
    public async Task An_Anonymous_Caller_Is_Rejected()
    {
        (await ExportAsync(null, new { password = "x" })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("secretariat")]
    [InlineData("finance")]
    [InlineData("enseignant")]
    public async Task Only_The_Directeur_May_Export(string role)
    {
        var (email, password) = role switch
        {
            "secretariat" => (AuthApiFactory.SecretaireEmail, AuthApiFactory.SecretairePassword),
            "finance" => (AuthApiFactory.FinanceEmail, AuthApiFactory.FinancePassword),
            _ => (AuthApiFactory.EnseignantEmail, AuthApiFactory.EnseignantPassword)
        };
        var token = await LoginAsync(email, password);

        // Même avec le bon mot de passe du compte : le rôle suffit à refuser.
        (await ExportAsync(token, new { password })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_Missing_Or_Wrong_Password_Is_A_422_On_The_Password_Field_Not_A_Logout()
    {
        var token = await LoginAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);

        var missing = await ExportAsync(token, new { });
        var wrong = await ExportAsync(token, new { password = "mauvais-mot-de-passe" });

        missing.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        wrong.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await wrong.Content.ReadAsStringAsync()).Should().Contain("Mot de passe incorrect");
    }

    [Fact]
    public async Task The_Directeur_With_The_Right_Password_Downloads_A_Zip_Without_Any_Secret()
    {
        var token = await LoginAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);

        var response = await ExportAsync(token, new { password = AuthApiFactory.DirecteurPassword });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/zip");
        response.Content.Headers.ContentDisposition!.FileName.Should().StartWith("export-").And.EndWith(".zip");

        await using var content = await response.Content.ReadAsStreamAsync();
        using var zip = new ZipArchive(content, ZipArchiveMode.Read);
        var names = zip.Entries.Select(e => e.FullName).ToList();
        names.Should().Contain(["manifest.json", "LISEZMOI.txt"]);
        names.Should().NotContain(n => n.Contains("refresh_tokens") || n.Contains("password_reset_tokens"));

        foreach (var entry in zip.Entries.Where(e => e.FullName.EndsWith(".csv")))
        {
            using var reader = new StreamReader(entry.Open());
            var header = await reader.ReadLineAsync() ?? string.Empty;
            header.Should().NotContainEquivalentOf("PasswordHash", $"{entry.FullName} ne doit jamais exposer de hachage");
        }
    }
}
