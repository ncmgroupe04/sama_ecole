using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Xunit;

namespace SamaEcole.FunctionalTests.Users;

/// <summary>
/// PATCH /users/{userId}/password — le Directeur fixe directement un nouveau mot de passe.
/// Critères : l'ancien mot de passe cesse de fonctionner, le nouveau fonctionne, les sessions déjà
/// ouvertes sont révoquées (même raisonnement que le blocage, JGK-A05), impossible de réinitialiser
/// son propre mot de passe par cette voie, réservé au Directeur.
/// </summary>
public class ResetUserPasswordEndpointsTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    public Task InitializeAsync() => factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private record Tokens(string AccessToken, string RefreshToken, int ExpiresIn);
    private record ResetResult(Guid UserId, int RevokedSessions);

    private const string NewPassword = "New-Correct-Horse-9!";

    private async Task<Tokens> LoginAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!;
    }

    private Task<Tokens> LoginAsDirecteurAsync() =>
        LoginAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);

    private async Task<HttpResponseMessage> ResetPasswordAsync(string accessToken, Guid userId, string newPassword)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/users/{userId}/password")
        {
            Content = JsonContent.Create(new { newPassword })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return await _client.SendAsync(request);
    }

    [Fact]
    public async Task Resetting_A_Password_Should_Kill_The_Existing_Session_And_Reject_The_Old_Password()
    {
        // La secrétaire est connectée AVANT la réinitialisation : sans révocation, elle resterait
        // opérationnelle sous l'ancien mot de passe jusqu'à 14 jours (même raisonnement que JGK-A05).
        var secretaire = await LoginAsync(AuthApiFactory.SecretaireEmail, AuthApiFactory.SecretairePassword);

        var directeur = await LoginAsDirecteurAsync();
        var response = await ResetPasswordAsync(directeur.AccessToken, AuthApiFactory.SecretaireId, NewPassword);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<ResetResult>())!;
        result.RevokedSessions.Should().BeGreaterThan(0, "la session ouverte doit être coupée, pas laissée vivre");

        var refreshed = await _client.PostAsJsonAsync(
            "/api/v1/auth/refresh", new { refreshToken = secretaire.RefreshToken });
        refreshed.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var loginWithOldPassword = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = AuthApiFactory.SecretaireEmail,
            password = AuthApiFactory.SecretairePassword
        });
        loginWithOldPassword.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "l'ancien mot de passe ne doit plus jamais fonctionner");
    }

    [Fact]
    public async Task The_New_Password_Should_Work_Immediately()
    {
        var directeur = await LoginAsDirecteurAsync();

        var response = await ResetPasswordAsync(directeur.AccessToken, AuthApiFactory.SecretaireId, NewPassword);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = AuthApiFactory.SecretaireEmail,
            password = NewPassword
        });

        login.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_Weak_New_Password_Should_Be_Rejected()
    {
        var directeur = await LoginAsDirecteurAsync();

        var response = await ResetPasswordAsync(directeur.AccessToken, AuthApiFactory.SecretaireId, "short");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_Directeur_Should_Not_Be_Able_To_Reset_His_Own_Password_This_Way()
    {
        var directeur = await LoginAsDirecteurAsync();

        var response = await ResetPasswordAsync(directeur.AccessToken, AuthApiFactory.DirecteurId, NewPassword);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_Secretariat_Must_Not_Be_Allowed_To_Reset_Anyones_Password()
    {
        var secretaire = await LoginAsync(AuthApiFactory.SecretaireEmail, AuthApiFactory.SecretairePassword);

        var response = await ResetPasswordAsync(secretaire.AccessToken, AuthApiFactory.DirecteurId, NewPassword);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Resetting_The_Password_Of_An_Unknown_User_Should_Return_404()
    {
        var directeur = await LoginAsDirecteurAsync();

        var response = await ResetPasswordAsync(directeur.AccessToken, Guid.NewGuid(), NewPassword);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
