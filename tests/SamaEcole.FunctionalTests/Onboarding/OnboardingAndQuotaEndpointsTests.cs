using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SamaEcole.FunctionalTests.Common;
using Xunit;

namespace SamaEcole.FunctionalTests.Onboarding;

/// <summary>
/// Onboarding & Pricing SaaS, de bout en bout par l'API : le garde de routage (PendingOnboarding), le choix
/// du profil/de la tranche, et le quota d'élèves (plafond nominal, avertissement, refus 422).
/// </summary>
public class OnboardingAndQuotaEndpointsTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    private record Tokens(string AccessToken, int ExpiresIn);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Remet la souscription de l'école de test dans son état d'origine : Active, illimitée.</summary>
    public Task InitializeAsync() => SetSubscriptionAsync("Active", int.MaxValue, int.MaxValue);

    public Task DisposeAsync() => Task.CompletedTask;

    // ------------------------------------------------------------------ Garde de routage

    [Fact]
    public async Task While_Pending_Onboarding_Every_Business_Route_Is_Refused_With_The_Redirect_Target()
    {
        var token = await TokenAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);
        await SetSubscriptionAsync("PendingOnboarding", 150, 160);

        var response = await SendAsync(HttpMethod.Get, "/api/v1/students?page=1&pageSize=5", token);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await ReadJsonAsync(response);
        body.GetProperty("code").GetString().Should().Be("ONBOARDING_REQUIRED");
        body.GetProperty("details").GetProperty("redirectTo").GetString().Should().Be("/onboarding/select-profile");
    }

    [Fact]
    public async Task While_Pending_Onboarding_Session_And_Onboarding_Routes_Stay_Open()
    {
        await SetSubscriptionAsync("PendingOnboarding", 150, 160);

        // Connexion : jamais bloquée.
        var token = await TokenAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);

        var response = await SendAsync(HttpMethod.Get, "/api/v1/onboarding/subscription", token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadJsonAsync(response)).GetProperty("status").GetString().Should().Be("PendingOnboarding");
    }

    [Fact]
    public async Task An_Active_School_Is_Not_Restricted()
    {
        var token = await TokenAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);

        (await SendAsync(HttpMethod.Get, "/api/v1/students?page=1&pageSize=5", token)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ------------------------------------------------------------------ Choix du profil et de la tranche

    [Fact]
    public async Task The_Director_Selecting_A_Profile_Activates_The_School_And_Applies_The_Tier_And_The_Modules()
    {
        var token = await TokenAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);
        await SetSubscriptionAsync("PendingOnboarding", 150, 160);

        var response = await SendAsync(HttpMethod.Post, "/api/v1/onboarding/select-profile", token,
            new { profile = "InternatDaara", tier = "Tier2_400" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await ReadJsonAsync(response);
        dto.GetProperty("status").GetString().Should().Be("Active");
        dto.GetProperty("profileType").GetString().Should().Be("InternatDaara");
        dto.GetProperty("studentQuotaTier").GetString().Should().Be("Tier2_400");
        dto.GetProperty("maxStudentLimit").GetInt32().Should().Be(400);
        dto.GetProperty("softQuotaLimit").GetInt32().Should().Be(420);
        dto.GetProperty("isInternatEnabled").GetBoolean().Should().BeTrue();
        dto.GetProperty("isCoranModuleEnabled").GetBoolean().Should().BeTrue();

        // L'école n'est plus restreinte…
        (await SendAsync(HttpMethod.Get, "/api/v1/students?page=1&pageSize=5", token)).StatusCode.Should().Be(HttpStatusCode.OK);

        // …et la sidebar (qui lit encore SchoolSettings) voit le même profil et les mêmes modules.
        var settings = await ReadJsonAsync(await SendAsync(HttpMethod.Get, "/api/v1/schools/current/settings", token));
        settings.GetProperty("profileEtablissement").GetString().Should().Be("DaaraInternat");
        settings.GetProperty("isInternatEnabled").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Selecting_A_Profile_Twice_Is_Refused_And_Keeps_The_First_Choice()
    {
        var token = await TokenAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);
        await SetSubscriptionAsync("PendingOnboarding", 150, 160);

        (await SendAsync(HttpMethod.Post, "/api/v1/onboarding/select-profile", token,
            new { profile = "Elementaire", tier = "Tier3_800" })).StatusCode.Should().Be(HttpStatusCode.OK);

        var second = await SendAsync(HttpMethod.Post, "/api/v1/onboarding/select-profile", token,
            new { profile = "ComptabiliteRapports", tier = "Tier1_150" });

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadJsonAsync(second)).GetProperty("code").GetString().Should().Be("ONBOARDING_ALREADY_COMPLETED");

        var current = await ReadJsonAsync(await SendAsync(HttpMethod.Get, "/api/v1/onboarding/subscription", token));
        current.GetProperty("profileType").GetString().Should().Be("Elementaire");
        current.GetProperty("maxStudentLimit").GetInt32().Should().Be(800);
    }

    [Fact]
    public async Task The_Custom_Tier_Cannot_Be_Chosen_By_The_School()
    {
        var token = await TokenAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);
        await SetSubscriptionAsync("PendingOnboarding", 150, 160);

        var response = await SendAsync(HttpMethod.Post, "/api/v1/onboarding/select-profile", token,
            new { profile = "EnseignementGeneral", tier = "Tier4_Custom" });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Only_The_Director_Can_Select_The_Profile()
    {
        var token = await TokenAsync(AuthApiFactory.SecretaireEmail, AuthApiFactory.SecretairePassword);
        await SetSubscriptionAsync("PendingOnboarding", 150, 160);

        var response = await SendAsync(HttpMethod.Post, "/api/v1/onboarding/select-profile", token,
            new { profile = "EnseignementGeneral", tier = "Tier1_150" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ------------------------------------------------------------------ Quota

    [Fact]
    public async Task Student_Creation_Warns_Past_The_Nominal_Limit_Then_Is_Refused_Past_The_Soft_Cap()
    {
        var token = await TokenAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);
        var classroomId = await CreateClassroomAsync(token);
        var existing = await CountStudentsAsync();

        // Plafond nominal = effectif + 1, tolérance = effectif + 2.
        await SetSubscriptionAsync("Active", existing + 1, existing + 2);

        var first = await CreateStudentAsync(token, classroomId, "Eleve Un");
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        (await ReadJsonAsync(first)).TryGetProperty("quotaWarning", out var none).Should().BeTrue();
        none.ValueKind.Should().Be(JsonValueKind.Null, "le plafond nominal n'est pas encore dépassé");

        var second = await CreateStudentAsync(token, classroomId, "Eleve Deux");
        second.StatusCode.Should().Be(HttpStatusCode.Created, "la tolérance admet encore cet élève");
        var warning = (await ReadJsonAsync(second)).GetProperty("quotaWarning");
        warning.GetProperty("currentStudentCount").GetInt32().Should().Be(existing + 2);
        warning.GetProperty("maxStudentLimit").GetInt32().Should().Be(existing + 1);
        warning.GetProperty("softQuotaLimit").GetInt32().Should().Be(existing + 2);
        warning.GetProperty("remainingBeforeBlock").GetInt32().Should().Be(0);

        var third = await CreateStudentAsync(token, classroomId, "Eleve Trois");
        third.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var error = await ReadJsonAsync(third);
        error.GetProperty("code").GetString().Should().Be("STUDENT_QUOTA_EXCEEDED");
        error.GetProperty("details").GetProperty("currentStudentCount").GetInt32().Should().Be(existing + 2);
        error.GetProperty("details").GetProperty("softQuotaLimit").GetInt32().Should().Be(existing + 2);

        (await CountStudentsAsync()).Should().Be(existing + 2, "le refus n'écrit rien");
    }

    [Theory]
    [InlineData("Suspended")]
    [InlineData("Expired")]
    [InlineData("PendingApproval")]
    public async Task A_Subscription_That_Is_Not_Active_Refuses_Student_Creation(string status)
    {
        var token = await TokenAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);
        var classroomId = await CreateClassroomAsync(token);
        await SetSubscriptionAsync(status, int.MaxValue, int.MaxValue);

        var response = await CreateStudentAsync(token, classroomId, "Eleve Bloque");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ReadJsonAsync(response)).GetProperty("code").GetString().Should().Be("SUBSCRIPTION_NOT_ACTIVE");
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
            new { name = $"CI {Guid.NewGuid():N}"[..12], level = "Primaire", capacity = 40 });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await ReadJsonAsync(response)).GetProperty("id").GetGuid();
    }

    private Task<HttpResponseMessage> CreateStudentAsync(string token, Guid classroomId, string fullName) =>
        SendAsync(HttpMethod.Post, "/api/v1/students", token, new
        {
            fullName,
            birthDate = "2015-03-12",
            birthPlace = "Dakar",
            gender = "F",
            classroomId,
            guardianName = "Tuteur Test",
            guardianPhone = "+221771234567"
        });

    private async Task<int> CountStudentsAsync()
    {
        var count = 0;
        await factory.SeedAsOwnerAsync(async db =>
            count = await db.Students.IgnoreQueryFilters().CountAsync(s => s.SchoolId == AuthApiFactory.EcoleId && !s.IsDeleted));
        return count;
    }

    private Task SetSubscriptionAsync(string status, int max, int soft) =>
        factory.ExecuteOwnerSqlAsync(
            $"""
             UPDATE tenant_subscriptions
                SET "Status" = '{status}', "MaxStudentLimit" = {max}, "SoftQuotaLimit" = {soft}
              WHERE "SchoolId" = '{AuthApiFactory.EcoleId}'
             """);
}
