using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Xunit;

namespace SamaEcole.FunctionalTests.Platform;

/// <summary>
/// PUT /api/v1/admin/platform/schools/{id}/tenant-subscription — le Super Admin change tranche, plafond
/// sur mesure et statut d'une école, malgré la RLS (fonctions SECURITY DEFINER), et l'effet est immédiat
/// côté école.
/// </summary>
public class UpdateTenantSubscriptionEndpointsTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    private record Tokens(string AccessToken, int ExpiresIn);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static string Route(Guid schoolId) => $"/api/v1/admin/platform/schools/{schoolId}/tenant-subscription";

    /// <summary>État d'origine de l'école de test : Active, illimitée (le nettoyage de la fabrique le garantit).</summary>
    public Task InitializeAsync() => factory.ResetTestUsersAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_Super_Admin_Can_Move_A_School_To_A_Standard_Tier_And_The_School_Sees_It_Immediately()
    {
        var superAdmin = await TokenAsync(AuthApiFactory.SuperAdminEmail, AuthApiFactory.SuperAdminPassword);

        var response = await SendAsync(HttpMethod.Put, Route(AuthApiFactory.EcoleId), superAdmin, new { tier = "Tier2_400" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await ReadJsonAsync(response);
        dto.GetProperty("studentQuotaTier").GetString().Should().Be("Tier2_400");
        (dto.GetProperty("maxStudentLimit").GetInt32(), dto.GetProperty("softQuotaLimit").GetInt32()).Should().Be((400, 420));
        dto.GetProperty("status").GetString().Should().Be("Active", "un changement de tranche ne touche pas au statut");

        var directeur = await TokenAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);
        var seenBySchool = await ReadJsonAsync(await SendAsync(HttpMethod.Get, "/api/v1/onboarding/subscription", directeur));
        seenBySchool.GetProperty("maxStudentLimit").GetInt32().Should().Be(400);
    }

    [Fact]
    public async Task A_Custom_Tier_Takes_The_Limit_Given_By_The_Super_Admin_With_A_Computed_Tolerance()
    {
        var superAdmin = await TokenAsync(AuthApiFactory.SuperAdminEmail, AuthApiFactory.SuperAdminPassword);

        var response = await SendAsync(HttpMethod.Put, Route(AuthApiFactory.EcoleId), superAdmin,
            new { tier = "Tier4_Custom", customMaxStudentLimit = 1000 });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await ReadJsonAsync(response);
        dto.GetProperty("studentQuotaTier").GetString().Should().Be("Tier4_Custom");
        (dto.GetProperty("maxStudentLimit").GetInt32(), dto.GetProperty("softQuotaLimit").GetInt32()).Should().Be((1000, 1040));
    }

    [Fact]
    public async Task Suspending_A_School_Blocks_Student_Creation_Until_It_Is_Reactivated()
    {
        var superAdmin = await TokenAsync(AuthApiFactory.SuperAdminEmail, AuthApiFactory.SuperAdminPassword);
        var directeur = await TokenAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);
        var classroomId = await CreateClassroomAsync(directeur);

        (await SendAsync(HttpMethod.Put, Route(AuthApiFactory.EcoleId), superAdmin, new { status = "Suspended" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var blocked = await CreateStudentAsync(directeur, classroomId);
        blocked.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ReadJsonAsync(blocked)).GetProperty("code").GetString().Should().Be("SUBSCRIPTION_NOT_ACTIVE");

        (await SendAsync(HttpMethod.Put, Route(AuthApiFactory.EcoleId), superAdmin, new { status = "Active" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await CreateStudentAsync(directeur, classroomId)).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Theory]
    [InlineData("{}")]                                                       // rien à modifier
    [InlineData("{\"tier\":\"Tier4_Custom\"}")]                              // sur mesure sans plafond
    [InlineData("{\"tier\":\"Tier4_Custom\",\"customMaxStudentLimit\":0}")]  // plafond nul
    [InlineData("{\"tier\":\"Tier2_400\",\"customMaxStudentLimit\":500}")]   // plafond hors sur-mesure
    [InlineData("{\"status\":\"PendingOnboarding\"}")]                       // état de naissance non assignable
    public async Task Inconsistent_Requests_Are_Refused_With_422(string body)
    {
        var superAdmin = await TokenAsync(AuthApiFactory.SuperAdminEmail, AuthApiFactory.SuperAdminPassword);
        var request = new HttpRequestMessage(HttpMethod.Put, Route(AuthApiFactory.EcoleId))
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", superAdmin);

        (await _client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_School_That_Has_Not_Chosen_Its_Profile_Cannot_Be_Reconfigured_By_The_Super_Admin()
    {
        var superAdmin = await TokenAsync(AuthApiFactory.SuperAdminEmail, AuthApiFactory.SuperAdminPassword);
        await factory.ExecuteOwnerSqlAsync(
            $"""UPDATE tenant_subscriptions SET "Status" = 'PendingOnboarding' WHERE "SchoolId" = '{AuthApiFactory.EcoleId}'""");

        var response = await SendAsync(HttpMethod.Put, Route(AuthApiFactory.EcoleId), superAdmin, new { status = "Active" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadJsonAsync(response)).GetProperty("code").GetString().Should().Be("ONBOARDING_NOT_COMPLETED");
    }

    [Fact]
    public async Task An_Unknown_School_Is_Not_Found()
    {
        var superAdmin = await TokenAsync(AuthApiFactory.SuperAdminEmail, AuthApiFactory.SuperAdminPassword);

        (await SendAsync(HttpMethod.Put, Route(Guid.NewGuid()), superAdmin, new { tier = "Tier1_150" }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_School_Director_Cannot_Change_Its_Own_Tier()
    {
        var directeur = await TokenAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);

        (await SendAsync(HttpMethod.Put, Route(AuthApiFactory.EcoleId), directeur, new { tier = "Tier1_150" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var current = await ReadJsonAsync(await SendAsync(HttpMethod.Get, "/api/v1/onboarding/subscription", directeur));
        current.GetProperty("maxStudentLimit").GetInt32().Should().Be(int.MaxValue, "rien n'a bougé");
    }

    // ------------------------------------------------------------------ Aides

    private async Task<string> TokenAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!.AccessToken;
    }

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

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(), Json);

    private async Task<Guid> CreateClassroomAsync(string token)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/v1/classrooms", token,
            new { name = $"CT {Guid.NewGuid():N}"[..12], level = "Primaire", capacity = 40 });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await ReadJsonAsync(response)).GetProperty("id").GetGuid();
    }

    private Task<HttpResponseMessage> CreateStudentAsync(string token, Guid classroomId) =>
        SendAsync(HttpMethod.Post, "/api/v1/students", token, new
        {
            fullName = "Eleve Tranche",
            birthDate = "2015-03-12",
            birthPlace = "Dakar",
            gender = "F",
            classroomId,
            guardianName = "Tuteur Test",
            guardianPhone = "+221771234567"
        });
}
