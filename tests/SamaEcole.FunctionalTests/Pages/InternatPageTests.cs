using System.Net;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using SamaEcole.FunctionalTests.Common;
using Xunit;

namespace SamaEcole.FunctionalTests.Pages;

/// <summary>
/// Gabarit de /internat (lot G, tâche 5) : conteneur à onglets, onglet Occupation conservé, onglet Pavillons, corbeilles. Les gabarits sont
/// anonymes : on contrôle le HTML servi et la présence réelle des scripts ; le comportement des composants est testé en JavaScript
/// (tests/js/internat-*.test.mjs) et le rendu relève de la recette navigateur.
/// </summary>
public class InternatPageTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient(
        new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });

    private async Task<string> InternatHtmlAsync()
    {
        var response = await _client.GetAsync("/internat");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync();
    }

    [Fact]
    public async Task The_Page_Is_A_Tab_Container_That_Keeps_The_Historical_Occupation_Screen()
    {
        var html = await InternatHtmlAsync();

        html.Should().Contain("x-data=\"internatTabs()\"");
        html.Should().Contain("x-data=\"occupationTab()\"", "l'onglet Occupation (tableau de bord et affectation sur /api/v1/boarding)");
        html.Should().NotContain("internatPage()", "l'ancien composant, fondé sur les routes héritées, a disparu");
        html.Should().Contain("x-data=\"dormitoriesTab()\"", "l'onglet Pavillons");
        html.Should().Contain("tab === 'occupation'").And.Contain("tab === 'pavillons'");
    }

    [Theory]
    [InlineData("dormitories")]
    [InlineData("dormitory-rooms")]
    [InlineData("beds")]
    public async Task The_Pavilions_Tab_Embeds_The_Three_Trash_Panels(string kind)
    {
        (await InternatHtmlAsync()).Should().Contain($"trashPanel('{kind}')");
    }

    [Fact]
    public async Task The_Scripts_Are_Loaded_In_Dependency_Order_And_Are_Really_Served()
    {
        var html = await InternatHtmlAsync();

        var scripts = Regex.Matches(html, "<script src=\"(/js/[^\"?]+)").Select(m => m.Groups[1].Value).ToList();
        var helpers = scripts.IndexOf("/js/internat-helpers.js");
        var dormitories = scripts.IndexOf("/js/internat-dormitories.js");
        var occupation = scripts.IndexOf("/js/internat-occupation.js");
        var screen = scripts.IndexOf("/js/internat.js");

        helpers.Should().BeGreaterThanOrEqualTo(0);
        dormitories.Should().BeGreaterThan(helpers, "le composant utilise les helpers");
        occupation.Should().BeGreaterThan(helpers, "le composant utilise les helpers");
        screen.Should().BeGreaterThan(Math.Max(dormitories, occupation));
        scripts.IndexOf("/js/trash.js").Should().BeGreaterThanOrEqualTo(0, "les panneaux de corbeille sont chargés par le layout");

        foreach (var script in new[] { "/js/internat-helpers.js", "/js/internat-dormitories.js", "/js/internat-occupation.js", "/js/internat.js", "/js/trash.js" })
        {
            var response = await _client.GetAsync(script);
            response.StatusCode.Should().Be(HttpStatusCode.OK, script);
            (await response.Content.ReadAsStringAsync()).Should().NotBeNullOrWhiteSpace(script);
        }
    }

    [Fact]
    public async Task The_Page_Still_Declares_The_Internat_Module()
    {
        (await InternatHtmlAsync()).Should().Contain("<meta name=\"required-module\" content=\"Internat\"");
    }
}
