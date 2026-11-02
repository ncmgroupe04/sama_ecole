using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.Domain.Enums;
using SamaEcole.FunctionalTests.Common;
using Xunit;

namespace SamaEcole.FunctionalTests.Subscriptions;

/// <summary>
/// Ticket JGK-B03 — passage automatique en lecture seule à expiration (SubscriptionAwaitingPaymentMiddleware,
/// étendu). Contrairement à AwaitingPayment (blocage total), ReadOnly ne bloque QUE les requêtes
/// mutantes : l'établissement garde un accès en lecture pendant qu'il régularise.
/// </summary>
public class SubscriptionReadOnlyMiddlewareTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    public Task InitializeAsync() => factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private record Tokens(string AccessToken, string RefreshToken, int ExpiresIn);
    private record SubmitResult(string TrackingReference);
    private record ListItem(Guid Id, string TrackingReference);
    private record ApprovalResult(Guid SchoolId, Guid DirectorUserId, Guid SubscriptionId, string SubscriptionStatus);

    private const string DirectorPassword = "Correct-Horse-9";

    private async Task<Tokens> LoginAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!;
    }

    private Task<Tokens> LoginAsSuperAdminAsync() =>
        LoginAsync(AuthApiFactory.SuperAdminEmail, AuthApiFactory.SuperAdminPassword);

    /// <summary>
    /// Fait naître une école dotée d'un abonnement de bout en bout via le parcours self-service I01 ->
    /// I03 (même helper qu'utilisé par SubscriptionAwaitingPaymentMiddlewareTests, JGK-I04), point
    /// d'entrée le plus simple pour obtenir une VRAIE ligne `subscriptions` — l'école par défaut de
    /// AuthApiFactory (DirecteurEmail) n'en a délibérément aucune.
    /// </summary>
    private async Task<ApprovalResult> CreateSchoolWithSubscriptionAsync(string schoolName, string directorEmail)
    {
        var submit = await _client.PostAsJsonAsync("/api/v1/registration-requests", new
        {
            directorFullName = "Fatou Sarr",
            directorEmail,
            directorPhone = "+221771119988",
            directorPassword = DirectorPassword,
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

    private async Task<HttpResponseMessage> SendAsync(string accessToken, HttpMethod method, string path, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await _client.SendAsync(request);
    }

    [Fact]
    public async Task A_ReadOnly_School_Should_Still_Be_Allowed_To_Read()
    {
        var approval = await CreateSchoolWithSubscriptionAsync("École Lecture Seule 1", "lecture1@i0b3.sn");
        await factory.SetSubscriptionStatusAsync(approval.SchoolId, SubscriptionStatus.ReadOnly);
        await factory.CompleteOnboardingAsync(approval.SchoolId);
        var director = await LoginAsync("lecture1@i0b3.sn", DirectorPassword);

        var response = await SendAsync(director.AccessToken, HttpMethod.Get, "/api/v1/students");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "un abonnement en lecture seule doit encore laisser consulter les données existantes");
    }

    [Fact]
    public async Task A_ReadOnly_School_Should_Be_Blocked_From_Writing()
    {
        var approval = await CreateSchoolWithSubscriptionAsync("École Lecture Seule 2", "lecture2@i0b3.sn");
        await factory.SetSubscriptionStatusAsync(approval.SchoolId, SubscriptionStatus.ReadOnly);
        await factory.CompleteOnboardingAsync(approval.SchoolId);
        var director = await LoginAsync("lecture2@i0b3.sn", DirectorPassword);

        var response = await SendAsync(director.AccessToken, HttpMethod.Post, "/api/v1/classrooms",
            new { name = "6e A" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var responseBody = await response.Content.ReadAsStringAsync();
        responseBody.Should().Contain("SUBSCRIPTION_READ_ONLY");
    }

    [Fact]
    public async Task The_Payments_Path_Should_Not_Be_Intercepted_While_ReadOnly()
    {
        // Même exception que pour AwaitingPayment (JGK-I04) : la régularisation elle-même ne doit
        // jamais être bloquée par la restriction qu'elle sert précisément à lever.
        var approval = await CreateSchoolWithSubscriptionAsync("École Lecture Seule 3", "lecture3@i0b3.sn");
        await factory.SetSubscriptionStatusAsync(approval.SchoolId, SubscriptionStatus.ReadOnly);
        await factory.CompleteOnboardingAsync(approval.SchoolId);
        var director = await LoginAsync("lecture3@i0b3.sn", DirectorPassword);

        var response = await SendAsync(
            director.AccessToken, HttpMethod.Post, $"/api/v1/subscriptions/{approval.SchoolId}/payments",
            new { method = "MobileMoney", billingPeriod = "Monthly" });

        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("SUBSCRIPTION_READ_ONLY");
    }

    [Fact]
    public async Task An_Active_School_Should_Not_Be_Restricted()
    {
        var approval = await CreateSchoolWithSubscriptionAsync("École Lecture Seule 4", "lecture4@i0b3.sn");
        await factory.SetSubscriptionStatusAsync(approval.SchoolId, SubscriptionStatus.Active);
        await factory.CompleteOnboardingAsync(approval.SchoolId);
        var director = await LoginAsync("lecture4@i0b3.sn", DirectorPassword);

        var response = await SendAsync(director.AccessToken, HttpMethod.Get, "/api/v1/students");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_Active_School_Whose_Expiry_Date_Has_Passed_Should_Be_Blocked_Even_Before_The_Daily_Check_Ran()
    {
        // Le contrôle quotidien n'a pas basculé le statut (le serveur dormait) : l'échéance dépassée suffit.
        var approval = await CreateSchoolWithSubscriptionAsync("École Échue 1", "echue1@expiry.sn");
        await factory.SetSubscriptionStatusAsync(approval.SchoolId, SubscriptionStatus.Active);
        await factory.SetSubscriptionExpiryAsync(approval.SchoolId, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-2));
        await factory.CompleteOnboardingAsync(approval.SchoolId);
        var director = await LoginAsync("echue1@expiry.sn", DirectorPassword);

        var response = await SendAsync(director.AccessToken, HttpMethod.Post, "/api/v1/classrooms", new { name = "6e A" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().Contain("SUBSCRIPTION_READ_ONLY");
    }

    [Fact]
    public async Task An_Active_School_Whose_Expiry_Date_Has_Passed_Should_Still_Be_Allowed_To_Read()
    {
        var approval = await CreateSchoolWithSubscriptionAsync("École Échue 2", "echue2@expiry.sn");
        await factory.SetSubscriptionStatusAsync(approval.SchoolId, SubscriptionStatus.Active);
        await factory.SetSubscriptionExpiryAsync(approval.SchoolId, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-2));
        await factory.CompleteOnboardingAsync(approval.SchoolId);
        var director = await LoginAsync("echue2@expiry.sn", DirectorPassword);

        var response = await SendAsync(director.AccessToken, HttpMethod.Get, "/api/v1/students");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_Active_School_Whose_Expiry_Date_Has_Not_Passed_Should_Not_Be_Restricted()
    {
        // Échéance demain (et non « aujourd'hui », pour ne pas dépendre d'un passage de minuit UTC pendant le test).
        var approval = await CreateSchoolWithSubscriptionAsync("École Échue 3", "echue3@expiry.sn");
        await factory.SetSubscriptionStatusAsync(approval.SchoolId, SubscriptionStatus.Active);
        await factory.SetSubscriptionExpiryAsync(approval.SchoolId, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1));
        await factory.CompleteOnboardingAsync(approval.SchoolId);
        var director = await LoginAsync("echue3@expiry.sn", DirectorPassword);

        var response = await SendAsync(director.AccessToken, HttpMethod.Post, "/api/v1/classrooms", new { name = "6e B" });

        (await response.Content.ReadAsStringAsync()).Should().NotContain("SUBSCRIPTION_READ_ONLY");
    }

    [Fact]
    public async Task An_Active_School_Without_Expiry_Date_Should_Not_Be_Restricted()
    {
        var approval = await CreateSchoolWithSubscriptionAsync("École Échue 4", "echue4@expiry.sn");
        await factory.SetSubscriptionStatusAsync(approval.SchoolId, SubscriptionStatus.Active);
        await factory.SetSubscriptionExpiryAsync(approval.SchoolId, null);
        await factory.CompleteOnboardingAsync(approval.SchoolId);
        var director = await LoginAsync("echue4@expiry.sn", DirectorPassword);

        var response = await SendAsync(director.AccessToken, HttpMethod.Post, "/api/v1/classrooms", new { name = "6e C" });

        (await response.Content.ReadAsStringAsync()).Should().NotContain("SUBSCRIPTION_READ_ONLY");
    }

    [Fact]
    public async Task The_Payments_Path_Should_Not_Be_Intercepted_When_The_Expiry_Date_Has_Passed()
    {
        // La voie de sortie (renouvellement) reste ouverte même quand la restriction vient de l'échéance et non du statut.
        var approval = await CreateSchoolWithSubscriptionAsync("École Échue 5", "echue5@expiry.sn");
        await factory.SetSubscriptionStatusAsync(approval.SchoolId, SubscriptionStatus.Active);
        await factory.SetSubscriptionExpiryAsync(approval.SchoolId, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-2));
        await factory.CompleteOnboardingAsync(approval.SchoolId);
        var director = await LoginAsync("echue5@expiry.sn", DirectorPassword);

        var response = await SendAsync(
            director.AccessToken, HttpMethod.Post, $"/api/v1/subscriptions/{approval.SchoolId}/payments",
            new { method = "MobileMoney", billingPeriod = "Monthly" });

        (await response.Content.ReadAsStringAsync()).Should().NotContain("SUBSCRIPTION_READ_ONLY");
    }
}
