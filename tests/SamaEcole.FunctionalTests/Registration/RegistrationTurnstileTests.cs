using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SamaEcole.FunctionalTests.Common;
using SamaEcole.Web.Security;
using Xunit;

namespace SamaEcole.FunctionalTests.Registration;

/// <summary>
/// Cloudflare Turnstile sur le formulaire PUBLIC d'inscription : inactif sans clés (développement, tests), obligatoire et
/// vérifié côté serveur dès que les deux clés sont posées. Le verdict de Cloudflare est simulé : aucun appel réseau.
/// La politique de sécurité des scripts n'ouvre Cloudflare QUE sur la page d'inscription, et seulement si Turnstile est actif.
/// </summary>
public class RegistrationTurnstileTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private sealed class FakeTurnstileVerifier(TurnstileVerdict verdict) : ITurnstileVerifier
    {
        public int Calls { get; private set; }
        public string? LastToken { get; private set; }

        public Task<TurnstileVerdict> VerifyAsync(string? token, string? remoteIp, CancellationToken cancellationToken)
        {
            Calls++;
            LastToken = token;
            return Task.FromResult(string.IsNullOrWhiteSpace(token) ? TurnstileVerdict.Invalid : verdict);
        }
    }

    private HttpClient ClientWith(FakeTurnstileVerifier verifier, bool withKeys = true) =>
        factory.WithWebHostBuilder(builder =>
        {
            if (withKeys)
            {
                builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Turnstile:SiteKey"] = "site-key-publique",
                    ["Turnstile:SecretKey"] = "cle-secrete-de-test"
                }));
            }

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ITurnstileVerifier>();
                services.AddSingleton<ITurnstileVerifier>(verifier);
            });
        }).CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    /// <summary>Message du champ TurnstileToken dans la réponse 422 normalisée (le JSON échappe les accents : on le décode).</summary>
    private static async Task<string> TurnstileMessageAsync(HttpResponseMessage response)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("details").GetProperty("TurnstileToken")[0].GetString() ?? "";
    }

    private static object Payload(string email, string? turnstileToken = null, string? website = null) => new
    {
        directorFullName = "Awa Ndiaye",
        directorEmail = email,
        directorPhone = "+221771234567",
        directorPassword = "Correct-Horse-9",
        schoolName = "École Turnstile",
        schoolAddress = "Rue 12, Médina, Dakar",
        city = "Dakar",
        region = "Dakar",
        estimatedStudentCount = 250,
        ownership = "Private", cycleProfile = "Primaire", sizeTier = "Small",
        website,
        turnstileToken
    };

    [Fact]
    public async Task Without_Keys_No_Token_Is_Required_And_Cloudflare_Is_Never_Called()
    {
        var verifier = new FakeTurnstileVerifier(TurnstileVerdict.Invalid);
        using var client = ClientWith(verifier, withKeys: false);

        var response = await client.PostAsJsonAsync("/api/v1/registration-requests", Payload("sans-cle@ecole.sn"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        verifier.Calls.Should().Be(0);
    }

    [Fact]
    public async Task With_Keys_A_Missing_Token_Is_Refused_With_422()
    {
        var verifier = new FakeTurnstileVerifier(TurnstileVerdict.Valid);
        using var client = ClientWith(verifier);

        var response = await client.PostAsJsonAsync("/api/v1/registration-requests", Payload("sans-jeton@ecole.sn"));

        response.StatusCode.Should().Be((HttpStatusCode)422);
        (await response.Content.ReadAsStringAsync()).Should().Contain("TurnstileToken");
    }

    [Fact]
    public async Task With_Keys_A_Rejected_Token_Is_Refused_With_422()
    {
        var verifier = new FakeTurnstileVerifier(TurnstileVerdict.Invalid);
        using var client = ClientWith(verifier);

        var response = await client.PostAsJsonAsync("/api/v1/registration-requests", Payload("jeton-refuse@ecole.sn", "jeton-forge"));

        response.StatusCode.Should().Be((HttpStatusCode)422);
        (await TurnstileMessageAsync(response)).Should().Contain("vérification de sécurité a échoué");
    }

    [Fact]
    public async Task With_Keys_An_Unreachable_Cloudflare_Fails_Closed_With_A_Retry_Message()
    {
        var verifier = new FakeTurnstileVerifier(TurnstileVerdict.Unavailable);
        using var client = ClientWith(verifier);

        var response = await client.PostAsJsonAsync("/api/v1/registration-requests", Payload("cloudflare-panne@ecole.sn", "jeton"));

        response.StatusCode.Should().Be((HttpStatusCode)422, "un robot ne doit pas profiter d'une panne de Cloudflare");
        (await TurnstileMessageAsync(response)).Should().Contain("momentanément indisponible");
    }

    [Fact]
    public async Task With_Keys_A_Valid_Token_Is_Accepted_And_The_Request_Is_Recorded()
    {
        var verifier = new FakeTurnstileVerifier(TurnstileVerdict.Valid);
        using var client = ClientWith(verifier);

        var response = await client.PostAsJsonAsync("/api/v1/registration-requests", Payload("jeton-valide@ecole.sn", "jeton-valide"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        verifier.LastToken.Should().Be("jeton-valide");
    }

    [Fact]
    public async Task The_Honeypot_Still_Answers_Silently_And_Spends_No_Cloudflare_Call()
    {
        var verifier = new FakeTurnstileVerifier(TurnstileVerdict.Valid);
        using var client = ClientWith(verifier);

        var response = await client.PostAsJsonAsync("/api/v1/registration-requests", Payload("robot@ecole.sn", website: "http://spam.example"));

        response.StatusCode.Should().Be(HttpStatusCode.OK, "le piège répond comme un succès, sans rien révéler");
        verifier.Calls.Should().Be(0);
    }

    [Fact]
    public async Task The_Registration_Page_Shows_The_Widget_And_Opens_Cloudflare_In_Its_Policy_Only_When_Enabled()
    {
        using var enabled = ClientWith(new FakeTurnstileVerifier(TurnstileVerdict.Valid));
        var page = await enabled.GetAsync("/inscription");
        var html = await page.Content.ReadAsStringAsync();

        page.StatusCode.Should().Be(HttpStatusCode.OK);
        html.Should().Contain("cf-turnstile").And.Contain("site-key-publique");
        html.Should().NotContain("cle-secrete-de-test", "la clé secrète ne sort jamais du serveur");
        page.Headers.GetValues("Content-Security-Policy").Single().Should().Contain("https://challenges.cloudflare.com");

        // Le reste du site garde la politique stricte, sans aucune origine tierce en script.
        var login = await enabled.GetAsync("/login");
        login.Headers.GetValues("Content-Security-Policy").Single().Should().NotContain("cloudflare");

        using var disabled = ClientWith(new FakeTurnstileVerifier(TurnstileVerdict.Valid), withKeys: false);
        var plain = await disabled.GetAsync("/inscription");
        (await plain.Content.ReadAsStringAsync()).Should().NotContain("cf-turnstile");
        plain.Headers.GetValues("Content-Security-Policy").Single().Should().NotContain("cloudflare");
    }
}
