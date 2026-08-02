using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace SamaEcole.FunctionalTests.Students;

/// <summary>
/// GET /students/export/pdf contre un vrai PostgreSQL — docs/Volume_7_Security.md §15 (matrice
/// Élèves, ligne « Export PDF ») : Directeur, Secrétariat et Finance uniquement, Enseignant exclu.
/// </summary>
public class StudentsExportPdfEndpointsTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public StudentsExportPdfEndpointsTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public Task InitializeAsync() => _factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private record Tokens(string AccessToken, int ExpiresIn);
    private record ClassroomDto(Guid Id, string Name, string Level, int Capacity, int StudentCount);

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

    private async Task<ClassroomDto> CreateClassroomAsync(string token, string name)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/classrooms")
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
            Content = JsonContent.Create(new { name, level = "Primaire", capacity = 40 })
        };
        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ClassroomDto>())!;
    }

    private async Task CreateStudentAsync(string token, Guid classroomId, string fullName)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/students")
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
            Content = JsonContent.Create(new
            {
                fullName,
                birthDate = "2015-03-12",
                birthPlace = "Thiès",
                gender = "F",
                classroomId,
                guardianName = "Tuteur Test",
                guardianPhone = "+221771234567"
            })
        };
        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task An_Enseignant_Must_Not_Export_The_Students_List()
    {
        // StudentsController.ExportRoles = "Directeur,Secretariat,Finance" (docs/Volume_7_Security.md
        // §15) : contrairement à la lecture (GET /students, GET /students/{id}), l'Enseignant n'a pas
        // accès à cet export en lot.
        var enseignant = await EnseignantTokenAsync();

        var response = await SendAsync(HttpMethod.Get, "/api/v1/students/export/pdf", enseignant);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_Directeur_Can_Export_The_Students_List_As_A_Pdf()
    {
        var directeur = await DirecteurTokenAsync();
        var classroom = await CreateClassroomAsync(directeur, "CI Export PDF");
        await CreateStudentAsync(directeur, classroom.Id, "Mariama Sy");

        var response = await SendAsync(HttpMethod.Get, "/api/v1/students/export/pdf", directeur);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-", "l'en-tête magique d'un PDF");
    }

    [Theory]
    [InlineData(AuthApiFactory.SecretaireEmail, AuthApiFactory.SecretairePassword)]
    [InlineData(AuthApiFactory.FinanceEmail, AuthApiFactory.FinancePassword)]
    public async Task Secretariat_And_Finance_Can_Also_Export_The_Students_List(string email, string password)
    {
        var token = await TokenAsync(email, password);

        var response = await SendAsync(HttpMethod.Get, "/api/v1/students/export/pdf", token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_Export_Can_Be_Filtered_By_Classroom()
    {
        var directeur = await DirecteurTokenAsync();
        var classroomA = await CreateClassroomAsync(directeur, "CI Export Filtre A");
        var classroomB = await CreateClassroomAsync(directeur, "CI Export Filtre B");
        await CreateStudentAsync(directeur, classroomA.Id, "Élève Classe A");
        await CreateStudentAsync(directeur, classroomB.Id, "Élève Classe B");

        var response = await SendAsync(
            HttpMethod.Get, $"/api/v1/students/export/pdf?classroomId={classroomA.Id}", directeur);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
    }
}
