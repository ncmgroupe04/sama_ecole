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
public class ClassroomsEndpointsTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public ClassroomsEndpointsTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public Task InitializeAsync() => _factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private record Tokens(string AccessToken, int ExpiresIn);
    private record ClassroomDto(Guid Id, string Name, string Level, int Capacity, int StudentCount, string? Cycle, uint RowVersion);
    private record ClassroomUpdateResult(Guid Id, string Name, string Level, int Capacity, string? Cycle, uint RowVersion);
    private record StudentCreated(Guid Id, string Matricule);
    private record StudentItem(Guid Id, string Matricule, string FullName, string Gender, string ClassroomName);
    private record PagedStudents(List<StudentItem> Items, int TotalCount, int Page, int PageSize);

    private async Task<string> TokenAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!.AccessToken;
    }

    private Task<string> AccessTokenAsync() =>
        TokenAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);

    private Task<string> SecretaireTokenAsync() =>
        TokenAsync(AuthApiFactory.SecretaireEmail, AuthApiFactory.SecretairePassword);

    private Task<string> FinanceTokenAsync() =>
        TokenAsync(AuthApiFactory.FinanceEmail, AuthApiFactory.FinancePassword);

    private Task<string> EnseignantTokenAsync() =>
        TokenAsync(AuthApiFactory.EnseignantEmail, AuthApiFactory.EnseignantPassword);

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

    /// <summary>
    /// CreateClassroomResult (POST) ne porte pas RowVersion : le jeton xmin n'existe qu'après la
    /// première lecture via GET /classrooms (ClassroomDto), comme ClassFeeDto dans FeesEndpointsTests.
    /// </summary>
    private async Task<ClassroomDto> FetchClassroomAsync(string token, Guid id)
    {
        var response = await SendAsync(HttpMethod.Get, "/api/v1/classrooms", token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var classrooms = (await response.Content.ReadFromJsonAsync<List<ClassroomDto>>())!;
        return classrooms.Single(c => c.Id == id);
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

    /// <summary>
    /// Le cycle d'une classe se DÉDUIT de son niveau, côté serveur, et doit survivre à l'aller-retour
    /// HTTP complet. Régression d'origine : Classroom.Cycle n'était branché sur aucune commande, donc
    /// jamais écrit — toute classe, y compris de Primaire, restait sur le défaut College. Le bulletin
    /// d'un CM2 s'intitulait alors « COLLÈGE DE », ses notes se saisissaient sur /20 au lieu de /10 et
    /// sa moyenne se pondérait par des coefficients que le primaire n'utilise pas. Aucun test ne
    /// couvrait le cycle jusqu'ici, ce qui a laissé passer le bug jusqu'à l'impression d'un bulletin.
    /// </summary>
    [Theory]
    [InlineData("Primaire", "Primaire")]
    [InlineData("Collège", "College")]
    [InlineData("Lycée", "Lycee")]
    [InlineData("Maternelle", "Maternelle")]
    [InlineData("Crèche", "Maternelle")]
    public async Task Created_Classroom_Should_Derive_Its_Cycle_From_Its_Level(string level, string expectedCycle)
    {
        var token = await AccessTokenAsync();

        var response = await SendAsync(HttpMethod.Post, "/api/v1/classrooms", token, new
        {
            name = $"Cycle {level} {Guid.NewGuid():N}"[..24],
            level,
            capacity = 40
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = (await response.Content.ReadFromJsonAsync<ClassroomDto>())!;
        created.Cycle.Should().Be(expectedCycle, "la réponse de création annonce le cycle déduit");

        // Et surtout : le cycle a bien été PERSISTÉ, pas seulement calculé pour la réponse.
        var list = await SendAsync(HttpMethod.Get, "/api/v1/classrooms", token);
        var classrooms = (await list.Content.ReadFromJsonAsync<List<ClassroomDto>>())!;

        classrooms.Should().ContainSingle(c => c.Id == created.Id)
            .Which.Cycle.Should().Be(expectedCycle);
    }

    /// <summary>
    /// Corriger le niveau d'une classe saisie par erreur doit RE-dériver son cycle : sans cela la
    /// correction resterait cosmétique et le bulletin garderait l'en-tête et le barème d'origine.
    /// </summary>
    [Fact]
    public async Task Updating_The_Level_Should_Re_Derive_The_Cycle()
    {
        var directeur = await AccessTokenAsync();
        var created = await CreateClassroomAsync(directeur, "6e Saisie Erronee");
        var classroom = await FetchClassroomAsync(directeur, created.Id);

        classroom.Cycle.Should().Be("Primaire", "le helper crée la classe avec le niveau « Primaire »");

        var response = await SendAsync(HttpMethod.Put, $"/api/v1/classrooms/{classroom.Id}", directeur, new
        {
            name = "6e Saisie Corrigee",
            level = "Collège",
            capacity = 40,
            rowVersion = classroom.RowVersion
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = (await response.Content.ReadFromJsonAsync<ClassroomUpdateResult>())!;

        updated.Cycle.Should().Be("College", "le cycle suit le niveau corrigé, il ne reste pas figé sur Primaire");
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
    public async Task An_Enseignant_Must_Not_Create_A_Classroom()
    {
        // ClassroomsController.ManageRoles = "Directeur,Secretariat" : l'Enseignant consulte
        // l'arborescence des classes mais ne la modifie pas (docs/Volume_7_Security.md §15).
        var enseignant = await EnseignantTokenAsync();

        var response = await SendAsync(HttpMethod.Post, "/api/v1/classrooms", enseignant, new
        {
            name = "Classe Interdite",
            level = "Primaire",
            capacity = 30
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
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
            birthPlace = "Dakar",
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
            birthPlace = "Dakar",
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

    // ---------------------------------------------------------------- PUT /classrooms/{id}

    [Fact]
    public async Task A_Directeur_Can_Update_A_Classroom()
    {
        var directeur = await AccessTokenAsync();
        var created = await CreateClassroomAsync(directeur, "CM2 Avant Correction");
        var classroom = await FetchClassroomAsync(directeur, created.Id);

        var response = await SendAsync(HttpMethod.Put, $"/api/v1/classrooms/{classroom.Id}", directeur, new
        {
            name = "CM2 Après Correction",
            level = "Élémentaire",
            capacity = 45,
            rowVersion = classroom.RowVersion
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = (await response.Content.ReadFromJsonAsync<ClassroomUpdateResult>())!;
        updated.Name.Should().Be("CM2 Après Correction");
        updated.Level.Should().Be("Élémentaire");
        updated.Capacity.Should().Be(45);
    }

    [Fact]
    public async Task A_Finance_Must_Not_Update_A_Classroom()
    {
        // ClassroomsController.ManageRoles = "Directeur,Secretariat" : la Finance n'y figure pas.
        var directeur = await AccessTokenAsync();
        var created = await CreateClassroomAsync(directeur, "CM1 Protégée");
        var classroom = await FetchClassroomAsync(directeur, created.Id);

        var finance = await FinanceTokenAsync();
        var response = await SendAsync(HttpMethod.Put, $"/api/v1/classrooms/{classroom.Id}", finance, new
        {
            name = "Tentative Interdite",
            level = "Primaire",
            capacity = 40,
            rowVersion = classroom.RowVersion
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Updating_An_Unknown_Classroom_Should_Return_404()
    {
        var directeur = await AccessTokenAsync();

        var response = await SendAsync(HttpMethod.Put, $"/api/v1/classrooms/{Guid.NewGuid()}", directeur, new
        {
            name = "Fantôme",
            level = "Primaire",
            capacity = 40,
            rowVersion = 1u
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Updating_A_Classroom_With_A_Stale_RowVersion_Should_Return_409()
    {
        var directeur = await AccessTokenAsync();
        var created = await CreateClassroomAsync(directeur, "CM2 Concurrente");
        var staleVersion = (await FetchClassroomAsync(directeur, created.Id)).RowVersion;

        // Une première correction fait tourner le jeton xmin.
        var firstEdit = await SendAsync(HttpMethod.Put, $"/api/v1/classrooms/{created.Id}", directeur, new
        {
            name = "CM2 Déjà Modifiée",
            level = "Primaire",
            capacity = 42,
            rowVersion = staleVersion
        });
        firstEdit.StatusCode.Should().Be(HttpStatusCode.OK);

        // La seconde tentative, avec le jeton PÉRIMÉ, ne doit jamais écraser silencieusement la première.
        var response = await SendAsync(HttpMethod.Put, $"/api/v1/classrooms/{created.Id}", directeur, new
        {
            name = "CM2 Écrasement Refusé",
            level = "Primaire",
            capacity = 50,
            rowVersion = staleVersion
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await FetchClassroomAsync(directeur, created.Id)).Name.Should().Be("CM2 Déjà Modifiée");
    }

    // ---------------------------------------------------------------- DELETE /classrooms/{id}

    [Fact]
    public async Task A_Directeur_Can_Delete_A_Classroom_As_A_Soft_Delete()
    {
        var directeur = await AccessTokenAsync();
        var created = await CreateClassroomAsync(directeur, "CM2 À Archiver");
        var classroom = await FetchClassroomAsync(directeur, created.Id);

        var response = await SendAsync(
            HttpMethod.Delete, $"/api/v1/classrooms/{classroom.Id}?rowVersion={classroom.RowVersion}", directeur);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var list = await SendAsync(HttpMethod.Get, "/api/v1/classrooms", directeur);
        (await list.Content.ReadFromJsonAsync<List<ClassroomDto>>())!
            .Should().NotContain(c => c.Id == classroom.Id, "le Global Query Filter doit masquer la classe archivée");

        // AGENTS.md règle #6 : jamais une suppression physique — vérifié directement en base, en
        // contournant le filtre applicatif (IgnoreQueryFilters), pas seulement via l'absence côté API.
        var archived = await _factory.GetClassroomAsync(classroom.Id);
        archived.Should().NotBeNull("la ligne doit toujours exister en base, seulement marquée supprimée");
        archived!.IsDeleted.Should().BeTrue();
        archived.DeletedAt.Should().NotBeNull();
        archived.DeletedBy.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task A_Finance_Must_Not_Delete_A_Classroom()
    {
        var directeur = await AccessTokenAsync();
        var created = await CreateClassroomAsync(directeur, "CM1 Protégée Suppression");
        var classroom = await FetchClassroomAsync(directeur, created.Id);

        var finance = await FinanceTokenAsync();
        var response = await SendAsync(
            HttpMethod.Delete, $"/api/v1/classrooms/{classroom.Id}?rowVersion={classroom.RowVersion}", finance);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _factory.GetClassroomAsync(classroom.Id))!.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task Deleting_An_Unknown_Classroom_Should_Return_404()
    {
        var directeur = await AccessTokenAsync();

        var response = await SendAsync(
            HttpMethod.Delete, $"/api/v1/classrooms/{Guid.NewGuid()}?rowVersion=1", directeur);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Deleting_A_Classroom_With_A_Stale_RowVersion_Should_Return_409()
    {
        var directeur = await AccessTokenAsync();
        var created = await CreateClassroomAsync(directeur, "CM2 Suppression Concurrente");
        var staleVersion = (await FetchClassroomAsync(directeur, created.Id)).RowVersion;

        // Une correction concurrente fait tourner le jeton xmin avant la tentative de suppression.
        var edit = await SendAsync(HttpMethod.Put, $"/api/v1/classrooms/{created.Id}", directeur, new
        {
            name = "CM2 Modifiée Avant Suppression",
            level = "Primaire",
            capacity = 41,
            rowVersion = staleVersion
        });
        edit.StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await SendAsync(
            HttpMethod.Delete, $"/api/v1/classrooms/{created.Id}?rowVersion={staleVersion}", directeur);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await _factory.GetClassroomAsync(created.Id))!.IsDeleted
            .Should().BeFalse("le conflit ne doit jamais entraîner une suppression silencieuse");
    }
}
