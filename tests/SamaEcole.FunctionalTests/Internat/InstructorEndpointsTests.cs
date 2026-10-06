using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Xunit;

namespace SamaEcole.FunctionalTests.Internat;

/// <summary>
/// Gestion des Oustaz par le vrai pipeline HTTP (<c>/api/v1/internat/instructors</c>) : rôles, liaison au compte,
/// verrou optimiste, et surtout le parcours complet — le Directeur crée un Oustaz lié à un compte Enseignant, ce compte
/// ouvre SA Halqa et pas celle d'un autre, puis la suspension lui coupe l'accès.
/// </summary>
public class InstructorEndpointsTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    public Task InitializeAsync() => factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private record Tokens(string AccessToken, string RefreshToken, int ExpiresIn);

    private record ApiError(string Code, string Message);

    private record InstructorResponse(
        Guid Id, string FullName, string? FullNameAr, string? Phone, string Status,
        Guid? UserId, string? UserEmail, int StudentCount, uint RowVersion);

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

    private const string Instructors = "/api/v1/internat/instructors";
    private const string MyHalqa = "/api/v1/internat/my-halqa";

    private record HalqaResponse(Guid InstructorId, string InstructorName, int StudentCount);

    private static string HalqaOf(Guid id) => $"{Instructors}/{id}/students";

    private static object UpdateBody(
        InstructorResponse current, string status = "Active", Guid? userId = null, bool keepUser = true, string? name = null) => new
    {
        fullName = name ?? current.FullName,
        fullNameAr = current.FullNameAr,
        phone = current.Phone,
        userId = keepUser ? current.UserId : userId,
        status,
        rowVersion = current.RowVersion
    };

    private async Task<InstructorResponse> CreateAsync(string directeur, string name, Guid? userId = null)
    {
        var response = await SendAsync(HttpMethod.Post, Instructors, directeur, new { fullName = name, userId });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<InstructorResponse>())!;
    }

    [Fact]
    public async Task Every_Route_Requires_Authentication()
    {
        (await SendAsync(HttpMethod.Get, MyHalqa, null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await SendAsync(HttpMethod.Get, Instructors, null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await SendAsync(HttpMethod.Post, Instructors, null, new { fullName = "X" })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await SendAsync(HttpMethod.Put, $"{Instructors}/{Guid.NewGuid()}", null, new { })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_Routes_Are_Locked_By_The_Internat_Module_Gate_By_Default()
    {
        var response = await SendAsync(HttpMethod.Get, Instructors, await DirecteurAsync());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadFromJsonAsync<ApiError>())!.Code.Should().Be("MODULE_DISABLED");
    }

    [Fact]
    public async Task Only_The_Director_Writes_And_The_Direction_Reads_While_A_Teacher_Cannot_List()
    {
        var directeur = await DirecteurAsync();
        await EnableInternatAsync(directeur);
        var secretaire = await SecretaireAsync();
        var enseignant = await EnseignantAsync();
        var oustaz = await CreateAsync(directeur, "serigne modou");

        (await SendAsync(HttpMethod.Post, Instructors, secretaire, new { fullName = "Intrus" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Put, $"{Instructors}/{oustaz.Id}", secretaire, UpdateBody(oustaz))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Post, Instructors, enseignant, new { fullName = "Intrus" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await SendAsync(HttpMethod.Get, Instructors, secretaire)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Get, Instructors, enseignant)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_Director_Creates_Lists_And_Updates_An_Oustaz_With_Optimistic_Locking()
    {
        var directeur = await DirecteurAsync();
        await EnableInternatAsync(directeur);

        var created = await CreateAsync(directeur, "serigne modou");
        created.FullName.Should().Be("Serigne Modou");
        created.Status.Should().Be("Active");

        var list = await (await SendAsync(HttpMethod.Get, Instructors, directeur)).Content.ReadFromJsonAsync<List<InstructorResponse>>();
        list.Should().ContainSingle(i => i.Id == created.Id).Which.StudentCount.Should().Be(0);

        // Un vrai changement (le nom) : sans lui, EF n'écrit rien et le jeton xmin reste valable.
        var updated = await SendAsync(HttpMethod.Put, $"{Instructors}/{created.Id}", directeur, UpdateBody(created, name: "serigne modou bamba"));
        updated.StatusCode.Should().Be(HttpStatusCode.OK);
        (await updated.Content.ReadFromJsonAsync<InstructorResponse>())!.FullName.Should().Be("Serigne Modou Bamba");

        // Rejouer l'ancien jeton : la fiche a changé depuis → 409, jamais un écrasement.
        var stale = await SendAsync(HttpMethod.Put, $"{Instructors}/{created.Id}", directeur, UpdateBody(created, name: "autre nom"));
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Invalid_Input_And_Unknown_Resources_Get_The_Right_Status()
    {
        var directeur = await DirecteurAsync();
        await EnableInternatAsync(directeur);

        (await SendAsync(HttpMethod.Post, Instructors, directeur, new { fullName = " " })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await SendAsync(HttpMethod.Post, Instructors, directeur, new { fullName = "Oustaz", userId = Guid.NewGuid() })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        // Un compte réel mais de rôle Directeur : refusé sur le champ, 422.
        (await SendAsync(HttpMethod.Post, Instructors, directeur, new { fullName = "Oustaz", userId = AuthApiFactory.DirecteurId })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await SendAsync(HttpMethod.Put, $"{Instructors}/{Guid.NewGuid()}", directeur,
            new { fullName = "X", status = "Active", rowVersion = 1u })).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_Linked_Teacher_Account_Opens_Its_Own_Halqa_Then_Loses_Access_When_The_Oustaz_Is_Suspended()
    {
        var directeur = await DirecteurAsync();
        await EnableInternatAsync(directeur);
        var enseignant = await EnseignantAsync();

        var mine = await CreateAsync(directeur, "mon oustaz", userId: AuthApiFactory.EnseignantId);
        var other = await CreateAsync(directeur, "autre oustaz");
        mine.UserEmail.Should().Be(AuthApiFactory.EnseignantEmail);

        // Un compte = un seul Oustaz : le second rattachement du même compte est refusé.
        (await SendAsync(HttpMethod.Post, Instructors, directeur, new { fullName = "Doublon", userId = AuthApiFactory.EnseignantId }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        // Entrée de la tablette : sa propre Halqa, sans identifiant en paramètre.
        var my = await SendAsync(HttpMethod.Get, MyHalqa, enseignant);
        my.StatusCode.Should().Be(HttpStatusCode.OK);
        (await my.Content.ReadFromJsonAsync<HalqaResponse>())!.InstructorId.Should().Be(mine.Id);
        // La Direction n'a pas de Halqa « à elle » : route réservée aux Oustaz.
        (await SendAsync(HttpMethod.Get, MyHalqa, directeur)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Sa Halqa : 200. Celle d'un autre Oustaz : 403.
        (await SendAsync(HttpMethod.Get, HalqaOf(mine.Id), enseignant)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Get, HalqaOf(other.Id), enseignant)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Suspension : l'accès est coupé à la requête suivante.
        (await SendAsync(HttpMethod.Put, $"{Instructors}/{mine.Id}", directeur, UpdateBody(mine, status: "Suspended")))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var cut = await SendAsync(HttpMethod.Get, HalqaOf(mine.Id), enseignant);
        cut.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Get, MyHalqa, enseignant)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await cut.Content.ReadFromJsonAsync<ApiError>())!.Message.Should().Contain("pas active");
    }
}
