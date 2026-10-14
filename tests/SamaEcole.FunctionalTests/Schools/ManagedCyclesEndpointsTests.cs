using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Xunit;

namespace SamaEcole.FunctionalTests.Schools;

/// <summary>
/// PUT /api/v1/schools/current/settings/managed-cycles : le Directeur déclare les cycles de son établissement ;
/// retirer un cycle qui contient encore des classes est refusé (409 CYCLE_HAS_CLASSROOMS) avec le détail.
///
/// Les cycles gérés sont ceux de l'ABONNEMENT : tout changement (ajout, retrait) est intercepté (409
/// SUBSCRIPTION_CHANGE_REQUIRED, avec l'aperçu) et devient une demande que le Super Admin valide. Seule la redéclaration des
/// cycles actuels passe. Les états de départ de ces tests sont posés directement en base.
/// </summary>
public class ManagedCyclesEndpointsTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    private record Tokens(string AccessToken, int ExpiresIn);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private const string Route = "/api/v1/schools/current/settings/managed-cycles";

    /// <summary>État d'origine : tous les cycles, aucune classe (le nettoyage de la fabrique purge les classes).</summary>
    public async Task InitializeAsync()
    {
        await factory.ResetTestUsersAsync();
        await factory.ExecuteOwnerSqlAsync(
            $"""UPDATE school_settings SET "ManagedCycles" = 'Maternelle,Primaire,College,Lycee' WHERE "SchoolId" = '{AuthApiFactory.EcoleId}'""");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_Settings_Report_Every_Cycle_By_Default()
    {
        var token = await TokenAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);

        var settings = await ReadJsonAsync(await SendAsync(HttpMethod.Get, "/api/v1/schools/current/settings", token));

        Names(settings).Should().Equal("Maternelle", "Primaire", "College", "Lycee");
    }

    [Fact]
    public async Task The_Director_Cannot_Change_Cycles_Directly_The_Attempt_Becomes_A_Change_Preview_And_Nothing_Is_Written()
    {
        var token = await TokenAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);

        var response = await SendAsync(HttpMethod.Put, Route, token, new { cycles = new[] { "Primaire", "Maternelle" } });

        // Les cycles gérés sont ceux de l'abonnement : en retirer passe par une demande validée par le Super Admin.
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var error = await ReadJsonAsync(response);
        error.GetProperty("code").GetString().Should().Be("SUBSCRIPTION_CHANGE_REQUIRED");
        var preview = error.GetProperty("details");
        preview.GetProperty("isDowngrade").GetBoolean().Should().BeTrue("retirer un cycle est une réduction, traitée par le support");
        preview.GetProperty("changes").EnumerateArray().Select(c => c.GetProperty("field").GetString()).Should().Contain("Cycles");
        preview.GetProperty("request").GetProperty("cycles").EnumerateArray().Select(c => c.GetString())
            .Should().Equal("Primaire", "Maternelle");

        Names(await ReadJsonAsync(await SendAsync(HttpMethod.Get, "/api/v1/schools/current/settings", token)))
            .Should().Equal("Maternelle", "Primaire", "College", "Lycee");
    }

    [Fact]
    public async Task Declaring_Exactly_The_Current_Cycles_Is_Not_A_Change_And_Is_Accepted()
    {
        var token = await TokenAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);

        var response = await SendAsync(HttpMethod.Put, Route, token, new { cycles = new[] { "Lycee", "College", "Primaire", "Maternelle" } });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        Names(await ReadJsonAsync(response)).Should().Equal("Maternelle", "Primaire", "College", "Lycee");
    }

    [Fact]
    public async Task Adding_A_Cycle_Is_Intercepted_As_An_Upgrade()
    {
        var token = await TokenAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);
        await SetCyclesAsync(token, "Primaire");

        var response = await SendAsync(HttpMethod.Put, Route, token, new { cycles = new[] { "Primaire", "College" } });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var preview = (await ReadJsonAsync(response)).GetProperty("details");
        preview.GetProperty("isDowngrade").GetBoolean().Should().BeFalse("ajouter un cycle est une montée en gamme");
        Names(await ReadJsonAsync(await SendAsync(HttpMethod.Get, "/api/v1/schools/current/settings", token))).Should().Equal("Primaire");
    }

    [Fact]
    public async Task Other_Settings_Updates_Keep_Reporting_The_Managed_Cycles()
    {
        var token = await TokenAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);
        await SetCyclesAsync(token, "Primaire");

        var current = await ReadJsonAsync(await SendAsync(HttpMethod.Get, "/api/v1/schools/current/settings", token));
        var body = JsonSerializer.Deserialize<Dictionary<string, object?>>(current.GetRawText(), Json)!;
        body["autoLogoutMinutes"] = 15;

        var saved = await SendAsync(HttpMethod.Put, "/api/v1/schools/current/settings", token, body);

        saved.StatusCode.Should().Be(HttpStatusCode.OK);
        Names(await ReadJsonAsync(saved)).Should().Equal(new[] { "Primaire" }, "un autre réglage ne touche pas aux cycles");
    }

    [Fact]
    public async Task The_General_Settings_Update_Cannot_Be_Used_To_Bypass_The_Cycle_Rule()
    {
        var token = await TokenAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);
        await CreateClassroomAsync(token, "Tle S1", "Lycée");

        // Le PUT général relit puis renvoie les réglages (comme settings.js) : on y glisse un « managedCycles »
        // qui retirerait le Lycée alors qu'il contient une classe. Le serveur doit l'ignorer.
        var current = await ReadJsonAsync(await SendAsync(HttpMethod.Get, "/api/v1/schools/current/settings", token));
        var body = JsonSerializer.Deserialize<Dictionary<string, object?>>(current.GetRawText(), Json)!;
        body["managedCycles"] = new[] { "Primaire" };

        (await SendAsync(HttpMethod.Put, "/api/v1/schools/current/settings", token, body)).StatusCode.Should().Be(HttpStatusCode.OK);

        Names(await ReadJsonAsync(await SendAsync(HttpMethod.Get, "/api/v1/schools/current/settings", token)))
            .Should().Equal("Maternelle", "Primaire", "College", "Lycee");
    }

    [Theory]
    [InlineData("""{"cycles":[]}""")]
    [InlineData("""{"cycles":["Creche"]}""")]
    [InlineData("""{"cycles":["Primaire","Primaire"]}""")]
    [InlineData("""{"cycles":["2"]}""")]
    [InlineData("""{}""")]
    public async Task An_Empty_Unknown_Or_Duplicated_List_Is_Refused_With_422(string body)
    {
        var token = await TokenAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);
        var request = new HttpRequestMessage(HttpMethod.Put, Route)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        (await _client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Only_The_Director_Can_Change_The_Managed_Cycles()
    {
        var token = await TokenAsync(AuthApiFactory.SecretaireEmail, AuthApiFactory.SecretairePassword);

        (await SendAsync(HttpMethod.Put, Route, token, new { cycles = new[] { "Primaire" } }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Removing_A_Cycle_That_Has_Classrooms_Is_Refused_With_The_Count_Then_Succeeds_Once_They_Are_Gone()
    {
        var token = await TokenAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);
        await CreateClassroomAsync(token, "6e A", "Collège");
        await CreateClassroomAsync(token, "5e A", "Collège");

        var refused = await SendAsync(HttpMethod.Put, Route, token, new { cycles = new[] { "Maternelle", "Primaire", "Lycee" } });

        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var error = await ReadJsonAsync(refused);
        error.GetProperty("code").GetString().Should().Be("CYCLE_HAS_CLASSROOMS");
        error.GetProperty("message").GetString().Should().Contain("Collège compte 2 classes");
        var detail = error.GetProperty("details").EnumerateArray().Single();
        detail.GetProperty("cycle").GetString().Should().Be("College");
        detail.GetProperty("classroomCount").GetInt32().Should().Be(2);

        // Rien n'a été écrit.
        Names(await ReadJsonAsync(await SendAsync(HttpMethod.Get, "/api/v1/schools/current/settings", token)))
            .Should().Equal("Maternelle", "Primaire", "College", "Lycee");

        // Les classes supprimées logiquement ne bloquent plus : le Directeur peut décocher.
        await factory.ExecuteOwnerSqlAsync(
            $"""UPDATE classrooms SET "IsDeleted" = true WHERE "SchoolId" = '{AuthApiFactory.EcoleId}' AND "Level" = 'Collège'""");

        // Plus de classes : le refus « le cycle contient des classes » tombe. Le retrait reste une RÉDUCTION d'abonnement, donc une
        // demande — mais plus un blocage : c'est la modale « demande de changement » qui prend le relais.
        var afterCleanup = await SendAsync(HttpMethod.Put, Route, token, new { cycles = new[] { "Maternelle", "Primaire", "Lycee" } });
        afterCleanup.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadJsonAsync(afterCleanup)).GetProperty("code").GetString().Should().Be("SUBSCRIPTION_CHANGE_REQUIRED");
    }

    // ------------------------------------------------------------------ Aides

    /// <summary>
    /// Pose les cycles gérés directement en base : l'API ne le permet plus (verrou d'abonnement). La ligne de réglages doit
    /// exister — un GET n'en crée pas — donc elle est d'abord créée par un enregistrement neutre des réglages actuels.
    /// </summary>
    private async Task SetCyclesAsync(string token, string cycles)
    {
        var current = await ReadJsonAsync(await SendAsync(HttpMethod.Get, "/api/v1/schools/current/settings", token));
        var body = JsonSerializer.Deserialize<Dictionary<string, object?>>(current.GetRawText(), Json)!;
        (await SendAsync(HttpMethod.Put, "/api/v1/schools/current/settings", token, body)).StatusCode.Should().Be(HttpStatusCode.OK);

        await factory.ExecuteOwnerSqlAsync(
            $"""UPDATE school_settings SET "ManagedCycles" = '{cycles}' WHERE "SchoolId" = '{AuthApiFactory.EcoleId}'""");
    }

    private static IEnumerable<string> Names(JsonElement settings) =>
        settings.GetProperty("managedCycles").EnumerateArray().Select(e => e.GetString()!).ToList();

    private async Task<string> TokenAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!.AccessToken;
    }

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

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(), Json);

    private async Task<Guid> CreateClassroomAsync(string token, string name, string level)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/v1/classrooms", token, new { name, level, capacity = 40 });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await ReadJsonAsync(response)).GetProperty("id").GetGuid();
    }
}
