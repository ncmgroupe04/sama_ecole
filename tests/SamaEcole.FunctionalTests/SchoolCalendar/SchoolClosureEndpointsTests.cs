using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace SamaEcole.FunctionalTests.SchoolCalendar;

/// <summary>
/// Calendrier de fermeture (Franco-Arabe Bloc 5a) — /school-calendar/closures à travers la vraie pile
/// HTTP : lecture ouverte aux rôles du cahier, écriture réservée au Directeur, période tenue dans
/// l'année scolaire, suppression logique.
/// </summary>
public class SchoolClosureEndpointsTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private const string ClosuresUrl = "/api/v1/school-calendar/closures";

    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public SchoolClosureEndpointsTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public Task InitializeAsync() => _factory.ResetTestUsersAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private record Tokens(string AccessToken, int ExpiresIn);

    private record SchoolYearDto(Guid Id, string Label, DateOnly StartDate, DateOnly EndDate, bool IsActive, bool IsClosed);

    private record ClosureDto(
        Guid Id, Guid SchoolYearId, DateOnly StartDate, DateOnly EndDate, string Kind, string Label, string? LabelAr);

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private async Task<string> TokenAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!.AccessToken;
    }

    private Task<string> DirecteurAsync() => TokenAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await _client.SendAsync(request);
    }

    /// <summary>Année en cours (définie relativement à aujourd'hui, pour ne pas vieillir) et ACTIVE.</summary>
    private async Task<SchoolYearDto> CreateCurrentYearAsync(string token)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/v1/school-years", token, new
        {
            label = $"{Today.Year - 1}-{Today.Year}-T{Guid.NewGuid():N}"[..20],
            startDate = Today.AddDays(-150).ToString("yyyy-MM-dd"),
            endDate = Today.AddDays(90).ToString("yyyy-MM-dd")
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<SchoolYearDto>())!;
    }

    private static object ClosureBody(DateOnly start, DateOnly end, string label = "Tabaski", string? labelAr = "عيد الأضحى") =>
        new { startDate = start.ToString("yyyy-MM-dd"), endDate = end.ToString("yyyy-MM-dd"), kind = "Holiday", label, labelAr };

    [Fact]
    public async Task A_Directeur_Creates_Lists_Updates_And_Deletes_A_Closure()
    {
        var directeur = await DirecteurAsync();
        var year = await CreateCurrentYearAsync(directeur);
        var day = Today.AddDays(-10);

        // Création
        var created = await SendAsync(HttpMethod.Post, $"{ClosuresUrl}?schoolYearId={year.Id}", directeur, ClosureBody(day, day));
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var closure = (await created.Content.ReadFromJsonAsync<ClosureDto>())!;
        closure.SchoolYearId.Should().Be(year.Id);
        closure.LabelAr.Should().Be("عيد الأضحى");

        // Lecture (sans id d'année : année ACTIVE)
        var listed = (await (await SendAsync(HttpMethod.Get, ClosuresUrl, directeur)).Content.ReadFromJsonAsync<List<ClosureDto>>())!;
        listed.Should().ContainSingle().Which.Id.Should().Be(closure.Id);

        // Modification : l'intervalle s'étend, le libellé arabe vide redevient NULL
        var updated = await SendAsync(HttpMethod.Put, $"{ClosuresUrl}/{closure.Id}", directeur,
            ClosureBody(day, day.AddDays(2), "Vacances", labelAr: ""));
        updated.StatusCode.Should().Be(HttpStatusCode.OK);
        var after = (await updated.Content.ReadFromJsonAsync<ClosureDto>())!;
        after.EndDate.Should().Be(day.AddDays(2));
        after.LabelAr.Should().BeNull();

        // Suppression (logique) : la fermeture disparaît de la liste
        (await SendAsync(HttpMethod.Delete, $"{ClosuresUrl}/{closure.Id}", directeur)).StatusCode
            .Should().Be(HttpStatusCode.NoContent);
        (await (await SendAsync(HttpMethod.Get, ClosuresUrl, directeur)).Content.ReadFromJsonAsync<List<ClosureDto>>())!
            .Should().BeEmpty();
    }

    [Fact]
    public async Task A_Closure_Outside_The_School_Year_Is_Rejected()
    {
        var directeur = await DirecteurAsync();
        var year = await CreateCurrentYearAsync(directeur);
        var outside = year.EndDate.AddDays(30);

        var response = await SendAsync(HttpMethod.Post, $"{ClosuresUrl}?schoolYearId={year.Id}", directeur,
            ClosureBody(outside, outside));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task An_Inverted_Period_Is_Rejected()
    {
        var directeur = await DirecteurAsync();
        var year = await CreateCurrentYearAsync(directeur);

        var response = await SendAsync(HttpMethod.Post, $"{ClosuresUrl}?schoolYearId={year.Id}", directeur,
            ClosureBody(Today.AddDays(-5), Today.AddDays(-9)));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task An_Unknown_Year_Gives_404()
    {
        var directeur = await DirecteurAsync();

        var response = await SendAsync(HttpMethod.Post, $"{ClosuresUrl}?schoolYearId={Guid.NewGuid()}", directeur,
            ClosureBody(Today, Today));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Updating_Or_Deleting_An_Unknown_Closure_Gives_404()
    {
        var directeur = await DirecteurAsync();

        (await SendAsync(HttpMethod.Put, $"{ClosuresUrl}/{Guid.NewGuid()}", directeur, ClosureBody(Today, Today)))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await SendAsync(HttpMethod.Delete, $"{ClosuresUrl}/{Guid.NewGuid()}", directeur))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Secretariat_And_Enseignant_Can_Read_But_Not_Write()
    {
        var directeur = await DirecteurAsync();
        var year = await CreateCurrentYearAsync(directeur);
        (await SendAsync(HttpMethod.Post, $"{ClosuresUrl}?schoolYearId={year.Id}", directeur,
            ClosureBody(Today.AddDays(-10), Today.AddDays(-10)))).StatusCode.Should().Be(HttpStatusCode.Created);

        foreach (var (email, password) in new[]
        {
            (AuthApiFactory.SecretaireEmail, AuthApiFactory.SecretairePassword),
            (AuthApiFactory.EnseignantEmail, AuthApiFactory.EnseignantPassword)
        })
        {
            var token = await TokenAsync(email, password);

            (await SendAsync(HttpMethod.Get, ClosuresUrl, token)).StatusCode.Should().Be(HttpStatusCode.OK, email);
            (await SendAsync(HttpMethod.Post, $"{ClosuresUrl}?schoolYearId={year.Id}", token,
                ClosureBody(Today.AddDays(-3), Today.AddDays(-3)))).StatusCode.Should().Be(HttpStatusCode.Forbidden, email);
        }
    }

    [Fact]
    public async Task Finance_Cannot_Read_The_Closures()
    {
        var finance = await TokenAsync(AuthApiFactory.FinanceEmail, AuthApiFactory.FinancePassword);

        (await SendAsync(HttpMethod.Get, ClosuresUrl, finance)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Anonymous_Callers_Are_Rejected()
    {
        (await _client.GetAsync(ClosuresUrl)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
