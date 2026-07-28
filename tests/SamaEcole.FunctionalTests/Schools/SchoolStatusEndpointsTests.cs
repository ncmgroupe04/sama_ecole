using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Xunit;

namespace SamaEcole.FunctionalTests.Schools;

/// <summary>
/// Ticket JGK-B01 — activation/suspension/blocage d'un établissement (console Super Admin, écran
/// Établissements). Critères testés : seul un Super Admin peut agir ; un motif est obligatoire ;
/// suspendre/bloquer coupe IMMÉDIATEMENT l'accès (login ET refresh) de tous les utilisateurs de
/// l'école visée, pas seulement à l'expiration naturelle des tokens ; réactiver restaure l'accès.
/// </summary>
public class SchoolStatusEndpointsTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    public Task InitializeAsync() => factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private record Tokens(string AccessToken, string RefreshToken, int ExpiresIn);
    private record SchoolResult(Guid SchoolId, string Name, Guid DirectorUserId, string DirectorEmail);
    private record ChangeStatusResult(Guid SchoolId, string PreviousStatus, string NewStatus, int RevokedSessions);

    private async Task<Tokens> LoginAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!;
    }

    private Task<Tokens> LoginAsSuperAdminAsync() =>
        LoginAsync(AuthApiFactory.SuperAdminEmail, AuthApiFactory.SuperAdminPassword);

    private async Task<SchoolResult> CreateSchoolAsync(string accessToken, string name, string directorEmail)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/schools")
        {
            Content = JsonContent.Create(new
            {
                name,
                address = "Rue 12, Médina, Dakar",
                phone = "+221771234567",
                directorEmail,
                directorFullName = "Aminata Sow"
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<SchoolResult>())!;
    }

    private async Task<HttpResponseMessage> ChangeStatusAsync(
        string accessToken, Guid schoolId, string status, string? reason = "Impayé constaté après relance.")
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/schools/{schoolId}/status")
        {
            Content = JsonContent.Create(new { status, reason })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return await _client.SendAsync(request);
    }

    [Fact]
    public async Task SuperAdmin_Should_Suspend_A_School()
    {
        var superAdmin = await LoginAsSuperAdminAsync();
        var school = await CreateSchoolAsync(superAdmin.AccessToken, "École Les Filaos", "directrice1@filaos.sn");

        var response = await ChangeStatusAsync(superAdmin.AccessToken, school.SchoolId, "Suspended");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<ChangeStatusResult>())!;
        result.PreviousStatus.Should().Be("Active");
        result.NewStatus.Should().Be("Suspended");
    }

    [Fact]
    public async Task Suspending_A_School_Should_Immediately_Prevent_Its_Director_From_Logging_In()
    {
        var superAdmin = await LoginAsSuperAdminAsync();
        var school = await CreateSchoolAsync(superAdmin.AccessToken, "École Les Filaos", "directrice2@filaos.sn");

        var email = factory.Emails.LastTo("directrice2@filaos.sn")!;
        var password = FakeEmailSender.ExtractPassword(email);

        // La connexion fonctionne encore normalement AVANT la suspension.
        await LoginAsync("directrice2@filaos.sn", password);

        await ChangeStatusAsync(superAdmin.AccessToken, school.SchoolId, "Suspended");

        var loginResponse = await _client.PostAsJsonAsync(
            "/api/v1/auth/login", new { email = "directrice2@filaos.sn", password });

        loginResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "un établissement suspendu ne doit plus délivrer de token à aucun de ses utilisateurs");
    }

    [Fact]
    public async Task Suspending_A_School_Should_Revoke_Already_Issued_Refresh_Tokens()
    {
        // C'EST le critère central du ticket : sans révocation immédiate, le Directeur déjà connecté
        // pourrait continuer à travailler jusqu'à 14 jours via son refresh token — la suspension ne
        // suspendrait alors rien en pratique.
        var superAdmin = await LoginAsSuperAdminAsync();
        var school = await CreateSchoolAsync(superAdmin.AccessToken, "École Les Filaos", "directrice3@filaos.sn");

        var email = factory.Emails.LastTo("directrice3@filaos.sn")!;
        var password = FakeEmailSender.ExtractPassword(email);
        var director = await LoginAsync("directrice3@filaos.sn", password);

        var statusResponse = await ChangeStatusAsync(superAdmin.AccessToken, school.SchoolId, "Suspended");
        var result = (await statusResponse.Content.ReadFromJsonAsync<ChangeStatusResult>())!;
        result.RevokedSessions.Should().BeGreaterThanOrEqualTo(1);

        var refreshResponse = await _client.PostAsJsonAsync(
            "/api/v1/auth/refresh", new { refreshToken = director.RefreshToken });

        refreshResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "le refresh token émis avant la suspension doit être révoqué, pas seulement bloqué a posteriori");
    }

    [Fact]
    public async Task Reactivating_A_School_Should_Restore_Login()
    {
        var superAdmin = await LoginAsSuperAdminAsync();
        var school = await CreateSchoolAsync(superAdmin.AccessToken, "École Les Filaos", "directrice4@filaos.sn");

        var email = factory.Emails.LastTo("directrice4@filaos.sn")!;
        var password = FakeEmailSender.ExtractPassword(email);

        await ChangeStatusAsync(superAdmin.AccessToken, school.SchoolId, "Suspended");
        var reactivateResponse = await ChangeStatusAsync(superAdmin.AccessToken, school.SchoolId, "Active");
        reactivateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var loginResponse = await _client.PostAsJsonAsync(
            "/api/v1/auth/login", new { email = "directrice4@filaos.sn", password });

        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_Directeur_Must_Not_Be_Allowed_To_Change_A_School_Status()
    {
        var superAdmin = await LoginAsSuperAdminAsync();
        var school = await CreateSchoolAsync(superAdmin.AccessToken, "École Les Filaos", "directrice5@filaos.sn");
        var directeur = await LoginAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);

        var response = await ChangeStatusAsync(directeur.AccessToken, school.SchoolId, "Suspended");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_Empty_Reason_Should_Be_Rejected()
    {
        var superAdmin = await LoginAsSuperAdminAsync();
        var school = await CreateSchoolAsync(superAdmin.AccessToken, "École Les Filaos", "directrice6@filaos.sn");

        var response = await ChangeStatusAsync(superAdmin.AccessToken, school.SchoolId, "Suspended", reason: "");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Setting_The_Same_Status_Twice_Should_Be_Rejected()
    {
        var superAdmin = await LoginAsSuperAdminAsync();
        var school = await CreateSchoolAsync(superAdmin.AccessToken, "École Les Filaos", "directrice7@filaos.sn");

        var response = await ChangeStatusAsync(superAdmin.AccessToken, school.SchoolId, "Active");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "réécrire le même statut ne changerait rien mais polluerait l'audit d'entrées vides");
    }
}
