using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Xunit;

namespace SamaEcole.FunctionalTests.Schools;

/// <summary>
/// Ticket JGK-B01 — CRUD Établissements. Critères : seul un Super Admin peut créer une école ;
/// le Directeur créé reçoit ses identifiants par e-mail.
/// </summary>
public class SchoolEndpointsTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    public Task InitializeAsync() => factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private record Tokens(string AccessToken, string RefreshToken, int ExpiresIn);
    private record SchoolResult(Guid SchoolId, string Name, Guid DirectorUserId, string DirectorEmail);
    private record SchoolSummary(Guid Id, string Name, string? Address, string? Phone, string Status);

    private async Task<Tokens> LoginAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!;
    }

    private Task<Tokens> LoginAsSuperAdminAsync() =>
        LoginAsync(AuthApiFactory.SuperAdminEmail, AuthApiFactory.SuperAdminPassword);

    private async Task<HttpResponseMessage> CreateSchoolAsync(
        string accessToken, string name, string directorEmail)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/schools")
        {
            Content = JsonContent.Create(new
            {
                name,
                address = "Rue 12, Médina, Dakar",
                phone = "+221771234567",
                directorEmail,
                directorFullName = "Aminata Sow"
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return await _client.SendAsync(request);
    }

    [Fact]
    public async Task SuperAdmin_Should_Create_A_School_With_Its_Initial_Director()
    {
        var superAdmin = await LoginAsSuperAdminAsync();

        var response = await CreateSchoolAsync(
            superAdmin.AccessToken, "École Les Filaos", "directrice@filaos.sn");

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var result = (await response.Content.ReadFromJsonAsync<SchoolResult>())!;
        result.SchoolId.Should().NotBeEmpty();
        result.DirectorUserId.Should().NotBeEmpty("l'école doit naître AVEC son Directeur, sinon elle est inaccessible");
        result.DirectorEmail.Should().Be("directrice@filaos.sn");
    }

    [Fact]
    public async Task The_Generated_Password_Must_Never_Appear_In_The_Http_Response()
    {
        var superAdmin = await LoginAsSuperAdminAsync();

        var response = await CreateSchoolAsync(
            superAdmin.AccessToken, "École Les Filaos", "directrice@filaos.sn");

        var body = await response.Content.ReadAsStringAsync();
        var email = factory.Emails.LastTo("directrice@filaos.sn")!;
        var password = FakeEmailSender.ExtractPassword(email);

        // Le renvoyer exposerait le mot de passe aux journaux d'accès, au cache du navigateur et à
        // l'historique de l'outil appelant. Il ne doit transiter QUE par l'e-mail.
        body.Should().NotContain(password);
    }

    [Fact]
    public async Task The_New_Director_Should_Be_Able_To_Login_With_The_Emailed_Password()
    {
        // LE test de bout en bout : il prouve toute la chaîne — création sous RLS via la fonction
        // SECURITY DEFINER, hachage du mot de passe généré, envoi de l'e-mail, puis connexion réelle.
        var superAdmin = await LoginAsSuperAdminAsync();

        await CreateSchoolAsync(superAdmin.AccessToken, "École Les Filaos", "directrice@filaos.sn");

        var email = factory.Emails.LastTo("directrice@filaos.sn");
        email.Should().NotBeNull("le Directeur doit recevoir ses identifiants par e-mail (critère du ticket)");

        var password = FakeEmailSender.ExtractPassword(email!);

        var tokens = await LoginAsync("directrice@filaos.sn", password);

        tokens.AccessToken.Should().NotBeNullOrWhiteSpace(
            "le Directeur créé doit pouvoir se connecter avec le mot de passe reçu");
    }

    [Fact]
    public async Task A_Directeur_Must_Not_Be_Allowed_To_Create_A_School()
    {
        // Critère du ticket : « seul un SuperAdmin peut créer une école ».
        var directeur = await LoginAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);

        var response = await CreateSchoolAsync(
            directeur.AccessToken, "École pirate", "pirate@filaos.sn");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Creating_A_School_Without_A_Token_Should_Return_401()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/schools", new
        {
            name = "École anonyme",
            address = "Quelque part",
            directorEmail = "anonyme@filaos.sn"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Reusing_An_Existing_Email_Should_Be_Rejected()
    {
        // Un e-mail identifie un compte sur TOUTE la plateforme : sans ce contrôle, la création
        // échouerait sur la contrainte d'unicité et remonterait en 409 illisible.
        var superAdmin = await LoginAsSuperAdminAsync();

        var response = await CreateSchoolAsync(
            superAdmin.AccessToken, "École Les Filaos", AuthApiFactory.DirecteurEmail);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task An_Invalid_Director_Email_Should_Be_Rejected()
    {
        var superAdmin = await LoginAsSuperAdminAsync();

        var response = await CreateSchoolAsync(
            superAdmin.AccessToken, "École Les Filaos", "pas-un-email");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task SuperAdmin_Should_List_All_Schools()
    {
        var superAdmin = await LoginAsSuperAdminAsync();

        await CreateSchoolAsync(superAdmin.AccessToken, "École Les Filaos", "directrice@filaos.sn");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/schools");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", superAdmin.AccessToken);

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var schools = (await response.Content.ReadFromJsonAsync<List<SchoolSummary>>())!;

        // L'école semée + celle qu'on vient de créer. `schools` n'étant pas une table tenant, le
        // Super Admin les voit toutes — c'est le seul endroit du système où c'est vrai.
        schools.Should().HaveCount(2);
        schools.Should().Contain(s => s.Name == "École Les Filaos");
    }

    [Fact]
    public async Task A_Directeur_Must_Not_Be_Allowed_To_List_Schools()
    {
        var directeur = await LoginAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/schools");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", directeur.AccessToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
