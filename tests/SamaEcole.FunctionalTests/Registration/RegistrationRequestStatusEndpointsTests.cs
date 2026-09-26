using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Xunit;

namespace SamaEcole.FunctionalTests.Registration;

/// <summary>
/// Ticket JGK-I02 — GET /registration-requests/{trackingReference}/status. Critères testés : accès
/// anonyme ; strict minimum public (jamais l'e-mail, le téléphone ou le mot de passe du Directeur) ;
/// référence inconnue -> 404 ; recherche tolérante à la casse et aux espaces.
/// </summary>
public class RegistrationRequestStatusEndpointsTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    public Task InitializeAsync() => factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private record SubmitResult(string TrackingReference);

    private record StatusResult(
        string TrackingReference, string SchoolName, string Status, DateTimeOffset SubmittedAt, string? RejectionReason);

    private const string DirectorEmail = "directeur@suivi.sn";
    private const string DirectorPhone = "+221771119988";

    private async Task<string> SubmitAsync(string schoolName)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/registration-requests", new
        {
            directorFullName = "Fatou Sarr",
            directorEmail = DirectorEmail,
            directorPhone = DirectorPhone,
            directorPassword = "Correct-Horse-9",
            schoolName,
            ownership = "Private", cycleProfile = "Primaire", sizeTier = "Small"
        });

        var result = (await response.Content.ReadFromJsonAsync<SubmitResult>())!;
        return result.TrackingReference;
    }

    [Fact]
    public async Task A_Freshly_Submitted_Request_Should_Be_Pending()
    {
        var reference = await SubmitAsync("École Statut Initial");

        var response = await _client.GetAsync($"/api/v1/registration-requests/{reference}/status");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = (await response.Content.ReadFromJsonAsync<StatusResult>())!;
        result.TrackingReference.Should().Be(reference);
        result.SchoolName.Should().Be("École Statut Initial");
        result.Status.Should().Be("Pending");
        result.RejectionReason.Should().BeNull();
        result.SubmittedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task No_Authentication_Should_Be_Required()
    {
        var reference = await SubmitAsync("École Sans Auth Suivi");

        var response = await _client.GetAsync($"/api/v1/registration-requests/{reference}/status");

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_Response_Must_Never_Contain_Personal_Or_Sensitive_Data()
    {
        var reference = await SubmitAsync("École Confidentialité Suivi");

        var response = await _client.GetAsync($"/api/v1/registration-requests/{reference}/status");
        var body = await response.Content.ReadAsStringAsync();

        // Strict minimum public (critère du ticket) : ni e-mail, ni téléphone, ni mot de passe —
        // seuls trackingReference/schoolName/status/submittedAt/rejectionReason doivent apparaître.
        body.Should().NotContain(DirectorEmail);
        body.Should().NotContain(DirectorPhone);
        body.Should().NotContain("Correct-Horse-9");
        body.Should().NotContain("directorFullName", "le nom du Directeur n'est pas une information publique");
    }

    [Fact]
    public async Task An_Unknown_Reference_Should_Return_404()
    {
        var response = await _client.GetAsync("/api/v1/registration-requests/REG-INCONNU1/status");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_Search_Should_Be_Case_And_Whitespace_Insensitive()
    {
        var reference = await SubmitAsync("École Casse Suivi");

        var response = await _client.GetAsync(
            $"/api/v1/registration-requests/{Uri.EscapeDataString($"  {reference.ToLowerInvariant()}  ")}/status");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = (await response.Content.ReadFromJsonAsync<StatusResult>())!;
        result.TrackingReference.Should().Be(reference);
    }
}
