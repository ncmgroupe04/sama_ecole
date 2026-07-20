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
    private record SubjectDto(Guid Id, string Name, string Level, decimal Coefficient, uint RowVersion);
    private record SubjectUpdateResult(Guid Id, string Name, string Level, decimal Coefficient, uint RowVersion);

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

    /// <summary>
    /// SubjectResult (POST) ne porte pas RowVersion : le jeton xmin n'existe qu'après la première
    /// lecture via GET /subjects, comme ClassFeeDto dans FeesEndpointsTests.
    /// </summary>
    private async Task<SubjectDto> FetchSubjectAsync(string token, Guid id)
    {
        var response = await SendAsync(HttpMethod.Get, "/api/v1/subjects", token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var subjects = (await response.Content.ReadFromJsonAsync<List<SubjectDto>>())!;
        return subjects.Single(s => s.Id == id);
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
    public async Task A_Secretary_Can_Create_A_Subject_Without_Any_Delegation_Setting()
    {
        // Contrairement au barème/mentions (GradingPolicies.CanManageGradingScale, toujours
        // conditionnés par SchoolSettings.AllowSecretaryToManageGrading), la gestion des matières est
        // ouverte au Secrétariat sans réglage à activer.
        var token = await SecretaireTokenAsync();

        var response = await SendAsync(HttpMethod.Post, "/api/v1/subjects", token,
            new { name = "Histoire", level = "Collège", coefficient = 3 });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task An_Enseignant_Can_Create_A_Subject()
    {
        var enseignant = await AccessTokenAsync(AuthApiFactory.EnseignantEmail, AuthApiFactory.EnseignantPassword);

        var response = await SendAsync(HttpMethod.Post, "/api/v1/subjects", enseignant,
            new { name = "Histoire", level = "Collège", coefficient = 3 });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
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

    // ---------------------------------------------------------------- PUT /subjects/{id}

    [Fact]
    public async Task A_Directeur_Can_Update_A_Subject()
    {
        var directeur = await DirecteurTokenAsync();
        var created = await CreateSubjectAsync(directeur, "Physique-Chimie", "Collège", 3);
        var subject = await FetchSubjectAsync(directeur, created.Id);

        var response = await SendAsync(HttpMethod.Put, $"/api/v1/subjects/{subject.Id}", directeur, new
        {
            name = "Sciences Physiques",
            level = "Collège",
            coefficient = 4,
            rowVersion = subject.RowVersion
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = (await response.Content.ReadFromJsonAsync<SubjectUpdateResult>())!;
        updated.Name.Should().Be("Sciences Physiques");
        updated.Coefficient.Should().Be(4);
    }

    [Fact]
    public async Task A_Secretary_Can_Update_A_Subject_Without_Any_Delegation_Setting()
    {
        var directeur = await DirecteurTokenAsync();
        var created = await CreateSubjectAsync(directeur, "Histoire-Géo", "Collège", 3);
        var subject = await FetchSubjectAsync(directeur, created.Id);

        var secretaire = await SecretaireTokenAsync();
        var response = await SendAsync(HttpMethod.Put, $"/api/v1/subjects/{subject.Id}", secretaire, new
        {
            name = "Histoire-Géographie",
            level = "Collège",
            coefficient = 3,
            rowVersion = subject.RowVersion
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_Enseignant_Can_Update_A_Subject()
    {
        var directeur = await DirecteurTokenAsync();
        var created = await CreateSubjectAsync(directeur, "Anglais LV1", "Collège", 3);
        var subject = await FetchSubjectAsync(directeur, created.Id);

        var enseignant = await AccessTokenAsync(AuthApiFactory.EnseignantEmail, AuthApiFactory.EnseignantPassword);
        var response = await SendAsync(HttpMethod.Put, $"/api/v1/subjects/{subject.Id}", enseignant, new
        {
            name = "Anglais",
            level = "Collège",
            coefficient = 3,
            rowVersion = subject.RowVersion
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Updating_An_Unknown_Subject_Should_Return_404()
    {
        var directeur = await DirecteurTokenAsync();

        var response = await SendAsync(HttpMethod.Put, $"/api/v1/subjects/{Guid.NewGuid()}", directeur, new
        {
            name = "Fantôme",
            level = "Collège",
            coefficient = 3,
            rowVersion = 1u
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Updating_A_Subject_With_A_Stale_RowVersion_Should_Return_409()
    {
        var directeur = await DirecteurTokenAsync();
        var created = await CreateSubjectAsync(directeur, "SVT", "Collège", 3);
        var staleVersion = (await FetchSubjectAsync(directeur, created.Id)).RowVersion;

        var firstEdit = await SendAsync(HttpMethod.Put, $"/api/v1/subjects/{created.Id}", directeur, new
        {
            name = "SVT Déjà Modifiée",
            level = "Collège",
            coefficient = 3,
            rowVersion = staleVersion
        });
        firstEdit.StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await SendAsync(HttpMethod.Put, $"/api/v1/subjects/{created.Id}", directeur, new
        {
            name = "SVT Écrasement Refusé",
            level = "Collège",
            coefficient = 5,
            rowVersion = staleVersion
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await FetchSubjectAsync(directeur, created.Id)).Name.Should().Be("SVT Déjà Modifiée");
    }

    // ---------------------------------------------------------------- DELETE /subjects/{id}

    [Fact]
    public async Task A_Directeur_Can_Delete_A_Subject_As_A_Soft_Delete()
    {
        var directeur = await DirecteurTokenAsync();
        var created = await CreateSubjectAsync(directeur, "Musique", "Primaire", 1);
        var subject = await FetchSubjectAsync(directeur, created.Id);

        var response = await SendAsync(
            HttpMethod.Delete, $"/api/v1/subjects/{subject.Id}?rowVersion={subject.RowVersion}", directeur);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var list = await SendAsync(HttpMethod.Get, "/api/v1/subjects", directeur);
        (await list.Content.ReadFromJsonAsync<List<SubjectDto>>())!
            .Should().NotContain(s => s.Id == subject.Id, "le Global Query Filter doit masquer la matière archivée");

        var archived = await _factory.GetSubjectAsync(subject.Id);
        archived.Should().NotBeNull("la ligne doit toujours exister en base, seulement marquée supprimée");
        archived!.IsDeleted.Should().BeTrue();
        archived.DeletedAt.Should().NotBeNull();
        archived.DeletedBy.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task A_Secretary_Can_Delete_A_Subject_Without_Any_Delegation_Setting()
    {
        var directeur = await DirecteurTokenAsync();
        var created = await CreateSubjectAsync(directeur, "Arts Plastiques", "Primaire", 1);
        var subject = await FetchSubjectAsync(directeur, created.Id);

        var secretaire = await SecretaireTokenAsync();
        var response = await SendAsync(
            HttpMethod.Delete, $"/api/v1/subjects/{subject.Id}?rowVersion={subject.RowVersion}", secretaire);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _factory.GetSubjectAsync(subject.Id))!.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task An_Enseignant_Can_Delete_A_Subject()
    {
        var directeur = await DirecteurTokenAsync();
        var created = await CreateSubjectAsync(directeur, "Dessin", "Primaire", 1);
        var subject = await FetchSubjectAsync(directeur, created.Id);

        var enseignant = await AccessTokenAsync(AuthApiFactory.EnseignantEmail, AuthApiFactory.EnseignantPassword);
        var response = await SendAsync(
            HttpMethod.Delete, $"/api/v1/subjects/{subject.Id}?rowVersion={subject.RowVersion}", enseignant);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _factory.GetSubjectAsync(subject.Id))!.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task Deleting_An_Unknown_Subject_Should_Return_404()
    {
        var directeur = await DirecteurTokenAsync();

        var response = await SendAsync(
            HttpMethod.Delete, $"/api/v1/subjects/{Guid.NewGuid()}?rowVersion=1", directeur);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Deleting_A_Subject_With_A_Stale_RowVersion_Should_Return_409()
    {
        var directeur = await DirecteurTokenAsync();
        var created = await CreateSubjectAsync(directeur, "Anglais", "Collège", 2);
        var staleVersion = (await FetchSubjectAsync(directeur, created.Id)).RowVersion;

        var edit = await SendAsync(HttpMethod.Put, $"/api/v1/subjects/{created.Id}", directeur, new
        {
            name = "Anglais Modifiée Avant Suppression",
            level = "Collège",
            coefficient = 2,
            rowVersion = staleVersion
        });
        edit.StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await SendAsync(
            HttpMethod.Delete, $"/api/v1/subjects/{created.Id}?rowVersion={staleVersion}", directeur);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await _factory.GetSubjectAsync(created.Id))!.IsDeleted
            .Should().BeFalse("le conflit ne doit jamais entraîner une suppression silencieuse");
    }
}
