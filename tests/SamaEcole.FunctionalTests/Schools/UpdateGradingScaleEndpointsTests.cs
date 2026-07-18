using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Xunit;

namespace SamaEcole.FunctionalTests.Schools;

/// <summary>
/// Ticket JGK-G02 — PUT /schools/current/settings/grading-scale, protégé par la politique
/// CanManageGradingScale (SamaEcole.Web.Authorization), pas par un rôle codé en dur : le Directeur
/// passe toujours, le Secrétariat seulement si SON école a activé
/// SchoolSettings.AllowSecretaryToManageGrading (fermé par défaut — voir
/// An_Enseignant_Must_Not_Update_The_Grading_Scale et A_Secretariat_Must_Not_Update_The_Grading_Scale_By_Default).
///
/// Endpoint dédié, distinct de PUT /schools/current/settings : seul le barème est délégable, le reste
/// des réglages (matricules, déconnexion auto, mensualités) reste Directeur seul — voir
/// SchoolSettingsEndpointsTests pour ces autres champs.
/// </summary>
public class UpdateGradingScaleEndpointsTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    public Task InitializeAsync() => factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private record Tokens(string AccessToken, string RefreshToken, int ExpiresIn);
    private record Settings(string GradingScale);

    private async Task<string> AccessTokenAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!.AccessToken;
    }

    private Task<string> DirecteurTokenAsync() =>
        AccessTokenAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);

    private Task<string> SecretaireTokenAsync() =>
        AccessTokenAsync(AuthApiFactory.SecretaireEmail, AuthApiFactory.SecretairePassword);

    private Task<string> EnseignantTokenAsync() =>
        AccessTokenAsync(AuthApiFactory.EnseignantEmail, AuthApiFactory.EnseignantPassword);

    private async Task<HttpResponseMessage> PutGradingScaleAsync(string accessToken, string gradingScale)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/schools/current/settings/grading-scale")
        {
            Content = JsonContent.Create(new { gradingScale })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await _client.SendAsync(request);
    }

    private async Task<Settings> GetSettingsAsync(string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/schools/current/settings");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var response = await _client.SendAsync(request);
        return (await response.Content.ReadFromJsonAsync<Settings>())!;
    }

    /// <summary>Seul geste du Directeur capable d'activer la délégation : PUT /schools/current/settings.</summary>
    private async Task EnableSecretaryDelegationAsync(string directeurToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/schools/current/settings")
        {
            Content = JsonContent.Create(new
            {
                gradingScale = "20",
                studentMatriculeFormat = "ELEV-{YEAR}-{SEQ:4}",
                teacherMatriculeFormat = "ENS-{YEAR}-{SEQ:3}",
                autoLogoutMinutes = 10,
                dateFormat = "dd/MM/yyyy",
                tuitionMonthsPerYear = 9,
                allowSecretaryToManageGrading = true
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", directeurToken);

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_Directeur_Can_Update_The_Grading_Scale()
    {
        var directeur = await DirecteurTokenAsync();

        var response = await PutGradingScaleAsync(directeur, "10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetSettingsAsync(directeur)).GradingScale.Should().Be("10");
    }

    [Fact]
    public async Task A_Directeur_Can_Update_The_Grading_Scale_Even_Without_Delegation_Enabled()
    {
        // Le Directeur n'a jamais besoin de la délégation : elle ne conditionne QUE le Secrétariat.
        var directeur = await DirecteurTokenAsync();

        var response = await PutGradingScaleAsync(directeur, "10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_Secretariat_Must_Not_Update_The_Grading_Scale_By_Default()
    {
        // Délégation FACULTATIVE : fermée tant que le Directeur ne l'a pas explicitement activée
        // (SchoolSettingsDefaults.AllowSecretaryToManageGrading = false).
        var secretaire = await SecretaireTokenAsync();

        var response = await PutGradingScaleAsync(secretaire, "10");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_Secretariat_Can_Update_The_Grading_Scale_Once_The_Directeur_Enables_Delegation()
    {
        var directeur = await DirecteurTokenAsync();
        await EnableSecretaryDelegationAsync(directeur);

        var secretaire = await SecretaireTokenAsync();
        var response = await PutGradingScaleAsync(secretaire, "10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetSettingsAsync(directeur)).GradingScale.Should().Be("10");
    }

    [Fact]
    public async Task A_Secretariat_Loses_Access_Again_Once_The_Directeur_Disables_Delegation()
    {
        var directeur = await DirecteurTokenAsync();
        await EnableSecretaryDelegationAsync(directeur);

        // Le Directeur revient sur sa décision.
        var disableRequest = new HttpRequestMessage(HttpMethod.Put, "/api/v1/schools/current/settings")
        {
            Content = JsonContent.Create(new
            {
                gradingScale = "20",
                studentMatriculeFormat = "ELEV-{YEAR}-{SEQ:4}",
                teacherMatriculeFormat = "ENS-{YEAR}-{SEQ:3}",
                autoLogoutMinutes = 10,
                dateFormat = "dd/MM/yyyy",
                tuitionMonthsPerYear = 9,
                allowSecretaryToManageGrading = false
            })
        };
        disableRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", directeur);
        (await _client.SendAsync(disableRequest)).StatusCode.Should().Be(HttpStatusCode.OK);

        var secretaire = await SecretaireTokenAsync();
        var response = await PutGradingScaleAsync(secretaire, "10");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_Enseignant_Must_Not_Update_The_Grading_Scale()
    {
        var directeur = await DirecteurTokenAsync();
        await EnableSecretaryDelegationAsync(directeur);

        // La délégation ne concerne QUE le Secrétariat : elle n'ouvre rien à l'Enseignant.
        var enseignant = await EnseignantTokenAsync();

        var response = await PutGradingScaleAsync(enseignant, "10");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_Secretariat_Must_Not_Reach_The_Other_Settings_Through_This_Endpoint()
    {
        // Le reste des réglages (matricules, déconnexion auto, mensualités) reste Directeur seul,
        // même délégation activée : ce n'est pas cet endpoint, réservé au barème, qui pourrait les
        // exposer, mais on vérifie ici que le PUT bundlé leur reste bien fermé.
        var directeur = await DirecteurTokenAsync();
        await EnableSecretaryDelegationAsync(directeur);

        var secretaire = await SecretaireTokenAsync();

        var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/schools/current/settings")
        {
            Content = JsonContent.Create(new
            {
                gradingScale = "10",
                studentMatriculeFormat = "ELEV-{YEAR}-{SEQ:4}",
                teacherMatriculeFormat = "ENS-{YEAR}-{SEQ:3}",
                autoLogoutMinutes = 10,
                dateFormat = "dd/MM/yyyy",
                tuitionMonthsPerYear = 9,
                allowSecretaryToManageGrading = true
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secretaire);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("15")]
    [InlineData("abc")]
    public async Task An_Invalid_Grading_Scale_Must_Be_Rejected(string gradingScale)
    {
        var directeur = await DirecteurTokenAsync();

        var response = await PutGradingScaleAsync(directeur, gradingScale);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Updating_The_Grading_Scale_Without_A_Token_Should_Return_401()
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/schools/current/settings/grading-scale")
        {
            Content = JsonContent.Create(new { gradingScale = "10" })
        };

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
