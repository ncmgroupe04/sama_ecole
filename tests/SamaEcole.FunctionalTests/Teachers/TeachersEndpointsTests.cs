using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace SamaEcole.FunctionalTests.Teachers;

/// <summary>
/// Ticket JGK-D03 — /teachers contre un vrai PostgreSQL, à travers la vraie pile HTTP.
/// </summary>
public class TeachersEndpointsTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public TeachersEndpointsTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public Task InitializeAsync() => _factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private record Tokens(string AccessToken, int ExpiresIn);
    private record SubjectDto(Guid Id, string Name, string Level, decimal Coefficient);
    private record TeacherResult(Guid Id, string Matricule);
    private record TeacherDto(Guid Id, string Matricule, string FullName, string Email, string? Phone, string Status, List<string> Subjects);
    private record PaginatedTeachers(List<TeacherDto> Items, int TotalCount, int Page, int PageSize);

    /// <summary>Miroir de GetTeacherByIdQuery.TeacherProfileDto — seuls les champs utiles aux tests d'Update/Delete.</summary>
    private record TeacherProfileDto(
        Guid Id, string FullName, string Email, string? Phone, DateOnly BirthDate, string? BirthPlace, string? Address,
        string? PhotoUrl, string Status, List<string> Subjects, uint RowVersion);

    private record TeacherUpdateResult(Guid Id, uint RowVersion);

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

    private Task<string> FinanceTokenAsync() =>
        AccessTokenAsync(AuthApiFactory.FinanceEmail, AuthApiFactory.FinancePassword);

    private Task<string> EnseignantTokenAsync() =>
        AccessTokenAsync(AuthApiFactory.EnseignantEmail, AuthApiFactory.EnseignantPassword);

    private Task<string> SuperAdminTokenAsync() =>
        AccessTokenAsync(AuthApiFactory.SuperAdminEmail, AuthApiFactory.SuperAdminPassword);

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await _client.SendAsync(request);
    }

    private async Task<Guid> CreateSubjectAsync(string directeurToken, string name)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/v1/subjects", directeurToken,
            new { name, level = "Primaire", coefficient = 4 });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<SubjectDto>())!.Id;
    }

    private async Task<TeacherResult> CreateTeacherAsync(
        string token, string fullName, string email, IEnumerable<Guid> subjectIds)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/v1/teachers", token,
            new { fullName, email, birthDate = "1985-04-12", subjectIds });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<TeacherResult>())!;
    }

    /// <summary>
    /// CreateTeacherResult (POST) ne porte pas RowVersion : le jeton xmin n'existe qu'après la première
    /// lecture via GET /teachers/{id} (TeacherProfileDto), comme ClassFeeDto dans FeesEndpointsTests.
    /// </summary>
    private async Task<TeacherProfileDto> FetchTeacherAsync(string token, Guid id)
    {
        var response = await SendAsync(HttpMethod.Get, $"/api/v1/teachers/{id}", token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return (await response.Content.ReadFromJsonAsync<TeacherProfileDto>())!;
    }

    [Fact]
    public async Task Listing_Teachers_Without_A_Token_Should_Return_401()
    {
        var response = await _client.GetAsync("/api/v1/teachers");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Created_Teacher_Should_Appear_In_The_List_With_Its_Subject()
    {
        var directeur = await DirecteurTokenAsync();
        var mathId = await CreateSubjectAsync(directeur, "Mathématiques");

        var created = await CreateTeacherAsync(directeur, "Moussa Ndiaye", "moussa.ndiaye@sama-ecole.sn", [mathId]);

        created.Matricule.Should().StartWith("ENS-");

        var response = await SendAsync(HttpMethod.Get, "/api/v1/teachers", directeur);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var teachers = (await response.Content.ReadFromJsonAsync<PaginatedTeachers>())!;

        teachers.Items.Should().Contain(t =>
            t.Id == created.Id && t.FullName == "Moussa Ndiaye" && t.Subjects.Contains("Mathématiques"));
    }

    [Fact]
    public async Task Matricules_Should_Be_Sequential_Per_School()
    {
        var directeur = await DirecteurTokenAsync();
        var subjectId = await CreateSubjectAsync(directeur, "Français");

        var first = await CreateTeacherAsync(directeur, "Awa Fall", "awa.fall@sama-ecole.sn", [subjectId]);
        var second = await CreateTeacherAsync(directeur, "Modou Diop", "modou.diop@sama-ecole.sn", [subjectId]);

        first.Matricule.Should().NotBe(second.Matricule);
    }

    [Fact]
    public async Task A_Secretary_Should_Be_Able_To_Create_A_Teacher()
    {
        // Volume_7_Security.md « Enseignants » : Créer/Modifier → Directeur ET Secrétariat.
        var directeur = await DirecteurTokenAsync();
        var subjectId = await CreateSubjectAsync(directeur, "Histoire");

        var secretaire = await SecretaireTokenAsync();
        var response = await SendAsync(HttpMethod.Post, "/api/v1/teachers", secretaire,
            new { fullName = "Fatou Sarr", email = "fatou.sarr@sama-ecole.sn", birthDate = "1985-04-12", subjectIds = new[] { subjectId } });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Finance_Must_Not_Create_A_Teacher()
    {
        var directeur = await DirecteurTokenAsync();
        var subjectId = await CreateSubjectAsync(directeur, "Sciences");

        var finance = await FinanceTokenAsync();
        var response = await SendAsync(HttpMethod.Post, "/api/v1/teachers", finance,
            new { fullName = "Intrus", email = "intrus@sama-ecole.sn", birthDate = "1985-04-12", subjectIds = new[] { subjectId } });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Finance_Must_Not_List_Teachers()
    {
        // Contrairement aux Élèves, Finance n'a AUCUN accès aux Enseignants, même en lecture
        // (Volume_7_Security.md « Enseignants » : Voir limité à Super Admin/Directeur/Secrétariat).
        var finance = await FinanceTokenAsync();
        var response = await SendAsync(HttpMethod.Get, "/api/v1/teachers", finance);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_Enseignant_Must_Not_List_Teachers()
    {
        var enseignant = await EnseignantTokenAsync();
        var response = await SendAsync(HttpMethod.Get, "/api/v1/teachers", enseignant);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_SuperAdmin_Should_List_But_Not_Create_A_Teacher()
    {
        var superAdmin = await SuperAdminTokenAsync();

        var listResponse = await SendAsync(HttpMethod.Get, "/api/v1/teachers", superAdmin);
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var createResponse = await SendAsync(HttpMethod.Post, "/api/v1/teachers", superAdmin,
            new { fullName = "Intrus", email = "intrus@sama-ecole.sn", birthDate = "1985-04-12", subjectIds = Array.Empty<Guid>() });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Creating_A_Teacher_Without_Any_Subject_Should_Return_422()
    {
        var directeur = await DirecteurTokenAsync();

        var response = await SendAsync(HttpMethod.Post, "/api/v1/teachers", directeur,
            new { fullName = "Sans matière", email = "sans.matiere@sama-ecole.sn", birthDate = "1985-04-12", subjectIds = Array.Empty<Guid>() });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Creating_A_Teacher_With_An_Unknown_Subject_Should_Return_422()
    {
        var directeur = await DirecteurTokenAsync();

        var response = await SendAsync(HttpMethod.Post, "/api/v1/teachers", directeur,
            new { fullName = "Matière fantôme", email = "fantome@sama-ecole.sn", birthDate = "1985-04-12", subjectIds = new[] { Guid.NewGuid() } });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Search_Should_Filter_By_Name()
    {
        var directeur = await DirecteurTokenAsync();
        var subjectId = await CreateSubjectAsync(directeur, "Anglais");

        await CreateTeacherAsync(directeur, "Awa Fall", "awa.fall2@sama-ecole.sn", [subjectId]);
        await CreateTeacherAsync(directeur, "Modou Diop", "modou.diop2@sama-ecole.sn", [subjectId]);

        var response = await SendAsync(HttpMethod.Get, "/api/v1/teachers?search=fall", directeur);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var teachers = (await response.Content.ReadFromJsonAsync<PaginatedTeachers>())!;

        teachers.Items.Should().ContainSingle().Which.FullName.Should().Be("Awa Fall");
    }

    [Fact]
    public async Task A_Teacher_Can_Be_Linked_To_An_Enseignant_Account(/* ticket JGK-D06 */)
    {
        var directeur = await DirecteurTokenAsync();
        var subjectId = await CreateSubjectAsync(directeur, "Mathématiques");

        var response = await SendAsync(HttpMethod.Post, "/api/v1/teachers", directeur, new
        {
            fullName = "Enseignant de test", email = "prof.lie@sama-ecole.sn", birthDate = "1985-04-12",
            subjectIds = new[] { subjectId }, userId = AuthApiFactory.EnseignantId
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Linking_A_Non_Enseignant_Account_Should_Return_422()
    {
        // Le compte Directeur n'est pas un Enseignant : il ne peut pas être rattaché à une fiche.
        var directeur = await DirecteurTokenAsync();
        var subjectId = await CreateSubjectAsync(directeur, "Mathématiques");

        var response = await SendAsync(HttpMethod.Post, "/api/v1/teachers", directeur, new
        {
            fullName = "Fiche invalide", email = "invalide@sama-ecole.sn", birthDate = "1985-04-12",
            subjectIds = new[] { subjectId }, userId = AuthApiFactory.DirecteurId
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Linking_The_Same_Account_To_Two_Teachers_Should_Return_422()
    {
        var directeur = await DirecteurTokenAsync();
        var subjectId = await CreateSubjectAsync(directeur, "Mathématiques");

        var first = await SendAsync(HttpMethod.Post, "/api/v1/teachers", directeur, new
        {
            fullName = "Premier", email = "premier@sama-ecole.sn", birthDate = "1985-04-12",
            subjectIds = new[] { subjectId }, userId = AuthApiFactory.EnseignantId
        });
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        var second = await SendAsync(HttpMethod.Post, "/api/v1/teachers", directeur, new
        {
            fullName = "Second", email = "second@sama-ecole.sn", birthDate = "1985-04-12",
            subjectIds = new[] { subjectId }, userId = AuthApiFactory.EnseignantId
        });

        second.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    // ---------------------------------------------------------------- PUT /teachers/{id}

    /// <summary>
    /// Aller-retour COMPLET de l'adresse de résidence : POST puis GET, PUT puis GET. Un test qui se
    /// contenterait du 201/200 ne prouverait rien — TeachersController recompose la commande champ par
    /// champ depuis UpdateTeacherRequest, et un champ oublié dans ce mappage se perd en silence, sans
    /// erreur ni test rouge (c'est exactement ce qui était arrivé à SchoolSettingsController).
    /// </summary>
    [Fact]
    public async Task A_Teacher_Address_Should_Survive_Creation_And_Update()
    {
        var directeur = await DirecteurTokenAsync();
        var subjectId = await CreateSubjectAsync(directeur, "Histoire");

        var createResponse = await SendAsync(HttpMethod.Post, "/api/v1/teachers", directeur, new
        {
            fullName = "Awa Ndour",
            email = "awa.ndour@sama-ecole.sn",
            birthDate = "1990-02-20",
            address = "Rue 12, Médina, Dakar",
            subjectIds = new[] { subjectId }
        });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = (await createResponse.Content.ReadFromJsonAsync<TeacherResult>())!;

        var afterCreate = await FetchTeacherAsync(directeur, created.Id);
        afterCreate.Address.Should().Be("Rue 12, Médina, Dakar");

        var updateResponse = await SendAsync(HttpMethod.Put, $"/api/v1/teachers/{created.Id}", directeur, new
        {
            fullName = "Awa Ndour",
            email = "awa.ndour@sama-ecole.sn",
            phone = (string?)null,
            birthDate = "1990-02-20",
            birthPlace = (string?)null,
            address = "Cité Keur Gorgui, Villa 24, Dakar",
            photoUrl = (string?)null,
            subjectIds = new[] { subjectId },
            rowVersion = afterCreate.RowVersion
        });
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var afterUpdate = await FetchTeacherAsync(directeur, created.Id);
        afterUpdate.Address.Should().Be("Cité Keur Gorgui, Villa 24, Dakar");
    }

    [Fact]
    public async Task A_Directeur_Can_Update_A_Teacher()
    {
        var directeur = await DirecteurTokenAsync();
        var mathId = await CreateSubjectAsync(directeur, "Mathématiques");
        var created = await CreateTeacherAsync(directeur, "Ousmane Sarr", "ousmane.sarr@sama-ecole.sn", [mathId]);
        var teacher = await FetchTeacherAsync(directeur, created.Id);

        var response = await SendAsync(HttpMethod.Put, $"/api/v1/teachers/{teacher.Id}", directeur, new
        {
            fullName = "Ousmane Sarr Diallo",
            email = "ousmane.diallo@sama-ecole.sn",
            phone = "+221770000000",
            birthDate = "1985-04-12",
            birthPlace = "Thiès",
            photoUrl = (string?)null,
            subjectIds = new[] { mathId },
            rowVersion = teacher.RowVersion
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await FetchTeacherAsync(directeur, teacher.Id);
        updated.FullName.Should().Be("Ousmane Sarr Diallo");
        updated.Email.Should().Be("ousmane.diallo@sama-ecole.sn");
        updated.BirthPlace.Should().Be("Thiès");
    }

    [Fact]
    public async Task Finance_Must_Not_Update_A_Teacher()
    {
        var directeur = await DirecteurTokenAsync();
        var subjectId = await CreateSubjectAsync(directeur, "Français");
        var created = await CreateTeacherAsync(directeur, "Bineta Sy", "bineta.sy@sama-ecole.sn", [subjectId]);
        var teacher = await FetchTeacherAsync(directeur, created.Id);

        var finance = await FinanceTokenAsync();
        var response = await SendAsync(HttpMethod.Put, $"/api/v1/teachers/{teacher.Id}", finance, new
        {
            fullName = "Tentative Interdite",
            email = "intrus@sama-ecole.sn",
            phone = (string?)null,
            birthDate = "1985-04-12",
            birthPlace = (string?)null,
            photoUrl = (string?)null,
            subjectIds = new[] { subjectId },
            rowVersion = teacher.RowVersion
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Updating_An_Unknown_Teacher_Should_Return_404()
    {
        var directeur = await DirecteurTokenAsync();
        var subjectId = await CreateSubjectAsync(directeur, "Espagnol");

        var response = await SendAsync(HttpMethod.Put, $"/api/v1/teachers/{Guid.NewGuid()}", directeur, new
        {
            fullName = "Fantôme",
            email = "fantome@sama-ecole.sn",
            phone = (string?)null,
            birthDate = "1985-04-12",
            birthPlace = (string?)null,
            photoUrl = (string?)null,
            subjectIds = new[] { subjectId },
            rowVersion = 1u
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Updating_A_Teacher_With_A_Stale_RowVersion_Should_Return_409()
    {
        var directeur = await DirecteurTokenAsync();
        var subjectId = await CreateSubjectAsync(directeur, "Philosophie");
        var created = await CreateTeacherAsync(directeur, "Assane Ba", "assane.ba@sama-ecole.sn", [subjectId]);
        var staleVersion = (await FetchTeacherAsync(directeur, created.Id)).RowVersion;

        var firstEdit = await SendAsync(HttpMethod.Put, $"/api/v1/teachers/{created.Id}", directeur, new
        {
            fullName = "Assane Ba Déjà Modifié",
            email = "assane.ba@sama-ecole.sn",
            phone = (string?)null,
            birthDate = "1985-04-12",
            birthPlace = (string?)null,
            photoUrl = (string?)null,
            subjectIds = new[] { subjectId },
            rowVersion = staleVersion
        });
        firstEdit.StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await SendAsync(HttpMethod.Put, $"/api/v1/teachers/{created.Id}", directeur, new
        {
            fullName = "Assane Ba Écrasement Refusé",
            email = "assane.ba@sama-ecole.sn",
            phone = (string?)null,
            birthDate = "1985-04-12",
            birthPlace = (string?)null,
            photoUrl = (string?)null,
            subjectIds = new[] { subjectId },
            rowVersion = staleVersion
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await FetchTeacherAsync(directeur, created.Id)).FullName.Should().Be("Assane Ba Déjà Modifié");
    }

    // ---------------------------------------------------------------- DELETE /teachers/{id}

    [Fact]
    public async Task A_Directeur_Can_Delete_A_Teacher_As_A_Soft_Delete()
    {
        var directeur = await DirecteurTokenAsync();
        var subjectId = await CreateSubjectAsync(directeur, "Économie");
        var created = await CreateTeacherAsync(directeur, "Khady Diouf", "khady.diouf@sama-ecole.sn", [subjectId]);
        var teacher = await FetchTeacherAsync(directeur, created.Id);

        var response = await SendAsync(
            HttpMethod.Delete, $"/api/v1/teachers/{teacher.Id}?rowVersion={teacher.RowVersion}", directeur);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var list = await SendAsync(HttpMethod.Get, "/api/v1/teachers", directeur);
        (await list.Content.ReadFromJsonAsync<PaginatedTeachers>())!.Items
            .Should().NotContain(t => t.Id == teacher.Id, "le Global Query Filter doit masquer la fiche archivée");

        var archived = await _factory.GetTeacherAsync(teacher.Id);
        archived.Should().NotBeNull("la ligne doit toujours exister en base, seulement marquée supprimée");
        archived!.IsDeleted.Should().BeTrue();
        archived.DeletedAt.Should().NotBeNull();
        archived.DeletedBy.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Finance_Must_Not_Delete_A_Teacher()
    {
        var directeur = await DirecteurTokenAsync();
        var subjectId = await CreateSubjectAsync(directeur, "Gymnastique");
        var created = await CreateTeacherAsync(directeur, "Modou Kane", "modou.kane@sama-ecole.sn", [subjectId]);
        var teacher = await FetchTeacherAsync(directeur, created.Id);

        var finance = await FinanceTokenAsync();
        var response = await SendAsync(
            HttpMethod.Delete, $"/api/v1/teachers/{teacher.Id}?rowVersion={teacher.RowVersion}", finance);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _factory.GetTeacherAsync(teacher.Id))!.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task Deleting_An_Unknown_Teacher_Should_Return_404()
    {
        var directeur = await DirecteurTokenAsync();

        var response = await SendAsync(
            HttpMethod.Delete, $"/api/v1/teachers/{Guid.NewGuid()}?rowVersion=1", directeur);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Deleting_A_Teacher_With_A_Stale_RowVersion_Should_Return_409()
    {
        var directeur = await DirecteurTokenAsync();
        var subjectId = await CreateSubjectAsync(directeur, "Informatique");
        var created = await CreateTeacherAsync(directeur, "Aida Diagne", "aida.diagne@sama-ecole.sn", [subjectId]);
        var staleVersion = (await FetchTeacherAsync(directeur, created.Id)).RowVersion;

        var edit = await SendAsync(HttpMethod.Put, $"/api/v1/teachers/{created.Id}", directeur, new
        {
            fullName = "Aida Diagne Modifiée Avant Suppression",
            email = "aida.diagne@sama-ecole.sn",
            phone = (string?)null,
            birthDate = "1985-04-12",
            birthPlace = (string?)null,
            photoUrl = (string?)null,
            subjectIds = new[] { subjectId },
            rowVersion = staleVersion
        });
        edit.StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await SendAsync(
            HttpMethod.Delete, $"/api/v1/teachers/{created.Id}?rowVersion={staleVersion}", directeur);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await _factory.GetTeacherAsync(created.Id))!.IsDeleted
            .Should().BeFalse("le conflit ne doit jamais entraîner une suppression silencieuse");
    }
}
