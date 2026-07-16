using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace SamaEcole.FunctionalTests.Users;

/// <summary>
/// Gestion des utilisateurs par le Directeur — création de comptes Secrétariat/Finance/Enseignant et
/// lecture de la liste. Critères : réservé au Directeur ; SuperAdmin et Directeur ne sont pas des
/// rôles assignables par cette voie ; l'e-mail est unique sur toute la plateforme ; le mot de passe
/// suit la politique de docs/Volume_7_Security.md §2 ; le compte créé peut se connecter immédiatement ;
/// la liste ne montre que les comptes de l'école courante (isolation multi-tenant).
/// </summary>
public class CreateUserEndpointsTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public CreateUserEndpointsTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public Task InitializeAsync() => _factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private record Tokens(string AccessToken, int ExpiresIn);
    private record CreateUserResult(Guid UserId, string FullName, string Email, string Role, string Status);
    private record UserSummary(Guid Id, string FullName, string Email, string Role, string Status, bool IsSelf);
    private record SchoolResult(Guid SchoolId, string Name, Guid DirectorUserId, string DirectorEmail);

    private const string ValidPassword = "Correct-Horse-Battery-9!";

    private async Task<string> TokenAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!.AccessToken;
    }

    private Task<string> DirecteurTokenAsync() => TokenAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);
    private Task<string> SecretaireTokenAsync() => TokenAsync(AuthApiFactory.SecretaireEmail, AuthApiFactory.SecretairePassword);
    private Task<string> FinanceTokenAsync() => TokenAsync(AuthApiFactory.FinanceEmail, AuthApiFactory.FinancePassword);
    private Task<string> SuperAdminTokenAsync() => TokenAsync(AuthApiFactory.SuperAdminEmail, AuthApiFactory.SuperAdminPassword);

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await _client.SendAsync(request);
    }

    [Fact]
    public async Task Directeur_Should_Create_A_Secretariat_Account_That_Can_Log_In()
    {
        var directeur = await DirecteurTokenAsync();

        var response = await SendAsync(HttpMethod.Post, "/api/v1/users", directeur, new
        {
            fullName = "Mariam Fall",
            email = "mariam.fall@sama-ecole.sn",
            password = ValidPassword,
            role = "Secretariat"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var result = (await response.Content.ReadFromJsonAsync<CreateUserResult>())!;
        result.Role.Should().Be("Secretariat");
        result.Status.Should().Be("Active");

        // Critère central : le mot de passe saisi par le Directeur fonctionne réellement.
        var newAccountToken = await TokenAsync("mariam.fall@sama-ecole.sn", ValidPassword);
        newAccountToken.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("Finance")]
    [InlineData("Enseignant")]
    public async Task Every_Assignable_Role_Should_Be_Creatable(string role)
    {
        var directeur = await DirecteurTokenAsync();

        var response = await SendAsync(HttpMethod.Post, "/api/v1/users", directeur, new
        {
            fullName = "Personnel de test",
            email = $"personnel.{role.ToLowerInvariant()}@sama-ecole.sn",
            password = ValidPassword,
            role
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Theory]
    [InlineData("SuperAdmin")]
    [InlineData("Directeur")]
    public async Task SuperAdmin_And_Directeur_Must_Not_Be_Assignable_Through_This_Endpoint(string role)
    {
        // SuperAdmin est un rôle plateforme ; Directeur ne se crée que via POST /schools (JGK-B01).
        var directeur = await DirecteurTokenAsync();

        var response = await SendAsync(HttpMethod.Post, "/api/v1/users", directeur, new
        {
            fullName = "Tentative Escalade",
            email = "escalade@sama-ecole.sn",
            password = ValidPassword,
            role
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Theory]
    [MemberData(nameof(NonDirecteurTokens))]
    public async Task Only_A_Directeur_May_Create_A_User(string tokenFactoryName)
    {
        var token = tokenFactoryName switch
        {
            nameof(SecretaireTokenAsync) => await SecretaireTokenAsync(),
            nameof(FinanceTokenAsync) => await FinanceTokenAsync(),
            _ => throw new ArgumentOutOfRangeException(nameof(tokenFactoryName))
        };

        var response = await SendAsync(HttpMethod.Post, "/api/v1/users", token, new
        {
            fullName = "Ne Devrait Pas Exister",
            email = "ne-devrait-pas-exister@sama-ecole.sn",
            password = ValidPassword,
            role = "Secretariat"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    public static IEnumerable<object[]> NonDirecteurTokens()
    {
        yield return [nameof(SecretaireTokenAsync)];
        yield return [nameof(FinanceTokenAsync)];
    }

    [Fact]
    public async Task Reusing_An_Existing_Email_Should_Be_Rejected()
    {
        var directeur = await DirecteurTokenAsync();

        var response = await SendAsync(HttpMethod.Post, "/api/v1/users", directeur, new
        {
            fullName = "Doublon",
            email = AuthApiFactory.SecretaireEmail, // déjà utilisé par le compte semé
            password = ValidPassword,
            role = "Secretariat"
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_Weak_Password_Should_Be_Rejected()
    {
        var directeur = await DirecteurTokenAsync();

        var response = await SendAsync(HttpMethod.Post, "/api/v1/users", directeur, new
        {
            fullName = "Mot De Passe Faible",
            email = "faible@sama-ecole.sn",
            password = "short",
            role = "Secretariat"
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task The_List_Should_Show_The_Created_User_And_Flag_The_Actor_As_Self()
    {
        var directeur = await DirecteurTokenAsync();

        var create = await SendAsync(HttpMethod.Post, "/api/v1/users", directeur, new
        {
            fullName = "Nouvelle Recrue",
            email = "nouvelle.recrue@sama-ecole.sn",
            password = ValidPassword,
            role = "Enseignant"
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);

        var response = await SendAsync(HttpMethod.Get, "/api/v1/users", directeur);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var users = (await response.Content.ReadFromJsonAsync<List<UserSummary>>())!;

        users.Should().Contain(u => u.Email == "nouvelle.recrue@sama-ecole.sn" && u.Role == "Enseignant" && !u.IsSelf);
        users.Should().ContainSingle(u => u.Id == AuthApiFactory.DirecteurId && u.IsSelf);
    }

    [Fact]
    public async Task A_School_Must_Not_See_Another_Schools_Users()
    {
        // Isolation multi-tenant (AGENTS.md règle #2) : la table users n'a pas de Global Query Filter
        // automatique (SchoolId nullable pour le Super Admin) — GetUsersQueryHandler filtre à la main,
        // c'est CE filtre que ce test vérifie réellement.
        var superAdmin = await SuperAdminTokenAsync();

        var createSchool = await SendAsync(HttpMethod.Post, "/api/v1/schools", superAdmin, new
        {
            name = "École Les Filaos",
            address = "Rue 12, Médina, Dakar",
            phone = "+221771234567",
            directorEmail = "directrice@filaos.sn",
            directorFullName = "Aminata Sow"
        });
        createSchool.StatusCode.Should().Be(HttpStatusCode.Created);

        var password = FakeEmailSender.ExtractPassword(_factory.Emails.LastTo("directrice@filaos.sn")!);
        var otherDirecteur = await TokenAsync("directrice@filaos.sn", password);

        var response = await SendAsync(HttpMethod.Get, "/api/v1/users", otherDirecteur);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var users = (await response.Content.ReadFromJsonAsync<List<UserSummary>>())!;

        users.Should().ContainSingle().Which.Email.Should().Be("directrice@filaos.sn");
        users.Should().NotContain(u => u.Email == AuthApiFactory.DirecteurEmail || u.Email == AuthApiFactory.SecretaireEmail);
    }
}
