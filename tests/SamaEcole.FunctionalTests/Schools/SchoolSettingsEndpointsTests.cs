using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Xunit;

namespace SamaEcole.FunctionalTests.Schools;

/// <summary>
/// Ticket JGK-B02 — GET/PUT /schools/current/settings.
/// Critères : valeurs par défaut appliquées à la création ; seul le Directeur peut modifier.
/// </summary>
public class SchoolSettingsEndpointsTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    public Task InitializeAsync() => factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private record Tokens(string AccessToken, string RefreshToken, int ExpiresIn);

    private record Settings(
        string GradingScale,
        string StudentMatriculeFormat,
        string TeacherMatriculeFormat,
        int AutoLogoutMinutes,
        string DateFormat);

    private async Task<Tokens> LoginAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!;
    }

    private Task<Tokens> LoginAsDirecteurAsync() =>
        LoginAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);

    private async Task<HttpResponseMessage> GetSettingsAsync(string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/schools/current/settings");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> PutSettingsAsync(string accessToken, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/schools/current/settings")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await _client.SendAsync(request);
    }

    private static object ValidBody(
        string gradingScale = "20",
        string studentFormat = "ELEV-{YEAR}-{SEQ:4}",
        int autoLogout = 10) => new
        {
            gradingScale,
            studentMatriculeFormat = studentFormat,
            teacherMatriculeFormat = "ENS-{YEAR}-{SEQ:3}",
            autoLogoutMinutes = autoLogout,
            dateFormat = "dd/MM/yyyy"
        };

    [Fact]
    public async Task A_School_Should_Report_Its_Default_Settings()
    {
        var directeur = await LoginAsDirecteurAsync();

        var response = await GetSettingsAsync(directeur.AccessToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var settings = (await response.Content.ReadFromJsonAsync<Settings>())!;

        settings.GradingScale.Should().Be("20");
        settings.StudentMatriculeFormat.Should().Be("ELEV-{YEAR}-{SEQ:4}");
        settings.AutoLogoutMinutes.Should().Be(10);
        settings.DateFormat.Should().Be("dd/MM/yyyy");
    }

    [Fact]
    public async Task A_Directeur_Should_Update_The_Settings()
    {
        var directeur = await LoginAsDirecteurAsync();

        var response = await PutSettingsAsync(
            directeur.AccessToken, ValidBody(gradingScale: "10", studentFormat: "BAOBAB-{SEQ:5}", autoLogout: 30));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Relu depuis la base, pas seulement renvoyé par le PUT.
        var reread = (await (await GetSettingsAsync(directeur.AccessToken)).Content.ReadFromJsonAsync<Settings>())!;

        reread.GradingScale.Should().Be("10");
        reread.StudentMatriculeFormat.Should().Be("BAOBAB-{SEQ:5}");
        reread.AutoLogoutMinutes.Should().Be(30);
    }

    [Fact]
    public async Task A_Secretariat_Must_Not_Be_Allowed_To_Modify_The_Settings()
    {
        // Critère du ticket : « seul le Directeur peut modifier ».
        var secretaire = await LoginAsync(AuthApiFactory.SecretaireEmail, AuthApiFactory.SecretairePassword);

        var response = await PutSettingsAsync(secretaire.AccessToken, ValidBody());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_Secretariat_Should_Still_Be_Able_To_READ_The_Settings()
    {
        // Le format de date et le barème pilotent l'affichage de TOUS les écrans : les réserver au
        // Directeur ferait afficher les dates au mauvais format à tous les autres rôles.
        var secretaire = await LoginAsync(AuthApiFactory.SecretaireEmail, AuthApiFactory.SecretairePassword);

        var response = await GetSettingsAsync(secretaire.AccessToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_Format_Without_A_Sequence_Must_Be_Rejected()
    {
        // Sans {SEQ}, tous les élèves recevraient le même matricule — la panne n'apparaîtrait qu'à la
        // deuxième inscription, en pleine rentrée. On refuse le réglage plutôt que de subir la panne.
        var directeur = await LoginAsDirecteurAsync();

        var response = await PutSettingsAsync(
            directeur.AccessToken, ValidBody(studentFormat: "ELEV-{YEAR}"));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Theory]
    [InlineData("15")]  // barème hors des valeurs admises
    [InlineData("abc")]
    public async Task An_Invalid_Grading_Scale_Must_Be_Rejected(string gradingScale)
    {
        var directeur = await LoginAsDirecteurAsync();

        var response = await PutSettingsAsync(directeur.AccessToken, ValidBody(gradingScale: gradingScale));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task An_Absurd_AutoLogout_Must_Be_Rejected()
    {
        // 0 minute déconnecterait l'utilisateur en boucle.
        var directeur = await LoginAsDirecteurAsync();

        var response = await PutSettingsAsync(directeur.AccessToken, ValidBody(autoLogout: 0));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Reading_The_Settings_Without_A_Token_Should_Return_401()
    {
        var response = await _client.GetAsync("/api/v1/schools/current/settings");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
