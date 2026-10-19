using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace SamaEcole.FunctionalTests.Deletion;

/// <summary>
/// Suppression guidée des bâtiments, salles, catégories de frais, catégories d'inventaire et biens, de bout en bout
/// contre un vrai PostgreSQL (spec 2026-10-17, §6) : liens énumérés, rupture explicite, blocage financier, accès par
/// rôle, élément inconnu.
/// </summary>
public class InfraFeesInventoryDeletionEndpointsTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public InfraFeesInventoryDeletionEndpointsTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public Task InitializeAsync() => _factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private record Tokens(string AccessToken, int ExpiresIn);
    private record Created(Guid Id);
    private record Versioned(Guid Id, uint RowVersion);
    private record Link(string Kind, string Label, int Count, bool Breakable, string BreakAction);
    private record Impact(bool CanDelete, bool FinancialBlock, string? Message, List<Link> Links);
    private record ApiError(string Code, string Message);

    private async Task<string> TokenAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!.AccessToken;
    }

    private Task<string> DirecteurTokenAsync() => TokenAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);
    private Task<string> SecretaireTokenAsync() => TokenAsync(AuthApiFactory.SecretaireEmail, AuthApiFactory.SecretairePassword);
    private Task<string> FinanceTokenAsync() => TokenAsync(AuthApiFactory.FinanceEmail, AuthApiFactory.FinancePassword);

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await _client.SendAsync(request);
    }

    private async Task<Versioned> CreateAsync(string token, string url, object body)
    {
        var response = await SendAsync(HttpMethod.Post, url, token, body);
        response.StatusCode.Should().Be(HttpStatusCode.Created, url);
        return (await response.Content.ReadFromJsonAsync<Versioned>())!;
    }

    private async Task<Impact> ImpactAsync(string token, string route)
    {
        var response = await SendAsync(HttpMethod.Get, $"/api/v1/{route}/deletion-impact", token);
        response.StatusCode.Should().Be(HttpStatusCode.OK, route);
        return (await response.Content.ReadFromJsonAsync<Impact>())!;
    }

    private Task<Versioned> CreateBuildingAsync(string token, string name)
        => CreateAsync(token, "/api/v1/buildings", new { name, description = "Test" });

    private Task<Versioned> CreateRoomAsync(string token, string name, Guid buildingId)
        => CreateAsync(token, "/api/v1/rooms", new { name, capacity = 30, type = "SalleDeClasse", buildingId });

    private Task<Versioned> CreateInventoryCategoryAsync(string token, string name)
        => CreateAsync(token, "/api/v1/inventory/categories", new { name, description = "Test" });

    private Task<Versioned> CreateItemAsync(string token, string name, Guid categoryId, Guid? roomId = null)
        => CreateAsync(token, "/api/v1/inventory/items", new { name, categoryId, initialQuantity = 5, roomId });

    // ------------------------------------------------------------------------------------------ Bâtiments et salles

    [Fact]
    public async Task A_Building_With_A_Room_Is_Blocked_Until_The_Room_Is_Deleted()
    {
        var directeur = await DirecteurTokenAsync();
        var building = await CreateBuildingAsync(directeur, "Bâtiment Chargé");
        var room = await CreateRoomAsync(directeur, "Salle 12", building.Id);

        var impact = await ImpactAsync(directeur, $"buildings/{building.Id}");
        impact.CanDelete.Should().BeFalse();
        impact.Links.Should().ContainSingle(l => l.Kind == "Room" && !l.Breakable).Which.Label.Should().Be("1 salle");

        var blocked = await SendAsync(HttpMethod.Delete, $"/api/v1/buildings/{building.Id}?rowVersion={building.RowVersion}", directeur);
        blocked.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var error = (await blocked.Content.ReadFromJsonAsync<ApiError>())!;
        error.Code.Should().Be("DEPENDENCIES_EXIST");
        error.Message.Should().Contain("1 salle").And.Contain("Vous devez rompre ces liens");

        var unlink = await SendAsync(HttpMethod.Post, $"/api/v1/buildings/{building.Id}/unlink-dependencies", directeur);
        unlink.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await unlink.Content.ReadFromJsonAsync<ApiError>())!.Code.Should().Be("DEPENDENCIES_NOT_BREAKABLE");

        (await SendAsync(HttpMethod.Delete, $"/api/v1/rooms/{room.Id}?rowVersion={room.RowVersion}", directeur))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await ImpactAsync(directeur, $"buildings/{building.Id}")).CanDelete.Should().BeTrue();
        (await SendAsync(HttpMethod.Delete, $"/api/v1/buildings/{building.Id}?rowVersion={building.RowVersion}", directeur))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task A_Room_Holding_Inventory_Items_Is_Unlinked_Then_Deleted_Without_Losing_The_Items()
    {
        var directeur = await DirecteurTokenAsync();
        var building = await CreateBuildingAsync(directeur, "Bâtiment Stock");
        var room = await CreateRoomAsync(directeur, "Réserve", building.Id);
        var category = await CreateInventoryCategoryAsync(directeur, "Manuels");
        var item = await CreateItemAsync(directeur, "Manuel de maths", category.Id, room.Id);

        var blocked = await SendAsync(HttpMethod.Delete, $"/api/v1/rooms/{room.Id}?rowVersion={room.RowVersion}", directeur);
        blocked.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await blocked.Content.ReadFromJsonAsync<ApiError>())!.Code.Should().Be("DEPENDENCIES_EXIST");

        var impact = await ImpactAsync(directeur, $"rooms/{room.Id}");
        impact.Links.Should().ContainSingle(l => l.Kind == "InventoryItem" && l.Breakable);

        (await SendAsync(HttpMethod.Post, $"/api/v1/rooms/{room.Id}/unlink-dependencies", directeur)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Delete, $"/api/v1/rooms/{room.Id}?rowVersion={room.RowVersion}", directeur))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Le bien existe toujours : seul son emplacement a été vidé.
        (await SendAsync(HttpMethod.Get, $"/api/v1/inventory/items/{item.Id}", directeur)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ------------------------------------------------------------------------------------------ Inventaire

    [Fact]
    public async Task An_Inventory_Category_With_Items_Is_Blocked_Until_The_Items_Are_Deleted()
    {
        var directeur = await DirecteurTokenAsync();
        var category = await CreateInventoryCategoryAsync(directeur, "Mobilier");
        var item = await CreateItemAsync(directeur, "Chaise", category.Id);

        var impact = await ImpactAsync(directeur, $"inventory/categories/{category.Id}");
        impact.CanDelete.Should().BeFalse();
        impact.Links.Should().ContainSingle(l => l.Kind == "InventoryItem" && !l.Breakable);

        (await SendAsync(HttpMethod.Delete, $"/api/v1/inventory/categories/{category.Id}?rowVersion={category.RowVersion}", directeur))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await ImpactAsync(directeur, $"inventory/items/{item.Id}")).CanDelete.Should().BeTrue();
        (await SendAsync(HttpMethod.Delete, $"/api/v1/inventory/items/{item.Id}?rowVersion={item.RowVersion}", directeur))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await SendAsync(HttpMethod.Delete, $"/api/v1/inventory/categories/{category.Id}?rowVersion={category.RowVersion}", directeur))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ------------------------------------------------------------------------------------------ Catégories de frais

    [Fact]
    public async Task A_Fee_Category_With_A_Fee_Schedule_Is_Unlinked_Then_Deleted()
    {
        var directeur = await DirecteurTokenAsync();
        (await SendAsync(HttpMethod.Post, "/api/v1/classrooms", directeur, new { name = "CP Barème", level = "Primaire", capacity = 40 }))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        var category = await CreateAsync(directeur, "/api/v1/finance/fee-categories", new { name = "Cantine", isRecurring = true });
        (await SendAsync(HttpMethod.Post, "/api/v1/finance/fees/apply-standard", directeur,
            new { feeCategoryId = category.Id, amount = 8_000m, overwriteExisting = false })).StatusCode.Should().Be(HttpStatusCode.OK);

        var impact = await ImpactAsync(directeur, $"finance/fee-categories/{category.Id}");
        impact.FinancialBlock.Should().BeFalse();
        impact.CanDelete.Should().BeFalse();
        impact.Links.Should().ContainSingle(l => l.Kind == "ClassFee" && l.Breakable);

        var blocked = await SendAsync(HttpMethod.Delete, $"/api/v1/finance/fee-categories/{category.Id}", directeur);
        blocked.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await blocked.Content.ReadFromJsonAsync<ApiError>())!.Code.Should().Be("DEPENDENCIES_EXIST");

        (await SendAsync(HttpMethod.Post, $"/api/v1/finance/fee-categories/{category.Id}/unlink-dependencies", directeur))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Delete, $"/api/v1/finance/fee-categories/{category.Id}", directeur))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task A_Fee_Category_Billed_To_An_Enrollment_Is_Blocked_Financially_Without_Any_Break_Option()
    {
        var directeur = await DirecteurTokenAsync();
        var classroom = await CreateAsync(directeur, "/api/v1/classrooms", new { name = "CM1 Facturée", level = "Primaire", capacity = 40 });
        (await SendAsync(HttpMethod.Post, "/api/v1/school-years", directeur, new
        {
            label = $"{Today.Year}-{Today.Year + 1}",
            startDate = Today.AddDays(-150).ToString("yyyy-MM-dd"),
            endDate = Today.AddDays(+120).ToString("yyyy-MM-dd")
        })).StatusCode.Should().Be(HttpStatusCode.Created);

        var category = await CreateAsync(directeur, "/api/v1/finance/fee-categories", new { name = "Inscription", isRecurring = false });
        (await SendAsync(HttpMethod.Post, "/api/v1/finance/fees/apply-standard", directeur,
            new { feeCategoryId = category.Id, amount = 10_000m, overwriteExisting = false })).StatusCode.Should().Be(HttpStatusCode.OK);

        var secretaire = await SecretaireTokenAsync();
        (await SendAsync(HttpMethod.Post, "/api/v1/enrollments", secretaire, new
        {
            type = "NewEnrollment", classroomId = classroom.Id, fullName = "Awa Ndiaye", birthDate = "2015-05-20", birthPlace = "Dakar", gender = "F"
        })).StatusCode.Should().Be(HttpStatusCode.Created);

        var impact = await ImpactAsync(directeur, $"finance/fee-categories/{category.Id}");
        impact.FinancialBlock.Should().BeTrue();
        impact.CanDelete.Should().BeFalse();
        impact.Message.Should().Contain("Impossible de supprimer cette catégorie de frais");

        var blocked = await SendAsync(HttpMethod.Delete, $"/api/v1/finance/fee-categories/{category.Id}", directeur);
        blocked.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await blocked.Content.ReadFromJsonAsync<ApiError>())!.Code.Should().Be("FINANCIAL_RECORDS_EXIST");

        var unlink = await SendAsync(HttpMethod.Post, $"/api/v1/finance/fee-categories/{category.Id}/unlink-dependencies", directeur);
        unlink.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await unlink.Content.ReadFromJsonAsync<ApiError>())!.Code.Should().Be("FINANCIAL_RECORDS_EXIST");
    }

    // ------------------------------------------------------------------------------------------ Accès et 404

    [Fact]
    public async Task Finance_Cannot_Use_The_Deletion_Routes_Of_Buildings_Rooms_And_Inventory()
    {
        var directeur = await DirecteurTokenAsync();
        var building = await CreateBuildingAsync(directeur, "Bâtiment Interdit Finance");
        var room = await CreateRoomAsync(directeur, "Salle F", building.Id);
        var category = await CreateInventoryCategoryAsync(directeur, "Cat Finance");
        var item = await CreateItemAsync(directeur, "Bien Finance", category.Id);
        var finance = await FinanceTokenAsync();

        foreach (var resource in new[]
                 {
                     $"buildings/{building.Id}", $"rooms/{room.Id}",
                     $"inventory/categories/{category.Id}", $"inventory/items/{item.Id}"
                 })
        {
            (await SendAsync(HttpMethod.Get, $"/api/v1/{resource}/deletion-impact", finance)).StatusCode.Should().Be(HttpStatusCode.Forbidden, resource);
            (await SendAsync(HttpMethod.Post, $"/api/v1/{resource}/unlink-dependencies", finance)).StatusCode.Should().Be(HttpStatusCode.Forbidden, resource);
        }
    }

    [Fact]
    public async Task The_Deletion_Routes_Return_404_For_Unknown_Elements()
    {
        var directeur = await DirecteurTokenAsync();

        foreach (var resource in new[] { "buildings", "rooms", "inventory/categories", "inventory/items", "finance/fee-categories" })
        {
            (await SendAsync(HttpMethod.Get, $"/api/v1/{resource}/{Guid.NewGuid()}/deletion-impact", directeur))
                .StatusCode.Should().Be(HttpStatusCode.NotFound, resource);
            (await SendAsync(HttpMethod.Post, $"/api/v1/{resource}/{Guid.NewGuid()}/unlink-dependencies", directeur))
                .StatusCode.Should().Be(HttpStatusCode.NotFound, resource);
        }
    }
}
