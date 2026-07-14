using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Xunit;

namespace SamaEcole.FunctionalTests.Auth;

/// <summary>
/// Ticket JGK-A04 — parcours HTTP réel contre l'API et un vrai PostgreSQL.
/// Couvre les trois critères d'acceptation du ticket :
///   * token expiré → 401
///   * refresh token révoqué après logout
///   * mot de passe stocké hashé (le compte est semé avec un hash Identity, jamais en clair)
/// </summary>
public class AuthEndpointsTests : IClassFixture<AuthApiFactory>
{
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public AuthEndpointsTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private record Tokens(string AccessToken, string RefreshToken, int ExpiresIn);

    private async Task<Tokens> LoginAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = AuthApiFactory.DirecteurEmail,
            password = AuthApiFactory.DirecteurPassword
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!;
    }

    private HttpRequestMessage Authenticated(HttpMethod method, string url, string accessToken)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    [Fact]
    public async Task Login_With_Valid_Credentials_Should_Return_Both_Tokens()
    {
        var tokens = await LoginAsync();

        tokens.AccessToken.Should().NotBeNullOrWhiteSpace();
        tokens.RefreshToken.Should().NotBeNullOrWhiteSpace();
        tokens.ExpiresIn.Should().Be(900); // 15 min, conforme à openapi.yaml
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
        var tokens = await LoginAsync();

        var response = await _client.SendAsync(
            Authenticated(HttpMethod.Post, "/api/v1/auth/logout", tokens.AccessToken));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Refresh_Should_Rotate_The_Token_And_Invalidate_The_Old_One()
    {
        var tokens = await LoginAsync();

        var refreshed = await _client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = tokens.RefreshToken });
        refreshed.StatusCode.Should().Be(HttpStatusCode.OK);

        var newTokens = (await refreshed.Content.ReadFromJsonAsync<Tokens>())!;
        newTokens.RefreshToken.Should().NotBe(tokens.RefreshToken, "le refresh token doit tourner à chaque usage");

        // Le token consommé ne doit plus jamais servir : un refresh token est à usage unique.
        var replayed = await _client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = tokens.RefreshToken });
        replayed.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_Token_Should_Be_Rejected_After_Logout()
    {
        // Critère du ticket : « refresh token révoqué après logout ».
        var tokens = await LoginAsync();

        var logout = await _client.SendAsync(
            Authenticated(HttpMethod.Post, "/api/v1/auth/logout", tokens.AccessToken));
        logout.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var afterLogout = await _client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = tokens.RefreshToken });

        afterLogout.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Unknown_Refresh_Token_Should_Return_401()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = "token-inexistant" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
