using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace SamaEcole.FunctionalTests.Teachers;

/// <summary>
/// GET /teachers/export/pdf contre un vrai PostgreSQL — docs/Volume_7_Security.md « Enseignants ».
/// Contrairement à l'export des élèves (Directeur/Secrétariat/Finance), l'export du corps professoral
/// suit les rôles de CONSULTATION de la fiche enseignant : Super Admin, Directeur, Secrétariat. Ni le
/// rôle Finance ni le rôle Enseignant n'ont accès à la liste des enseignants, même en lecture.
/// </summary>
public class TeachersExportPdfEndpointsTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public TeachersExportPdfEndpointsTests(AuthApiFactory factory)
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

    private Task<string> DirecteurTokenAsync() => TokenAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);
    private Task<string> SecretaireTokenAsync() => TokenAsync(AuthApiFactory.SecretaireEmail, AuthApiFactory.SecretairePassword);
    private Task<string> FinanceTokenAsync() => TokenAsync(AuthApiFactory.FinanceEmail, AuthApiFactory.FinancePassword);
    private Task<string> EnseignantTokenAsync() => TokenAsync(AuthApiFactory.EnseignantEmail, AuthApiFactory.EnseignantPassword);

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(request);
    }

    private record SubjectDto(Guid Id, string Name);

    private async Task<Guid> CreateSubjectAsync(string token, string name)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/subjects")
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
            Content = JsonContent.Create(new { name, level = "Primaire", coefficient = 4 })
        };
        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<SubjectDto>())!.Id;
    }

    /// <summary>
    /// Au moins une matière est obligatoire (CreateTeacherCommandValidator) : l'enseignant est créé
    /// avec une matière fraîche, ce qui garantit aussi que la colonne « Matières » du PDF est remplie.
    /// </summary>
    private async Task CreateTeacherAsync(string token, string fullName, string email)
    {
        var subjectId = await CreateSubjectAsync(token, $"Mathématiques {Guid.NewGuid():N}"[..24]);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/teachers")
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
            Content = JsonContent.Create(new
            {
                fullName,
                email,
                phone = "+221771234567",
                birthDate = "1988-04-21",
                birthPlace = "Saint-Louis",
                address = "Sicap Liberté 6",
                subjectIds = new[] { subjectId }
            })
        };
        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task An_Enseignant_Must_Not_Export_The_Teachers_List()
    {
        var enseignant = await EnseignantTokenAsync();

        var response = await SendAsync(HttpMethod.Get, "/api/v1/teachers/export/pdf", enseignant);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_Finance_User_Must_Not_Export_The_Teachers_List()
    {
        // Le rôle Finance exporte les ÉLÈVES mais n'a aucun accès aux fiches enseignants : l'export
        // ne doit pas devenir une porte dérobée vers un module qui lui est fermé à l'écran.
        var finance = await FinanceTokenAsync();

        var response = await SendAsync(HttpMethod.Get, "/api/v1/teachers/export/pdf", finance);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_Directeur_Can_Export_The_Teachers_List_As_A_Pdf()
    {
        var directeur = await DirecteurTokenAsync();
        await CreateTeacherAsync(directeur, "Awa Ndiaye", $"awa.ndiaye.{Guid.NewGuid():N}@ecole.sn");

        var response = await SendAsync(HttpMethod.Get, "/api/v1/teachers/export/pdf", directeur);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-", "l'en-tête magique d'un PDF");
    }

    [Fact]
    public async Task A_Secretaire_Can_Export_The_Teachers_List_Filtered_By_Status()
    {
        var secretaire = await SecretaireTokenAsync();

        var response = await SendAsync(HttpMethod.Get, "/api/v1/teachers/export/pdf?status=Active", secretaire);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
    }

    [Fact]
    public async Task Exporting_An_Empty_Teachers_List_Still_Produces_A_Valid_Pdf()
    {
        // Un périmètre vide ne doit jamais faire échouer la génération : le document sort avec sa
        // ligne « Aucun enseignant pour ce périmètre. » — même contrat que l'export des élèves.
        var directeur = await DirecteurTokenAsync();

        var response = await SendAsync(HttpMethod.Get, "/api/v1/teachers/export/pdf?status=Blocked", directeur);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
    }
}
