using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Xunit;

namespace SamaEcole.FunctionalTests.Internat;

/// <summary>
/// Sessions de pointage par le vrai pipeline HTTP : authentification, garde de module, rôles (la Direction configure ; le
/// Surveillant lit ; l'Oustaz ne voit que les sessions de Halqa) et traduction des erreurs (401, 403, 404, 409, 422). La portée fine
/// d'un Oustaz et les comptes du récapitulatif sont prouvés sur la base par <c>AttendanceSessionsTests</c>.
/// </summary>
public class AttendanceSessionsEndpointsTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    public Task InitializeAsync() => factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private record Tokens(string AccessToken, string RefreshToken, int ExpiresIn);
    private record ApiError(string Code, string Message);
    private record SessionDto(Guid Id, string Name, string Audience, List<int> Days, string? TimeLabel, bool IsActive, int SortOrder, bool HasRecords);
    private record TemplateDto(string Code, string Name, string Audience);
    private record InstructorDto(Guid Id, string FullName);

    private const string Sessions = "/api/v1/boarding/attendance/sessions";
    private const string Templates = "/api/v1/boarding/attendance/session-templates";
    private const string HalqaSessions = "/api/v1/internat/attendance/sessions";

    private async Task<string> LoginAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!.AccessToken;
    }

    private Task<string> DirecteurAsync() => LoginAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);
    private Task<string> EnseignantAsync() => LoginAsync(AuthApiFactory.EnseignantEmail, AuthApiFactory.EnseignantPassword);
    private Task<string> SecretaireAsync() => LoginAsync(AuthApiFactory.SecretaireEmail, AuthApiFactory.SecretairePassword);
    private Task<string> FinanceAsync() => LoginAsync(AuthApiFactory.FinanceEmail, AuthApiFactory.FinancePassword);

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? accessToken, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await _client.SendAsync(request);
    }

    private async Task SetInternatAsync(string directeurToken, bool enabled)
    {
        var response = await SendAsync(HttpMethod.Put, "/api/v1/schools/current/settings", directeurToken, new
        {
            gradingScale = "20", studentMatriculeFormat = "ELEV-{YEAR}-{SEQ:4}", teacherMatriculeFormat = "ENS-{YEAR}-{SEQ:3}",
            autoLogoutMinutes = 10, dateFormat = "dd/MM/yyyy", tuitionMonthsPerYear = 9, allowSecretaryToManageGrading = false,
            isPedagogyEnabled = true, isFinanceEnabled = true, isInternatEnabled = enabled, isCoranModuleEnabled = false
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static string Unique(string name) => $"{name} {Guid.NewGuid():N}"[..(name.Length + 9)];

    private static object Body(string name, string audience = "AllBoarders", int[]? days = null, bool active = true) =>
        new { name, audience, days = days ?? [1, 2, 3, 4, 5, 6, 7], timeLabel = "12 h 30", isActive = active };

    private async Task<SessionDto> CreateOkAsync(string token, string name, string audience = "AllBoarders", bool active = true)
    {
        var response = await SendAsync(HttpMethod.Post, Sessions, token, Body(name, audience, active: active));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<SessionDto>())!;
    }

    // ------------------------------------------------------------------ authentification et garde de module

    [Fact]
    public async Task Every_Route_Requires_Authentication()
    {
        var id = Guid.NewGuid();
        var calls = new (HttpMethod Method, string Url)[]
        {
            (HttpMethod.Get, Sessions), (HttpMethod.Get, Templates), (HttpMethod.Post, Sessions),
            (HttpMethod.Put, $"{Sessions}/{id}"), (HttpMethod.Delete, $"{Sessions}/{id}"), (HttpMethod.Put, $"{Sessions}/order"),
            (HttpMethod.Get, "/api/v1/boarding/attendance/sheet"), (HttpMethod.Post, "/api/v1/boarding/attendance/sheet"),
            (HttpMethod.Get, "/api/v1/boarding/attendance/monthly-summary"), (HttpMethod.Get, "/api/v1/boarding/attendance/monthly-summary/pdf"),
            (HttpMethod.Get, HalqaSessions), (HttpMethod.Get, "/api/v1/internat/attendance/sheet"), (HttpMethod.Post, "/api/v1/internat/attendance"),
            (HttpMethod.Get, "/api/v1/internat/attendance/monthly-summary")
        };

        foreach (var (method, url) in calls)
        {
            (await SendAsync(method, url, null, method == HttpMethod.Get ? null : new { })).StatusCode
                .Should().Be(HttpStatusCode.Unauthorized, $"{method} {url}");
        }
    }

    [Fact]
    public async Task The_Routes_Are_Locked_By_The_Internat_Module_Gate_While_The_Module_Is_Off()
    {
        var directeur = await DirecteurAsync();
        await SetInternatAsync(directeur, enabled: false);

        foreach (var url in new[] { Sessions, Templates, HalqaSessions })
        {
            var response = await SendAsync(HttpMethod.Get, url, directeur);
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden, url);
            (await response.Content.ReadFromJsonAsync<ApiError>())!.Code.Should().Be("MODULE_DISABLED", url);
        }
    }

    // ------------------------------------------------------------------ rôles

    [Fact]
    public async Task Only_The_Director_Configures_Sessions_While_The_Wider_Direction_And_Supervisors_Read_Them()
    {
        var directeur = await DirecteurAsync();
        await SetInternatAsync(directeur, enabled: true);
        var secretaire = await SecretaireAsync();
        var enseignant = await EnseignantAsync();
        var finance = await FinanceAsync();

        foreach (var token in new[] { secretaire, enseignant, finance })
        {
            (await SendAsync(HttpMethod.Post, Sessions, token, Body(Unique("Refusée")))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await SendAsync(HttpMethod.Get, Templates, token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await SendAsync(HttpMethod.Put, $"{Sessions}/order", token, new { orderedIds = Array.Empty<Guid>() })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        (await SendAsync(HttpMethod.Get, Sessions, secretaire)).StatusCode.Should().Be(HttpStatusCode.OK, "la lecture est ouverte à la Direction élargie");
        (await SendAsync(HttpMethod.Get, Sessions, enseignant)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "l'Oustaz passe par la route de la Halqa");
        (await SendAsync(HttpMethod.Get, Sessions, finance)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_Oustaz_Lists_Only_The_Active_Halqa_Sessions()
    {
        var directeur = await DirecteurAsync();
        await SetInternatAsync(directeur, enabled: true);
        var halqa = await CreateOkAsync(directeur, Unique("Halqa active"), "Halqa");
        var inactive = await CreateOkAsync(directeur, Unique("Halqa inactive"), "Halqa", active: false);
        var meal = await CreateOkAsync(directeur, Unique("Repas"), "AllBoarders");

        var response = await SendAsync(HttpMethod.Get, HalqaSessions, await EnseignantAsync());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var ids = (await response.Content.ReadFromJsonAsync<List<SessionDto>>())!.Select(s => s.Id).ToList();
        ids.Should().Contain(halqa.Id).And.NotContain(inactive.Id).And.NotContain(meal.Id);

        (await SendAsync(HttpMethod.Get, HalqaSessions, await FinanceAsync())).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_Teacher_Account_Without_An_Instructor_Record_Cannot_Read_Or_Write_A_Halqa_Roll()
    {
        var directeur = await DirecteurAsync();
        await SetInternatAsync(directeur, enabled: true);
        var halqa = await CreateOkAsync(directeur, Unique("Halqa"), "Halqa");
        var enseignant = await EnseignantAsync();

        var sheet = await SendAsync(HttpMethod.Get, $"/api/v1/internat/attendance/sheet?sessionId={halqa.Id}&date=2026-10-12", enseignant);
        sheet.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await sheet.Content.ReadFromJsonAsync<ApiError>())!.Message.Should().Contain("fiche d'Oustaz");

        var record = await SendAsync(HttpMethod.Post, "/api/v1/internat/attendance", enseignant,
            new { sessionId = halqa.Id, date = "2026-10-12", entries = new[] { new { studentId = Guid.NewGuid(), isPresent = true } } });
        record.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ------------------------------------------------------------------ configuration

    [Fact]
    public async Task A_Session_Goes_Through_Create_List_Update_Reorder_And_Delete()
    {
        var directeur = await DirecteurAsync();
        await SetInternatAsync(directeur, enabled: true);
        var name = Unique("Goûter");

        var created = await CreateOkAsync(directeur, name);
        created.Should().Match<SessionDto>(s => s.Name == name && s.Audience == "AllBoarders" && s.IsActive && !s.HasRecords && s.TimeLabel == "12 h 30");
        created.Days.Should().Equal(1, 2, 3, 4, 5, 6, 7);

        var renamed = Unique("Collation");
        var update = await SendAsync(HttpMethod.Put, $"{Sessions}/{created.Id}", directeur, Body(renamed, "HalfBoarders", [1, 3, 5], active: false));
        update.StatusCode.Should().Be(HttpStatusCode.OK);
        (await update.Content.ReadFromJsonAsync<SessionDto>())!.Should()
            .Match<SessionDto>(s => s.Name == renamed && s.Audience == "HalfBoarders" && !s.IsActive && s.Days.Count == 3);

        var list = (await (await SendAsync(HttpMethod.Get, Sessions, directeur)).Content.ReadFromJsonAsync<List<SessionDto>>())!;
        list.Should().Contain(s => s.Id == created.Id);

        var reverse = list.Select(s => s.Id).Reverse().ToArray();
        (await SendAsync(HttpMethod.Put, $"{Sessions}/order", directeur, new { orderedIds = reverse })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var reordered = (await (await SendAsync(HttpMethod.Get, Sessions, directeur)).Content.ReadFromJsonAsync<List<SessionDto>>())!;
        reordered.Select(s => s.Id).Should().Equal(reverse);

        (await SendAsync(HttpMethod.Delete, $"{Sessions}/{created.Id}", directeur)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await SendAsync(HttpMethod.Delete, $"{Sessions}/{created.Id}", directeur)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await SendAsync(HttpMethod.Put, $"{Sessions}/{created.Id}", directeur, Body(renamed))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Invalid_Or_Duplicate_Sessions_Are_Refused_With_422()
    {
        var directeur = await DirecteurAsync();
        await SetInternatAsync(directeur, enabled: true);
        var name = Unique("Dîner");
        await CreateOkAsync(directeur, name);

        (await SendAsync(HttpMethod.Post, Sessions, directeur, Body(name.ToUpperInvariant()))).StatusCode
            .Should().Be(HttpStatusCode.UnprocessableEntity, "un nom par école, sans tenir compte de la casse");
        (await SendAsync(HttpMethod.Post, Sessions, directeur, Body(""))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await SendAsync(HttpMethod.Post, Sessions, directeur, Body(Unique("Sans jour"), days: []))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await SendAsync(HttpMethod.Post, Sessions, directeur, Body(Unique("Jour 9"), days: [9]))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await SendAsync(HttpMethod.Put, $"{Sessions}/order", directeur, new { orderedIds = new[] { Guid.NewGuid() } })).StatusCode
            .Should().Be(HttpStatusCode.UnprocessableEntity, "une liste qui ne correspond pas aux sessions de l'école");
    }

    [Fact]
    public async Task The_Offered_Templates_Are_Served_By_The_Server()
    {
        var directeur = await DirecteurAsync();
        await SetInternatAsync(directeur, enabled: true);

        var response = await SendAsync(HttpMethod.Get, Templates, directeur);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var templates = (await response.Content.ReadFromJsonAsync<List<TemplateDto>>())!;
        templates.Select(t => t.Name).Should().Equal("Appel de nuit", "Appel du matin", "Petit-déjeuner", "Déjeuner", "Dîner", "Appel de la Halqa");
        templates.Single(t => t.Code == "halqa").Audience.Should().Be("Halqa");
    }

    // ------------------------------------------------------------------ feuilles et récapitulatif

    [Fact]
    public async Task A_Group_Sheet_Needs_A_Known_Session_And_A_Group()
    {
        var directeur = await DirecteurAsync();
        await SetInternatAsync(directeur, enabled: true);
        var meal = await CreateOkAsync(directeur, Unique("Déjeuner"), "AllBoarders");
        var night = await CreateOkAsync(directeur, Unique("Nuit"), "Interns");

        (await SendAsync(HttpMethod.Get, $"/api/v1/boarding/attendance/sheet?sessionId={Guid.NewGuid()}&groupId={Guid.NewGuid()}&date=2026-10-12", directeur))
            .StatusCode.Should().Be(HttpStatusCode.NotFound, "session inconnue");
        (await SendAsync(HttpMethod.Get, $"/api/v1/boarding/attendance/sheet?sessionId={meal.Id}&date=2026-10-12", directeur))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "la Direction doit désigner la classe");
        (await SendAsync(HttpMethod.Get, $"/api/v1/boarding/attendance/sheet?sessionId={night.Id}&groupId={Guid.NewGuid()}&date=2026-10-12", directeur))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "les internes se pointent par pavillon");
        (await SendAsync(HttpMethod.Post, "/api/v1/boarding/attendance/sheet", directeur,
                new { sessionId = meal.Id, groupId = Guid.NewGuid(), date = "2026-10-12", entries = Array.Empty<object>() }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "une feuille vide est refusée");
    }

    [Fact]
    public async Task The_Monthly_Summary_Is_Served_As_Json_And_As_A_Pdf_And_Refuses_A_Bad_Month()
    {
        var directeur = await DirecteurAsync();
        await SetInternatAsync(directeur, enabled: true);
        var halqa = await CreateOkAsync(directeur, Unique("Halqa du mois"), "Halqa");

        var created = await SendAsync(HttpMethod.Post, "/api/v1/internat/instructors", directeur, new { fullName = Unique("Oustaz Récap") });
        created.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK);
        var oustaz = (await created.Content.ReadFromJsonAsync<InstructorDto>())!;
        var month = DateTime.UtcNow.ToString("yyyy-MM");
        var query = $"month={month}&sessionId={halqa.Id}&groupId={oustaz.Id}";

        var json = await SendAsync(HttpMethod.Get, $"/api/v1/boarding/attendance/monthly-summary?{query}", directeur);
        json.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await json.Content.ReadAsStringAsync();
        body.Should().Contain(halqa.Id.ToString()).And.Contain("\"rows\"").And.Contain("\"days\"");

        var pdf = await SendAsync(HttpMethod.Get, $"/api/v1/boarding/attendance/monthly-summary/pdf?{query}", directeur);
        pdf.StatusCode.Should().Be(HttpStatusCode.OK);
        pdf.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        (await pdf.Content.ReadAsByteArrayAsync()).Take(4).Should().Equal("%PDF"u8.ToArray());

        // Même résumé par la route de l'Oustaz (la Direction y désigne l'Oustaz).
        (await SendAsync(HttpMethod.Get, $"/api/v1/internat/attendance/monthly-summary?month={month}&sessionId={halqa.Id}&instructorId={oustaz.Id}", directeur))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        foreach (var bad in new[] { "", "2026-13", "octobre", "2026-10-05" })
        {
            (await SendAsync(HttpMethod.Get, $"/api/v1/boarding/attendance/monthly-summary?month={bad}&sessionId={halqa.Id}&groupId={oustaz.Id}", directeur))
                .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, $"mois « {bad} »");
        }

        (await SendAsync(HttpMethod.Get, $"/api/v1/boarding/attendance/monthly-summary?month={month}&sessionId={Guid.NewGuid()}&groupId={oustaz.Id}", directeur))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
