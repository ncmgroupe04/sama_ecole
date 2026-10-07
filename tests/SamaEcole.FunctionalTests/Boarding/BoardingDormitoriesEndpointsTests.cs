using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.FunctionalTests.Common;
using SamaEcole.Infrastructure.Security;
using Xunit;

namespace SamaEcole.FunctionalTests.Boarding;

/// <summary>
/// Pavillons, chambres et lits (lot B) de bout en bout — HTTP → MediatR → PostgreSQL réel (conteneur jetable de ce
/// projet de test). Couvre la garde de module, la matrice de rôles (Surveillant en lecture seule), le verrou
/// optimiste xmin, les codes 409/422 et la corbeille.
/// </summary>
public class BoardingDormitoriesEndpointsTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private const string SurveillantEmail = "surveillant@sama-ecole.sn";
    private const string SurveillantPassword = "SurveillantMotdepasse!2026";

    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public BoardingDormitoriesEndpointsTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public async Task InitializeAsync()
    {
        await _factory.ResetTestUsersAsync();

        // Le compte Surveillant n'est PAS dans le jeu de comptes partagé (≈ 87 usages) : on le crée ici seulement.
        await _factory.SeedAsOwnerAsync(owner =>
        {
            owner.Users.Add(new User
            {
                SchoolId = AuthApiFactory.EcoleId,
                Email = SurveillantEmail,
                PasswordHash = new IdentityPasswordHasher().Hash(SurveillantPassword),
                FullName = "Surveillant de test",
                Role = Role.Surveillant,
                Status = EntityStatus.Active
            });
            return Task.CompletedTask;
        });
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private record Tokens(string AccessToken, int ExpiresIn);
    private record ApiError(string Code, string Message);
    private record DormitoryDto(Guid Id, string Name, string Gender, string? SupervisorName, uint RowVersion);
    private record BedDto(Guid Id, int BedNumber, string Status, uint RowVersion);
    private record RoomResult(Guid Id, Guid DormitoryId, string Name, List<BedDto> Beds, uint RowVersion);
    private record SummaryDto(
        Guid Id, string Name, int RoomCount, int Capacity, int OccupiedBeds, int MaintenanceBeds,
        decimal OccupancyRate, uint RowVersion);
    private record TrashItem(Guid Id, string Name, Guid? ParentId);

    private async Task<string> TokenAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!.AccessToken;
    }

    private Task<string> DirecteurAsync() => TokenAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);
    private Task<string> SecretaireAsync() => TokenAsync(AuthApiFactory.SecretaireEmail, AuthApiFactory.SecretairePassword);
    private Task<string> SurveillantAsync() => TokenAsync(SurveillantEmail, SurveillantPassword);
    private Task<string> EnseignantAsync() => TokenAsync(AuthApiFactory.EnseignantEmail, AuthApiFactory.EnseignantPassword);
    private Task<string> FinanceAsync() => TokenAsync(AuthApiFactory.FinanceEmail, AuthApiFactory.FinancePassword);

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await _client.SendAsync(request);
    }

    private async Task EnableInternatAsync(string directeurToken, bool enabled = true)
    {
        var response = await SendAsync(HttpMethod.Put, "/api/v1/schools/current/settings", directeurToken, new
        {
            gradingScale = "20",
            studentMatriculeFormat = "ELEV-{YEAR}-{SEQ:4}",
            teacherMatriculeFormat = "ENS-{YEAR}-{SEQ:3}",
            autoLogoutMinutes = 10,
            dateFormat = "dd/MM/yyyy",
            tuitionMonthsPerYear = 9,
            allowSecretaryToManageGrading = false,
            isPedagogyEnabled = true,
            isFinanceEnabled = true,
            isInternatEnabled = enabled,
            isCoranModuleEnabled = false
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Une classe de test partage UN conteneur PostgreSQL : les noms (uniques par école) doivent différer d'un test
    /// à l'autre, et les assertions portent sur l'identifiant créé, jamais sur « la liste est vide ».
    /// </summary>
    private static string Unique(string name) => $"{name} #{Guid.NewGuid():N}"[..(name.Length + 8)];

    private async Task<DormitoryDto> CreateDormitoryAsync(string token, string name, string gender = "Garcons")
    {
        name = Unique(name);
        var response = await SendAsync(HttpMethod.Post, "/api/v1/boarding/dormitories", token, new { name, gender });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<DormitoryDto>())!;
    }

    private async Task<RoomResult> CreateRoomAsync(string token, Guid dormitory, string name, int bedCount)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/v1/boarding/rooms", token, new { dormitoryId = dormitory, name, bedCount });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<RoomResult>())!;
    }

    private async Task<List<SummaryDto>> ListAsync(string token)
    {
        var response = await SendAsync(HttpMethod.Get, "/api/v1/boarding/dormitories", token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<List<SummaryDto>>())!;
    }

    private static async Task<string> ErrorCodeAsync(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<ApiError>())!.Code;

    // ------------------------------------------------------------------ Garde de module

    [Fact]
    public async Task Every_Route_Returns_403_MODULE_DISABLED_Until_The_Module_Is_Enabled()
    {
        var directeur = await DirecteurAsync();
        // Les autres tests de la classe activent le module dans la MÊME base : on le désactive explicitement, pour que ce
        // test ne dépende pas de l'ordre d'exécution.
        await EnableInternatAsync(directeur, enabled: false);

        var response = await SendAsync(HttpMethod.Get, "/api/v1/boarding/dormitories", directeur);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ErrorCodeAsync(response)).Should().Be("MODULE_DISABLED");
    }

    // ------------------------------------------------------------------ Pavillons

    [Fact]
    public async Task The_Director_Can_Create_List_Update_And_Delete_A_Dormitory_With_Optimistic_Locking()
    {
        var directeur = await DirecteurAsync();
        await EnableInternatAsync(directeur);

        var created = await CreateDormitoryAsync(directeur, "Pavillon Oustaz Ahmad");
        (await ListAsync(directeur)).Should().ContainSingle(d => d.Id == created.Id);

        var update = await SendAsync(HttpMethod.Put, $"/api/v1/boarding/dormitories/{created.Id}", directeur,
            new { name = "Pavillon Oustaz Ahmad II", gender = "Garcons", rowVersion = created.RowVersion });
        update.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = (await update.Content.ReadFromJsonAsync<DormitoryDto>())!;
        updated.Name.Should().Be("Pavillon Oustaz Ahmad II");
        updated.RowVersion.Should().NotBe(created.RowVersion, "xmin change à chaque écriture");

        var delete = await SendAsync(HttpMethod.Delete, $"/api/v1/boarding/dormitories/{created.Id}?rowVersion={updated.RowVersion}", directeur);
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ListAsync(directeur)).Should().NotContain(d => d.Id == created.Id);
    }

    [Fact]
    public async Task A_Stale_RowVersion_On_Update_Returns_409()
    {
        var directeur = await DirecteurAsync();
        await EnableInternatAsync(directeur);
        var created = await CreateDormitoryAsync(directeur, "Pavillon A");

        var first = await SendAsync(HttpMethod.Put, $"/api/v1/boarding/dormitories/{created.Id}", directeur,
            new { name = "Pavillon A bis", gender = "Garcons", rowVersion = created.RowVersion });
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        var stale = await SendAsync(HttpMethod.Put, $"/api/v1/boarding/dormitories/{created.Id}", directeur,
            new { name = "Pavillon A ter", gender = "Garcons", rowVersion = created.RowVersion });

        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Mixte_Is_Refused_With_422()
    {
        var directeur = await DirecteurAsync();
        await EnableInternatAsync(directeur);

        var name = Unique("Pavillon Mixte");
        var response = await SendAsync(HttpMethod.Post, "/api/v1/boarding/dormitories", directeur, new { name, gender = "Mixte" });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ListAsync(directeur)).Should().NotContain(d => d.Name == name);
    }

    [Fact]
    public async Task An_Unknown_Gender_Value_Is_Refused_With_422_Not_Persisted()
    {
        var directeur = await DirecteurAsync();
        await EnableInternatAsync(directeur);

        var name = Unique("Pavillon X");
        var response = await SendAsync(HttpMethod.Post, "/api/v1/boarding/dormitories", directeur, new { name, gender = 99 });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ListAsync(directeur)).Should().NotContain(d => d.Name == name);
    }

    [Fact]
    public async Task Deleting_A_Dormitory_That_Still_Has_Rooms_Returns_409_RESOURCE_IN_USE()
    {
        var directeur = await DirecteurAsync();
        await EnableInternatAsync(directeur);
        var dormitory = await CreateDormitoryAsync(directeur, "Pavillon A");
        await CreateRoomAsync(directeur, dormitory.Id, "Chambre 1", 2);

        var response = await SendAsync(HttpMethod.Delete,
            $"/api/v1/boarding/dormitories/{dormitory.Id}?rowVersion={dormitory.RowVersion}", directeur);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ErrorCodeAsync(response)).Should().Be("RESOURCE_IN_USE");
    }

    [Fact]
    public async Task A_Deleted_Dormitory_Appears_In_The_Trash_And_Can_Be_Restored()
    {
        var directeur = await DirecteurAsync();
        await EnableInternatAsync(directeur);
        var created = await CreateDormitoryAsync(directeur, "Pavillon A");
        (await SendAsync(HttpMethod.Delete, $"/api/v1/boarding/dormitories/{created.Id}?rowVersion={created.RowVersion}", directeur))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var trash = await SendAsync(HttpMethod.Get, "/api/v1/boarding/dormitories/deleted", directeur);
        trash.StatusCode.Should().Be(HttpStatusCode.OK);
        (await trash.Content.ReadFromJsonAsync<List<TrashItem>>())!.Should().ContainSingle(t => t.Id == created.Id);

        var restore = await SendAsync(HttpMethod.Post, $"/api/v1/boarding/dormitories/{created.Id}/restore", directeur);
        restore.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ListAsync(directeur)).Should().ContainSingle(d => d.Id == created.Id);
    }

    // ------------------------------------------------------------------ Chambres et lits

    [Fact]
    public async Task Creating_A_Room_Generates_Its_Beds_And_The_Dormitory_List_Reports_The_Capacity()
    {
        var directeur = await DirecteurAsync();
        await EnableInternatAsync(directeur);
        var dormitory = await CreateDormitoryAsync(directeur, "Pavillon A");

        var room = await CreateRoomAsync(directeur, dormitory.Id, "Chambre 101", 6);

        room.Beds.Select(b => b.BedNumber).Should().Equal(1, 2, 3, 4, 5, 6);
        room.Beds.Should().OnlyContain(b => b.Status == "Available");
        var summary = (await ListAsync(directeur)).Single(d => d.Id == dormitory.Id);
        (summary.RoomCount, summary.Capacity, summary.OccupiedBeds, summary.OccupancyRate).Should().Be((1, 6, 0, 0m));
    }

    [Fact]
    public async Task A_Bed_Can_Go_To_Maintenance_And_Back_And_The_List_Reports_It()
    {
        var directeur = await DirecteurAsync();
        await EnableInternatAsync(directeur);
        var dormitory = await CreateDormitoryAsync(directeur, "Pavillon A");
        var room = await CreateRoomAsync(directeur, dormitory.Id, "Chambre 1", 4);
        var bed = room.Beds[0];

        var toMaintenance = await SendAsync(HttpMethod.Put, $"/api/v1/boarding/beds/{bed.Id}/status", directeur,
            new { status = "Maintenance", rowVersion = bed.RowVersion });
        toMaintenance.StatusCode.Should().Be(HttpStatusCode.OK);
        var inMaintenance = (await toMaintenance.Content.ReadFromJsonAsync<BedDto>())!;
        inMaintenance.Status.Should().Be("Maintenance");
        (await ListAsync(directeur)).Single(d => d.Id == dormitory.Id).MaintenanceBeds.Should().Be(1);

        var back = await SendAsync(HttpMethod.Put, $"/api/v1/boarding/beds/{bed.Id}/status", directeur,
            new { status = "Available", rowVersion = inMaintenance.RowVersion });
        back.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ListAsync(directeur)).Single(d => d.Id == dormitory.Id).MaintenanceBeds.Should().Be(0);

        var occupied = await SendAsync(HttpMethod.Put, $"/api/v1/boarding/beds/{bed.Id}/status", directeur,
            new { status = "Occupied", rowVersion = bed.RowVersion });
        occupied.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "Occupied n'est jamais demandé par un client");
    }

    // ------------------------------------------------------------------ Rôles

    [Fact]
    public async Task The_Secretary_Can_Manage_Dormitories()
    {
        await EnableInternatAsync(await DirecteurAsync());

        var created = await CreateDormitoryAsync(await SecretaireAsync(), "Pavillon Secrétariat", "Filles");

        created.Gender.Should().Be("Filles");
    }

    [Fact]
    public async Task The_Surveillant_Can_Read_But_Not_Write()
    {
        var directeur = await DirecteurAsync();
        await EnableInternatAsync(directeur);
        var dormitory = await CreateDormitoryAsync(directeur, "Pavillon A");
        var surveillant = await SurveillantAsync();

        (await SendAsync(HttpMethod.Get, "/api/v1/boarding/dormitories", surveillant)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Get, $"/api/v1/boarding/dormitories/{dormitory.Id}", surveillant)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await SendAsync(HttpMethod.Post, "/api/v1/boarding/dormitories", surveillant, new { name = "X", gender = "Garcons" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Put, $"/api/v1/boarding/dormitories/{dormitory.Id}", surveillant,
            new { name = "Y", gender = "Garcons", rowVersion = dormitory.RowVersion })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Delete, $"/api/v1/boarding/dormitories/{dormitory.Id}?rowVersion={dormitory.RowVersion}", surveillant))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Get, "/api/v1/boarding/dormitories/deleted", surveillant)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("GET", "/api/v1/boarding/dormitories")]
    [InlineData("POST", "/api/v1/boarding/dormitories")]
    [InlineData("POST", "/api/v1/boarding/rooms")]
    [InlineData("POST", "/api/v1/boarding/beds")]
    [InlineData("GET", "/api/v1/boarding/beds/deleted")]
    public async Task Teachers_And_Finance_Get_403_On_Every_Route(string method, string url)
    {
        await EnableInternatAsync(await DirecteurAsync());
        object? body = method == "POST" ? new { } : null;

        foreach (var token in new[] { await EnseignantAsync(), await FinanceAsync() })
        {
            (await SendAsync(new HttpMethod(method), url, token, body)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
    }
}
