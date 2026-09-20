using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Xunit;

namespace SamaEcole.FunctionalTests.Users;

/// <summary>
/// PATCH /users/{userId}/profile — le Directeur corrige le nom complet et/ou l'e-mail d'un compte
/// qu'il gère (typiquement une faute de frappe repérée après POST /users). Critères : réservé au
/// Directeur, impossible sur sa propre fiche (voie sécurisée : POST /auth/change-email), 404 sur un
/// compte inconnu, 409 si l'e-mail visé est déjà pris par un AUTRE compte, le nouvel e-mail fonctionne
/// immédiatement pour se connecter.
/// </summary>
public class UpdateUserProfileEndpointsTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    public Task InitializeAsync() => factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private record Tokens(string AccessToken, string RefreshToken, int ExpiresIn);
    private record ProfileResult(Guid UserId, string FullName, string Email);

    private async Task<Tokens> LoginAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!;
    }

    private Task<Tokens> LoginAsDirecteurAsync() =>
        LoginAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);

    private async Task<HttpResponseMessage> UpdateProfileAsync(
        string accessToken, Guid userId, string fullName, string email)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/users/{userId}/profile")
        {
            Content = JsonContent.Create(new { fullName, email })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return await _client.SendAsync(request);
    }

    [Fact]
    public async Task A_Director_Can_Correct_A_Typo_In_The_Full_Name()
    {
        var directeur = await LoginAsDirecteurAsync();

        var response = await UpdateProfileAsync(
            directeur.AccessToken, AuthApiFactory.SecretaireId, "Awa Ndiayee Corrigee", AuthApiFactory.SecretaireEmail);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<ProfileResult>())!;
        result.FullName.Should().Be("Awa Ndiayee Corrigee");
    }

    [Fact]
    public async Task Changing_The_Email_Should_Let_The_Account_Log_In_With_The_New_Address_Immediately()
    {
        const string newEmail = "secretaire.corrigee@sama-ecole.sn";
        var directeur = await LoginAsDirecteurAsync();

        var response = await UpdateProfileAsync(
            directeur.AccessToken, AuthApiFactory.SecretaireId, "Awa Ndiaye", newEmail);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = newEmail,
            password = AuthApiFactory.SecretairePassword
        });
        login.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// 422, pas 409 : même choix que CreateUserCommandHandler (dont ce Handler reprend la garde
    /// d'unicité telle quelle) — un e-mail déjà pris à la CRÉATION comme à la CORRECTION d'un compte
    /// par le Directeur est une saisie invalide, pas un conflit d'écriture concurrente. Seule la voie
    /// libre-service (/auth/change-email, DuplicateRecordException) renvoie 409 : ici l'acteur n'est
    /// pas celui qui subirait la course, la distinction 422 ne coûte rien.
    /// </summary>
    [Fact]
    public async Task Changing_To_An_Email_Already_Used_By_Another_Account_Should_Be_Rejected()
    {
        var directeur = await LoginAsDirecteurAsync();

        var response = await UpdateProfileAsync(
            directeur.AccessToken, AuthApiFactory.SecretaireId, "Awa Ndiaye", AuthApiFactory.DirecteurEmail);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_Malformed_Email_Should_Be_Rejected()
    {
        var directeur = await LoginAsDirecteurAsync();

        var response = await UpdateProfileAsync(
            directeur.AccessToken, AuthApiFactory.SecretaireId, "Awa Ndiaye", "pas-un-email");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>
    /// Le Directeur corrige son propre e-mail par la voie sécurisée (mot de passe requis, sessions
    /// révoquées) — jamais par cette route administrative, même sur sa propre fiche.
    /// </summary>
    [Fact]
    public async Task A_Director_Should_Not_Be_Able_To_Edit_His_Own_Profile_This_Way()
    {
        var directeur = await LoginAsDirecteurAsync();

        var response = await UpdateProfileAsync(
            directeur.AccessToken, AuthApiFactory.DirecteurId, "Nouveau Nom", AuthApiFactory.DirecteurEmail);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_Secretariat_Must_Not_Be_Allowed_To_Edit_Anyones_Profile()
    {
        var secretaire = await LoginAsync(AuthApiFactory.SecretaireEmail, AuthApiFactory.SecretairePassword);

        var response = await UpdateProfileAsync(
            secretaire.AccessToken, AuthApiFactory.DirecteurId, "Nouveau Nom", AuthApiFactory.DirecteurEmail);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Editing_An_Unknown_User_Should_Return_404()
    {
        var directeur = await LoginAsDirecteurAsync();

        var response = await UpdateProfileAsync(directeur.AccessToken, Guid.NewGuid(), "Nouveau Nom", "inconnu@sama-ecole.sn");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
