using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using SamaEcole.FunctionalTests.Common;
using Xunit;

namespace SamaEcole.FunctionalTests.SoftDelete;

/// <summary>
/// Tâche 6 (plan soft delete 2026-10-01) — les routes HTTP de corbeille/restauration et les états UX
/// de conflit, pour Building/Classroom/Room/FeeCategory. <c>MentionsEndpointsTests</c> couvre déjà ce
/// parcours pour les mentions ; aucune suite fonctionnelle ne l'exerçait encore pour ces quatre modules
/// (ni même, pour Room, aucune suite fonctionnelle du tout) — seuls des tests d'INTÉGRATION appelaient
/// les Handlers directement. Ce fichier referme cet écart au niveau HTTP : GET .../deleted, POST
/// .../restore, 409 ARCHIVED_ENTITY_EXISTS à la recréation, 409 ACTIVE_ENTITY_CONFLICT à la
/// restauration quand l'identité a été reprise — tels qu'un client HTTP réel les observe.
/// </summary>
public class TrashAndRestoreEndpointsTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public TrashAndRestoreEndpointsTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public Task InitializeAsync() => _factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private record Tokens(string AccessToken, int ExpiresIn);
    private record IdResult(Guid Id);
    private record BuildingDto(Guid Id, string Name, string? Description, uint RowVersion);
    private record ClassroomDto(Guid Id, string Name, string Level, int Capacity, int StudentCount, string? Cycle, uint RowVersion);
    private record RoomResult(Guid Id, string Name, int Capacity, string Type, Guid BuildingId, uint RowVersion);
    private record FeeCategoryDto(Guid Id, string Name, bool IsRecurring);

    private async Task<string> TokenAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!.AccessToken;
    }

    private Task<string> DirecteurTokenAsync() =>
        TokenAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await _client.SendAsync(request);
    }

    // ------------------------------------------------------------------ Building

    [Fact]
    public async Task Building_Delete_Then_Trash_Then_Recreate_Conflict_Then_Restore()
    {
        var directeur = await DirecteurTokenAsync();

        var create = await SendAsync(HttpMethod.Post, "/api/v1/buildings", directeur,
            new { name = "Bâtiment HTTP", description = (string?)null });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var building = (await create.Content.ReadFromJsonAsync<BuildingDto>())!;

        var delete = await SendAsync(HttpMethod.Delete, $"/api/v1/buildings/{building.Id}?rowVersion={building.RowVersion}", directeur);
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var trash = await (await SendAsync(HttpMethod.Get, "/api/v1/buildings/deleted", directeur))
            .Content.ReadAsStringAsync();
        trash.Should().Contain("Bâtiment HTTP");

        var active = await (await SendAsync(HttpMethod.Get, "/api/v1/buildings", directeur))
            .Content.ReadAsStringAsync();
        active.Should().NotContain("Bâtiment HTTP", "invisible de la liste normale après suppression");

        var recreate = await SendAsync(HttpMethod.Post, "/api/v1/buildings", directeur,
            new { name = "Bâtiment HTTP", description = (string?)null });
        recreate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await recreate.Content.ReadAsStringAsync()).Should().Contain("ARCHIVED_ENTITY_EXISTS");

        var restore = await SendAsync(HttpMethod.Post, $"/api/v1/buildings/{building.Id}/restore", directeur);
        restore.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var afterRestore = await (await SendAsync(HttpMethod.Get, "/api/v1/buildings", directeur))
            .Content.ReadAsStringAsync();
        afterRestore.Should().Contain("Bâtiment HTTP", "visible normalement après restauration");

        var trashAfter = await (await SendAsync(HttpMethod.Get, "/api/v1/buildings/deleted", directeur))
            .Content.ReadFromJsonAsync<List<BuildingDto>>();
        trashAfter!.Should().NotContain(b => b.Id == building.Id, "quitte la corbeille après restauration");
    }

    [Fact]
    public async Task Restoring_A_Building_Whose_Name_Was_Taken_By_An_Active_One_Returns_Active_Conflict()
    {
        var directeur = await DirecteurTokenAsync();

        var create = await SendAsync(HttpMethod.Post, "/api/v1/buildings", directeur,
            new { name = "Bâtiment Repris", description = (string?)null });
        var building = (await create.Content.ReadFromJsonAsync<BuildingDto>())!;

        await SendAsync(HttpMethod.Delete, $"/api/v1/buildings/{building.Id}?rowVersion={building.RowVersion}", directeur);

        // Une autre équipe reprend le nom entre-temps, sans savoir que l'identité était en corbeille.
        var recreated = await SendAsync(HttpMethod.Post, "/api/v1/buildings", directeur,
            new { name = "Bâtiment Repris", description = (string?)null });
        recreated.StatusCode.Should().Be(HttpStatusCode.Conflict, "l'identité est archivée, pas libre");

        // Une ligne ACTIVE finit par porter ce nom — par exemple via une voie hors de ce contrôleur
        // (import, script de migration de données) : simulé directement en base, côté propriétaire,
        // pour isoler l'état final attendu sans dépendre d'un second parcours HTTP.
        await _factory.SeedAsOwnerAsync(async db =>
        {
            db.Buildings.Add(new SamaEcole.Domain.Entities.Building
            {
                SchoolId = AuthApiFactory.EcoleId,
                Name = "Bâtiment Repris"
            });
            await Task.CompletedTask;
        });

        var restore = await SendAsync(HttpMethod.Post, $"/api/v1/buildings/{building.Id}/restore", directeur);

        restore.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await restore.Content.ReadAsStringAsync()).Should().Contain("ACTIVE_ENTITY_CONFLICT");
    }

    // ------------------------------------------------------------------ Classroom

    [Fact]
    public async Task Classroom_Delete_Then_Trash_Then_Recreate_Conflict_Then_Restore()
    {
        var directeur = await DirecteurTokenAsync();

        var create = await SendAsync(HttpMethod.Post, "/api/v1/classrooms", directeur,
            new { name = "Classe HTTP", level = "Primaire", capacity = 30 });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var classroomId = (await create.Content.ReadFromJsonAsync<IdResult>())!.Id;

        var fetched = await FetchClassroomAsync(directeur, classroomId);

        var delete = await SendAsync(HttpMethod.Delete, $"/api/v1/classrooms/{classroomId}?rowVersion={fetched.RowVersion}", directeur);
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var trash = await (await SendAsync(HttpMethod.Get, "/api/v1/classrooms/deleted", directeur))
            .Content.ReadAsStringAsync();
        trash.Should().Contain("Classe HTTP");

        var active = await (await SendAsync(HttpMethod.Get, "/api/v1/classrooms", directeur))
            .Content.ReadAsStringAsync();
        active.Should().NotContain("Classe HTTP", "invisible du sélecteur de classes après suppression");

        var recreate = await SendAsync(HttpMethod.Post, "/api/v1/classrooms", directeur,
            new { name = "Classe HTTP", level = "Primaire", capacity = 30 });
        recreate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await recreate.Content.ReadAsStringAsync()).Should().Contain("ARCHIVED_ENTITY_EXISTS");

        var restore = await SendAsync(HttpMethod.Post, $"/api/v1/classrooms/{classroomId}/restore", directeur);
        restore.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var afterRestore = await (await SendAsync(HttpMethod.Get, "/api/v1/classrooms", directeur))
            .Content.ReadAsStringAsync();
        afterRestore.Should().Contain("Classe HTTP", "visible normalement après restauration");

        var trashAfter = await (await SendAsync(HttpMethod.Get, "/api/v1/classrooms/deleted", directeur))
            .Content.ReadFromJsonAsync<List<ClassroomDto>>();
        trashAfter!.Should().NotContain(c => c.Id == classroomId, "quitte la corbeille après restauration");
    }

    private async Task<ClassroomDto> FetchClassroomAsync(string token, Guid id)
    {
        var response = await SendAsync(HttpMethod.Get, "/api/v1/classrooms", token);
        var classrooms = (await response.Content.ReadFromJsonAsync<List<ClassroomDto>>())!;
        return classrooms.Single(c => c.Id == id);
    }

    // ------------------------------------------------------------------ Room

    [Fact]
    public async Task Room_Delete_Then_Trash_Then_Recreate_Conflict_Then_Restore()
    {
        var directeur = await DirecteurTokenAsync();

        var building = (await (await SendAsync(HttpMethod.Post, "/api/v1/buildings", directeur,
            new { name = "Bâtiment B2 Rooms", description = (string?)null }))
            .Content.ReadFromJsonAsync<BuildingDto>())!;

        var create = await SendAsync(HttpMethod.Post, "/api/v1/rooms", directeur,
            new { name = "Salle HTTP", capacity = 20, type = "SalleDeClasse", buildingId = building.Id });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var room = (await create.Content.ReadFromJsonAsync<RoomResult>())!;

        var delete = await SendAsync(HttpMethod.Delete, $"/api/v1/rooms/{room.Id}?rowVersion={room.RowVersion}", directeur);
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var trash = await (await SendAsync(HttpMethod.Get, "/api/v1/rooms/deleted", directeur))
            .Content.ReadAsStringAsync();
        trash.Should().Contain("Salle HTTP");

        // Invisible du GET /buildings, dont la hiérarchie imbrique les salles de chaque bâtiment.
        var buildingsAfterDelete = await (await SendAsync(HttpMethod.Get, "/api/v1/buildings", directeur))
            .Content.ReadAsStringAsync();
        buildingsAfterDelete.Should().NotContain("Salle HTTP");

        var recreate = await SendAsync(HttpMethod.Post, "/api/v1/rooms", directeur,
            new { name = "Salle HTTP", capacity = 20, type = "SalleDeClasse", buildingId = building.Id });
        recreate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await recreate.Content.ReadAsStringAsync()).Should().Contain("ARCHIVED_ENTITY_EXISTS");

        var restore = await SendAsync(HttpMethod.Post, $"/api/v1/rooms/{room.Id}/restore", directeur);
        restore.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var buildingsAfterRestore = await (await SendAsync(HttpMethod.Get, "/api/v1/buildings", directeur))
            .Content.ReadAsStringAsync();
        buildingsAfterRestore.Should().Contain("Salle HTTP", "visible normalement après restauration");

        var trashAfter = await (await SendAsync(HttpMethod.Get, "/api/v1/rooms/deleted", directeur))
            .Content.ReadAsStringAsync();
        trashAfter.Should().NotContain("Salle HTTP", "quitte la corbeille après restauration");
    }

    // ------------------------------------------------------------------ FeeCategory

    [Fact]
    public async Task FeeCategory_Delete_Then_Trash_Then_Recreate_Conflict_Then_Restore()
    {
        var directeur = await DirecteurTokenAsync();

        var create = await SendAsync(HttpMethod.Post, "/api/v1/finance/fee-categories", directeur,
            new { name = "Catégorie HTTP", isRecurring = false, isBoardingFee = false, isOptional = false });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var category = (await create.Content.ReadFromJsonAsync<FeeCategoryDto>())!;

        var delete = await SendAsync(HttpMethod.Delete, $"/api/v1/finance/fee-categories/{category.Id}", directeur);
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var trash = await (await SendAsync(HttpMethod.Get, "/api/v1/finance/fee-categories/deleted", directeur))
            .Content.ReadAsStringAsync();
        trash.Should().Contain("Catégorie HTTP");

        var active = await (await SendAsync(HttpMethod.Get, "/api/v1/finance/fee-categories", directeur))
            .Content.ReadAsStringAsync();
        active.Should().NotContain("Catégorie HTTP", "invisible de la liste des catégories après suppression");

        var recreate = await SendAsync(HttpMethod.Post, "/api/v1/finance/fee-categories", directeur,
            new { name = "Catégorie HTTP", isRecurring = false, isBoardingFee = false, isOptional = false });
        recreate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await recreate.Content.ReadAsStringAsync()).Should().Contain("ARCHIVED_ENTITY_EXISTS");

        var restore = await SendAsync(HttpMethod.Post, $"/api/v1/finance/fee-categories/{category.Id}/restore", directeur);
        restore.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var afterRestore = await (await SendAsync(HttpMethod.Get, "/api/v1/finance/fee-categories", directeur))
            .Content.ReadAsStringAsync();
        afterRestore.Should().Contain("Catégorie HTTP", "visible normalement après restauration");

        var trashAfter = await (await SendAsync(HttpMethod.Get, "/api/v1/finance/fee-categories/deleted", directeur))
            .Content.ReadFromJsonAsync<List<FeeCategoryDto>>();
        trashAfter!.Should().NotContain(c => c.Id == category.Id, "quitte la corbeille après restauration");
    }

    /// <summary>
    /// Restaurer un identifiant inexistant (jamais créé, ou d'un autre tenant si l'école n'avait rien
    /// d'autre à y perdre) renvoie 404 — jamais un 200 silencieux ni un 409 qui laisserait deviner
    /// qu'une ligne existe quelque part. L'isolation tenant complète (deux écoles réelles, RLS
    /// PostgreSQL) est déjà prouvée en intégration (RestorableAggregatesEndToEndTests,
    /// ReferenceEntitiesRestoreTests) ; ce test couvre le même contrat au niveau HTTP, sans dupliquer
    /// la mise en place d'un second tenant que cette fabrique de tests ne provisionne pas.
    /// </summary>
    [Fact]
    public async Task Restoring_An_Unknown_Fee_Category_Id_Returns_404()
    {
        var directeur = await DirecteurTokenAsync();

        var restore = await SendAsync(
            HttpMethod.Post, $"/api/v1/finance/fee-categories/{Guid.NewGuid()}/restore", directeur);

        restore.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
