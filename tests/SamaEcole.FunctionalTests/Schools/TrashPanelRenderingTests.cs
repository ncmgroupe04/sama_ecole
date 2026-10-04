using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using SamaEcole.FunctionalTests.Common;
using Xunit;

namespace SamaEcole.FunctionalTests.Schools;

/// <summary>
/// Corbeille et restauration (conception soft delete 2026-10-01 §3.2) : le partial <c>_TrashPanel</c> est résolu et
/// rendu à l'exécution sur les écrans concernés, et le script partagé est chargé. Une vue Razor qui référence un
/// partial inexistant ne casse qu'à l'affichage de la page — ce test le rattrape. Le comportement du panneau
/// (chargement, restauration, conflit) est couvert côté JS (tests/js/trash.test.mjs) et celui de l'API par les tests
/// d'intégration SoftDelete.
/// </summary>
public class TrashPanelRenderingTests : IClassFixture<AuthApiFactory>
{
    private readonly HttpClient _client;

    public TrashPanelRenderingTests(AuthApiFactory factory)
    {
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });
    }

    private async Task<string> PageAsync(string path)
    {
        var response = await _client.GetAsync(path);
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"{path} doit s'afficher");
        return await response.Content.ReadAsStringAsync();
    }

    [Theory]
    [InlineData("/infrastructures", new[] { "buildings", "rooms" })]
    [InlineData("/classes", new[] { "classrooms" })]
    [InlineData("/frais", new[] { "fee-categories" })]
    [InlineData("/parametres", new[] { "mentions", "school-years" })]
    public async Task The_Page_Renders_Its_Trash_Panels(string path, string[] kinds)
    {
        var html = await PageAsync(path);

        html.Should().Contain("/js/trash.js", "le script partagé est chargé par le layout");
        foreach (var kind in kinds)
        {
            html.Should().Contain($"trashPanel('{kind}')", $"{path} affiche la corbeille « {kind} »");
        }
    }

    [Theory]
    [InlineData("/infrastructures", "createBuildingErrors.archivedConflict")]
    [InlineData("/infrastructures", "createRoomErrors.archivedConflict")]
    [InlineData("/classes", "createErrors.archivedConflict")]
    [InlineData("/frais", "categoryErrors.archivedConflict")]
    [InlineData("/parametres", "mentionCreateErrors.archivedConflict")]
    [InlineData("/parametres", "createErrors.archivedConflict")]
    public async Task Creation_Forms_Offer_To_Restore_When_The_Identity_Is_Archived(string path, string flag)
    {
        var html = await PageAsync(path);

        html.Should().Contain(flag, "le bandeau d'erreur du formulaire propose de voir les éléments supprimés");
        html.Should().Contain("Voir dans les éléments supprimés");
    }
}
