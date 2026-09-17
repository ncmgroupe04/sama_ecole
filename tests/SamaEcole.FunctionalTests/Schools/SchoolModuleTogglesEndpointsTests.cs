using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Xunit;

namespace SamaEcole.FunctionalTests.Schools;

/// <summary>
/// Feature flags par établissement (SchoolModule, [RequireModule]) — PUT /schools/current/settings
/// pilote RÉELLEMENT l'accès aux modules Pédagogie et Finance, pas seulement leur affichage dans la
/// sidebar (le masquage côté client, sidebarNav() dans auth.js, n'est qu'un confort d'affichage).
///
/// Contrats prouvés de bout en bout (HTTP → MediatR → PostgreSQL réel) :
///   * Pédagogie et Finance sont activés par défaut (socle métier déjà livré) ;
///   * désactiver un module renvoie 403 MODULE_DISABLED sur les routes qui en dépendent ;
///   * réactiver le module restaure l'accès sans autre changement ;
///   * Classes et Enseignants restent accessibles même Pédagogie désactivée — arbitrage acté avec le
///     client : la Paie et les Frais par classe en dépendent (voir _Layout.cshtml) ;
///   * seul le Directeur peut modifier ces réglages, comme le reste de PUT /schools/current/settings.
/// </summary>
public class SchoolModuleTogglesEndpointsTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    public Task InitializeAsync() => factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private record Tokens(string AccessToken, string RefreshToken, int ExpiresIn);

    private record Settings(
        bool IsPedagogyEnabled, bool IsFinanceEnabled, bool IsInternatEnabled, bool IsCoranModuleEnabled);

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

    private Task<HttpResponseMessage> GetSettingsAsync(string accessToken) =>
        SendAsync(HttpMethod.Get, "/api/v1/schools/current/settings", accessToken);

    private async Task<HttpResponseMessage> PutSettingsAsync(string accessToken, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/schools/current/settings")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await _client.SendAsync(request);
    }

    private Task<HttpResponseMessage> GetFeeCategoriesAsync(string accessToken) =>
        SendAsync(HttpMethod.Get, "/api/v1/finance/fee-categories", accessToken);

    private Task<HttpResponseMessage> GetSubjectsAsync(string accessToken) =>
        SendAsync(HttpMethod.Get, "/api/v1/subjects", accessToken);

    private Task<HttpResponseMessage> GetTeachersAsync(string accessToken) =>
        SendAsync(HttpMethod.Get, "/api/v1/teachers", accessToken);

    private Task<HttpResponseMessage> GetClassroomsAsync(string accessToken) =>
        SendAsync(HttpMethod.Get, "/api/v1/classrooms", accessToken);

    /// <summary>Corps PUT complet — même convention que SchoolSettingsEndpointsTests : le formulaire
    /// envoie toujours l'objet entier, jamais un correctif partiel.</summary>
    private static object ValidBody(
        bool isPedagogyEnabled = true,
        bool isFinanceEnabled = true,
        bool isInternatEnabled = false,
        bool isCoranModuleEnabled = false) => new
        {
            gradingScale = "20",
            studentMatriculeFormat = "ELEV-{YEAR}-{SEQ:4}",
            teacherMatriculeFormat = "ENS-{YEAR}-{SEQ:3}",
            autoLogoutMinutes = 10,
            dateFormat = "dd/MM/yyyy",
            tuitionMonthsPerYear = 9,
            allowSecretaryToManageGrading = false,
            isPedagogyEnabled,
            isFinanceEnabled,
            isInternatEnabled,
            isCoranModuleEnabled
        };

    [Fact]
    public async Task A_Fresh_School_Has_Pedagogy_And_Finance_Enabled_By_Default()
    {
        var directeur = await LoginAsDirecteurAsync();

        var settings = (await (await GetSettingsAsync(directeur.AccessToken)).Content.ReadFromJsonAsync<Settings>())!;

        settings.IsPedagogyEnabled.Should().BeTrue("le socle métier reste actif tant que le Directeur ne le désactive pas");
        settings.IsFinanceEnabled.Should().BeTrue();
        settings.IsInternatEnabled.Should().BeFalse("aucun module Internat n'existe encore derrière ce réglage");
        settings.IsCoranModuleEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Disabling_Finance_Blocks_Finance_Routes_With_A_Normalized_403()
    {
        var directeur = await LoginAsDirecteurAsync();

        (await PutSettingsAsync(directeur.AccessToken, ValidBody(isFinanceEnabled: false)))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await GetFeeCategoriesAsync(directeur.AccessToken);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var error = (await response.Content.ReadFromJsonAsync<ApiError>())!;
        error.Code.Should().Be("MODULE_DISABLED");
    }

    [Fact]
    public async Task Reenabling_Finance_Restores_Access()
    {
        var directeur = await LoginAsDirecteurAsync();

        await PutSettingsAsync(directeur.AccessToken, ValidBody(isFinanceEnabled: false));
        (await GetFeeCategoriesAsync(directeur.AccessToken)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await PutSettingsAsync(directeur.AccessToken, ValidBody(isFinanceEnabled: true));
        (await GetFeeCategoriesAsync(directeur.AccessToken)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Disabling_Pedagogy_Blocks_Pedagogy_Routes_With_A_Normalized_403()
    {
        var directeur = await LoginAsDirecteurAsync();

        (await PutSettingsAsync(directeur.AccessToken, ValidBody(isPedagogyEnabled: false)))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await GetSubjectsAsync(directeur.AccessToken);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var error = (await response.Content.ReadFromJsonAsync<ApiError>())!;
        error.Code.Should().Be("MODULE_DISABLED");
    }

    [Fact]
    public async Task Disabling_Pedagogy_Must_Not_Block_Classrooms_Or_Teachers()
    {
        // Périmètre acté avec le client : Classes et Enseignants restent utilisables même Pédagogie
        // désactivée — la Paie (heures/contrats enseignants) et les Frais par classe en dépendent.
        var directeur = await LoginAsDirecteurAsync();

        await PutSettingsAsync(directeur.AccessToken, ValidBody(isPedagogyEnabled: false));

        (await GetClassroomsAsync(directeur.AccessToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetTeachersAsync(directeur.AccessToken)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_Secretariat_Must_Not_Be_Allowed_To_Toggle_Modules()
    {
        // Même garde que le reste de PUT /schools/current/settings (SchoolSettingsController).
        var secretaire = await LoginAsync(AuthApiFactory.SecretaireEmail, AuthApiFactory.SecretairePassword);

        var response = await PutSettingsAsync(secretaire.AccessToken, ValidBody(isFinanceEnabled: false));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
