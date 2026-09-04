using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Xunit;

namespace SamaEcole.FunctionalTests.Auth;

/// <summary>
/// POST /auth/change-password — l'utilisateur AUTHENTIFIÉ change lui-même son mot de passe, preuve à
/// l'appui de l'ancien. Distinct de PasswordResetEndpointsTests (jeton par e-mail, pas de session) et
/// de ResetUserPasswordEndpointsTests (un Directeur fixe le mot de passe d'AUTRUI) : critères propres
/// à cette troisième voie — l'ancien mot de passe doit être VÉRIFIÉ, pas seulement remplacé, et la
/// route doit fonctionner identiquement pour un Super Admin (aucun SchoolId, `users` sous RLS —
/// ticket JGK-A03) et pour un Directeur.
/// </summary>
public class ChangePasswordEndpointsTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    public Task InitializeAsync() => factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private record Tokens(string AccessToken, string RefreshToken, int ExpiresIn);
    private record ChangeResult(int RevokedSessions);

    private const string NewPassword = "Change-Moi-Vraiment-9!";

    private async Task<Tokens> LoginAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!;
    }

    private async Task<HttpResponseMessage> ChangePasswordAsync(
        string accessToken, string currentPassword, string newPassword)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/change-password")
        {
            Content = JsonContent.Create(new { currentPassword, newPassword })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return await _client.SendAsync(request);
    }

    [Fact]
    public async Task A_Correct_Current_Password_Should_Let_The_User_Change_It_And_Revoke_Every_Session()
    {
        // Connectée AVANT le changement : sans révocation, cette session resterait opérationnelle
        // sous l'ancien mot de passe (même raisonnement que les deux autres voies de changement).
        var beforeChange = await LoginAsync(AuthApiFactory.SecretaireEmail, AuthApiFactory.SecretairePassword);

        var response = await ChangePasswordAsync(
            beforeChange.AccessToken, AuthApiFactory.SecretairePassword, NewPassword);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<ChangeResult>())!;
        result.RevokedSessions.Should().BeGreaterThan(0, "la session qui vient de faire la demande doit elle aussi être coupée");

        var refreshed = await _client.PostAsJsonAsync(
            "/api/v1/auth/refresh", new { refreshToken = beforeChange.RefreshToken });
        refreshed.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var loginWithOldPassword = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = AuthApiFactory.SecretaireEmail,
            password = AuthApiFactory.SecretairePassword
        });
        loginWithOldPassword.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "l'ancien mot de passe ne doit plus jamais fonctionner");

        var loginWithNewPassword = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = AuthApiFactory.SecretaireEmail,
            password = NewPassword
        });
        loginWithNewPassword.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Le cœur de cette route : contrairement à ResetUserPasswordCommand (un Directeur fixe le mot de
    /// passe d'AUTRUI sans le connaître), celle-ci exige de PROUVER l'ancien. Un mot de passe correct
    /// mais qui n'est pas le sien ne doit rien changer.
    /// </summary>
    [Fact]
    public async Task An_Incorrect_Current_Password_Should_Be_Rejected_And_Change_Nothing()
    {
        var session = await LoginAsync(AuthApiFactory.SecretaireEmail, AuthApiFactory.SecretairePassword);

        var response = await ChangePasswordAsync(session.AccessToken, "Ceci-Nest-Pas-Le-Bon-9!", NewPassword);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        // Rien n'a changé : l'ancien mot de passe fonctionne toujours, la session n'a pas été coupée.
        var loginWithOldPassword = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = AuthApiFactory.SecretaireEmail,
            password = AuthApiFactory.SecretairePassword
        });
        loginWithOldPassword.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_Weak_New_Password_Should_Be_Rejected()
    {
        var session = await LoginAsync(AuthApiFactory.SecretaireEmail, AuthApiFactory.SecretairePassword);

        var response = await ChangePasswordAsync(
            session.AccessToken, AuthApiFactory.SecretairePassword, "short");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task An_Anonymous_Request_Should_Be_Rejected()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/change-password")
        {
            Content = JsonContent.Create(new
            {
                currentPassword = AuthApiFactory.SecretairePassword,
                newPassword = NewPassword
            })
        };

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Critère décisif de cette implémentation : `users` est sous policy RLS (ticket JGK-A03), et un
    /// Super Admin n'a AUCUN SchoolId de session pour la satisfaire. Le Handler doit passer entièrement
    /// par IAuthStore (fonctions SECURITY DEFINER du chemin de login), jamais par une requête EF directe
    /// sur DbSet&lt;User&gt; — sans quoi cette route échouerait silencieusement (0 ligne affectée) pour
    /// le seul rôle qui n'appartient à aucune école.
    /// </summary>
    [Fact]
    public async Task A_Super_Admin_Should_Also_Be_Able_To_Change_Their_Own_Password()
    {
        var session = await LoginAsync(AuthApiFactory.SuperAdminEmail, AuthApiFactory.SuperAdminPassword);

        var response = await ChangePasswordAsync(
            session.AccessToken, AuthApiFactory.SuperAdminPassword, NewPassword);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var loginWithNewPassword = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = AuthApiFactory.SuperAdminEmail,
            password = NewPassword
        });
        loginWithNewPassword.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
