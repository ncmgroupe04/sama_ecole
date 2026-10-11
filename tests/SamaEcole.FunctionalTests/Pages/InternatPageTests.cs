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

    [Fact]
    public async Task The_Boarders_Tab_Is_Embedded_By_Its_Full_Path_And_Hides_The_Medical_Section_From_The_Dom_Behind_An_X_If()
    {
        var html = await InternatHtmlAsync();

        html.Should().Contain("tab === 'pensionnaires'");
        html.Should().Contain("x-data=\"boardersTab()\"", "l'onglet Pensionnaires");

        // La fiche médicale doit être derrière un x-if (absente du DOM), jamais un x-show (présente mais cachée) : un x-show laisserait la
        // donnée de santé dans la page servie au Secrétariat.
        html.Should().Contain("<template x-if=\"showMedical\">");
        html.Should().NotMatchRegex("x-show=\"showMedical\"");
        html.Should().NotContain("medicalText\" x-show");
    }

    [Fact]
    public async Task The_Leaves_Tab_Is_Embedded_And_Never_Blocks_On_An_Unlisted_Companion()
    {
        var html = await InternatHtmlAsync();

        html.Should().Contain("tab === 'sorties'");
        html.Should().Contain("x-data=\"leavesTab()\"", "l'onglet Sorties");
        html.Should().Contain("companionUnlisted", "avertissement d'accompagnateur hors liste");
        html.Should().Contain("Non habilité", "pastille du registre");
        html.Should().Contain("closeDeclare()").And.Contain("closeReturn()", "déclaration et retour passent par des modales");
    }

    [Fact]
    public async Task The_Attendance_Tab_Is_Embedded_Locks_Boarders_On_Leave_And_Offers_The_Register_Pdf()
    {
        var html = await InternatHtmlAsync();

        html.Should().Contain("tab === 'pointage'");
        html.Should().Contain("x-data=\"attendanceTab()\"", "l'onglet Pointage");
        html.Should().Contain(":disabled=\"line.onLeave\"", "la case « Présent » est verrouillée pour un pensionnaire en permission");
        html.Should().Contain("openRegisterPdf()", "bouton « Registre PDF »");
        html.Should().Contain("rejectedLines", "les refus du serveur sont détaillés ligne par ligne");
    }

    [Fact]
    public async Task Every_Tab_Root_Listens_To_The_Tab_Shown_Announcement_So_It_Reloads_When_Opened()
    {
        var html = await InternatHtmlAsync();

        // Sans cette écoute, un onglet ne chargerait ses données qu'à l'ouverture de la page : une sortie déclarée dans « Sorties » n'apparaîtrait pas
        // (verrou « absent ») dans « Pointage » avant un rechargement (constat de la recette navigateur).
        foreach (var component in new[] { "occupationTab", "dormitoriesTab", "boardersTab", "leavesTab", "attendanceTab" })
        {
            html.Should().Contain($"x-data=\"{component}()\" x-on:internat-tab-shown.window=\"onTabShown($event.detail.key)\"", component);
        }
    }

    [Fact]
    public async Task The_Five_Tabs_Are_Declared_In_Order()
    {
        var html = await InternatHtmlAsync();

        var panels = new[] { "occupation", "pavillons", "pensionnaires", "sorties", "pointage" }
            .Select(tab => html.IndexOf($"tab === '{tab}'", StringComparison.Ordinal)).ToList();

        panels.Should().OnlyContain(index => index >= 0, "chaque onglet a son panneau");
        panels.Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task The_Boarder_Sheet_Pdf_Goes_Through_The_Shared_Preview_Modal_Loaded_By_The_Layout()
    {
        var html = await InternatHtmlAsync();

        html.Should().Contain("openBoarderPdf()", "bouton « Fiche PDF » de la fiche");
        html.Should().Contain("closePdfPreview()", "la modale d'aperçu PDF commune est incluse");
        html.Should().Contain("/js/pdf-preview.js", "le moteur d'aperçu est chargé par le layout");
        html.Should().NotContain("/boarders/${", "aucun lien <a href> direct vers une route PDF : elle exige l'en-tête Authorization");
        html.Should().NotMatchRegex("<a[^>]+href=\"[^\"]*/pdf");
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
        var boarders = scripts.IndexOf("/js/internat-boarders.js");
        var leaves = scripts.IndexOf("/js/internat-leaves.js");
        var attendance = scripts.IndexOf("/js/internat-attendance.js");
        var screen = scripts.IndexOf("/js/internat.js");

        helpers.Should().BeGreaterThanOrEqualTo(0);
        dormitories.Should().BeGreaterThan(helpers, "le composant utilise les helpers");
        occupation.Should().BeGreaterThan(helpers, "le composant utilise les helpers");
        boarders.Should().BeGreaterThan(helpers, "le composant utilise les helpers");
        leaves.Should().BeGreaterThan(helpers, "le composant utilise les helpers");
        attendance.Should().BeGreaterThan(helpers, "le composant utilise les helpers");
        screen.Should().BeGreaterThan(new[] { dormitories, occupation, boarders, leaves, attendance }.Max());
        scripts.IndexOf("/js/trash.js").Should().BeGreaterThanOrEqualTo(0, "les panneaux de corbeille sont chargés par le layout");

        foreach (var script in new[] { "/js/internat-helpers.js", "/js/internat-dormitories.js", "/js/internat-occupation.js", "/js/internat-boarders.js", "/js/internat-leaves.js", "/js/internat-attendance.js", "/js/internat.js", "/js/trash.js" })
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
