using System.Net;
using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Testing;
using SamaEcole.Domain.Enums;
using SamaEcole.FunctionalTests.Common;
using SamaEcole.Web.Authorization;
using SamaEcole.Web.Controllers;
using Xunit;

namespace SamaEcole.FunctionalTests.Pages;

/// <summary>
/// Module requis d'une page (lot G, tâche 2). Les gabarits sont anonymes (le navigateur n'envoie pas d'en-tête
/// <c>Authorization</c> en naviguant) : le module est donc déclaré dans le HTML (<c>&lt;meta name="required-module"&gt;</c>)
/// et la décision se prend côté client (module-route-guard.js). La vraie barrière reste l'API (<c>[RequireModule]</c>).
///
/// DÉCISION G-D1 : les quatre écrans Internat/Daara relèvent du module INTERNAT, pas Coran. Ils appellent
/// <c>/api/v1/internat/*</c> (<c>InternatController</c>, <c>[RequireModule(Internat)]</c>) ; <c>/api/v1/quran</c> (module
/// Coran) n'a aucun consommateur dans le front. La spec §8.1 disait l'inverse : elle est corrigée.
/// </summary>
public class PageModuleTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    private static readonly string[] InternatPageActions = ["Internat", "Halqa", "Instructors", "QuranProgress"];

    private readonly HttpClient _client = factory.CreateClient(
        new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });

    private static SchoolModule? PageModuleOf(MethodInfo action) => action.GetCustomAttribute<PageModuleAttribute>()?.Module;

    // ------------------------------------------------------------------ Le HTML porte la déclaration

    [Theory]
    [InlineData("/internat")]
    [InlineData("/halqa")]
    [InlineData("/oustaz")]
    [InlineData("/suivi-coranique")]
    public async Task The_Internat_And_Daara_Pages_Declare_The_Internat_Module(string url)
    {
        var response = await _client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.OK, url);
        (await response.Content.ReadAsStringAsync()).Should().Contain("<meta name=\"required-module\" content=\"Internat\"");
    }

    [Theory]
    [InlineData("/eleves")]
    [InlineData("/inventaire")]
    [InlineData("/classes")]
    public async Task Pages_Of_Other_Modules_Declare_No_Required_Module(string url)
    {
        var response = await _client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.OK, url);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("required-module", url);
    }

    [Theory]
    [InlineData("/internat")]
    [InlineData("/eleves")]
    public async Task The_Layout_Loads_The_Guard_After_The_Scripts_It_Depends_On(string url)
    {
        var html = await (await _client.GetAsync(url)).Content.ReadAsStringAsync();

        var guard = html.IndexOf("/js/module-route-guard.js", StringComparison.Ordinal);
        guard.Should().BeGreaterThan(0, url);
        foreach (var dependency in new[] { "/js/auth.js", "/js/api.js", "/js/ui-components.js" })
        {
            html.IndexOf(dependency, StringComparison.Ordinal).Should().BeInRange(1, guard - 1, $"{dependency} doit précéder la garde ({url})");
        }

        // Le script de la garde est classique (synchrone) : il s'exécute avant Alpine (defer) et enregistre son écouteur à temps.
        html.IndexOf("/js/vendor/alpine.min.js", StringComparison.Ordinal).Should().BeGreaterThan(guard);
    }

    // ------------------------------------------------------------------ Verrou de la décision G-D1

    [Fact]
    public void The_Four_Internat_And_Daara_Pages_Carry_Exactly_PageModule_Internat()
    {
        foreach (var name in InternatPageActions)
        {
            var action = typeof(PagesController).GetMethod(name);

            action.Should().NotBeNull($"PagesController.{name} doit exister");
            PageModuleOf(action!).Should().Be(
                SchoolModule.Internat,
                $"{name} appelle /api/v1/internat/* (module Internat), pas /api/v1/quran (module Coran, sans consommateur) — décision G-D1");
        }
    }

    [Fact]
    public void No_Other_Page_Action_Declares_A_Module_And_None_Declares_Coran()
    {
        var declared = typeof(PagesController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(m => (m.Name, Module: PageModuleOf(m)))
            .Where(x => x.Module is not null)
            .ToList();

        declared.Select(x => x.Name).Should().BeEquivalentTo(InternatPageActions, "seules ces quatre pages déclarent un module");
        declared.Should().NotContain(x => x.Module == SchoolModule.Coran, "aucun écran ne consomme encore /api/v1/quran");
    }

    [Fact]
    public void The_Api_Behind_These_Pages_Is_Guarded_By_The_Internat_Module()
    {
        var policy = typeof(InternatController).GetCustomAttributes<RequireModuleAttribute>().Single().Policy;

        policy.Should().Be(ModulePolicies.NameFor(SchoolModule.Internat));
        typeof(BoardingController).GetCustomAttributes<RequireModuleAttribute>().Single().Policy
            .Should().Be(ModulePolicies.NameFor(SchoolModule.Internat));
    }

    [Fact]
    public void The_Page_Module_Attribute_Is_Not_An_Authorization_Barrier()
    {
        // La redirection est de l'ergonomie, jamais une barrière d'accès : l'attribut ne doit pas être un AuthorizeAttribute.
        typeof(PageModuleAttribute).Should().NotBeAssignableTo<IAuthorizeData>();
    }
}
