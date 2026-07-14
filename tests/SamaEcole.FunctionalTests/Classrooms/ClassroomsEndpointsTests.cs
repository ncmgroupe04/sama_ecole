using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace SamaEcole.FunctionalTests.Classrooms;

/// <summary>
/// Ticket JGK-C02 — /classrooms et la lecture de /students, contre un vrai PostgreSQL.
/// Ces deux routes n'existaient pas : les vues Élèves et Classes appelaient un backend absent.
/// </summary>
public class ClassroomsEndpointsTests : IClassFixture<AuthApiFactory>
{
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public ClassroomsEndpointsTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    private record Tokens(string AccessToken, int ExpiresIn);
    private record ClassroomDto(Guid Id, string Name, string Level, int Capacity, int StudentCount);
    private record StudentCreated(Guid Id, string Matricule);
    private record StudentItem(Guid Id, string Matricule, string FullName, string Gender, string ClassroomName);
    private record PagedStudents(List<StudentItem> Items, int TotalCount, int Page, int PageSize);

    private async Task<string> AccessTokenAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = AuthApiFactory.DirecteurEmail,
            password = AuthApiFactory.DirecteurPassword
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!.AccessToken;
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await _client.SendAsync(request);
    }

    private async Task<ClassroomDto> CreateClassroomAsync(string token, string name, int capacity = 40)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/v1/classrooms", token, new
        {
            name,
            level = "Primaire",
            capacity
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ClassroomDto>())!;
    }

    [Fact]
    public async Task Listing_Classrooms_Without_A_Token_Should_Return_401()
    {
        // L'école vient du JWT : sans jeton, il n'y a pas d'école, donc rien à lire.
        var response = await _client.GetAsync("/api/v1/classrooms");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Created_Classroom_Should_Appear_In_The_List()
    {
        var token = await AccessTokenAsync();

        var created = await CreateClassroomAsync(token, "CM2 Apparition");

        var response = await SendAsync(HttpMethod.Get, "/api/v1/classrooms", token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var classrooms = (await response.Content.ReadFromJsonAsync<List<ClassroomDto>>())!;

        classrooms.Should().Contain(c => c.Id == created.Id && c.Name == "CM2 Apparition");
    }

    [Fact]
    public async Task Duplicate_Name_In_The_Same_School_Should_Return_409()
    {
        var token = await AccessTokenAsync();
        await CreateClassroomAsync(token, "CM1 Doublon");

        // Deux classes de même nom dans une même école : conflit, jamais un écrasement silencieux
        // (AGENTS.md règle #5).
        var second = await SendAsync(HttpMethod.Post, "/api/v1/classrooms", token, new
        {
            name = "CM1 Doublon",
            level = "Primaire",
            capacity = 30
        });

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Absurd_Capacity_Should_Be_Refused()
    {
        var token = await AccessTokenAsync();

        var response = await SendAsync(HttpMethod.Post, "/api/v1/classrooms", token, new
        {
            name = "Classe irréaliste",
            level = "Primaire",
            capacity = 0
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Created_Student_Should_Appear_In_The_Paginated_List_With_Its_Classroom_Name()
    {
        var token = await AccessTokenAsync();
        var classroom = await CreateClassroomAsync(token, "CI Liste");

        var creation = await SendAsync(HttpMethod.Post, "/api/v1/students", token, new
        {
            fullName = "Awa Fall Test",
            birthDate = "2015-03-12",
            gender = "F",
            classroomId = classroom.Id,
            guardianName = "Fatou Fall",
            guardianPhone = "+221771234567"
        });

        creation.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = (await creation.Content.ReadFromJsonAsync<StudentCreated>())!;
        created.Matricule.Should().NotBeNullOrWhiteSpace("le matricule est généré dans la transaction d'enregistrement");

        // Recherche par nom : c'est ainsi que la liste est réellement utilisée, et cela évite de
        // dépendre des élèves créés par les autres tests de cette classe.
        var response = await SendAsync(
            HttpMethod.Get, "/api/v1/students?page=1&pageSize=10&search=Awa Fall Test", token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var page = (await response.Content.ReadFromJsonAsync<PagedStudents>())!;

        page.Items.Should().ContainSingle()
            .Which.ClassroomName.Should().Be("CI Liste", "la liste doit afficher le nom de la classe, pas son identifiant");
    }

    [Fact]
    public async Task Creating_A_Student_In_An_Unknown_Classroom_Should_Return_422()
    {
        var token = await AccessTokenAsync();

        // Sans contrôle applicatif, la clé étrangère composite rejetterait bien la ligne — mais sous
        // la forme d'un 500. L'utilisateur doit recevoir une erreur de saisie, sur le bon champ.
        var response = await SendAsync(HttpMethod.Post, "/api/v1/students", token, new
        {
            fullName = "Élève sans classe",
            birthDate = "2015-03-12",
            gender = "M",
            classroomId = Guid.NewGuid(),
            guardianName = (string?)null,
            guardianPhone = (string?)null
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("ClassroomId");
    }

    [Fact]
    public async Task PageSize_Beyond_The_Cap_Should_Be_Refused()
    {
        var token = await AccessTokenAsync();

        // Le client ne dicte pas la taille de la réponse.
        var response = await SendAsync(HttpMethod.Get, "/api/v1/students?page=1&pageSize=100000", token);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }
}
