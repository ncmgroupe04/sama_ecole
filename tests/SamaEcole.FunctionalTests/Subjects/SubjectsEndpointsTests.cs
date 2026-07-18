using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace SamaEcole.FunctionalTests.Subjects;

/// <summary>
/// Ticket JGK-C03 — /subjects contre un vrai PostgreSQL, à travers la vraie pile HTTP.
/// </summary>
public class SubjectsEndpointsTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public SubjectsEndpointsTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public Task InitializeAsync() => _factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private record Tokens(string AccessToken, int ExpiresIn);
    private record SubjectDto(Guid Id, string Name, string Level, decimal Coefficient);

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

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await _client.SendAsync(request);
    }

    private async Task<SubjectDto> CreateSubjectAsync(string token, string name, string level, decimal coefficient)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/v1/subjects", token,
            new { name, level, coefficient });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<SubjectDto>())!;
    }

    [Fact]
    public async Task Listing_Subjects_Without_A_Token_Should_Return_401()
    {
        var response = await _client.GetAsync("/api/v1/subjects");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Created_Subject_Should_Appear_In_The_List_With_Its_Coefficient()
    {
        var token = await DirecteurTokenAsync();

        var created = await CreateSubjectAsync(token, "Mathématiques", "Primaire", 4);

        var response = await SendAsync(HttpMethod.Get, "/api/v1/subjects", token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var subjects = (await response.Content.ReadFromJsonAsync<List<SubjectDto>>())!;

        subjects.Should().Contain(s => s.Id == created.Id && s.Name == "Mathématiques" && s.Coefficient == 4);
    }

    [Fact]
    public async Task A_Decimal_Coefficient_Should_Be_Preserved()
    {
        var token = await DirecteurTokenAsync();

        // Le coefficient pilote les moyennes : un 1,5 saisi ne doit pas revenir en 1 ou en 2.
        var created = await CreateSubjectAsync(token, "Éducation physique", "Collège", 1.5m);

        created.Coefficient.Should().Be(1.5m);
    }

    [Fact]
    public async Task The_Same_Subject_At_Two_Levels_Should_Be_Allowed()
    {
        var token = await DirecteurTokenAsync();

        await CreateSubjectAsync(token, "Mathématiques", "Primaire", 4);

        // Même nom, niveau différent, coefficient différent : c'est le cas normal, pas un doublon.
        var response = await SendAsync(HttpMethod.Post, "/api/v1/subjects", token,
            new { name = "Mathématiques", level = "Terminale S", coefficient = 6 });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Duplicate_Subject_At_The_Same_Level_Should_Return_409()
    {
        var token = await DirecteurTokenAsync();

        await CreateSubjectAsync(token, "Français", "Primaire", 4);

        var duplicate = await SendAsync(HttpMethod.Post, "/api/v1/subjects", token,
            new { name = "Français", level = "Primaire", coefficient = 5 });

        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Absurd_Coefficient_Should_Return_422()
    {
        var token = await DirecteurTokenAsync();

        var response = await SendAsync(HttpMethod.Post, "/api/v1/subjects", token,
            new { name = "Matière absurde", level = "Primaire", coefficient = 0 });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_Secretary_Can_Create_A_Subject()
    {
        // Ticket JGK-G02 : délégation de la gestion des matières/coefficients au Secrétariat en cas
        // d'absence du Directeur (docs/Volume_7_Security.md « Paramètres de l'école »).
        var token = await SecretaireTokenAsync();

        var response = await SendAsync(HttpMethod.Post, "/api/v1/subjects", token,
            new { name = "Histoire", level = "Collège", coefficient = 3 });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task An_Enseignant_Must_Not_Create_A_Subject()
    {
        // Le coefficient relève de la notation : Directeur/Secrétariat uniquement, jamais l'Enseignant
        // (docs/Volume_7_Security.md « Paramètres de l'école »).
        var enseignant = await AccessTokenAsync(AuthApiFactory.EnseignantEmail, AuthApiFactory.EnseignantPassword);

        var response = await SendAsync(HttpMethod.Post, "/api/v1/subjects", enseignant,
            new { name = "Histoire", level = "Collège", coefficient = 3 });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_Secretary_Should_Still_Read_The_Subjects()
    {
        var directeur = await DirecteurTokenAsync();
        await CreateSubjectAsync(directeur, "Sciences", "Collège", 3);

        var secretaire = await SecretaireTokenAsync();
        var response = await SendAsync(HttpMethod.Get, "/api/v1/subjects", secretaire);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var subjects = (await response.Content.ReadFromJsonAsync<List<SubjectDto>>())!;
        subjects.Should().ContainSingle().Which.Name.Should().Be("Sciences");
    }
}
