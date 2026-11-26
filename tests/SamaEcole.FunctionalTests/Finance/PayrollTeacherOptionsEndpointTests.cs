using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace SamaEcole.FunctionalTests.Finance;

/// <summary>
/// Recette du 15/11/2026 : la Finance ouvrait l'écran Paie, GET /teachers (fermé à son rôle) répondait 403 et elle
/// voyait un bandeau d'erreur avec un sélecteur vide — impossible de créer un contrat d'enseignant. La route DÉDIÉE
/// <c>GET /finance/employee-contracts/teacher-options</c> sert le sélecteur sans ouvrir la liste générique (données
/// personnelles) : Directeur et Finance seulement, et uniquement identifiant / matricule / nom.
/// </summary>
public class PayrollTeacherOptionsEndpointTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private const string Url = "/api/v1/finance/employee-contracts/teacher-options";

    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public PayrollTeacherOptionsEndpointTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public Task InitializeAsync() => _factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private record Tokens(string AccessToken, int ExpiresIn);

    private async Task<string> TokenAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!.AccessToken;
    }

    private async Task<HttpResponseMessage> GetAsync(string url, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(request);
    }

    [Fact]
    public async Task Finance_Can_Read_The_Teacher_Options_While_The_Generic_Teacher_List_Stays_Closed_To_Her()
    {
        var token = await TokenAsync(AuthApiFactory.FinanceEmail, AuthApiFactory.FinancePassword);

        (await GetAsync(Url, token)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetAsync("/api/v1/teachers?page=1&pageSize=100", token)).StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "la liste générique porte des données personnelles : la Finance n'y a pas accès");
    }

    [Fact]
    public async Task Directeur_Can_Read_The_Teacher_Options()
    {
        var token = await TokenAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);

        (await GetAsync(Url, token)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Secretariat_Cannot_Read_The_Teacher_Options()
    {
        var token = await TokenAsync(AuthApiFactory.SecretaireEmail, AuthApiFactory.SecretairePassword);

        (await GetAsync(Url, token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Anonymous_Callers_Are_Rejected()
    {
        (await _client.GetAsync(Url)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
