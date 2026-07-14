using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Xunit;

namespace SamaEcole.FunctionalTests.Users;

/// <summary>
/// Ticket JGK-A05 — parcours HTTP réel : PATCH /users/{id}/status et son historique.
/// Critères du ticket : un compte Blocked ne peut plus obtenir de token ; l'historique est consultable.
/// </summary>
public class UserStatusEndpointsTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    // Chaque test bloque/débloque les mêmes comptes : on repart d'un état neuf, sinon l'ordre
    // d'exécution des tests deviendrait significatif.
    public Task InitializeAsync() => factory.ResetTestUsersAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private record Tokens(string AccessToken, string RefreshToken, int ExpiresIn);
    private record StatusResult(Guid UserId, string PreviousStatus, string NewStatus, int RevokedSessions);
    private record HistoryEntry(string PreviousStatus, string NewStatus, string Reason, Guid ChangedByUserId, DateTimeOffset ChangedAt);

    private async Task<Tokens> LoginAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!;
    }

    private Task<Tokens> LoginAsDirecteurAsync() =>
        LoginAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);

    private async Task<HttpResponseMessage> ChangeStatusAsync(
        string accessToken, Guid userId, string status, string reason)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/users/{userId}/status")
        {
            Content = JsonContent.Create(new { status, reason })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return await _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> GetHistoryAsync(string accessToken, Guid userId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/users/{userId}/status-history");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return await _client.SendAsync(request);
    }

    [Fact]
    public async Task Blocking_A_User_Should_Immediately_Kill_Their_Existing_Session()
    {
        // La secrétaire est connectée AVANT d'être bloquée : c'est tout l'enjeu du ticket. Sans
        // révocation de ses refresh tokens, elle resterait opérationnelle jusqu'à 14 jours.
        var secretaire = await LoginAsync(AuthApiFactory.SecretaireEmail, AuthApiFactory.SecretairePassword);

        var directeur = await LoginAsDirecteurAsync();
        var response = await ChangeStatusAsync(
            directeur.AccessToken, AuthApiFactory.SecretaireId, "Blocked", "Départ de l'établissement");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = (await response.Content.ReadFromJsonAsync<StatusResult>())!;
        result.PreviousStatus.Should().Be("Active");
        result.NewStatus.Should().Be("Blocked");
        result.RevokedSessions.Should().BeGreaterThan(0, "la session ouverte doit être coupée, pas laissée vivre");

        // Son refresh token ne vaut plus rien : elle ne peut pas prolonger sa session.
        var refreshed = await _client.PostAsJsonAsync(
            "/api/v1/auth/refresh", new { refreshToken = secretaire.RefreshToken });

        refreshed.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Blocked_User_Should_No_Longer_Obtain_A_Token()
    {
        // Critère explicite du ticket.
        var directeur = await LoginAsDirecteurAsync();

        var blocked = await ChangeStatusAsync(
            directeur.AccessToken, AuthApiFactory.SecretaireId, "Blocked", "Compte compromis");
        blocked.StatusCode.Should().Be(HttpStatusCode.OK);

        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = AuthApiFactory.SecretaireEmail,
            password = AuthApiFactory.SecretairePassword
        });

        login.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "un compte bloqué ne doit plus jamais obtenir de token, même avec le bon mot de passe");
    }

    [Fact]
    public async Task Status_History_Should_Be_Readable_And_Keep_The_Reason()
    {
        var directeur = await LoginAsDirecteurAsync();

        await ChangeStatusAsync(
            directeur.AccessToken, AuthApiFactory.SecretaireId, "Suspended", "Absences répétées non justifiées");
        await ChangeStatusAsync(
            directeur.AccessToken, AuthApiFactory.SecretaireId, "Active", "Régularisation de la situation");

        var response = await GetHistoryAsync(directeur.AccessToken, AuthApiFactory.SecretaireId);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var history = (await response.Content.ReadFromJsonAsync<List<HistoryEntry>>())!;

        history.Should().HaveCount(2);
        // Du plus récent au plus ancien.
        history[0].NewStatus.Should().Be("Active");
        history[0].Reason.Should().Be("Régularisation de la situation");
        history[1].NewStatus.Should().Be("Suspended");
        history[1].Reason.Should().Be("Absences répétées non justifiées");
        history[1].ChangedByUserId.Should().Be(AuthApiFactory.DirecteurId, "l'auteur vient du JWT");
    }

    [Fact]
    public async Task Changing_Status_Without_A_Reason_Should_Be_Rejected()
    {
        var directeur = await LoginAsDirecteurAsync();

        var response = await ChangeStatusAsync(
            directeur.AccessToken, AuthApiFactory.SecretaireId, "Suspended", reason: "");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "le motif est obligatoire — un blocage sans justification n'est pas auditable");
    }

    [Fact]
    public async Task A_Directeur_Should_Not_Be_Able_To_Change_His_Own_Status()
    {
        // Sinon il peut se bloquer lui-même et plus personne n'administre l'établissement.
        var directeur = await LoginAsDirecteurAsync();

        var response = await ChangeStatusAsync(
            directeur.AccessToken, AuthApiFactory.DirecteurId, "Blocked", "Erreur de manipulation");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_Secretariat_Must_Not_Be_Allowed_To_Suspend_Anyone()
    {
        // docs/Volume_7_Security.md §4 : « Suspendre » est réservé au Directeur.
        var secretaire = await LoginAsync(AuthApiFactory.SecretaireEmail, AuthApiFactory.SecretairePassword);

        var response = await ChangeStatusAsync(
            secretaire.AccessToken, AuthApiFactory.DirecteurId, "Blocked", "Tentative d'escalade");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Changing_The_Status_Of_An_Unknown_User_Should_Return_404()
    {
        var directeur = await LoginAsDirecteurAsync();

        var response = await ChangeStatusAsync(
            directeur.AccessToken, Guid.NewGuid(), "Blocked", "Utilisateur inexistant");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
