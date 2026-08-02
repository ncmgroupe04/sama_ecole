using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace SamaEcole.FunctionalTests.PublicDirectory;

/// <summary>
/// Annuaire PUBLIC des établissements (B2C), contre un vrai PostgreSQL — la seule surface de
/// l'application servie à un visiteur qui n'appartient à aucun établissement.
///
/// Ces tests ne vérifient pas seulement que l'annuaire fonctionne : ils vérifient surtout ce qu'il ne
/// laisse PAS passer. Un annuaire qui listerait une école n'ayant pas consenti, qui exposerait une
/// mention fiscale, ou qui ouvrirait un chemin vers les élèves d'un établissement serait une fuite de
/// données — pas un simple défaut d'affichage.
/// </summary>
public class PublicDirectoryEndpointsTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public PublicDirectoryEndpointsTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public Task InitializeAsync() => _factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private record Tokens(string AccessToken, int ExpiresIn);

    private record PublicSchoolDto(
        Guid Id, string Name, string? City, string? Region, string? Description,
        string? LogoUrl, string? Address, string? Phone, string? Email, List<string> Cycles);

    private record PaginatedPublicSchools(List<PublicSchoolDto> Items, int TotalCount, int Page, int PageSize);

    private async Task<string> DirecteurTokenAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = AuthApiFactory.DirecteurEmail,
            password = AuthApiFactory.DirecteurPassword
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!.AccessToken;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string? token = null, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await _client.SendAsync(request);
    }

    /// <summary>
    /// Bascule la publication de l'école de test via l'API RÉELLE du Directeur (PUT /schools/current),
    /// jamais par un UPDATE direct en base : c'est aussi ce qui prouve que le champ traverse bien toute
    /// la chaîne — requête, commande, entité — plutôt que d'être accepté puis silencieusement perdu.
    /// </summary>
    private async Task SetPubliclyListedAsync(bool isListed, string? city = "Dakar", string? description = null)
    {
        var directeur = await DirecteurTokenAsync();

        var response = await SendAsync(HttpMethod.Put, "/api/v1/schools/current", directeur, new
        {
            name = "École Primaire Les Baobabs",
            address = "Rue 12, Médina, Dakar",
            phone = "+221771234567",
            logoUrl = (string?)null,
            inspectionAcademie = "Dakar",
            inspectionEducationFormation = "Dakar-Médina",
            nomLycee = "Les Baobabs",
            email = "contact@baobabs.sn",
            ninea = "1234567890A",
            registreCommerce = "SN DKR 2020 B 1234",
            isPubliclyListed = isListed,
            city,
            region = "Dakar",
            publicDescription = description
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ------------------------------------------------------------------ Consentement

    [Fact]
    public async Task An_Anonymous_Visitor_Can_Browse_The_Directory_Without_Any_Token()
    {
        await SetPubliclyListedAsync(true, description: "Une école de référence à Dakar.");

        // Aucun en-tête Authorization : c'est tout l'objet de cet endpoint.
        var response = await SendAsync(HttpMethod.Get, "/api/v1/public/schools");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = (await response.Content.ReadFromJsonAsync<PaginatedPublicSchools>())!;
        page.Items.Should().ContainSingle(s => s.Name == "École Primaire Les Baobabs");
        page.Items.Single(s => s.Name == "École Primaire Les Baobabs").Description
            .Should().Be("Une école de référence à Dakar.");
    }

    [Fact]
    public async Task A_School_That_Has_Not_Consented_Is_Absent_From_The_Directory()
    {
        await SetPubliclyListedAsync(false);

        var response = await SendAsync(HttpMethod.Get, "/api/v1/public/schools");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = (await response.Content.ReadFromJsonAsync<PaginatedPublicSchools>())!;
        page.Items.Should().NotContain(s => s.Name == "École Primaire Les Baobabs",
            "une école sans consentement ne doit jamais apparaître dans l'annuaire public");
    }

    [Fact]
    public async Task Withdrawing_Consent_Removes_The_School_Immediately()
    {
        await SetPubliclyListedAsync(true);
        var listed = await SendAsync(HttpMethod.Get, "/api/v1/public/schools");
        (await listed.Content.ReadFromJsonAsync<PaginatedPublicSchools>())!
            .Items.Should().Contain(s => s.Name == "École Primaire Les Baobabs");

        // Retrait du consentement : aucun cache, aucun délai — la requête suivante ne doit plus la voir.
        await SetPubliclyListedAsync(false);

        var afterOptOut = await SendAsync(HttpMethod.Get, "/api/v1/public/schools");
        (await afterOptOut.Content.ReadFromJsonAsync<PaginatedPublicSchools>())!
            .Items.Should().NotContain(s => s.Name == "École Primaire Les Baobabs");
    }

    [Fact]
    public async Task The_Detail_Of_A_Non_Consenting_School_Returns_404_Not_403()
    {
        await SetPubliclyListedAsync(false);

        var response = await SendAsync(
            HttpMethod.Get, $"/api/v1/public/schools/{AuthApiFactory.EcoleId}");

        // 404 et non 403 : un 403 confirmerait l'existence de l'établissement, ce que refuse
        // précisément une école ayant choisi de ne pas être publiée.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Publishing_Without_A_City_Is_Refused()
    {
        var directeur = await DirecteurTokenAsync();

        var response = await SendAsync(HttpMethod.Put, "/api/v1/schools/current", directeur, new
        {
            name = "École Primaire Les Baobabs",
            isPubliclyListed = true,
            city = (string?)null
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "une fiche sans ville serait introuvable dans un annuaire dont la ville est le premier critère");
    }

    // ------------------------------------------------------------------ Étanchéité

    [Fact]
    public async Task The_Directory_Never_Exposes_Fiscal_Or_Administrative_Fields()
    {
        await SetPubliclyListedAsync(true);

        var response = await SendAsync(HttpMethod.Get, "/api/v1/public/schools");
        var payload = await response.Content.ReadAsStringAsync();

        // Ces valeurs sont bien enregistrées sur l'école (SetPubliclyListedAsync les envoie), mais la
        // vue SQL ne les projette pas : elles ne peuvent donc pas atteindre la réponse publique.
        using var document = JsonDocument.Parse(payload);
        var first = document.RootElement.GetProperty("items").EnumerateArray().First();

        first.TryGetProperty("ninea", out _).Should().BeFalse("le NINEA n'a rien à faire sur une vitrine publique");
        first.TryGetProperty("registreCommerce", out _).Should().BeFalse();
        first.TryGetProperty("status", out _).Should().BeFalse();
        first.TryGetProperty("inspectionAcademie", out _).Should().BeFalse();
        first.TryGetProperty("createdBy", out _).Should().BeFalse();

        payload.Should().NotContain("1234567890A", "la valeur du NINEA ne doit apparaître nulle part");
        payload.Should().NotContain("SN DKR 2020 B 1234");
    }

    [Fact]
    public async Task An_Anonymous_Visitor_Reaches_No_School_Data_Whatsoever()
    {
        await SetPubliclyListedAsync(true);

        // L'annuaire est ouvert, mais il n'ouvre RIEN d'autre : les routes métier restent fermées à
        // l'anonyme. C'est la garantie que l'annuaire n'est pas devenu une porte d'entrée latérale.
        foreach (var route in new[]
                 {
                     "/api/v1/students",
                     "/api/v1/grades?classroomId=00000000-0000-0000-0000-000000000000",
                     "/api/v1/finance/payments",
                     "/api/v1/enrollments"
                 })
        {
            var response = await SendAsync(HttpMethod.Get, route);

            response.StatusCode.Should().BeOneOf(
                [HttpStatusCode.Unauthorized, HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed],
                $"la route {route} ne doit jamais servir de données à un visiteur anonyme");
            response.StatusCode.Should().NotBe(HttpStatusCode.OK);
        }
    }

    // ------------------------------------------------------------------ Cycles proposés

    [Fact]
    public async Task The_Cycles_Offered_Are_Derived_From_The_Real_Classrooms()
    {
        // LE test critique de ce module. Les cycles viennent de `classrooms`, table sous RLS — et un
        // visiteur anonyme n'a AUCUN tenant, donc la RLS lui renverrait zéro classe. Ils ne remontent
        // que parce que la vue appartient au rôle propriétaire et lit classrooms hors RLS.
        //
        // Sans ce test, un annuaire affichant systématiquement « aucun cycle » passerait pour un
        // établissement sans classes plutôt que pour un contournement qui ne fonctionne pas.
        var directeur = await DirecteurTokenAsync();

        var classroom = await SendAsync(HttpMethod.Post, "/api/v1/classrooms", directeur,
            new { name = "CM2 Annuaire", level = "Primaire", capacity = 40 });
        classroom.StatusCode.Should().Be(HttpStatusCode.Created);

        await SetPubliclyListedAsync(true);

        var response = await SendAsync(HttpMethod.Get, "/api/v1/public/schools");
        var page = (await response.Content.ReadFromJsonAsync<PaginatedPublicSchools>())!;

        var school = page.Items.Single(s => s.Name == "École Primaire Les Baobabs");
        school.Cycles.Should().NotBeEmpty(
            "les cycles doivent traverser la RLS via la vue, sinon aucune filière ne s'affichera jamais");
        school.Cycles.Should().Contain("Primaire");
    }

    [Fact]
    public async Task Filtering_By_An_Offered_Cycle_Finds_The_School()
    {
        var directeur = await DirecteurTokenAsync();

        var classroom = await SendAsync(HttpMethod.Post, "/api/v1/classrooms", directeur,
            new { name = "CI Filtre Cycle", level = "Primaire", capacity = 30 });
        classroom.StatusCode.Should().Be(HttpStatusCode.Created);

        await SetPubliclyListedAsync(true);

        var matching = await SendAsync(HttpMethod.Get, "/api/v1/public/schools?cycle=Primaire");
        (await matching.Content.ReadFromJsonAsync<PaginatedPublicSchools>())!
            .Items.Should().Contain(s => s.Name == "École Primaire Les Baobabs");

        // Contre-épreuve : un cycle que l'école ne propose pas ne doit pas la faire remonter.
        var notMatching = await SendAsync(HttpMethod.Get, "/api/v1/public/schools?cycle=Lycee");
        (await notMatching.Content.ReadFromJsonAsync<PaginatedPublicSchools>())!
            .Items.Should().NotContain(s => s.Name == "École Primaire Les Baobabs");
    }

    // ------------------------------------------------------------------ Filtres et pagination

    [Fact]
    public async Task Filtering_By_A_Different_City_Returns_An_Empty_Page_Not_Everything()
    {
        await SetPubliclyListedAsync(true, city: "Dakar");

        var response = await SendAsync(HttpMethod.Get, "/api/v1/public/schools?city=Saint-Louis");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = (await response.Content.ReadFromJsonAsync<PaginatedPublicSchools>())!;
        page.Items.Should().NotContain(s => s.Name == "École Primaire Les Baobabs",
            "un filtre qui ne correspond à rien doit vider la page, jamais l'ignorer silencieusement");
    }

    [Fact]
    public async Task An_Unknown_Cycle_Is_Rejected_Rather_Than_Silently_Ignored()
    {
        var response = await SendAsync(HttpMethod.Get, "/api/v1/public/schools?cycle=Universite");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "un cycle inconnu doit être signalé, pas produire un annuaire faussement vide");
    }

    [Fact]
    public async Task An_Excessive_PageSize_Is_Refused()
    {
        var response = await SendAsync(HttpMethod.Get, "/api/v1/public/schools?pageSize=100000");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "cet endpoint est anonyme : sans plafond, la taille de page serait dictée par l'appelant");
    }

    // ------------------------------------------------------------------ Aller-retour du consentement

    [Fact]
    public async Task The_Directory_Fields_Survive_A_Full_Put_Then_Get_Round_Trip()
    {
        // Vérifie que les champs d'annuaire traversent réellement la chaîne (requête -> commande ->
        // entité -> base -> lecture) : un DTO mappé À LA MAIN dans le contrôleur avait déjà fait perdre
        // des champs silencieusement par le passé, sans qu'aucun test ne le voie.
        await SetPubliclyListedAsync(true, city: "Thiès", description: "Présentation de test.");

        var directeur = await DirecteurTokenAsync();
        var response = await SendAsync(HttpMethod.Get, "/api/v1/schools/current", directeur);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        root.GetProperty("isPubliclyListed").GetBoolean().Should().BeTrue();
        root.GetProperty("city").GetString().Should().Be("Thiès");
        root.GetProperty("region").GetString().Should().Be("Dakar");
        root.GetProperty("publicDescription").GetString().Should().Be("Présentation de test.");
    }
}
