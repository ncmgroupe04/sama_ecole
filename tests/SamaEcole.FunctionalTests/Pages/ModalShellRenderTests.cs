using System.Net;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using SamaEcole.FunctionalTests.Common;
using Xunit;

namespace SamaEcole.FunctionalTests.Pages;

/// <summary>
/// <c>&lt;modal-shell&gt;</c> : le titre, le sous-titre et le pied déclarés par <c>&lt;modal-title&gt;</c>, <c>&lt;modal-subtitle&gt;</c> et
/// <c>&lt;modal-footer&gt;</c> doivent apparaître dans l'en-tête de la fenêtre.
///
/// Régression d'origine : ces balises écrivaient dans <c>context.Items</c>, un dictionnaire en copie à l'écriture dont le parent ne voit
/// pas les écritures de ses enfants. Résultat : une quarantaine de fenêtres se rendaient SANS titre (« Supprimer le bâtiment »,
/// « Nouveau décaissement »…), sans que rien ne le signale. Les gabarits sont anonymes : on contrôle le HTML servi.
/// </summary>
public class ModalShellRenderTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient(
        new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });

    private async Task<string> HtmlAsync(string path)
    {
        var response = await _client.GetAsync(path);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync();
    }

    [Fact]
    public async Task A_Title_Declared_With_Modal_Title_Is_Rendered_In_The_Window_Header()
    {
        // Direct enfant de <modal-shell> (Buildings : la fenêtre de suppression guidée partagée, titre « Supprimer »).
        var html = await HtmlAsync("/infrastructures");

        Regex.IsMatch(html, @"<h2[^>]*>\s*Supprimer\s*</h2>").Should().BeTrue("le titre de la fenêtre de suppression doit s'afficher");
    }

    [Fact]
    public async Task A_Title_Declared_Inside_A_Form_Of_The_Window_Body_Is_Rendered_Too()
    {
        // <modal-title> posé DANS un <form> du corps (Fees : « Nouveau Décaissement ») : l'ancienne contrainte ParentTag l'écartait.
        var html = await HtmlAsync("/frais");

        Regex.IsMatch(html, @"<h2[^>]*>\s*Nouveau Décaissement\s*</h2>").Should().BeTrue();
        html.Should().NotContain("<modal-title>", "la balise ne doit jamais rester telle quelle dans le HTML servi");
    }

    [Fact]
    public async Task A_Subtitle_Declared_With_Modal_Subtitle_Is_Rendered_Under_The_Title()
    {
        var html = await HtmlAsync("/infrastructures");

        html.Should().Contain("Nom et description du bâtiment physique.", "le sous-titre de la fenêtre de création d'un bâtiment");
        html.Should().NotContain("<modal-subtitle>");
    }

    [Fact]
    public async Task The_Superadmin_Windows_Use_The_Dark_Variant_Of_The_Common_Window_And_Keep_Their_Title()
    {
        var html = await HtmlAsync("/admin/etablissements");

        Regex.IsMatch(html, @"<h2[^>]*>\s*Infiltrer un établissement\s*</h2>").Should().BeTrue("la fenêtre « Infiltrer » garde son titre");
        html.Should().Contain("bg-zinc-900/85").And.Contain("backdrop-blur-xl", "variante sombre du verre dépoli commun");
        html.Should().NotContain("fixed inset-0 z-40 flex items-center justify-center bg-black/70", "plus de fenêtre artisanale dans la console");
    }

    [Fact]
    public async Task The_Light_Window_Keeps_Its_Light_Palette()
    {
        var html = await HtmlAsync("/infrastructures");

        html.Should().Contain("bg-white/85").And.Contain("border-white/60");
        html.Should().NotContain("bg-zinc-900/85", "la palette sombre ne doit pas fuiter dans l'application claire");
    }
}
