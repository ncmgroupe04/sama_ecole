using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using SamaEcole.FunctionalTests.Common;
using Xunit;

namespace SamaEcole.FunctionalTests.Onboarding;

/// <summary>Pages de l'Onboarding : la nouvelle adresse sert l'assistant, l'ancienne y redirige.</summary>
public class OnboardingPagesTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient(
        new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });

    [Fact]
    public async Task The_Select_Profile_Page_Serves_The_Wizard_And_Its_Script()
    {
        var response = await _client.GetAsync("/onboarding/select-profile");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("onboardingWizard()").And.Contain("/js/onboarding.js");
    }

    [Fact]
    public async Task The_Legacy_Onboarding_Address_Redirects_To_The_New_One()
    {
        var response = await _client.GetAsync("/onboarding");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be("/onboarding/select-profile");
    }

    [Fact]
    public async Task The_Obsolete_Onboarding_Guard_Script_Is_No_Longer_Loaded_By_The_Layout()
    {
        // L'ancien garde (profil NULL → /onboarding) renverrait en boucle les écoles antérieures, qui n'ont pas
        // de profil mais une souscription Active. Seul api.js redirige désormais, sur ONBOARDING_REQUIRED.
        var html = await (await _client.GetAsync("/eleves")).Content.ReadAsStringAsync();

        html.Should().NotContain("onboarding-guard.js");
    }
}
