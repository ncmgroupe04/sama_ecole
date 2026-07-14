using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace SamaEcole.FunctionalTests.SchoolYears;

/// <summary>
/// Ticket JGK-C01 — /school-years contre un vrai PostgreSQL, à travers la vraie pile HTTP.
///
/// Trois règles du ticket sont vérifiées ici de bout en bout : une seule année active à la fois, les
/// années passées en lecture seule, et la ressaisie du mot de passe pour changer d'année active
/// (docs/Volume_7_Security.md §16).
/// </summary>
public class SchoolYearsEndpointsTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public SchoolYearsEndpointsTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    /// <summary>
    /// Une école n'a droit qu'à UNE année active : sans remise à zéro, l'année créée par un test
    /// resterait active et le suivant, croyant créer sa première année, en obtiendrait une inactive.
    /// </summary>
    public Task InitializeAsync() => _factory.ResetTestUsersAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private record Tokens(string AccessToken, int ExpiresIn);

    private record SchoolYearDto(
        Guid Id, string Label, DateOnly StartDate, DateOnly EndDate, bool IsActive, bool IsClosed);

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    // Trois périodes qui ne se chevauchent pas, définies RELATIVEMENT à aujourd'hui : des dates en dur
    // feraient basculer « année en cours » en « année passée » au fil du temps, et ces tests
    // finiraient par échouer tout seuls sans qu'une ligne de code ait changé.
    private static object PastYear(string label) => YearBody(label, -500, -160);      // terminée
    private static object CurrentYear(string label) => YearBody(label, -150, +90);    // en cours
    private static object FutureYear(string label) => YearBody(label, +100, +340);    // à venir

    private static object YearBody(string label, int startOffsetDays, int endOffsetDays) => new
    {
        label,
        startDate = Today.AddDays(startOffsetDays).ToString("yyyy-MM-dd"),
        endDate = Today.AddDays(endOffsetDays).ToString("yyyy-MM-dd")
    };

    private async Task<string> AccessTokenAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!.AccessToken;
    }

    private Task<string> DirecteurTokenAsync() =>
        AccessTokenAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);

    private Task<string> SecretaireTokenAsync() =>
        AccessTokenAsync(AuthApiFactory.SecretaireEmail, AuthApiFactory.SecretairePassword);

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await _client.SendAsync(request);
    }

    private async Task<SchoolYearDto> CreateYearAsync(string token, object body)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/v1/school-years", token, body);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<SchoolYearDto>())!;
    }

    private async Task<List<SchoolYearDto>> ListYearsAsync(string token)
    {
        var response = await SendAsync(HttpMethod.Get, "/api/v1/school-years", token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<List<SchoolYearDto>>())!;
    }

    [Fact]
    public async Task Listing_School_Years_Without_A_Token_Should_Return_401()
    {
        // L'école vient du JWT : sans jeton, il n'y a pas d'école, donc rien à lire.
        var response = await _client.GetAsync("/api/v1/school-years");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_First_Year_Of_A_School_Should_Be_Active_Straight_Away()
    {
        // Une école dont aucune année n'est active ne peut rien inscrire : exiger un second appel
        // pour activer la toute première année n'ajouterait aucune sécurité, juste une impasse.
        var token = await DirecteurTokenAsync();

        var created = await CreateYearAsync(token, CurrentYear("2026-2027"));

        created.IsActive.Should().BeTrue();
        created.IsClosed.Should().BeFalse();
    }

    [Fact]
    public async Task Creating_A_Second_Year_Must_Not_Switch_The_School_Over_To_It()
    {
        // LE piège que ce ticket doit éviter : une école qui prépare l'année suivante au mois d'août
        // travaille ENCORE sur l'année en cours. Si la création basculait l'établissement, les
        // inscriptions du jour s'imputeraient sur le mauvais exercice — silencieusement.
        var token = await DirecteurTokenAsync();

        var current = await CreateYearAsync(token, CurrentYear("2026-2027"));
        var next = await CreateYearAsync(token, FutureYear("2027-2028"));

        next.IsActive.Should().BeFalse("créer une année ne doit jamais faire basculer l'établissement dessus");

        var years = await ListYearsAsync(token);

        years.Should().HaveCount(2);
        years.Where(y => y.IsActive).Should().ContainSingle()
            .Which.Id.Should().Be(current.Id);
    }

    [Fact]
    public async Task An_Already_Finished_Year_Should_Never_Become_Active()
    {
        // Reprise d'historique : l'école saisit après coup une année déjà terminée. Elle ne doit pas
        // devenir l'exercice courant, même si l'école n'en a aucun — on ouvrirait les inscriptions sur
        // une année close.
        var token = await DirecteurTokenAsync();

        var past = await CreateYearAsync(token, PastYear("2024-2025"));

        past.IsActive.Should().BeFalse();
        past.IsClosed.Should().BeTrue();
    }

    [Fact]
    public async Task Activating_Another_Year_Should_Deactivate_The_Previous_One()
    {
        var token = await DirecteurTokenAsync();

        var current = await CreateYearAsync(token, CurrentYear("2026-2027"));
        var next = await CreateYearAsync(token, FutureYear("2027-2028"));

        var response = await SendAsync(
            HttpMethod.Post, $"/api/v1/school-years/{next.Id}/activate", token,
            new { password = AuthApiFactory.DirecteurPassword });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var activated = (await response.Content.ReadFromJsonAsync<SchoolYearDto>())!;
        activated.IsActive.Should().BeTrue();

        // « Une seule année active à la fois » : l'ancienne doit être retombée, pas seulement la
        // nouvelle être montée.
        var years = await ListYearsAsync(token);

        years.Where(y => y.IsActive).Should().ContainSingle()
            .Which.Id.Should().Be(next.Id);

        years.Single(y => y.Id == current.Id).IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Activating_Without_The_Right_Password_Should_Be_Refused()
    {
        // Double confirmation (docs/Volume_7_Security.md §16) : un poste laissé déverrouillé ne suffit
        // pas à faire basculer une école entière.
        var token = await DirecteurTokenAsync();

        var current = await CreateYearAsync(token, CurrentYear("2026-2027"));
        var next = await CreateYearAsync(token, FutureYear("2027-2028"));

        var response = await SendAsync(
            HttpMethod.Post, $"/api/v1/school-years/{next.Id}/activate", token,
            new { password = "ce-n-est-pas-le-bon" });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Password");

        // Et surtout : rien n'a bougé.
        var years = await ListYearsAsync(token);

        years.Where(y => y.IsActive).Should().ContainSingle()
            .Which.Id.Should().Be(current.Id);
    }

    [Fact]
    public async Task Activating_A_Finished_Year_Should_Be_Refused()
    {
        // « Années passées en lecture seule » (ticket JGK-C01).
        var token = await DirecteurTokenAsync();

        await CreateYearAsync(token, CurrentYear("2026-2027"));
        var past = await CreateYearAsync(token, PastYear("2024-2025"));

        var response = await SendAsync(
            HttpMethod.Post, $"/api/v1/school-years/{past.Id}/activate", token,
            new { password = AuthApiFactory.DirecteurPassword });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Activating_An_Unknown_Year_Should_Return_404()
    {
        var token = await DirecteurTokenAsync();

        var response = await SendAsync(
            HttpMethod.Post, $"/api/v1/school-years/{Guid.NewGuid()}/activate", token,
            new { password = AuthApiFactory.DirecteurPassword });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Duplicate_Label_In_The_Same_School_Should_Return_409()
    {
        var token = await DirecteurTokenAsync();

        await CreateYearAsync(token, CurrentYear("2026-2027"));

        // Même libellé, dates DIFFÉRENTES : sans quoi c'est le contrôle de chevauchement qui
        // répondrait (422), et l'index unique ne serait jamais mis à l'épreuve.
        var duplicate = await SendAsync(
            HttpMethod.Post, "/api/v1/school-years", token, FutureYear("2026-2027"));

        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Overlapping_Dates_Should_Return_422()
    {
        // Deux années qui se chevauchent rendent la question « en quelle année sommes-nous ? » sans
        // réponse : une inscription du jour relèverait des deux.
        var token = await DirecteurTokenAsync();

        await CreateYearAsync(token, CurrentYear("2026-2027"));

        var overlapping = await SendAsync(
            HttpMethod.Post, "/api/v1/school-years", token, YearBody("Autre libellé", -60, +200));

        overlapping.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var body = await overlapping.Content.ReadAsStringAsync();
        body.Should().Contain("StartDate");
    }

    [Fact]
    public async Task A_Secretary_Must_Not_Create_A_School_Year()
    {
        // L'année scolaire est un paramètre d'établissement : Directeur uniquement
        // (docs/Volume_7_Security.md §15).
        var token = await SecretaireTokenAsync();

        var response = await SendAsync(
            HttpMethod.Post, "/api/v1/school-years", token, CurrentYear("2026-2027"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_Secretary_Must_Not_Switch_The_Active_Year()
    {
        var directeur = await DirecteurTokenAsync();
        await CreateYearAsync(directeur, CurrentYear("2026-2027"));
        var next = await CreateYearAsync(directeur, FutureYear("2027-2028"));

        var secretaire = await SecretaireTokenAsync();

        // Même en connaissant SON propre mot de passe, la secrétaire n'a pas ce droit : le rôle est
        // vérifié avant toute confirmation.
        var response = await SendAsync(
            HttpMethod.Post, $"/api/v1/school-years/{next.Id}/activate", secretaire,
            new { password = AuthApiFactory.SecretairePassword });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_Secretary_Should_Still_Read_The_School_Years()
    {
        // L'année active est le contexte de travail de TOUS les écrans : la lecture reste ouverte.
        var directeur = await DirecteurTokenAsync();
        await CreateYearAsync(directeur, CurrentYear("2026-2027"));

        var secretaire = await SecretaireTokenAsync();
        var years = await ListYearsAsync(secretaire);

        years.Should().ContainSingle().Which.IsActive.Should().BeTrue();
    }
}
