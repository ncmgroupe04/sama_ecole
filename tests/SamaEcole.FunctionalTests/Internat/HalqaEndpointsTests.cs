using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Xunit;

namespace SamaEcole.FunctionalTests.Internat;

/// <summary>
/// Les quatre routes Halqa/Hizb de <c>/api/v1/internat</c> par le vrai pipeline HTTP : authentification, garde de
/// module, rôles, et traduction des erreurs (401, 403 FORBIDDEN / MODULE_DISABLED, 404, 422). La portée fine d'un
/// Oustaz (SA Halqa seulement) est prouvée sur la base par HalqaHandlersTests ; ici on prouve que le rôle Enseignant
/// sans fiche d'Oustaz reçoit bien un 403 actionnable, et que les rôles non autorisés n'atteignent pas l'écriture.
/// </summary>
public class HalqaEndpointsTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    public Task InitializeAsync() => factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private record Tokens(string AccessToken, string RefreshToken, int ExpiresIn);

    private record ApiError(string Code, string Message);

    private async Task<string> LoginAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!.AccessToken;
    }

    private Task<string> DirecteurAsync() => LoginAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);
    private Task<string> EnseignantAsync() => LoginAsync(AuthApiFactory.EnseignantEmail, AuthApiFactory.EnseignantPassword);
    private Task<string> SecretaireAsync() => LoginAsync(AuthApiFactory.SecretaireEmail, AuthApiFactory.SecretairePassword);

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

    private async Task EnableInternatAsync(string directeurToken)
    {
        var response = await SendAsync(HttpMethod.Put, "/api/v1/schools/current/settings", directeurToken, new
        {
            gradingScale = "20",
            studentMatriculeFormat = "ELEV-{YEAR}-{SEQ:4}",
            teacherMatriculeFormat = "ENS-{YEAR}-{SEQ:3}",
            autoLogoutMinutes = 10,
            dateFormat = "dd/MM/yyyy",
            tuitionMonthsPerYear = 9,
            allowSecretaryToManageGrading = false,
            isPedagogyEnabled = true,
            isFinanceEnabled = true,
            isInternatEnabled = true,
            isCoranModuleEnabled = false
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static string Halqa(Guid instructor) => $"/api/v1/internat/instructors/{instructor}/students";
    private static string Hizb(Guid student) => $"/api/v1/internat/students/{student}/hizb-progress";
    private const string Assign = "/api/v1/internat/students/assign-instructor";

    private static object HizbBody(int hizb = 1, int quarters = 2, int? rating = 3) =>
        new { hizbNumber = hizb, completedQuarters = quarters, rating, rowVersion = (uint?)null };

    [Fact]
    public async Task Every_Route_Requires_Authentication()
    {
        (await SendAsync(HttpMethod.Get, Halqa(Guid.NewGuid()), null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await SendAsync(HttpMethod.Post, Assign, null, new { studentIds = new[] { Guid.NewGuid() } })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await SendAsync(HttpMethod.Get, Hizb(Guid.NewGuid()), null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await SendAsync(HttpMethod.Put, Hizb(Guid.NewGuid()), null, HizbBody())).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_Routes_Are_Locked_By_The_Internat_Module_Gate_By_Default()
    {
        var directeur = await DirecteurAsync();

        var response = await SendAsync(HttpMethod.Get, Hizb(Guid.NewGuid()), directeur);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadFromJsonAsync<ApiError>())!.Code.Should().Be("MODULE_DISABLED");
    }

    [Fact]
    public async Task A_Teacher_Account_Without_An_Instructor_Record_Gets_An_Actionable_403()
    {
        await EnableInternatAsync(await DirecteurAsync());
        var enseignant = await EnseignantAsync();

        var response = await SendAsync(HttpMethod.Get, Halqa(Guid.NewGuid()), enseignant);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var error = (await response.Content.ReadFromJsonAsync<ApiError>())!;
        error.Code.Should().Be("FORBIDDEN");
        error.Message.Should().Contain("fiche d'Oustaz");
    }

    [Fact]
    public async Task Only_The_Director_Can_Assign_Students_To_An_Oustaz()
    {
        await EnableInternatAsync(await DirecteurAsync());
        var body = new { instructorId = Guid.NewGuid(), studentIds = new[] { Guid.NewGuid() } };

        (await SendAsync(HttpMethod.Post, Assign, await EnseignantAsync(), body)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Post, Assign, await SecretaireAsync(), body)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_Secretary_Can_Read_But_Not_Write_The_Hizb_Progress()
    {
        await EnableInternatAsync(await DirecteurAsync());
        var secretaire = await SecretaireAsync();

        // Lecture autorisée par le rôle : l'élève n'existe pas → 404, pas 403.
        (await SendAsync(HttpMethod.Get, Hizb(Guid.NewGuid()), secretaire)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        // Écriture : refusée par le rôle avant même de chercher l'élève.
        (await SendAsync(HttpMethod.Put, Hizb(Guid.NewGuid()), secretaire, HizbBody())).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Unknown_Resources_Are_Not_Found_For_The_Director()
    {
        var directeur = await DirecteurAsync();
        await EnableInternatAsync(directeur);

        (await SendAsync(HttpMethod.Get, Halqa(Guid.NewGuid()), directeur)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await SendAsync(HttpMethod.Get, Hizb(Guid.NewGuid()), directeur)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await SendAsync(HttpMethod.Put, Hizb(Guid.NewGuid()), directeur, HizbBody())).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData(0, 2, 3)]    // Hizb 0
    [InlineData(61, 2, 3)]   // Hizb 61
    [InlineData(1, 5, 3)]    // 5 quarts
    [InlineData(1, 2, 9)]    // note 9
    [InlineData(1, 0, 3)]    // non commencé avec une note
    public async Task Invalid_Hizb_Progress_Is_A_422_Before_Anything_Is_Read(int hizb, int quarters, int rating)
    {
        var directeur = await DirecteurAsync();
        await EnableInternatAsync(directeur);

        var response = await SendAsync(HttpMethod.Put, Hizb(Guid.NewGuid()), directeur, HizbBody(hizb, quarters, rating));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task An_Empty_Assignment_Is_A_422()
    {
        var directeur = await DirecteurAsync();
        await EnableInternatAsync(directeur);

        var response = await SendAsync(HttpMethod.Post, Assign, directeur, new { instructorId = Guid.NewGuid(), studentIds = Array.Empty<Guid>() });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }
}
