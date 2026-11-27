using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using SamaEcole.Application.Registration;
using SamaEcole.FunctionalTests.Common;
using Xunit;

namespace SamaEcole.FunctionalTests.Registration;

/// <summary>
/// Conditions d'utilisation, politique de confidentialité, mentions légales et cookies : quatre pages PUBLIQUES, la version
/// en vigueur affichée, l'identité de l'éditeur lue de la configuration (jamais inventée), et une acceptation OBLIGATOIRE
/// à l'inscription dont le serveur garde la preuve (version, date).
/// </summary>
public class LegalDocumentsTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient Anonymous(IDictionary<string, string?>? legal = null) =>
        (legal is null ? factory : factory.WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(legal))))
        .CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    /// <summary>Razor encode les accents en entités HTML (é → &amp;#xE9;) : on décode avant de chercher du texte français.</summary>
    private static async Task<string> DecodedAsync(HttpResponseMessage response) =>
        System.Net.WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

    private record SubmitResult(string TrackingReference);
    private record ListItem(string TrackingReference, string? TermsVersion, DateTimeOffset? TermsAcceptedAt);
    private record Tokens(string AccessToken, int ExpiresIn);

    [Theory]
    [InlineData("/conditions-utilisation", "Conditions d'utilisation", "Objet")]
    [InlineData("/politique-confidentialite", "Politique de confidentialité", "2008-12")]
    [InlineData("/mentions-legales", "Mentions légales", "Hébergement")]
    [InlineData("/cookies", "Cookies et stockage local", "sama_ecole_refresh_token")]
    public async Task Each_Legal_Page_Is_Public_Shows_The_Version_And_Its_Own_Content(string path, string title, string marker)
    {
        using var client = Anonymous();

        var response = await client.GetAsync(path);
        var html = await DecodedAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "ces pages sont anonymes : aucune connexion exigée");
        html.Should().Contain(title).And.Contain(marker);
        html.Should().Contain(LegalDocuments.CurrentVersion);
        html.Should().Contain("/conditions-utilisation").And.Contain("/politique-confidentialite")
            .And.Contain("/mentions-legales").And.Contain("/cookies", "chaque page renvoie vers les trois autres");
    }

    [Fact]
    public async Task Without_Publisher_Identity_The_Pages_Say_So_Instead_Of_Inventing_One()
    {
        using var client = Anonymous();

        var html = await DecodedAsync(await client.GetAsync("/mentions-legales"));

        html.Should().Contain("Informations de l");
        html.Should().Contain("à compléter");
    }

    [Fact]
    public async Task The_Configured_Publisher_Identity_Is_Shown_And_The_Warning_Disappears()
    {
        using var client = Anonymous(new Dictionary<string, string?>
        {
            ["Legal:PublisherName"] = "Unikol Technologies SARL",
            ["Legal:RegisteredOffice"] = "Rue 10, Dakar Plateau",
            ["Legal:ContactEmail"] = "contact@unikol.example",
            ["Legal:Ninea"] = "00123456789"
        });

        var notice = await DecodedAsync(await client.GetAsync("/mentions-legales"));
        var privacy = await DecodedAsync(await client.GetAsync("/politique-confidentialite"));

        notice.Should().Contain("Unikol Technologies SARL").And.Contain("Rue 10, Dakar Plateau").And.Contain("00123456789");
        notice.Should().NotContain("Informations de l");
        privacy.Should().Contain("contact@unikol.example", "c'est l'adresse d'exercice des droits");
    }

    [Fact]
    public async Task The_Public_And_Login_Pages_Link_To_The_Legal_Documents()
    {
        using var client = Anonymous();

        foreach (var path in new[] { "/login", "/inscription", "/vitrine" })
        {
            var html = await (await client.GetAsync(path)).Content.ReadAsStringAsync();
            html.Should().Contain("/conditions-utilisation", path).And.Contain("/politique-confidentialite", path);
        }
    }

    [Fact]
    public async Task The_Registration_Form_Has_The_Acceptance_Checkbox_With_Links()
    {
        using var client = Anonymous();

        var html = await (await client.GetAsync("/inscription")).Content.ReadAsStringAsync();

        html.Should().Contain("id=\"acceptedTerms\"").And.Contain("x-model=\"acceptedTerms\"");
    }

    private static object Payload(string email, bool? accepted) => new
    {
        directorFullName = "Awa Ndiaye", directorEmail = email, directorPhone = "+221771234567", directorPassword = "Correct-Horse-9",
        schoolName = "École Preuve", schoolAddress = "Rue 12, Médina, Dakar", city = "Dakar", region = "Dakar",
        estimatedStudentCount = 250, ownership = "Private", cycleProfile = "Primaire", sizeTier = "Small",
        acceptedTerms = accepted
    };

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public async Task A_Request_Without_Acceptance_Is_Refused_With_422(bool? accepted)
    {
        using var client = Anonymous();

        var response = await client.PostAsJsonAsync("/api/v1/registration-requests", Payload($"sans-accord-{accepted?.ToString() ?? "null"}@ecole.sn", accepted));

        response.StatusCode.Should().Be((HttpStatusCode)422);
        (await response.Content.ReadAsStringAsync()).Should().Contain("AcceptedTerms");
    }

    [Fact]
    public async Task An_Accepted_Request_Keeps_The_Proof_Version_And_Date_Visible_To_The_Super_Admin()
    {
        using var client = Anonymous();
        var before = DateTimeOffset.UtcNow.AddSeconds(-5);

        var submit = await client.PostAsJsonAsync("/api/v1/registration-requests", Payload("avec-accord@ecole.sn", true));
        submit.StatusCode.Should().Be(HttpStatusCode.OK);
        var reference = (await submit.Content.ReadFromJsonAsync<SubmitResult>())!.TrackingReference;

        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = AuthApiFactory.SuperAdminEmail, password = AuthApiFactory.SuperAdminPassword });
        var token = (await login.Content.ReadFromJsonAsync<Tokens>())!.AccessToken;
        var list = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/registration-requests?status=Pending");
        list.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var items = (await (await client.SendAsync(list)).Content.ReadFromJsonAsync<List<ListItem>>())!;

        var item = items.Single(r => r.TrackingReference == reference);
        item.TermsVersion.Should().Be(LegalDocuments.CurrentVersion);
        item.TermsAcceptedAt.Should().NotBeNull().And.BeOnOrAfter(before);
    }

    /// <summary>
    /// L'inscription est une page publique qui doit rester indexable ; le noindex ne vaut que pour les
    /// écrans privés (connexion, mot de passe oublié, réinitialisation, paiement en attente).
    /// </summary>
    [Fact]
    public async Task Registration_Is_Indexable_But_Private_Auth_Pages_Are_Not()
    {
        using var client = Anonymous();

        var registration = await DecodedAsync(await client.GetAsync("/inscription"));
        registration.Should().NotContain("name=\"robots\" content=\"noindex\"");
        registration.Should().Contain("rel=\"canonical\"");

        foreach (var path in new[] { "/login", "/mot-de-passe-oublie", "/reinitialiser-mot-de-passe" })
        {
            var html = await DecodedAsync(await client.GetAsync(path));
            html.Should().Contain("name=\"robots\" content=\"noindex\"", path);
            html.Should().Contain("rel=\"canonical\"", path);
        }
    }
}
