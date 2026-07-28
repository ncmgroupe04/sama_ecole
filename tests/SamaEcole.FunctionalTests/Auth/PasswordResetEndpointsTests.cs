using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Xunit;

namespace SamaEcole.FunctionalTests.Auth;

/// <summary>
/// POST /auth/forgot-password puis /auth/reset-password — parcours self-service, contre un vrai
/// PostgreSQL. Le jeton en clair ne sort QUE par l'e-mail (la base n'en garde que le SHA-256) : les
/// tests l'extraient du message capturé, ce qui prouve toute la chaîne, du lien reçu à la connexion
/// avec le nouveau mot de passe.
/// </summary>
public class PasswordResetEndpointsTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    public Task InitializeAsync() => factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private record Tokens(string AccessToken, string RefreshToken, int ExpiresIn);
    private record ResetResult(int RevokedSessions);

    private const string NewPassword = "Nouveau-Mot-De-Passe-9!";

    private Task<HttpResponseMessage> ForgotAsync(string email) =>
        _client.PostAsJsonAsync("/api/v1/auth/forgot-password", new { email });

    private Task<HttpResponseMessage> ResetAsync(string token, string newPassword) =>
        _client.PostAsJsonAsync("/api/v1/auth/reset-password", new { token, newPassword });

    private async Task<string> RequestTokenAsync(string email)
    {
        (await ForgotAsync(email)).StatusCode.Should().Be(HttpStatusCode.Accepted);

        var mail = factory.Emails.LastTo(email);
        mail.Should().NotBeNull("un compte actif doit recevoir son lien de réinitialisation");

        return FakeEmailSender.ExtractResetToken(mail!);
    }

    /// <summary>Le parcours nominal complet : lien reçu, mot de passe changé, connexion effective.</summary>
    [Fact]
    public async Task A_Received_Link_Should_Let_The_User_Set_A_New_Password_And_Sign_In()
    {
        var token = await RequestTokenAsync(AuthApiFactory.SecretaireEmail);

        var response = await ResetAsync(token, NewPassword);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // L'ancien mot de passe ne doit plus rien ouvrir…
        var withOld = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = AuthApiFactory.SecretaireEmail,
            password = AuthApiFactory.SecretairePassword
        });
        withOld.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // …et le nouveau doit fonctionner réellement.
        var withNew = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = AuthApiFactory.SecretaireEmail,
            password = NewPassword
        });
        withNew.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Arbitrage explicite : la réinitialisation coupe TOUTES les sessions ouvertes, comme la voie
    /// administrative. Sans cela, celui qui a détourné le compte resterait connecté sous l'ancien mot
    /// de passe — précisément ce que la victime cherche à interrompre en réinitialisant.
    /// </summary>
    [Fact]
    public async Task Resetting_Should_Revoke_Every_Open_Session()
    {
        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = AuthApiFactory.SecretaireEmail,
            password = AuthApiFactory.SecretairePassword
        });
        login.StatusCode.Should().Be(HttpStatusCode.OK);

        var token = await RequestTokenAsync(AuthApiFactory.SecretaireEmail);
        var response = await ResetAsync(token, NewPassword);

        var result = (await response.Content.ReadFromJsonAsync<ResetResult>())!;
        result.RevokedSessions.Should().BeGreaterThan(0, "la session ouverte juste avant doit être coupée");
    }

    /// <summary>USAGE UNIQUE : le lien reste dans la boîte mail, il ne doit pas pouvoir être rejoué.</summary>
    [Fact]
    public async Task A_Token_Should_Not_Be_Usable_Twice()
    {
        var token = await RequestTokenAsync(AuthApiFactory.SecretaireEmail);

        (await ResetAsync(token, NewPassword)).StatusCode.Should().Be(HttpStatusCode.OK);

        var replay = await ResetAsync(token, "Encore-Un-Autre-9!");
        replay.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>Une nouvelle demande périme la précédente : un seul lien vivant à la fois par compte.</summary>
    [Fact]
    public async Task Requesting_A_New_Link_Should_Invalidate_The_Previous_One()
    {
        var first = await RequestTokenAsync(AuthApiFactory.SecretaireEmail);
        var second = await RequestTokenAsync(AuthApiFactory.SecretaireEmail);

        second.Should().NotBe(first);

        (await ResetAsync(first, NewPassword)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ResetAsync(second, NewPassword)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// ANTI-ÉNUMÉRATION DE COMPTES — le critère de sécurité central de cette route. Une adresse
    /// inconnue et une adresse existante doivent produire EXACTEMENT la même réponse : tout écart
    /// (code, corps, message) transformerait l'endpoint en oracle permettant de dresser la liste des
    /// comptes de la plateforme. Seul l'e-mail réellement émis diffère, et l'appelant ne le voit pas.
    /// </summary>
    [Fact]
    public async Task An_Unknown_Address_Should_Be_Indistinguishable_From_A_Known_One()
    {
        var known = await ForgotAsync(AuthApiFactory.SecretaireEmail);
        var unknown = await ForgotAsync("personne.inconnue@example.com");

        unknown.StatusCode.Should().Be(known.StatusCode);
        (await unknown.Content.ReadAsStringAsync())
            .Should().Be(await known.Content.ReadAsStringAsync(),
                "le corps de réponse ne doit rien laisser filtrer sur l'existence du compte");

        factory.Emails.LastTo("personne.inconnue@example.com")
            .Should().BeNull("aucun e-mail ne part vers une adresse sans compte");
    }

    /// <summary>Un jeton inventé reçoit le même refus qu'un jeton expiré : rien ne renseigne l'attaquant.</summary>
    [Fact]
    public async Task An_Unknown_Token_Should_Be_Rejected()
    {
        var response = await ResetAsync("un-jeton-totalement-invente", NewPassword);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>
    /// La politique de mot de passe s'applique au self-service comme à la voie administrative : le
    /// jeton autorise à CHANGER le mot de passe, pas à en choisir un faible.
    /// </summary>
    [Fact]
    public async Task A_Weak_Password_Should_Be_Refused_Even_With_A_Valid_Token()
    {
        var token = await RequestTokenAsync(AuthApiFactory.SecretaireEmail);

        var response = await ResetAsync(token, "123");
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        // Le jeton n'a PAS été consommé par cet échec : l'utilisateur doit pouvoir réessayer avec un
        // mot de passe conforme, sans repasser par une nouvelle demande.
        (await ResetAsync(token, NewPassword)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Un compte suspendu ou bloqué ne doit pas pouvoir se rouvrir par ce chemin (ticket JGK-A05) —
    /// et la réponse reste indiscernable de celle d'une adresse inconnue.
    /// </summary>
    [Fact]
    public async Task A_Suspended_Account_Should_Receive_No_Link()
    {
        // Suspension par la VRAIE route (PATCH /users/{id}/status, JGK-A05) plutôt qu'en écrivant
        // directement en base : on vérifie le comportement tel qu'il se produira réellement.
        var directeur = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = AuthApiFactory.DirecteurEmail,
            password = AuthApiFactory.DirecteurPassword
        });
        var tokens = (await directeur.Content.ReadFromJsonAsync<Tokens>())!;

        var suspend = new HttpRequestMessage(
            HttpMethod.Patch, $"/api/v1/users/{AuthApiFactory.SecretaireId}/status")
        {
            Content = JsonContent.Create(new { status = "Suspended", reason = "Test de réinitialisation" })
        };
        suspend.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        (await _client.SendAsync(suspend)).StatusCode.Should().Be(HttpStatusCode.OK);

        factory.Emails.Clear();

        var response = await ForgotAsync(AuthApiFactory.SecretaireEmail);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        factory.Emails.LastTo(AuthApiFactory.SecretaireEmail)
            .Should().BeNull("un compte suspendu ne reçoit aucun lien de réinitialisation");
    }
}
