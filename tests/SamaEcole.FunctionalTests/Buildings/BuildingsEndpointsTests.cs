using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace SamaEcole.FunctionalTests.Buildings;

/// <summary>
/// Module Infrastructures — /buildings et /rooms, contre un vrai PostgreSQL (conteneur jetable de ce
/// projet de test, migré automatiquement via AuthApiFactory — n'a aucun rapport avec la base de dev
/// partagée). Mêmes conventions que ClassroomsEndpointsTests : CRUD, verrou optimiste xmin, matrice de
/// rôles (Directeur/Secretariat = ManageRoles).
/// </summary>
public class BuildingsEndpointsTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public BuildingsEndpointsTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public Task InitializeAsync() => _factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private record Tokens(string AccessToken, int ExpiresIn);
    private record RoomDto(Guid Id, string Name, int Capacity, string Type, Guid BuildingId, uint RowVersion);
    private record BuildingDto(Guid Id, string Name, string? Description, uint RowVersion, List<RoomDto> Rooms);
    private record RoomResult(Guid Id, string Name, int Capacity, string Type, Guid BuildingId, uint RowVersion);

    private async Task<string> TokenAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!.AccessToken;
    }

    private Task<string> DirecteurTokenAsync() =>
        TokenAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);

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

    private async Task<BuildingDto> CreateBuildingAsync(string token, string name)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/v1/buildings", token, new { name, description = "Description de test" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = (await response.Content.ReadFromJsonAsync<BuildingDto>())!;
        return await FetchBuildingAsync(token, created.Id);
    }

    /// <summary>Le POST ne porte pas la liste des salles (toujours vide à la création) : on relit via GET /buildings.</summary>
    private async Task<BuildingDto> FetchBuildingAsync(string token, Guid id)
    {
        var response = await SendAsync(HttpMethod.Get, "/api/v1/buildings", token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var buildings = (await response.Content.ReadFromJsonAsync<List<BuildingDto>>())!;
        return buildings.Single(b => b.Id == id);
    }

    // ---------------------------------------------------------------- GET /buildings

    [Fact]
    public async Task Listing_Buildings_Without_A_Token_Should_Return_401()
    {
        var response = await _client.GetAsync("/api/v1/buildings");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Created_Building_Should_Appear_In_The_List_With_An_Empty_Rooms_Array()
    {
        var token = await DirecteurTokenAsync();

        var created = await CreateBuildingAsync(token, "Bâtiment Apparition");

        created.Rooms.Should().BeEmpty();
    }

    // ---------------------------------------------------------------- POST /buildings

    [Fact]
    public async Task Duplicate_Building_Name_In_The_Same_School_Should_Return_409()
    {
        var token = await DirecteurTokenAsync();
        await CreateBuildingAsync(token, "Bâtiment Doublon");

        var second = await SendAsync(HttpMethod.Post, "/api/v1/buildings", token, new { name = "Bâtiment Doublon", description = (string?)null });

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task An_Enseignant_Must_Not_Create_A_Building()
    {
        var enseignant = await EnseignantTokenAsync();

        var response = await SendAsync(HttpMethod.Post, "/api/v1/buildings", enseignant, new { name = "Bâtiment Interdit", description = (string?)null });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ---------------------------------------------------------------- PUT /buildings/{id}

    [Fact]
    public async Task A_Directeur_Can_Update_A_Building()
    {
        var directeur = await DirecteurTokenAsync();
        var building = await CreateBuildingAsync(directeur, "Bâtiment Avant Correction");

        var response = await SendAsync(HttpMethod.Put, $"/api/v1/buildings/{building.Id}", directeur, new
        {
            name = "Bâtiment Après Correction",
            description = "Nouvelle description",
            rowVersion = building.RowVersion
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Updating_A_Building_With_A_Stale_RowVersion_Should_Return_409()
    {
        var directeur = await DirecteurTokenAsync();
        var building = await CreateBuildingAsync(directeur, "Bâtiment Concurrent");
        var staleVersion = building.RowVersion;

        var firstEdit = await SendAsync(HttpMethod.Put, $"/api/v1/buildings/{building.Id}", directeur, new
        {
            name = "Bâtiment Déjà Modifié",
            description = (string?)null,
            rowVersion = staleVersion
        });
        firstEdit.StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await SendAsync(HttpMethod.Put, $"/api/v1/buildings/{building.Id}", directeur, new
        {
            name = "Bâtiment Écrasement Refusé",
            description = (string?)null,
            rowVersion = staleVersion
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ---------------------------------------------------------------- DELETE /buildings/{id}

    [Fact]
    public async Task A_Directeur_Can_Delete_An_Empty_Building_As_A_Soft_Delete()
    {
        var directeur = await DirecteurTokenAsync();
        var building = await CreateBuildingAsync(directeur, "Bâtiment À Archiver");

        var response = await SendAsync(
            HttpMethod.Delete, $"/api/v1/buildings/{building.Id}?rowVersion={building.RowVersion}", directeur);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var list = await SendAsync(HttpMethod.Get, "/api/v1/buildings", directeur);
        (await list.Content.ReadFromJsonAsync<List<BuildingDto>>())!
            .Should().NotContain(b => b.Id == building.Id, "le Global Query Filter doit masquer le bâtiment archivé");

        // AGENTS.md règle #6 : jamais une suppression physique.
        var archived = await _factory.GetBuildingAsync(building.Id);
        archived.Should().NotBeNull("la ligne doit toujours exister en base, seulement marquée supprimée");
        archived!.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task Deleting_A_Building_With_Rooms_Still_Attached_Should_Return_409()
    {
        var directeur = await DirecteurTokenAsync();
        var building = await CreateBuildingAsync(directeur, "Bâtiment Avec Salle");

        var roomResponse = await SendAsync(HttpMethod.Post, "/api/v1/rooms", directeur, new
        {
            name = "Salle Empêchant Suppression",
            capacity = 30,
            type = "SalleDeClasse",
            buildingId = building.Id
        });
        roomResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var response = await SendAsync(
            HttpMethod.Delete, $"/api/v1/buildings/{building.Id}?rowVersion={building.RowVersion}", directeur);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ---------------------------------------------------------------- POST /rooms

    [Fact]
    public async Task A_Directeur_Can_Create_A_Room_In_A_Building()
    {
        var directeur = await DirecteurTokenAsync();
        var building = await CreateBuildingAsync(directeur, "Bâtiment Pour Salle");

        var response = await SendAsync(HttpMethod.Post, "/api/v1/rooms", directeur, new
        {
            name = "Salle 101",
            capacity = 35,
            type = "Laboratoire",
            buildingId = building.Id
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = (await response.Content.ReadFromJsonAsync<RoomResult>())!;
        created.Type.Should().Be("Laboratoire");

        var refreshed = await FetchBuildingAsync(directeur, building.Id);
        refreshed.Rooms.Should().ContainSingle(r => r.Id == created.Id && r.Name == "Salle 101");
    }

    [Fact]
    public async Task Creating_A_Room_In_An_Unknown_Building_Should_Return_422()
    {
        var directeur = await DirecteurTokenAsync();

        var response = await SendAsync(HttpMethod.Post, "/api/v1/rooms", directeur, new
        {
            name = "Salle Fantôme",
            capacity = 20,
            type = "SalleDeClasse",
            buildingId = Guid.NewGuid()
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Duplicate_Room_Name_In_The_Same_Building_Should_Return_409()
    {
        var directeur = await DirecteurTokenAsync();
        var building = await CreateBuildingAsync(directeur, "Bâtiment Salles Doublon");

        var first = await SendAsync(HttpMethod.Post, "/api/v1/rooms", directeur, new
        {
            name = "Salle Doublon",
            capacity = 20,
            type = "SalleDeClasse",
            buildingId = building.Id
        });
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        var second = await SendAsync(HttpMethod.Post, "/api/v1/rooms", directeur, new
        {
            name = "Salle Doublon",
            capacity = 25,
            type = "SalleDeClasse",
            buildingId = building.Id
        });

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task An_Enseignant_Must_Not_Create_A_Room()
    {
        var directeur = await DirecteurTokenAsync();
        var building = await CreateBuildingAsync(directeur, "Bâtiment Protégé Salle");

        var enseignant = await EnseignantTokenAsync();
        var response = await SendAsync(HttpMethod.Post, "/api/v1/rooms", enseignant, new
        {
            name = "Salle Interdite",
            capacity = 20,
            type = "SalleDeClasse",
            buildingId = building.Id
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ---------------------------------------------------------------- DELETE /rooms/{id}

    [Fact]
    public async Task A_Directeur_Can_Delete_A_Room_As_A_Soft_Delete_Without_Blocking_On_The_Building()
    {
        var directeur = await DirecteurTokenAsync();
        var building = await CreateBuildingAsync(directeur, "Bâtiment Salle À Supprimer");

        var roomResponse = await SendAsync(HttpMethod.Post, "/api/v1/rooms", directeur, new
        {
            name = "Salle À Supprimer",
            capacity = 20,
            type = "Bureau",
            buildingId = building.Id
        });
        var room = (await roomResponse.Content.ReadFromJsonAsync<RoomResult>())!;

        var response = await SendAsync(
            HttpMethod.Delete, $"/api/v1/rooms/{room.Id}?rowVersion={room.RowVersion}", directeur);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var refreshed = await FetchBuildingAsync(directeur, building.Id);
        refreshed.Rooms.Should().NotContain(r => r.Id == room.Id);

        // Le bâtiment reste supprimable maintenant que sa seule salle est archivée.
        var deleteBuilding = await SendAsync(
            HttpMethod.Delete, $"/api/v1/buildings/{building.Id}?rowVersion={refreshed.RowVersion}", directeur);
        deleteBuilding.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
