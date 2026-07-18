using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Xunit;

namespace SamaEcole.FunctionalTests.Subscriptions;

/// <summary>
/// Ticket JGK-I07 — GET /subscriptions/{schoolId}/payments. Critères testés : réservé au Directeur ;
/// l'établissement de l'URL doit correspondre à la session ; historique trié du plus récent au plus
/// ancien avec tous les champs attendus (date, montant, période, moyen, statut, référence) ; pagination ;
/// le statut/plan/échéance de l'abonnement voyagent dans la même réponse.
/// </summary>
public class GetSubscriptionPaymentsEndpointsTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    public Task InitializeAsync() => factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private record Tokens(string AccessToken, string RefreshToken, int ExpiresIn);
    private record SubmitResult(string TrackingReference);
    private record ListItem(Guid Id, string TrackingReference);
    private record ApprovalResult(Guid SchoolId, Guid DirectorUserId, Guid SubscriptionId, string SubscriptionStatus);
    private record InitiateResult(Guid PaymentId, string RedirectUrl, string Status);

    private record PaymentHistoryItem(
        Guid Id, DateTimeOffset InitiatedAt, DateTimeOffset? ConfirmedAt, decimal Amount, string Currency,
        string BillingPeriod, string Method, string Status, string? ProviderTransactionRef);

    private record PaginatedPayments(
        List<PaymentHistoryItem> Items, int TotalCount, int Page, int PageSize,
        string SubscriptionStatus, string SubscriptionPlan, DateOnly? SubscriptionExpiresAt);

    private const string DirectorPassword = "Correct-Horse-9";
    private const string WebhookProvider = "FakeProvider"; // paymentService.ProviderName du FakePaymentService

    private async Task<Tokens> LoginAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!;
    }

    private Task<Tokens> LoginAsSuperAdminAsync() =>
        LoginAsync(AuthApiFactory.SuperAdminEmail, AuthApiFactory.SuperAdminPassword);

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

    private async Task<Guid> InitiatePaymentAsync(
        string accessToken, Guid schoolId, string method = "MobileMoney", string billingPeriod = "Monthly")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/subscriptions/{schoolId}/payments")
        {
            Content = JsonContent.Create(new { method, billingPeriod })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var response = await _client.SendAsync(request);

        return (await response.Content.ReadFromJsonAsync<InitiateResult>())!.PaymentId;
    }

    private async Task ConfirmPaymentAsync(Guid paymentId)
    {
        var storedPayment = await factory.GetSubscriptionPaymentAsync(paymentId);
        factory.Payments.NextConfirmationIsPaid = true;
        factory.Payments.NextConfirmationAmount = storedPayment!.Amount;

        await _client.PostAsJsonAsync(
            $"/api/v1/webhooks/payments/{WebhookProvider}", new { internalPaymentId = paymentId, signatureValid = true });
    }

    private Task<HttpResponseMessage> ListPaymentsAsync(string accessToken, Guid schoolId, int? page = null, int? pageSize = null)
    {
        var query = new List<string>();
        if (page is { } p) query.Add($"page={p}");
        if (pageSize is { } ps) query.Add($"pageSize={ps}");
        var url = $"/api/v1/subscriptions/{schoolId}/payments" + (query.Count > 0 ? $"?{string.Join('&', query)}" : "");

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return _client.SendAsync(request);
    }

    [Fact]
    public async Task A_Director_Should_See_Their_Payment_History_Most_Recent_First()
    {
        var approval = await CreateAwaitingPaymentSchoolAsync("École Historique I07", "historique-i07@test.sn");
        var director = await LoginAsync("historique-i07@test.sn", DirectorPassword);

        var firstPaymentId = await InitiatePaymentAsync(director.AccessToken, approval.SchoolId, "MobileMoney", "Monthly");
        await ConfirmPaymentAsync(firstPaymentId);
        var secondPaymentId = await InitiatePaymentAsync(director.AccessToken, approval.SchoolId, "BankTransfer", "Yearly");

        var response = await ListPaymentsAsync(director.AccessToken, approval.SchoolId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<PaginatedPayments>())!;

        result.TotalCount.Should().Be(2);
        result.Items.Should().HaveCount(2);

        // Le plus récent (le second payé) doit arriver en premier.
        result.Items[0].Id.Should().Be(secondPaymentId);
        result.Items[0].Status.Should().Be("Initiated");
        result.Items[0].BillingPeriod.Should().Be("Yearly");
        result.Items[0].Method.Should().Be("BankTransfer");
        result.Items[0].ConfirmedAt.Should().BeNull();
        result.Items[0].ProviderTransactionRef.Should().NotBeNullOrWhiteSpace();

        result.Items[1].Id.Should().Be(firstPaymentId);
        result.Items[1].Status.Should().Be("Confirmed");
        result.Items[1].ConfirmedAt.Should().NotBeNull();
        result.Items[1].Amount.Should().Be(25_000m);
        result.Items[1].Currency.Should().Be("XOF");

        // L'abonnement, désormais actif grâce au premier paiement confirmé, voyage dans la même réponse.
        result.SubscriptionStatus.Should().Be("Active");
        result.SubscriptionPlan.Should().Be("Standard");
        result.SubscriptionExpiresAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Pagination_Should_Limit_And_Skip_Items()
    {
        var approval = await CreateAwaitingPaymentSchoolAsync("École Pagination I07", "pagination-i07@test.sn");
        var director = await LoginAsync("pagination-i07@test.sn", DirectorPassword);

        await InitiatePaymentAsync(director.AccessToken, approval.SchoolId);
        await InitiatePaymentAsync(director.AccessToken, approval.SchoolId);
        await InitiatePaymentAsync(director.AccessToken, approval.SchoolId);

        var firstPage = await ListPaymentsAsync(director.AccessToken, approval.SchoolId, page: 1, pageSize: 2);
        var firstResult = (await firstPage.Content.ReadFromJsonAsync<PaginatedPayments>())!;
        firstResult.Items.Should().HaveCount(2);
        firstResult.TotalCount.Should().Be(3);

        var secondPage = await ListPaymentsAsync(director.AccessToken, approval.SchoolId, page: 2, pageSize: 2);
        var secondResult = (await secondPage.Content.ReadFromJsonAsync<PaginatedPayments>())!;
        secondResult.Items.Should().HaveCount(1);
        secondResult.TotalCount.Should().Be(3);
    }

    [Fact]
    public async Task A_Secretariat_Must_Not_Be_Allowed_To_View_Payment_History()
    {
        var approval = await CreateAwaitingPaymentSchoolAsync("École Secrétariat I07", "secretariat-i07@test.sn");
        await factory.CreateAdditionalUserAsync(approval.SchoolId, "secr-i07@test.sn", "Correct-Horse-9", Domain.Enums.Role.Secretariat);
        var secretary = await LoginAsync("secr-i07@test.sn", "Correct-Horse-9");

        var response = await ListPaymentsAsync(secretary.AccessToken, approval.SchoolId);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Targeting_Another_Schools_Url_Should_Be_Rejected()
    {
        var approvalA = await CreateAwaitingPaymentSchoolAsync("École A I07", "ecole-a-i07@test.sn");
        var approvalB = await CreateAwaitingPaymentSchoolAsync("École B I07", "ecole-b-i07@test.sn");
        var directorA = await LoginAsync("ecole-a-i07@test.sn", DirectorPassword);

        var response = await ListPaymentsAsync(directorA.AccessToken, approvalB.SchoolId);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_School_Without_Any_Subscription_Should_Return_404()
    {
        // École semée par AuthApiFactory (parcours JGK-B01) : aucune ligne Subscriptions.
        var director = await LoginAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);

        var response = await ListPaymentsAsync(director.AccessToken, AuthApiFactory.EcoleId);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
