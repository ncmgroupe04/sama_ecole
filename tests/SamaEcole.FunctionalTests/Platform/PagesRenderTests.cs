using System.Net;
using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using SamaEcole.FunctionalTests.Common;
using SamaEcole.Web.Controllers;
using Xunit;

namespace SamaEcole.FunctionalTests.Platform;

/// <summary>
/// Chaque page servie par <see cref="PagesController"/> doit se rendre en HTML. Garde-fou contre un
/// piège déjà rencontré en production (/enseignants et /cahier-de-texte en 500) : toutes les vues sont
/// servies par PagesController, donc <c>Html.PartialAsync("_Nom")</c> ne cherche que dans Views/Pages et
/// Views/Shared — une partielle rangée dans Views/Teachers/ est introuvable. Le rendu échoue alors
/// APRÈS l'action, et ExceptionHandlingMiddleware le convertit en JSON INTERNAL_ERROR : aucun test
/// d'API ne le voit, seule la requête de la page elle-même.
/// </summary>
public class PagesRenderTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient(
        new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });

    public static IEnumerable<object[]> PageRoutes() =>
        typeof(PagesController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SelectMany(m => m.GetCustomAttributes<HttpGetAttribute>())
            .Select(a => a.Template!)
            .Where(t => !t.Contains('{'))
            .Distinct()
            .Select(t => new object[] { t });

    [Theory]
    [MemberData(nameof(PageRoutes))]
    public async Task Page_renders_html_and_never_a_500(string route)
    {
        var response = await _client.GetAsync(route);

        var body = response.StatusCode == HttpStatusCode.InternalServerError
            ? await response.Content.ReadAsStringAsync()
            : string.Empty;
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError, "GET {0} a renvoyé : {1}", route, body);

        if (response.StatusCode == HttpStatusCode.OK)
            response.Content.Headers.ContentType?.MediaType.Should().Be("text/html", "GET {0}", route);
    }
}
