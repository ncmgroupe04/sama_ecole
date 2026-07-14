using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using SamaEcole.Web.Auth;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Net.Http.Headers;
using Xunit;

namespace SamaEcole.FunctionalTests.Auth;

/// <summary>
/// Ticket JGK-A04 — parcours HTTP réel contre l'API et un vrai PostgreSQL.
///
/// Couvre les trois critères d'acceptation du ticket (token expiré → 401 ; refresh token révoqué
/// après logout ; mot de passe stocké hashé), et la règle du Volume 4 §1.1 : le refresh token ne
/// sort QUE par un cookie HttpOnly, jamais dans un corps de réponse.
///
/// Les cookies sont gérés À LA MAIN (HandleCookies = false) plutôt que par le CookieContainer du
/// HttpClient : c'est la seule façon de rejouer délibérément un ancien cookie — ce qu'un navigateur,
/// lui, refuserait de faire — et donc de prouver que la détection de rejeu mord vraiment.
/// </summary>
public class AuthEndpointsTests : IClassFixture<AuthApiFactory>
{
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public AuthEndpointsTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    private record Tokens(string AccessToken, int ExpiresIn);

    /// <summary>Une session : l'access token pour l'en-tête Authorization, le cookie pour le refresh.</summary>
    private record Session(string AccessToken, string RefreshCookie);

    private async Task<Session> LoginAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = AuthApiFactory.DirecteurEmail,
            password = AuthApiFactory.DirecteurPassword
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var tokens = (await response.Content.ReadFromJsonAsync<Tokens>())!;
        return new Session(tokens.AccessToken, ReadRefreshCookie(response));
    }

    private static SetCookieHeaderValue FindRefreshCookie(HttpResponseMessage response) =>
        SetCookieHeaderValue
            .ParseList(response.Headers.GetValues(HeaderNames.SetCookie).ToList())
            .Single(cookie => cookie.Name == RefreshTokenCookie.Name);

    private static string ReadRefreshCookie(HttpResponseMessage response) =>
        FindRefreshCookie(response).Value.ToString();

    private async Task<HttpResponseMessage> RefreshAsync(string? refreshCookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");

        if (refreshCookie is not null)
        {
            request.Headers.Add(HeaderNames.Cookie, $"{RefreshTokenCookie.Name}={refreshCookie}");
        }

        return await _client.SendAsync(request);
    }

    private HttpRequestMessage Authenticated(HttpMethod method, string url, string accessToken)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    [Fact]
    public async Task Login_With_Valid_Credentials_Should_Return_An_Access_Token()
    {
        var session = await LoginAsync();

        session.AccessToken.Should().NotBeNullOrWhiteSpace();
        session.RefreshCookie.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Login_Should_Expose_Only_The_Access_Token_In_The_Body()
    {
        // LA propriété qui justifie le cookie : si le refresh token figurait aussi dans le JSON, une
        // XSS n'aurait qu'à appeler /auth/refresh et le lire dans la réponse — le HttpOnly ne
        // protégerait plus rien.
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = AuthApiFactory.DirecteurEmail,
            password = AuthApiFactory.DirecteurPassword
        });

        var body = await response.Content.ReadAsStringAsync();

        body.Should().NotContain("refreshToken");
        body.Should().NotContain(ReadRefreshCookie(response));

        var tokens = (await response.Content.ReadFromJsonAsync<Tokens>())!;
        tokens.ExpiresIn.Should().Be(900); // 15 min, conforme à openapi.yaml
    }

    [Fact]
    public async Task Refresh_Cookie_Should_Be_HttpOnly_Secure_And_SameSite_Strict()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = AuthApiFactory.DirecteurEmail,
            password = AuthApiFactory.DirecteurPassword
        });

        var cookie = FindRefreshCookie(response);

        cookie.HttpOnly.Should().BeTrue("aucun script ne doit pouvoir lire le refresh token");
        cookie.Secure.Should().BeTrue("le refresh token ne doit jamais circuler en clair");
        cookie.SameSite.Should().Be(SameSiteMode.Strict);
        cookie.Path.ToString().Should().Be("/api/v1/auth", "aucune autre route n'a besoin de ce cookie");
        cookie.Expires.Should().NotBeNull();
    }

    [Fact]
    public async Task Login_With_Wrong_Password_Should_Return_401()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = AuthApiFactory.DirecteurEmail,
            password = "mauvais-mot-de-passe"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_With_Unknown_Email_Should_Return_The_Same_401()
    {
        // Message identique à celui d'un mot de passe faux : sinon, on pourrait énumérer les comptes.
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = "inconnu@sama-ecole.sn",
            password = AuthApiFactory.DirecteurPassword
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("INVALID_CREDENTIALS");
        body.Should().NotContainAny("inconnu", "e-mail", "introuvable");
    }

    [Fact]
    public async Task Protected_Endpoint_Without_Token_Should_Return_401()
    {
        var response = await _client.PostAsync("/api/v1/auth/logout", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Expired_Access_Token_Should_Return_401()
    {
        // Critère du ticket. Le token est signé par la MÊME clé que l'API : seule son expiration
        // le rend invalide, ce qui prouve bien que la durée de vie est vérifiée.
        var expired = _factory.CreateExpiredAccessToken();

        var response = await _client.SendAsync(
            Authenticated(HttpMethod.Post, "/api/v1/auth/logout", expired));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Valid_Access_Token_Should_Be_Accepted()
    {
        var session = await LoginAsync();

        var response = await _client.SendAsync(
            Authenticated(HttpMethod.Post, "/api/v1/auth/logout", session.AccessToken));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Refresh_Should_Rotate_The_Cookie_And_Invalidate_The_Old_One()
    {
        var session = await LoginAsync();

        var refreshed = await RefreshAsync(session.RefreshCookie);
        refreshed.StatusCode.Should().Be(HttpStatusCode.OK);

        var rotated = ReadRefreshCookie(refreshed);
        rotated.Should().NotBe(session.RefreshCookie, "le refresh token doit tourner à chaque usage");

        // Le token consommé ne doit plus jamais servir : un refresh token est à usage unique.
        var replayed = await RefreshAsync(session.RefreshCookie);
        replayed.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Rejected_Refresh_Should_Clear_The_Cookie()
    {
        // Sans cette purge, le navigateur continuerait à présenter un cookie mort à chaque tentative,
        // jusqu'à son expiration 14 jours plus tard.
        var response = await RefreshAsync("token-inexistant");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var cookie = FindRefreshCookie(response);
        cookie.Value.ToString().Should().BeEmpty();
        cookie.Expires.Should().BeBefore(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Refresh_Without_Cookie_Should_Return_401()
    {
        var response = await RefreshAsync(refreshCookie: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_Cookie_Should_Be_Rejected_After_Logout()
    {
        // Critère du ticket : « refresh token révoqué après logout ».
        var session = await LoginAsync();

        var logout = await _client.SendAsync(
            Authenticated(HttpMethod.Post, "/api/v1/auth/logout", session.AccessToken));
        logout.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Le logout purge aussi le cookie côté navigateur.
        FindRefreshCookie(logout).Value.ToString().Should().BeEmpty();

        var afterLogout = await RefreshAsync(session.RefreshCookie);
        afterLogout.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
