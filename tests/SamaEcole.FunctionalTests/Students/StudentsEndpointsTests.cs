using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace SamaEcole.FunctionalTests.Students;

/// <summary>
/// Ticket JGK-D02 — PUT/DELETE /students/{id} contre un vrai PostgreSQL. La lecture (GET) et le
/// chemin de création autorisé (POST) sont déjà couverts indirectement par
/// ClassroomsEndpointsTests et AuditLogsEndpointsTests ; ce fichier couvre spécifiquement la
/// restriction de la création aux rôles autorisés, la correction et l'archivage d'une fiche élève
/// déjà créée (StudentsController.ManageRoles = Directeur/Secrétariat), avec le verrouillage
/// optimiste xmin (AGENTS.md règle #5) et le soft delete (règle #6).
/// </summary>
public class StudentsEndpointsTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public StudentsEndpointsTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public Task InitializeAsync() => _factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private record Tokens(string AccessToken, int ExpiresIn);
    private record ClassroomDto(Guid Id, string Name, string Level, int Capacity, int StudentCount);
    private record StudentCreated(Guid Id, string Matricule);

    /// <summary>Miroir de GetStudentDetailQuery.StudentIdentityDto — seuls les champs utiles aux tests.</summary>
    private record StudentIdentityDto(
        Guid Id, string Matricule, string FullName, DateOnly BirthDate, string? BirthPlace, string Gender,
        Guid ClassroomId, string ClassroomName, string? PhotoUrl, string? GuardianName, string? GuardianPhone,
        uint RowVersion);

    private record StudentDetailDto(StudentIdentityDto Identity);
    private record UpdateStudentResult(Guid Id, uint RowVersion);

    private async Task<string> TokenAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!.AccessToken;
    }

    private Task<string> DirecteurTokenAsync() =>
        TokenAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);

    private Task<string> FinanceTokenAsync() =>
        TokenAsync(AuthApiFactory.FinanceEmail, AuthApiFactory.FinancePassword);

    private Task<string> EnseignantTokenAsync() =>
        TokenAsync(AuthApiFactory.EnseignantEmail, AuthApiFactory.EnseignantPassword);

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await _client.SendAsync(request);
    }

    private async Task<ClassroomDto> CreateClassroomAsync(string token, string name)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/v1/classrooms", token,
            new { name, level = "Primaire", capacity = 40 });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ClassroomDto>())!;
    }

    /// <summary>
    /// Élève créé DIRECTEMENT (hors inscription) : aucune inscription ni note ne lui est jamais
    /// rattachée, ce qui le garde toujours archivable pour les tests de Delete
    /// (DeleteStudentCommandHandler rejette sinon en 409 — règle métier volontairement hors périmètre
    /// de cette passe, qui cible le conflit RowVersion, pas les règles de données liées).
    /// </summary>
    private async Task<StudentCreated> CreateStudentAsync(string token, Guid classroomId, string fullName)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/v1/students", token, new
        {
            fullName,
            birthDate = "2015-03-12",
            birthPlace = "Thiès", // Obligatoire depuis la feature E.
            gender = "F",
            classroomId,
            guardianName = "Tuteur Test",
            guardianPhone = "+221771234567"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<StudentCreated>())!;
    }

    /// <summary>CreateStudentResult (POST) ne porte pas RowVersion : le jeton xmin vient de GET /students/{id}.</summary>
    private async Task<StudentIdentityDto> FetchStudentAsync(string token, Guid id)
    {
        var response = await SendAsync(HttpMethod.Get, $"/api/v1/students/{id}", token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var detail = (await response.Content.ReadFromJsonAsync<StudentDetailDto>())!;
        return detail.Identity;
    }

    [Fact]
    public async Task An_Enseignant_Must_Not_Create_A_Student()
    {
        // StudentsController.ManageRoles = "Directeur,Secretariat" : l'Enseignant consulte les
        // élèves de ses classes mais ne peut pas en inscrire (docs/Volume_7_Security.md §15).
        var directeur = await DirecteurTokenAsync();
        var classroom = await CreateClassroomAsync(directeur, "CI Restriction Enseignant");

        var enseignant = await EnseignantTokenAsync();
        var response = await SendAsync(HttpMethod.Post, "/api/v1/students", enseignant, new
        {
            fullName = "Élève Interdit",
            birthDate = "2015-03-12",
            birthPlace = "Thiès",
            gender = "M",
            classroomId = classroom.Id,
            guardianName = "Tuteur Test",
            guardianPhone = "+221771234567"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Reading_An_Unknown_Student_Should_Return_404()
    {
        var directeur = await DirecteurTokenAsync();

        var response = await SendAsync(HttpMethod.Get, $"/api/v1/students/{Guid.NewGuid()}", directeur);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_Enseignant_Never_Receives_The_Payments_Section()
    {
        // docs/Volume_7_Security.md « Finance » : l'Enseignant n'a aucun accès aux données
        // financières d'un élève. `payments` doit être `null` dans la réponse JSON elle-même — un
        // masquage côté UI seul n'empêcherait pas un appel direct à cette route de tout exposer.
        var directeur = await DirecteurTokenAsync();
        var classroom = await CreateClassroomAsync(directeur, "CI Confidentialité");
        var created = await CreateStudentAsync(directeur, classroom.Id, "Oumar Sarr");

        var enseignant = await EnseignantTokenAsync();
        var response = await SendAsync(HttpMethod.Get, $"/api/v1/students/{created.Id}", enseignant);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonDocument>();
        body!.RootElement.GetProperty("payments").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);

        // Contre-épreuve : le reste de la fiche reste servi normalement pour l'Enseignant.
        body.RootElement.GetProperty("identity").GetProperty("fullName").GetString().Should().Be("Oumar Sarr");
    }

    // ---------------------------------------------------------------- PUT /students/{id}

    [Fact]
    public async Task A_Directeur_Can_Update_A_Student()
    {
        var directeur = await DirecteurTokenAsync();
        var classroom = await CreateClassroomAsync(directeur, "CI Fiche");
        var created = await CreateStudentAsync(directeur, classroom.Id, "Aissatou Ndao");
        var student = await FetchStudentAsync(directeur, created.Id);

        var response = await SendAsync(HttpMethod.Put, $"/api/v1/students/{student.Id}", directeur, new
        {
            fullName = "Aissatou Ndao Diop",
            birthDate = "2015-03-12",
            birthPlace = "Dakar",
            gender = "F",
            classroomId = classroom.Id,
            photoUrl = (string?)null,
            guardianName = "Nouveau Tuteur",
            guardianPhone = "+221770000001",
            rowVersion = student.RowVersion
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await FetchStudentAsync(directeur, student.Id);
        updated.FullName.Should().Be("Aissatou Ndao Diop");
        updated.BirthPlace.Should().Be("Dakar");
        updated.GuardianName.Should().Be("Nouveau Tuteur");
        updated.Matricule.Should().Be(created.Matricule, "le matricule n'est jamais modifiable (AGENTS.md règle #3)");
    }

    [Fact]
    public async Task A_Finance_Must_Not_Update_A_Student()
    {
        // StudentsController.ManageRoles = "Directeur,Secretariat" : la Finance n'y figure pas.
        var directeur = await DirecteurTokenAsync();
        var classroom = await CreateClassroomAsync(directeur, "CI Protégée");
        var created = await CreateStudentAsync(directeur, classroom.Id, "Modou Fall");
        var student = await FetchStudentAsync(directeur, created.Id);

        var finance = await FinanceTokenAsync();
        var response = await SendAsync(HttpMethod.Put, $"/api/v1/students/{student.Id}", finance, new
        {
            fullName = "Tentative Interdite",
            birthDate = "2015-03-12",
            birthPlace = (string?)null,
            gender = "M",
            classroomId = classroom.Id,
            photoUrl = (string?)null,
            guardianName = (string?)null,
            guardianPhone = (string?)null,
            rowVersion = student.RowVersion
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Updating_An_Unknown_Student_Should_Return_404()
    {
        var directeur = await DirecteurTokenAsync();
        var classroom = await CreateClassroomAsync(directeur, "CI Fantôme");

        var response = await SendAsync(HttpMethod.Put, $"/api/v1/students/{Guid.NewGuid()}", directeur, new
        {
            fullName = "Fantôme",
            birthDate = "2015-03-12",
            birthPlace = "Thiès", // Valide : le 404 doit venir de l'élève introuvable, pas de la validation.
            gender = "M",
            classroomId = classroom.Id,
            photoUrl = (string?)null,
            guardianName = (string?)null,
            guardianPhone = (string?)null,
            rowVersion = 1u
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Updating_A_Student_With_A_Stale_RowVersion_Should_Return_409()
    {
        var directeur = await DirecteurTokenAsync();
        var classroom = await CreateClassroomAsync(directeur, "CI Concurrente");
        var created = await CreateStudentAsync(directeur, classroom.Id, "Fatou Cissé");
        var staleVersion = (await FetchStudentAsync(directeur, created.Id)).RowVersion;

        var firstEdit = await SendAsync(HttpMethod.Put, $"/api/v1/students/{created.Id}", directeur, new
        {
            fullName = "Fatou Cissé Déjà Modifiée",
            birthDate = "2015-03-12",
            birthPlace = "Thiès",
            gender = "F",
            classroomId = classroom.Id,
            photoUrl = (string?)null,
            guardianName = (string?)null,
            guardianPhone = (string?)null,
            rowVersion = staleVersion
        });
        firstEdit.StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await SendAsync(HttpMethod.Put, $"/api/v1/students/{created.Id}", directeur, new
        {
            fullName = "Fatou Cissé Écrasement Refusé",
            birthDate = "2015-03-12",
            birthPlace = "Thiès",
            gender = "F",
            classroomId = classroom.Id,
            photoUrl = (string?)null,
            guardianName = (string?)null,
            guardianPhone = (string?)null,
            rowVersion = staleVersion
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await FetchStudentAsync(directeur, created.Id)).FullName.Should().Be("Fatou Cissé Déjà Modifiée");
    }

    // ---------------------------------------------------------------- DELETE /students/{id}

    [Fact]
    public async Task A_Directeur_Can_Delete_A_Student_As_A_Soft_Delete()
    {
        var directeur = await DirecteurTokenAsync();
        var classroom = await CreateClassroomAsync(directeur, "CI À Archiver");
        var created = await CreateStudentAsync(directeur, classroom.Id, "Cheikh Anta Diop");
        var student = await FetchStudentAsync(directeur, created.Id);

        var response = await SendAsync(
            HttpMethod.Delete, $"/api/v1/students/{student.Id}?rowVersion={student.RowVersion}", directeur);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getAfterDelete = await SendAsync(HttpMethod.Get, $"/api/v1/students/{student.Id}", directeur);
        getAfterDelete.StatusCode.Should().Be(
            HttpStatusCode.NotFound, "le Global Query Filter doit masquer la fiche archivée");

        // AGENTS.md règle #6 : jamais une suppression physique — vérifié directement en base, en
        // contournant le filtre applicatif (IgnoreQueryFilters), pas seulement via l'absence côté API.
        var archived = await _factory.GetStudentAsync(student.Id);
        archived.Should().NotBeNull("la ligne doit toujours exister en base, seulement marquée supprimée");
        archived!.IsDeleted.Should().BeTrue();
        archived.DeletedAt.Should().NotBeNull();
        archived.DeletedBy.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task A_Finance_Must_Not_Delete_A_Student()
    {
        var directeur = await DirecteurTokenAsync();
        var classroom = await CreateClassroomAsync(directeur, "CI Protégée Suppression");
        var created = await CreateStudentAsync(directeur, classroom.Id, "Ndeye Sow");
        var student = await FetchStudentAsync(directeur, created.Id);

        var finance = await FinanceTokenAsync();
        var response = await SendAsync(
            HttpMethod.Delete, $"/api/v1/students/{student.Id}?rowVersion={student.RowVersion}", finance);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _factory.GetStudentAsync(student.Id))!.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task Deleting_An_Unknown_Student_Should_Return_404()
    {
        var directeur = await DirecteurTokenAsync();

        var response = await SendAsync(
            HttpMethod.Delete, $"/api/v1/students/{Guid.NewGuid()}?rowVersion=1", directeur);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Deleting_A_Student_With_A_Stale_RowVersion_Should_Return_409()
    {
        var directeur = await DirecteurTokenAsync();
        var classroom = await CreateClassroomAsync(directeur, "CI Suppression Concurrente");
        var created = await CreateStudentAsync(directeur, classroom.Id, "Ibrahima Gueye");
        var staleVersion = (await FetchStudentAsync(directeur, created.Id)).RowVersion;

        var edit = await SendAsync(HttpMethod.Put, $"/api/v1/students/{created.Id}", directeur, new
        {
            fullName = "Ibrahima Gueye Modifié Avant Suppression",
            birthDate = "2015-03-12",
            birthPlace = "Thiès",
            gender = "M",
            classroomId = classroom.Id,
            photoUrl = (string?)null,
            guardianName = (string?)null,
            guardianPhone = (string?)null,
            rowVersion = staleVersion
        });
        edit.StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await SendAsync(
            HttpMethod.Delete, $"/api/v1/students/{created.Id}?rowVersion={staleVersion}", directeur);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await _factory.GetStudentAsync(created.Id))!.IsDeleted
            .Should().BeFalse("le conflit ne doit jamais entraîner une suppression silencieuse");
    }
}
