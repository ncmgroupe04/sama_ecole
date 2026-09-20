using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Xunit;

namespace SamaEcole.FunctionalTests.Auth;

/// <summary>
/// POST /auth/change-email — l'utilisateur AUTHENTIFIÉ change lui-même son e-mail de connexion, preuve
/// du mot de passe actuel à l'appui. Même politique que ChangePasswordEndpointsTests (le mot de passe
/// actuel doit être VÉRIFIÉ) et même critère décisif : la route doit fonctionner identiquement pour un
/// Super Admin (aucun SchoolId, `users` sous RLS — ticket JGK-A03) et pour un Directeur, puisque
/// ChangeUserEmailCommandHandler passe entièrement par IAuthStore (fonction SECURITY DEFINER
/// auth_set_email), jamais par IApplicationDbContext.
///
/// Réservée au Directeur et au Super Admin depuis la décision produit du 20/09/2026 (AuthController.
/// ChangeEmail) : les comptes que le Directeur crée (Secrétariat, Finance, Enseignant, Surveillant)
/// n'ont plus accès à cette route — Directeur remplace donc Secrétariat comme acteur des scénarios
/// « ça marche » ci-dessous, et un test dédié couvre le refus du Secrétariat.
/// </summary>
public class ChangeEmailEndpointsTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    public Task InitializeAsync() => factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private record Tokens(string AccessToken, string RefreshToken, int ExpiresIn);
    private record ChangeResult(string Email, int RevokedSessions);

    private async Task<Tokens> LoginAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!;
    }

    private async Task<HttpResponseMessage> ChangeEmailAsync(
        string accessToken, string newEmail, string currentPassword)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/change-email")
        {
            Content = JsonContent.Create(new { newEmail, currentPassword })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return await _client.SendAsync(request);
    }

    [Fact]
    public async Task A_Correct_Current_Password_Should_Let_The_User_Change_Their_Email_And_Revoke_Every_Session()
    {
        const string newEmail = "nouvelle.adresse@sama-ecole.sn";

        // Connectée AVANT le changement : sans révocation, cette session resterait opérationnelle sous
        // l'ancien e-mail (même raisonnement que ChangePasswordEndpointsTests).
        var beforeChange = await LoginAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);

        var response = await ChangeEmailAsync(
            beforeChange.AccessToken, newEmail, AuthApiFactory.DirecteurPassword);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<ChangeResult>())!;
        result.Email.Should().Be(newEmail);
        result.RevokedSessions.Should().BeGreaterThan(0, "la session qui vient de faire la demande doit elle aussi être coupée");

        var refreshed = await _client.PostAsJsonAsync(
            "/api/v1/auth/refresh", new { refreshToken = beforeChange.RefreshToken });
        refreshed.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var loginWithOldEmail = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = AuthApiFactory.DirecteurEmail,
            password = AuthApiFactory.DirecteurPassword
        });
        loginWithOldEmail.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "l'ancien e-mail ne doit plus jamais permettre de se connecter");

        var loginWithNewEmail = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = newEmail,
            password = AuthApiFactory.DirecteurPassword
        });
        loginWithNewEmail.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Le cœur de cette route : un mot de passe incorrect ne doit rien changer, même si l'e-mail visé
    /// est parfaitement valide et libre.
    /// </summary>
    [Fact]
    public async Task An_Incorrect_Current_Password_Should_Be_Rejected_And_Change_Nothing()
    {
        var session = await LoginAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);

        var response = await ChangeEmailAsync(
            session.AccessToken, "nouvelle.adresse@sama-ecole.sn", "Ceci-Nest-Pas-Le-Bon-9!");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var loginWithOldEmail = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = AuthApiFactory.DirecteurEmail,
            password = AuthApiFactory.DirecteurPassword
        });
        loginWithOldEmail.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// L'e-mail identifie un compte sur TOUTE la plateforme (docs/Volume_3_DDS.md §5.2) : viser celui
    /// d'un autre compte, MÊME D'UNE AUTRE ÉCOLE, doit être refusé — 409, pas un écrasement silencieux.
    /// </summary>
    [Fact]
    public async Task Changing_To_An_Email_Already_Used_By_Another_Account_Should_Return_A_Conflict()
    {
        var session = await LoginAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);

        var response = await ChangeEmailAsync(
            session.AccessToken, AuthApiFactory.SecretaireEmail, AuthApiFactory.DirecteurPassword);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Rien n'a changé : l'ancien e-mail fonctionne toujours.
        var loginWithOldEmail = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = AuthApiFactory.DirecteurEmail,
            password = AuthApiFactory.DirecteurPassword
        });
        loginWithOldEmail.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_Malformed_Email_Should_Be_Rejected()
    {
        var session = await LoginAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);

        var response = await ChangeEmailAsync(session.AccessToken, "pas-un-email", AuthApiFactory.DirecteurPassword);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>
    /// Décision produit du 20/09/2026 : seuls Directeur et Super Admin peuvent changer leur propre
    /// e-mail par cette route. Un compte que le Directeur crée (Secrétariat, Finance, Enseignant,
    /// Surveillant) n'a que « Changer mon mot de passe » — jamais l'e-mail, pour ne pas faire remonter
    /// au Directeur des demandes qu'une fiche corrigée directement (PATCH /users/{id}/profile) couvre.
    /// </summary>
    [Fact]
    public async Task A_Secretariat_Account_Should_Not_Be_Allowed_To_Change_Their_Own_Email()
    {
        var session = await LoginAsync(AuthApiFactory.SecretaireEmail, AuthApiFactory.SecretairePassword);

        var response = await ChangeEmailAsync(
            session.AccessToken, "nouvelle.adresse@sama-ecole.sn", AuthApiFactory.SecretairePassword);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Rien n'a changé : l'ancien e-mail fonctionne toujours.
        var loginWithOldEmail = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = AuthApiFactory.SecretaireEmail,
            password = AuthApiFactory.SecretairePassword
        });
        loginWithOldEmail.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_Anonymous_Request_Should_Be_Rejected()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/change-email")
        {
            Content = JsonContent.Create(new
            {
                newEmail = "nouvelle.adresse@sama-ecole.sn",
                currentPassword = AuthApiFactory.SecretairePassword
            })
        };

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Critère décisif de cette implémentation (voir le brief JGK — parité avec ChangePasswordCommand) :
    /// `users` est sous policy RLS, et un Super Admin n'a AUCUN SchoolId de session pour la satisfaire.
    /// Le Handler doit passer entièrement par IAuthStore (fonction SECURITY DEFINER auth_set_email),
    /// jamais par une requête EF directe sur DbSet&lt;User&gt;, sans quoi cette route échouerait
    /// silencieusement (0 ligne affectée) pour le seul rôle qui n'appartient à aucune école.
    /// </summary>
    [Fact]
    public async Task A_Super_Admin_Should_Also_Be_Able_To_Change_Their_Own_Email()
    {
        const string newEmail = "nouveau.superadmin@sama-ecole.sn";

        var session = await LoginAsync(AuthApiFactory.SuperAdminEmail, AuthApiFactory.SuperAdminPassword);

        var response = await ChangeEmailAsync(session.AccessToken, newEmail, AuthApiFactory.SuperAdminPassword);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var loginWithNewEmail = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = newEmail,
            password = AuthApiFactory.SuperAdminPassword
        });
        loginWithNewEmail.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
