using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace SamaEcole.FunctionalTests.Schools;

/// <summary>
/// Bascule « bac à sable → mode réel » et son retour.
///
/// Contrats prouvés de bout en bout (HTTP → MediatR → PostgreSQL réel) :
///   * mode test par défaut ; la « Zone de danger » (reset-data) fonctionne ;
///   * « Passer en mode réel » est réservé au Directeur, confirmé par « CONFIRMER », non rejouable
///     tel quel (409 ALREADY_LIVE) ;
///   * une fois en mode réel, reset-data est refusé (409 RESET_UNAVAILABLE_LIVE_MODE) ;
///   * « Repasser en mode test » est réservé au Directeur, confirmé par « TEST », disponible À TOUT
///     MOMENT (aucun drapeau d'environnement) — il rejoue « Passer en mode réel », mais NE ROUVRE
///     JAMAIS reset-data (School.HasEverGoneLive, verrou permanent).
/// </summary>
public class SchoolModeEndpointsTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    public async Task InitializeAsync()
    {
        await factory.ResetTestUsersAsync();

        // Chaque test repart en mode test, verrou levé : les tests d'une classe partagent la
        // fabrique et un état laissé par un test précédent fausserait le suivant.
        await factory.SeedAsOwnerAsync(async db =>
        {
            var school = await db.Schools.FirstAsync(s => s.Id == AuthApiFactory.EcoleId);
            school.WentLiveAt = null;
            school.HasEverGoneLive = false;
        });
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private record Tokens(string AccessToken, string RefreshToken, int ExpiresIn);
    private record ModeDto(bool IsLive, DateTimeOffset? WentLiveAt, bool HasEverGoneLive);
    private record GoLiveResult(DateTimeOffset WentLiveAt);
    private record RevertResult(bool WasLive);
    private record ApiError(string Code, string Message);

    private async Task<Tokens> LoginAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!;
    }

    private Task<Tokens> LoginAsDirecteurAsync() =>
        LoginAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string accessToken, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }
        return await _client.SendAsync(request);
    }

    private Task<HttpResponseMessage> GetModeAsync(string token) =>
        SendAsync(HttpMethod.Get, "/api/v1/schools/current/mode", token);

    private Task<HttpResponseMessage> GoLiveAsync(string token, string confirmation) =>
        SendAsync(HttpMethod.Post, "/api/v1/schools/current/go-live", token, new { confirmation });

    private Task<HttpResponseMessage> ResetDataAsync(string token, string confirmation) =>
        SendAsync(HttpMethod.Post, "/api/v1/schools/current/reset-data", token, new { confirmation });

    private Task<HttpResponseMessage> RevertToTestAsync(string token, string confirmation) =>
        SendAsync(HttpMethod.Post, "/api/v1/schools/current/revert-to-test", token, new { confirmation });

    [Fact]
    public async Task A_Fresh_School_Is_In_Test_Mode()
    {
        var directeur = await LoginAsDirecteurAsync();

        var mode = (await (await GetModeAsync(directeur.AccessToken)).Content.ReadFromJsonAsync<ModeDto>())!;

        mode.IsLive.Should().BeFalse();
        mode.WentLiveAt.Should().BeNull();
    }

    [Fact]
    public async Task Any_Role_Can_Read_The_Mode()
    {
        var secretaire = await LoginAsync(AuthApiFactory.SecretaireEmail, AuthApiFactory.SecretairePassword);

        var response = await GetModeAsync(secretaire.AccessToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_Directeur_Can_Switch_To_Live_Mode()
    {
        var directeur = await LoginAsDirecteurAsync();

        var response = await GoLiveAsync(directeur.AccessToken, "CONFIRMER");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = (await response.Content.ReadFromJsonAsync<GoLiveResult>())!;
        result.WentLiveAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2));

        var mode = (await (await GetModeAsync(directeur.AccessToken)).Content.ReadFromJsonAsync<ModeDto>())!;
        mode.IsLive.Should().BeTrue();
        mode.WentLiveAt.Should().NotBeNull();
    }

    [Fact]
    public async Task The_School_Name_Also_Confirms_The_Switch()
    {
        var directeur = await LoginAsDirecteurAsync();

        // Le nom de l'école semée (voir AuthApiFactory) — accepté à la place du mot-clé.
        var response = await GoLiveAsync(directeur.AccessToken, "école de test");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Switching_With_A_Wrong_Word_Is_Rejected()
    {
        var directeur = await LoginAsDirecteurAsync();

        var response = await GoLiveAsync(directeur.AccessToken, "n'importe quoi");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Switching_Twice_Is_Refused_As_Already_Live()
    {
        var directeur = await LoginAsDirecteurAsync();

        (await GoLiveAsync(directeur.AccessToken, "CONFIRMER")).StatusCode.Should().Be(HttpStatusCode.OK);

        var second = await GoLiveAsync(directeur.AccessToken, "CONFIRMER");
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var error = (await second.Content.ReadFromJsonAsync<ApiError>())!;
        error.Code.Should().Be("ALREADY_LIVE");
    }

    [Fact]
    public async Task Only_The_Directeur_Can_Switch_To_Live_Mode()
    {
        var secretaire = await LoginAsync(AuthApiFactory.SecretaireEmail, AuthApiFactory.SecretairePassword);

        var response = await GoLiveAsync(secretaire.AccessToken, "CONFIRMER");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Switching_Without_A_Token_Returns_401()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/v1/schools/current/go-live", new { confirmation = "CONFIRMER" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Reset_Works_In_Test_Mode_But_Is_Refused_Once_Live()
    {
        var directeur = await LoginAsDirecteurAsync();

        // Mode test : la purge passe (0 ligne ou plus, peu importe).
        (await ResetDataAsync(directeur.AccessToken, "PURGER")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await GoLiveAsync(directeur.AccessToken, "CONFIRMER")).StatusCode.Should().Be(HttpStatusCode.OK);

        // Mode réel : la purge est verrouillée, avec un code stable pour l'interface.
        var refused = await ResetDataAsync(directeur.AccessToken, "PURGER");
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var error = (await refused.Content.ReadFromJsonAsync<ApiError>())!;
        error.Code.Should().Be("RESET_UNAVAILABLE_LIVE_MODE");
    }

    [Fact]
    public async Task Reverting_To_Test_Reopens_The_Switch_To_Live_But_Not_The_Purge()
    {
        var directeur = await LoginAsDirecteurAsync();

        (await GoLiveAsync(directeur.AccessToken, "CONFIRMER")).StatusCode.Should().Be(HttpStatusCode.OK);

        var revert = await RevertToTestAsync(directeur.AccessToken, "TEST");
        revert.StatusCode.Should().Be(HttpStatusCode.OK);
        (await revert.Content.ReadFromJsonAsync<RevertResult>())!.WasLive.Should().BeTrue();

        var mode = (await (await GetModeAsync(directeur.AccessToken)).Content.ReadFromJsonAsync<ModeDto>())!;
        mode.IsLive.Should().BeFalse();
        mode.HasEverGoneLive.Should().BeTrue(
            "le verrou reste posé même quand isLive redevient faux — l'écran s'en sert pour masquer " +
            "« Réinitialiser l'école » plutôt que de le proposer pour rien");

        // La bascule « Passer en mode réel » est de nouveau jouable...
        (await GoLiveAsync(directeur.AccessToken, "CONFIRMER")).StatusCode.Should().Be(HttpStatusCode.OK);

        // ... mais la purge, elle, reste verrouillée pour toujours (School.HasEverGoneLive) : ce
        // n'est PAS le même test que « repasser en mode test avant le premier passage réel ».
        var refused = await ResetDataAsync(directeur.AccessToken, "PURGER");
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await refused.Content.ReadFromJsonAsync<ApiError>())!.Code.Should().Be("RESET_UNAVAILABLE_LIVE_MODE");
    }

    [Fact]
    public async Task Reverting_To_Test_Before_Ever_Going_Live_Leaves_The_Purge_Available()
    {
        var directeur = await LoginAsDirecteurAsync();

        // Un retour en mode test sur une école qui n'a JAMAIS été réelle (WasLive=false) n'a aucune
        // raison de verrouiller quoi que ce soit.
        var revert = await RevertToTestAsync(directeur.AccessToken, "TEST");
        revert.StatusCode.Should().Be(HttpStatusCode.OK);
        (await revert.Content.ReadFromJsonAsync<RevertResult>())!.WasLive.Should().BeFalse();

        var mode = (await (await GetModeAsync(directeur.AccessToken)).Content.ReadFromJsonAsync<ModeDto>())!;
        mode.HasEverGoneLive.Should().BeFalse();

        (await ResetDataAsync(directeur.AccessToken, "PURGER")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Reverting_With_A_Wrong_Word_Is_Rejected()
    {
        var directeur = await LoginAsDirecteurAsync();

        (await GoLiveAsync(directeur.AccessToken, "CONFIRMER")).StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await RevertToTestAsync(directeur.AccessToken, "n'importe quoi");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Reverting_To_Test_Is_Reserved_To_The_Directeur()
    {
        var secretaire = await LoginAsync(AuthApiFactory.SecretaireEmail, AuthApiFactory.SecretairePassword);

        var response = await RevertToTestAsync(secretaire.AccessToken, "TEST");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
