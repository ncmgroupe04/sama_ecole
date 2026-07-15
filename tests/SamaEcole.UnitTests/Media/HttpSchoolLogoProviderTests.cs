using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SamaEcole.Infrastructure.Media;
using Xunit;

namespace SamaEcole.UnitTests.Media;

/// <summary>
/// Ticket JGK-E02 — garde-fous de CONTENU du fournisseur de logo (la garde réseau anti-SSRF est testée
/// séparément par <see cref="PrivateNetworkGuardTests"/>). On stube le transport HTTP pour vérifier, sans
/// réseau, qu'une URL absente n'entraîne aucun appel, qu'un non-http est refusé d'emblée, et qu'on
/// n'accepte QUE de vraies images de taille raisonnable — statut 200 compris.
/// </summary>
public class HttpSchoolLogoProviderTests
{
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        public StubHandler(HttpResponseMessage response) : this(_ => response) { }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(responder(request));
        }
    }

    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static HttpSchoolLogoProvider Provider(HttpMessageHandler handler) =>
        new(new StubFactory(handler), NullLogger<HttpSchoolLogoProvider>.Instance);

    private static HttpResponseMessage Ok(HttpContent content) => new(HttpStatusCode.OK) { Content = content };

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Returns_Null_Without_Any_Network_Call_For_A_Missing_Url(string? url)
    {
        var handler = new StubHandler(new HttpResponseMessage(HttpStatusCode.OK));

        var result = await Provider(handler).TryFetchAsync(url, CancellationToken.None);

        result.Should().BeNull();
        handler.Calls.Should().Be(0, "une URL absente n'entraîne aucun appel réseau");
    }

    [Theory]
    [InlineData("ftp://exemple.sn/logo.png")]
    [InlineData("file:///etc/passwd")]
    [InlineData("data:image/png;base64,AAAA")]
    [InlineData("pas-une-url")]
    public async Task Returns_Null_Without_Any_Network_Call_For_A_Non_Http_Scheme(string url)
    {
        var handler = new StubHandler(new HttpResponseMessage(HttpStatusCode.OK));

        var result = await Provider(handler).TryFetchAsync(url, CancellationToken.None);

        result.Should().BeNull();
        handler.Calls.Should().Be(0, "seul http(s) est parcouru");
    }

    [Fact]
    public async Task Returns_The_Bytes_For_A_Valid_Png()
    {
        var handler = new StubHandler(Ok(new ByteArrayContent(TinyPng)));

        var result = await Provider(handler).TryFetchAsync("https://exemple.sn/logo.png", CancellationToken.None);

        result.Should().Equal(TinyPng);
    }

    [Fact]
    public async Task Returns_Null_On_An_Http_Error_Status()
    {
        var handler = new StubHandler(new HttpResponseMessage(HttpStatusCode.NotFound));

        var result = await Provider(handler).TryFetchAsync("https://exemple.sn/absent.png", CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Rejects_A_200_That_Is_Not_A_Real_Image()
    {
        // Un serveur peut renvoyer 200 avec une page HTML (erreur déguisée) : sans signature PNG/JPEG,
        // on refuse — le contenu prime sur le statut et sur le type déclaré.
        var handler = new StubHandler(Ok(new StringContent("<html>oops</html>", Encoding.UTF8, "text/html")));

        var result = await Provider(handler).TryFetchAsync("https://exemple.sn/logo.png", CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Rejects_A_Body_Larger_Than_The_Cap()
    {
        var huge = new byte[3 * 1024 * 1024];
        huge[0] = 0x89; huge[1] = 0x50; huge[2] = 0x4E; huge[3] = 0x47; // en-tête PNG plausible
        var handler = new StubHandler(Ok(new ByteArrayContent(huge)));

        var result = await Provider(handler).TryFetchAsync("https://exemple.sn/enorme.png", CancellationToken.None);

        result.Should().BeNull();
    }
}
