using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Xunit;

namespace SamaEcole.FunctionalTests.Schools;

/// <summary>
/// Identité de l'établissement courant — GET/PUT /schools/current. Écran Paramètres, onglet
/// « Établissement ». Critères : lecture ouverte à tous les rôles ; écriture réservée au Directeur ;
/// le nom est obligatoire et le logo, s'il est fourni, doit être une URL http(s).
/// </summary>
public class SchoolProfileEndpointsTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    public Task InitializeAsync() => factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private record Tokens(string AccessToken, string RefreshToken, int ExpiresIn);
    private record Profile(string Name, string? Address, string? Phone, string? LogoUrl);

    private async Task<Tokens> LoginAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!;
    }

    private Task<Tokens> LoginAsDirecteurAsync() =>
        LoginAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);

    private async Task<HttpResponseMessage> GetProfileAsync(string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/schools/current");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> PutProfileAsync(string accessToken, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/schools/current")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await _client.SendAsync(request);
    }

    private static object ValidBody(
        string name = "Groupe Scolaire Teranga",
        string? address = "Rue 12, Médina, Dakar",
        string? phone = "+221 77 123 45 67",
        string? logoUrl = "https://ecole.sn/logo.png") =>
        new { name, address, phone, logoUrl };

    [Fact]
    public async Task A_School_Should_Report_Its_Identity()
    {
        var directeur = await LoginAsDirecteurAsync();

        var response = await GetProfileAsync(directeur.AccessToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var profile = (await response.Content.ReadFromJsonAsync<Profile>())!;
        profile.Name.Should().Be("École de test", "c'est le nom de l'école semée");
    }

    [Fact]
    public async Task A_Directeur_Should_Update_The_Identity()
    {
        var directeur = await LoginAsDirecteurAsync();

        var response = await PutProfileAsync(directeur.AccessToken, ValidBody());
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Relu depuis la base, pas seulement renvoyé par le PUT.
        var reread = (await (await GetProfileAsync(directeur.AccessToken)).Content.ReadFromJsonAsync<Profile>())!;

        reread.Name.Should().Be("Groupe Scolaire Teranga");
        reread.Address.Should().Be("Rue 12, Médina, Dakar");
        reread.Phone.Should().Be("+221 77 123 45 67");
        reread.LogoUrl.Should().Be("https://ecole.sn/logo.png");
    }

    [Fact]
    public async Task Clearing_The_Optional_Fields_Stores_Null_Not_Empty()
    {
        var directeur = await LoginAsDirecteurAsync();

        var response = await PutProfileAsync(
            directeur.AccessToken,
            new { name = "École Sans Coordonnées", address = "", phone = (string?)null, logoUrl = "" });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var reread = (await (await GetProfileAsync(directeur.AccessToken)).Content.ReadFromJsonAsync<Profile>())!;
        reread.Address.Should().BeNull("une chaîne vide devient NULL, pas \"\"");
        reread.Phone.Should().BeNull();
        reread.LogoUrl.Should().BeNull();
    }

    [Fact]
    public async Task A_Missing_Name_Must_Be_Rejected()
    {
        var directeur = await LoginAsDirecteurAsync();

        var response = await PutProfileAsync(directeur.AccessToken, ValidBody(name: ""));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_Non_Http_Logo_Url_Must_Be_Rejected()
    {
        var directeur = await LoginAsDirecteurAsync();

        var response = await PutProfileAsync(directeur.AccessToken, ValidBody(logoUrl: "pas-une-url"));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_Secretariat_Must_Not_Be_Allowed_To_Modify_The_Identity()
    {
        var secretaire = await LoginAsync(AuthApiFactory.SecretaireEmail, AuthApiFactory.SecretairePassword);

        var response = await PutProfileAsync(secretaire.AccessToken, ValidBody());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_Secretariat_Should_Still_Be_Able_To_READ_The_Identity()
    {
        // Le nom et les coordonnées s'affichent sur le reçu et les écrans, quel que soit le rôle.
        var secretaire = await LoginAsync(AuthApiFactory.SecretaireEmail, AuthApiFactory.SecretairePassword);

        var response = await GetProfileAsync(secretaire.AccessToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Reading_The_Identity_Without_A_Token_Should_Return_401()
    {
        var response = await _client.GetAsync("/api/v1/schools/current");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
