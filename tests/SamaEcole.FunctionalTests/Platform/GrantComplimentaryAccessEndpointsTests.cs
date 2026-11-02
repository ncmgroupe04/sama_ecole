using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.Domain.Enums;
using SamaEcole.FunctionalTests.Common;
using Xunit;

namespace SamaEcole.FunctionalTests.Platform;

/// <summary>
/// POST /admin/platform/schools/{schoolId}/complimentary-access — bouton « Offrir un accès » (module
/// Tarification &amp; Promotions). Sans test dédié jusqu'ici : la conversion `DateOnly.ToDateTime(...)`
/// passée telle quelle à la fonction SECURITY DEFINER `grant_complimentary_subscription(date)` se liait
/// en `timestamp`, que PostgreSQL ne convertit PAS implicitement vers `date` pour la résolution de
/// surcharge — l'appel échouait donc TOUJOURS en base réelle (42883 « function ... does not exist »),
/// masqué uniquement par l'absence de test exerçant le vrai rôle applicatif contre un vrai Postgres.
/// </summary>
public class GrantComplimentaryAccessEndpointsTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    public Task InitializeAsync() => factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private record Tokens(string AccessToken, string RefreshToken, int ExpiresIn);
    private record SubmitResult(string TrackingReference);
    private record ListItem(Guid Id, string TrackingReference);
    private record ApprovalResult(Guid SchoolId, Guid DirectorUserId, Guid SubscriptionId, string SubscriptionStatus);

    private async Task<Tokens> LoginAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!;
    }

    private Task<Tokens> LoginAsSuperAdminAsync() =>
        LoginAsync(AuthApiFactory.SuperAdminEmail, AuthApiFactory.SuperAdminPassword);

    private async Task<ApprovalResult> CreateSchoolWithSubscriptionAsync(string schoolName, string directorEmail)
    {
        var submit = await _client.PostAsJsonAsync("/api/v1/registration-requests", new
        {
            directorFullName = "Fatou Sarr",
            directorEmail,
            directorPhone = "+221771119988",
            directorPassword = "Correct-Horse-9",
            schoolName,
            ownership = "Private", cycleProfile = "Primaire", sizeTier = "Small"
        });
        var reference = (await submit.Content.ReadFromJsonAsync<SubmitResult>())!.TrackingReference;

        var superAdmin = await LoginAsSuperAdminAsync();

        var listRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/registration-requests?status=Pending");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", superAdmin.AccessToken);
        var listResponse = await _client.SendAsync(listRequest);
        var id = (await listResponse.Content.ReadFromJsonAsync<List<ListItem>>())!
            .Single(r => r.TrackingReference == reference).Id;

        var approveRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/registration-requests/{id}/approve");
        approveRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", superAdmin.AccessToken);
        var approveResponse = await _client.SendAsync(approveRequest);

        return (await approveResponse.Content.ReadFromJsonAsync<ApprovalResult>())!;
    }

    [Fact]
    public async Task SuperAdmin_Should_Grant_A_Complimentary_Access_And_Activate_The_Subscription()
    {
        var approval = await CreateSchoolWithSubscriptionAsync("École Cadeau", "cadeau@platform.sn");
        var superAdmin = await LoginAsSuperAdminAsync();

        var request = new HttpRequestMessage(
            HttpMethod.Post, $"/api/v1/admin/platform/schools/{approval.SchoolId}/complimentary-access")
        {
            Content = JsonContent.Create(new { plan = "Premium", durationMonths = 3 })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", superAdmin.AccessToken);

        var response = await _client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, "body was: {0}", body);

        var subscription = await factory.GetSubscriptionAsync(approval.SchoolId);
        subscription.Should().NotBeNull();
        subscription!.Status.Should().Be(SubscriptionStatus.Active);
        subscription.Plan.Should().Be(SubscriptionPlan.Premium);
        subscription.ExpiresAt.Should().NotBeNull();
    }

    [Fact]
    public async Task SuperAdmin_Should_Grant_A_Complimentary_Access_For_A_Number_Of_Days()
    {
        var approval = await CreateSchoolWithSubscriptionAsync("École Cadeau Jours", "cadeau-jours@platform.sn");
        var superAdmin = await LoginAsSuperAdminAsync();

        var before = DateOnly.FromDateTime(DateTime.UtcNow);
        var request = new HttpRequestMessage(
            HttpMethod.Post, $"/api/v1/admin/platform/schools/{approval.SchoolId}/complimentary-access")
        {
            Content = JsonContent.Create(new { plan = "Standard", durationDays = 10 })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", superAdmin.AccessToken);

        var response = await _client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        var after = DateOnly.FromDateTime(DateTime.UtcNow);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, "body was: {0}", body);

        var subscription = await factory.GetSubscriptionAsync(approval.SchoolId);
        subscription!.Status.Should().Be(SubscriptionStatus.Active);
        subscription.ExpiresAt.Should().NotBeNull();
        // Encadré par avant/après l'appel : le test ne casse pas s'il traverse minuit UTC.
        subscription.ExpiresAt!.Value.Should().BeOnOrAfter(before.AddDays(10)).And.BeOnOrBefore(after.AddDays(10));
    }

    [Theory]
    [InlineData("durationMonths", 100)]
    [InlineData("durationMonths", 0)]
    [InlineData("durationDays", 2000)]
    [InlineData("durationDays", 0)]
    public async Task A_Duration_Out_Of_Range_Must_Be_Rejected_With_A_Validation_Error(string field, int value)
    {
        // Régression : la commande était déclarée en IRequest simple, donc ValidationBehavior ne s'exécutait pas
        // et ces durées étaient acceptées (le validateur existait mais n'était jamais appelé).
        var approval = await CreateSchoolWithSubscriptionAsync($"École Cadeau {field}{value}", $"cadeau-{field}-{value}@platform.sn".ToLowerInvariant());
        var superAdmin = await LoginAsSuperAdminAsync();

        var request = new HttpRequestMessage(
            HttpMethod.Post, $"/api/v1/admin/platform/schools/{approval.SchoolId}/complimentary-access")
        {
            Content = JsonContent.Create(new Dictionary<string, object> { ["plan"] = "Standard", [field] = value })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", superAdmin.AccessToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Giving_Both_Months_And_Days_Must_Be_Rejected_With_A_Validation_Error()
    {
        var approval = await CreateSchoolWithSubscriptionAsync("École Cadeau Double", "cadeau-double@platform.sn");
        var superAdmin = await LoginAsSuperAdminAsync();

        var request = new HttpRequestMessage(
            HttpMethod.Post, $"/api/v1/admin/platform/schools/{approval.SchoolId}/complimentary-access")
        {
            Content = JsonContent.Create(new { plan = "Standard", durationMonths = 1, durationDays = 10 })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", superAdmin.AccessToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_Directeur_Must_Not_Be_Allowed_To_Grant_Complimentary_Access()
    {
        var approval = await CreateSchoolWithSubscriptionAsync("École Cadeau Interdite", "interdit@platform.sn");
        var directeur = await LoginAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);

        var request = new HttpRequestMessage(
            HttpMethod.Post, $"/api/v1/admin/platform/schools/{approval.SchoolId}/complimentary-access")
        {
            Content = JsonContent.Create(new { plan = "Premium", durationMonths = 3 })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", directeur.AccessToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
