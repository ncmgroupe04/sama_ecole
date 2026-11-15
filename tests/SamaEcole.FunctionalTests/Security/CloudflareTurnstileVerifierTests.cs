using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SamaEcole.Web.Security;
using Xunit;

namespace SamaEcole.FunctionalTests.Security;

/// <summary>Vérificateur Turnstile : verdicts, échec fermé et contenu exact de l'appel à Cloudflare (sans réseau réel).</summary>
public class CloudflareTurnstileVerifierTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<(Uri? Uri, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.RequestUri, request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken)));
            return await respond(request);
        }
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static (CloudflareTurnstileVerifier Verifier, StubHandler Handler) Build(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond)
    {
        var handler = new StubHandler(respond);
        var options = Options.Create(new TurnstileOptions { SiteKey = "site", SecretKey = "secret-de-test" });
        return (new CloudflareTurnstileVerifier(new HttpClient(handler), options, NullLogger<CloudflareTurnstileVerifier>.Instance), handler);
    }

    [Fact]
    public async Task A_Success_Answer_Is_Valid_And_The_Call_Carries_The_Secret_Token_And_Ip()
    {
        var (verifier, handler) = Build(_ => Task.FromResult(Json("""{"success":true}""")));

        var verdict = await verifier.VerifyAsync("le-jeton", "203.0.113.7", default);

        verdict.Should().Be(TurnstileVerdict.Valid);
        var call = handler.Requests.Should().ContainSingle().Subject;
        call.Uri!.ToString().Should().Be("https://challenges.cloudflare.com/turnstile/v0/siteverify");
        call.Body.Should().Contain("secret=secret-de-test").And.Contain("response=le-jeton").And.Contain("remoteip=203.0.113.7");
    }

    [Fact]
    public async Task A_Failure_Answer_Is_Invalid()
    {
        var (verifier, _) = Build(_ => Task.FromResult(Json("""{"success":false,"error-codes":["invalid-input-response"]}""")));

        (await verifier.VerifyAsync("jeton", null, default)).Should().Be(TurnstileVerdict.Invalid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_Missing_Token_Is_Invalid_Without_Any_Network_Call(string? token)
    {
        var (verifier, handler) = Build(_ => Task.FromResult(Json("""{"success":true}""")));

        (await verifier.VerifyAsync(token, null, default)).Should().Be(TurnstileVerdict.Invalid);
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task A_Server_Error_From_Cloudflare_Fails_Closed_As_Unavailable()
    {
        var (verifier, _) = Build(_ => Task.FromResult(Json("{}", HttpStatusCode.InternalServerError)));

        (await verifier.VerifyAsync("jeton", null, default)).Should().Be(TurnstileVerdict.Unavailable);
    }

    [Fact]
    public async Task A_Network_Failure_Fails_Closed_As_Unavailable()
    {
        var (verifier, _) = Build(_ => throw new HttpRequestException("DNS"));

        (await verifier.VerifyAsync("jeton", null, default)).Should().Be(TurnstileVerdict.Unavailable);
    }

    [Fact]
    public async Task An_Unreadable_Answer_Fails_Closed_As_Unavailable()
    {
        var (verifier, _) = Build(_ => Task.FromResult(Json("<html>pas du json</html>")));

        (await verifier.VerifyAsync("jeton", null, default)).Should().Be(TurnstileVerdict.Unavailable);
    }

    [Theory]
    [InlineData("site", "secret", true)]
    [InlineData("site", null, false)]
    [InlineData(null, "secret", false)]
    [InlineData("", "", false)]
    public void The_Control_Is_Enabled_Only_When_Both_Keys_Are_Set(string? site, string? secret, bool expected)
    {
        new TurnstileOptions { SiteKey = site, SecretKey = secret }.IsEnabled.Should().Be(expected);
    }
}
