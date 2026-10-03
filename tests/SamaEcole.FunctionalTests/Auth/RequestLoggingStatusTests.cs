using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using Serilog.Events;
using SamaEcole.FunctionalTests.Common;
using Xunit;

namespace SamaEcole.FunctionalTests.Auth;

/// <summary>
/// Le journal de requêtes (une ligne « HTTP POST /x responded N ») doit porter le code HTTP RÉELLEMENT
/// renvoyé au client. Avant correction, UseSerilogRequestLogging était placé À L'INTÉRIEUR de
/// ExceptionHandlingMiddleware : il voyait l'exception traverser, journalisait « responded 500 » au niveau
/// Error, puis le middleware la convertissait en 401 — un échec de connexion banal apparaissait donc comme
/// une panne serveur dans les journaux, alors que le client recevait bien 401.
///
/// La classe simule aussi une base locale NON MIGRÉE (colonne ProfileEtablissement absente) : c'est la
/// situation où le vrai 500 (lecture de school_settings) et les faux 500 (échecs de connexion) coexistent.
/// </summary>
public class RequestLoggingStatusTests : IClassFixture<AuthApiFactory>
{
    private readonly AuthApiFactory _factory;

    public RequestLoggingStatusTests(AuthApiFactory factory) => _factory = factory;

    private sealed class CollectingSink : ILogEventSink
    {
        public ConcurrentQueue<LogEvent> Events { get; } = new();
        public void Emit(LogEvent logEvent) => Events.Enqueue(logEvent);

        /// <summary>Ligne de journal de requête de CE chemin : (code HTTP journalisé, niveau).</summary>
        public (int Status, LogEventLevel Level) RequestLine(string path)
        {
            var line = Events.Single(e =>
                e.Properties.TryGetValue("RequestPath", out var p) && p is ScalarValue { Value: string s } && s == path
                && e.Properties.ContainsKey("StatusCode"));
            return ((int)((ScalarValue)line.Properties["StatusCode"]).Value!, line.Level);
        }
    }

    private (HttpClient Client, CollectingSink Sink) NewClient()
    {
        var sink = new CollectingSink();
        var factory = _factory.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddSingleton<ILogEventSink>(sink)));
        return (factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false }), sink);
    }

    [Fact]
    public async Task A_Failed_Login_Is_Logged_With_Its_Real_401_Status_Not_As_A_Server_Error()
    {
        var (client, sink) = NewClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = AuthApiFactory.DirecteurEmail,
            password = "mauvais-mot-de-passe"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var (status, level) = sink.RequestLine("/api/v1/auth/login");
        status.Should().Be(401, "le journal doit refléter le code réellement renvoyé");
        level.Should().NotBe(LogEventLevel.Error, "un mauvais mot de passe n'est pas une panne serveur");
    }

    [Fact]
    public async Task A_Not_Found_Is_Logged_As_404_Not_As_500()
    {
        var (client, sink) = NewClient();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = AuthApiFactory.DirecteurEmail,
            password = AuthApiFactory.DirecteurPassword
        });
        var token = (await login.Content.ReadFromJsonAsync<Tokens>())!.AccessToken;
        var unknown = Guid.NewGuid();

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/students/{unknown}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        sink.RequestLine($"/api/v1/students/{unknown}").Status.Should().Be(404);
    }

    [Fact]
    public async Task On_A_Database_Missing_The_ProfileEtablissement_Column_The_Real_500_Stays_Logged_As_500_And_Logins_Stay_401()
    {
        await _factory.ExecuteOwnerSqlAsync("""ALTER TABLE school_settings DROP COLUMN "ProfileEtablissement";""");
        try
        {
            var (client, sink) = NewClient();

            var bad = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = AuthApiFactory.DirecteurEmail, password = "mauvais" });
            bad.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

            var ok = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = AuthApiFactory.DirecteurEmail, password = AuthApiFactory.DirecteurPassword });
            var token = (await ok.Content.ReadFromJsonAsync<Tokens>())!.AccessToken;

            var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/schools/current/settings");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var settings = await client.SendAsync(request);

            settings.StatusCode.Should().Be(HttpStatusCode.InternalServerError, "lire school_settings sur une base non migrée est une vraie panne");
            var settingsLine = sink.RequestLine("/api/v1/schools/current/settings");
            settingsLine.Status.Should().Be(500);
            settingsLine.Level.Should().Be(LogEventLevel.Error, "un vrai 500 doit rester visible comme une erreur");

            // Les deux journaux « login » : le mauvais mot de passe (401) ne doit PAS être compté comme une panne.
            sink.Events.Where(e => e.Properties.TryGetValue("RequestPath", out var p) && p is ScalarValue { Value: "/api/v1/auth/login" }
                                   && e.Properties.ContainsKey("StatusCode"))
                .Select(e => (int)((ScalarValue)e.Properties["StatusCode"]).Value!)
                .Should().BeEquivalentTo(new[] { 401, 200 });
        }
        finally
        {
            await _factory.ExecuteOwnerSqlAsync("""ALTER TABLE school_settings ADD COLUMN IF NOT EXISTS "ProfileEtablissement" character varying(30);""");
        }
    }

    private record Tokens(string AccessToken, int ExpiresIn);
}
