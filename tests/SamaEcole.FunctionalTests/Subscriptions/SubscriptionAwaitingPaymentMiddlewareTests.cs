using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.Domain.Enums;
using SamaEcole.FunctionalTests.Common;
using Xunit;

namespace SamaEcole.FunctionalTests.Subscriptions;

/// <summary>
/// Ticket JGK-I04 — restriction d'accès tant que Subscriptions.Status = AwaitingPayment
/// (SubscriptionAwaitingPaymentMiddleware). Critères testés : blocage effectif sur les modules
/// fonctionnels, pour N'IMPORTE QUEL rôle de l'école concernée (pas seulement le Directeur) ; exceptions
/// obligatoires (session, paiement) ; le Super Admin n'est jamais concerné ; aucune régression pour un
/// établissement sans abonnement (parcours JGK-B01) ou dont l'abonnement est Active.
/// </summary>
public class SubscriptionAwaitingPaymentMiddlewareTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>, IAsyncLifetime
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

    /// <summary>Fait naître une école AwaitingPayment de bout en bout via le parcours I01 -> I03.</summary>
    private async Task<ApprovalResult> CreateAwaitingPaymentSchoolAsync(string schoolName, string directorEmail)
    {
        var submit = await _client.PostAsJsonAsync("/api/v1/registration-requests", new
        {
            directorFullName = "Fatou Sarr",
            directorEmail,
            directorPhone = "+221771119988",
            directorPassword = DirectorPassword,
            schoolName,
            requestedPlan = "Standard"
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

    private async Task<HttpResponseMessage> GetAsync(string accessToken, string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await _client.SendAsync(request);
    }

    // ------------------------------------------------------------ Blocage

    [Theory]
    [InlineData("/api/v1/students")]
    [InlineData("/api/v1/classrooms")]
    [InlineData("/api/v1/finance/dashboard")]
    public async Task A_Director_Of_A_School_Awaiting_Payment_Should_Be_Blocked_From_Functional_Modules(string path)
    {
        var approval = await CreateAwaitingPaymentSchoolAsync("École Bloquée", "bloque@i04.sn");
        var director = await LoginAsync("bloque@i04.sn", DirectorPassword);

        var response = await GetAsync(director.AccessToken, path);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("SUBSCRIPTION_AWAITING_PAYMENT");

        approval.SubscriptionStatus.Should().Be("AwaitingPayment");
    }

    [Fact]
    public async Task Any_Role_Of_The_Same_Restricted_School_Should_Also_Be_Blocked()
    {
        // Le middleware ne lit QUE le tenant (schoolId), jamais le rôle : le vérifier sur un rôle
        // DIFFÉRENT du Directeur prouve qu'il ne s'agit pas d'une coïncidence liée à un rôle précis.
        var approval = await CreateAwaitingPaymentSchoolAsync("École Bloquée Secrétariat", "secretariat@i04.sn");
        await factory.CreateAdditionalUserAsync(
            approval.SchoolId, "secretaire@i04.sn", "Correct-Horse-9", Role.Secretariat);

        var secretary = await LoginAsync("secretaire@i04.sn", "Correct-Horse-9");
        var response = await GetAsync(secretary.AccessToken, "/api/v1/students");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().Contain("SUBSCRIPTION_AWAITING_PAYMENT");
    }

    // ------------------------------------------------------------ Exceptions obligatoires

    [Fact]
    public async Task Logout_Should_Remain_Accessible_While_Awaiting_Payment()
    {
        await CreateAwaitingPaymentSchoolAsync("École Déconnexion", "deconnexion@i04.sn");
        var director = await LoginAsync("deconnexion@i04.sn", DirectorPassword);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", director.AccessToken);
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "la session (déconnexion) doit rester accessible même en mode restreint");
    }

    [Fact]
    public async Task The_Payments_Path_Should_Not_Be_Intercepted_By_The_Restriction()
    {
        // La route est désormais réellement implémentée (ticket JGK-I05) : ce test, écrit avant elle,
        // vérifiait alors l'absence de blocage par un 404 de routage. Il vérifie maintenant la même
        // chose plus directement — un vrai succès, la seule preuve possible qu'un Directeur restreint
        // peut effectivement l'utiliser (voir aussi InitiateSubscriptionPaymentEndpointsTests, JGK-I05).
        var approval = await CreateAwaitingPaymentSchoolAsync("École Paiement", "paiement@i04.sn");
        var director = await LoginAsync("paiement@i04.sn", DirectorPassword);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/subscriptions/{approval.SchoolId}/payments")
        {
            Content = JsonContent.Create(new { method = "MobileMoney", billingPeriod = "Monthly" })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", director.AccessToken);
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("SUBSCRIPTION_AWAITING_PAYMENT");
    }

    // ------------------------------------------------------------ Non-restriction

    [Fact]
    public async Task SuperAdmin_Should_Never_Be_Restricted()
    {
        // Même en présence d'une école AwaitingPayment ailleurs sur la plateforme.
        await CreateAwaitingPaymentSchoolAsync("École Sans Rapport", "sansrapport@i04.sn");
        var superAdmin = await LoginAsSuperAdminAsync();

        var response = await GetAsync(superAdmin.AccessToken, "/api/v1/schools");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_School_Without_Any_Subscription_Should_Not_Be_Restricted()
    {
        // Canari de non-régression : l'école semée par AuthApiFactory (parcours JGK-B01, sans ligne
        // Subscriptions) doit continuer à fonctionner exactement comme avant ce ticket.
        var director = await LoginAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);

        var response = await GetAsync(director.AccessToken, "/api/v1/students");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_School_With_An_Active_Subscription_Should_Not_Be_Restricted()
    {
        // Preuve que le middleware discrimine bien le STATUT (pas seulement la présence d'une ligne) :
        // simule la confirmation d'un paiement (webhook JGK-I06, pas encore livré) puis revérifie l'accès.
        var approval = await CreateAwaitingPaymentSchoolAsync("École Payée", "payee@i04.sn");
        await factory.SetSubscriptionStatusAsync(approval.SchoolId, SubscriptionStatus.Active);

        var director = await LoginAsync("payee@i04.sn", DirectorPassword);
        var response = await GetAsync(director.AccessToken, "/api/v1/students");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "le statut de l'abonnement en base gouverne l'accès, jamais un contenu figé dans le token");
    }
}
