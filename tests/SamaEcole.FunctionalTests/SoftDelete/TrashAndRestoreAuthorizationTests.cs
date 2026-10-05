using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using SamaEcole.FunctionalTests.Common;
using Xunit;

namespace SamaEcole.FunctionalTests.SoftDelete;

/// <summary>
/// Revue finale de la tâche 3 (plan soft delete 2026-10-01) — le plan demandait explicitement de tester
/// « qu'une restauration par un rôle non autorisé est refusée », à côté de l'isolation tenant (déjà
/// couverte par RestorableAggregatesEndToEndTests / ReferenceEntitiesRestoreTests / SchoolYearRestoreTests
/// côté intégration). La moitié TENANT existait ; la moitié RÔLE n'était testée nulle part — ni en
/// intégration (les Handlers de ces modules ne portent aucune garde de rôle : c'est [Authorize] côté HTTP
/// qui filtre), ni en fonctionnel. Ce fichier referme cet écart au niveau HTTP, là où la garde existe
/// réellement.
///
/// L'Enseignant sert de rôle non autorisé « générique » : [Authorize(Roles = ManageRoles)]
/// (Buildings/Classrooms), FinancePolicies.CanDeleteFees (FeeCategories) et
/// GradingPolicies.CanManageGradingScale (Mentions) l'excluent tous les trois par construction (voir
/// CanDeleteFeesHandler / CanManageGradingScaleHandler : seuls Directeur, et Secrétariat/Finance SI
/// délégué par SchoolSettings, peuvent réussir — l'Enseignant n'entre dans aucune de ces deux branches).
/// SchoolYear est volontairement DIRECTEUR SEUL (docs/Volume_7_Security.md §15, aucune délégation
/// possible) : on y vérifie en plus que le Secrétariat — autorisé partout ailleurs dans ce fichier — y
/// est également refusé, pour distinguer cette restriction plus stricte des autres.
///
/// L'autorisation ASP.NET Core s'évalue AVANT l'exécution de l'action : un identifiant inexistant
/// (Guid.NewGuid()) suffit pour les routes de restauration, la requête n'atteint jamais le Handler.
/// </summary>
public class TrashAndRestoreAuthorizationTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public TrashAndRestoreAuthorizationTests(AuthApiFactory factory)
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

    private Task<string> EnseignantTokenAsync() =>
        TokenAsync(AuthApiFactory.EnseignantEmail, AuthApiFactory.EnseignantPassword);

    private Task<string> SecretaireTokenAsync() =>
        TokenAsync(AuthApiFactory.SecretaireEmail, AuthApiFactory.SecretairePassword);

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(request);
    }

    // ------------------------------------------------------------------ Buildings (ManageRoles = Directeur,Secretariat)

    [Fact]
    public async Task An_Enseignant_Cannot_List_Deleted_Buildings()
    {
        var enseignant = await EnseignantTokenAsync();

        var response = await SendAsync(HttpMethod.Get, "/api/v1/buildings/deleted", enseignant);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_Enseignant_Cannot_Restore_A_Building()
    {
        var enseignant = await EnseignantTokenAsync();

        var response = await SendAsync(HttpMethod.Post, $"/api/v1/buildings/{Guid.NewGuid()}/restore", enseignant);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ------------------------------------------------------------------ Classrooms (ManageRoles = Directeur,Secretariat)

    [Fact]
    public async Task An_Enseignant_Cannot_List_Deleted_Classrooms()
    {
        var enseignant = await EnseignantTokenAsync();

        var response = await SendAsync(HttpMethod.Get, "/api/v1/classrooms/deleted", enseignant);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_Enseignant_Cannot_Restore_A_Classroom()
    {
        var enseignant = await EnseignantTokenAsync();

        var response = await SendAsync(HttpMethod.Post, $"/api/v1/classrooms/{Guid.NewGuid()}/restore", enseignant);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ------------------------------------------------------------------ FeeCategories (FinancePolicies.CanDeleteFees)

    [Fact]
    public async Task An_Enseignant_Cannot_List_Deleted_Fee_Categories()
    {
        var enseignant = await EnseignantTokenAsync();

        var response = await SendAsync(HttpMethod.Get, "/api/v1/finance/fee-categories/deleted", enseignant);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_Enseignant_Cannot_Restore_A_Fee_Category()
    {
        var enseignant = await EnseignantTokenAsync();

        var response = await SendAsync(HttpMethod.Post, $"/api/v1/finance/fee-categories/{Guid.NewGuid()}/restore", enseignant);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ------------------------------------------------------------------ Mentions (GradingPolicies.CanManageGradingScale)

    [Fact]
    public async Task An_Enseignant_Cannot_List_Deleted_Mentions()
    {
        var enseignant = await EnseignantTokenAsync();

        var response = await SendAsync(HttpMethod.Get, "/api/v1/grades/mentions/deleted", enseignant);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_Enseignant_Cannot_Restore_A_Mention()
    {
        var enseignant = await EnseignantTokenAsync();

        var response = await SendAsync(HttpMethod.Post, $"/api/v1/grades/mentions/{Guid.NewGuid()}/restore", enseignant);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ------------------------------------------------------------------ SchoolYear (Directeur SEUL, aucune délégation)

    [Fact]
    public async Task An_Enseignant_Cannot_List_Deleted_School_Years()
    {
        var enseignant = await EnseignantTokenAsync();

        var response = await SendAsync(HttpMethod.Get, "/api/v1/school-years/deleted", enseignant);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_Enseignant_Cannot_Restore_A_School_Year()
    {
        var enseignant = await EnseignantTokenAsync();

        var response = await SendAsync(HttpMethod.Post, $"/api/v1/school-years/{Guid.NewGuid()}/restore", enseignant);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Contrairement à Buildings/Classrooms/FeeCategories (délégables au Secrétariat ou à la Finance),
    /// l'année scolaire reste Directeur SEUL sans aucune délégation possible — plus strict, pas aligné
    /// sur ManageRoles. Le Secrétariat, qui réussirait la plupart des autres restaurations de ce fichier
    /// via une délégation activée, échoue ici dans tous les cas.
    /// </summary>
    [Fact]
    public async Task A_Secretariat_Cannot_List_Deleted_School_Years()
    {
        var secretariat = await SecretaireTokenAsync();

        var response = await SendAsync(HttpMethod.Get, "/api/v1/school-years/deleted", secretariat);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_Secretariat_Cannot_Restore_A_School_Year()
    {
        var secretariat = await SecretaireTokenAsync();

        var response = await SendAsync(HttpMethod.Post, $"/api/v1/school-years/{Guid.NewGuid()}/restore", secretariat);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
