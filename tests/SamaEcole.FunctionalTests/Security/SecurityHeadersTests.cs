using System.Net;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Xunit;

namespace SamaEcole.FunctionalTests.Security;

/// <summary>
/// Ticket JGK-F01 — SecurityHeadersMiddleware. Les en-têtes doivent coiffer TOUTES les réponses :
/// pages Razor, API (y compris les erreurs — le middleware est posé avant le traducteur
/// d'exceptions), avec la seule exemption documentée de la CSP pour Swagger UI (Development).
/// </summary>
public class SecurityHeadersTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static void AssertSecurityHeaders(HttpResponseMessage response)
    {
        response.Headers.GetValues("X-Content-Type-Options").Should().ContainSingle()
            .Which.Should().Be("nosniff");

        response.Headers.GetValues("X-Frame-Options").Should().ContainSingle()
            .Which.Should().Be("DENY");

        response.Headers.GetValues("X-XSS-Protection").Should().ContainSingle()
            .Which.Should().Be("1; mode=block");
    }

    [Fact]
    public async Task A_Razor_Page_Should_Carry_All_Security_Headers()
    {
        var response = await _client.GetAsync("/login");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        AssertSecurityHeaders(response);

        var csp = response.Headers.GetValues("Content-Security-Policy").Single();

        csp.Should().Contain("default-src 'self'");

        // 'unsafe-eval' est le prix d'Alpine.js (build standard) ; en revanche AUCUN script inline
        // n'est toléré — c'est la directive qui neutralise un <script> injecté par XSS.
        csp.Should().Contain("script-src 'self' 'unsafe-eval';");
        csp.Should().NotContain("script-src 'self' 'unsafe-inline'");

        csp.Should().Contain("frame-ancestors 'none'");
        csp.Should().Contain("object-src 'none'");
    }

    [Fact]
    public async Task An_Api_Error_Response_Should_Carry_The_Security_Headers_Too()
    {
        // Sans jeton -> 401. Prouve que les en-têtes précèdent l'authentification ET l'écriture des
        // erreurs : une réponse d'erreur non coiffée serait exactement le genre de trou qu'exploite
        // le MIME sniffing.
        var response = await _client.GetAsync("/api/v1/students");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        AssertSecurityHeaders(response);
        response.Headers.Contains("Content-Security-Policy").Should().BeTrue();
    }

    [Fact]
    public async Task Swagger_Keeps_The_Headers_But_Not_The_Csp()
    {
        // Swagger UI (Development uniquement — l'environnement de ces tests) embarque ses propres
        // scripts inline : la CSP stricte le casserait. Les trois autres en-têtes restent.
        var response = await _client.GetAsync("/swagger/index.html");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        AssertSecurityHeaders(response);
        response.Headers.Contains("Content-Security-Policy").Should().BeFalse();
    }
}
