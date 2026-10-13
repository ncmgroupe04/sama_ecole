using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using SamaEcole.Domain.Enums;
using SamaEcole.FunctionalTests.Common;
using SamaEcole.Web.Content;
using Xunit;

namespace SamaEcole.FunctionalTests.Subscriptions;

/// <summary>
/// Garde-fou : les montants PUBLIÉS sur la vitrine (/vitrine) et ceux que l'application FACTURE ne doivent jamais diverger.
/// Côté PRIVÉ, la vitrine rend la grille par tranche d'effectif d'après la MÊME source que le formulaire d'inscription et
/// que la facturation (table quota_tier_prices, éditée par le Super Admin) : ces tests prouvent que la page montre bien
/// cette grille, et qu'un prix modifié y apparaît tout de suite. Un client ne doit pas payer autre chose que ce qu'on lui
/// a affiché.
/// </summary>
public class SubscriptionPricingGridConsistencyTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    public Task InitializeAsync() => factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private record Tokens(string AccessToken, string RefreshToken, int ExpiresIn);
    private record TierDto(string Tier, int MaxStudents, int SoftLimit, decimal MonthlyXof, decimal AnnualXof,
        decimal BoardingSurchargeMonthlyXof, decimal BoardingSurchargeAnnualXof);
    private record PublicGrid(string Currency, int QuoteAboveStudents, List<TierDto> Tiers);

    /// <summary>Page vitrine décodée, espaces insécables ramenés à une espace simple (« 5 000 FCFA »).</summary>
    private async Task<string> VitrineAsync()
    {
        var response = await _client.GetAsync("/vitrine");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        return Regex.Replace(html, "[  ]", " ");
    }

    private static string Xof(decimal amount) =>
        amount.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("fr-FR")).Replace(' ', ' ').Replace(' ', ' ') + " FCFA";

    private async Task<string> SuperAdminTokenAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = AuthApiFactory.SuperAdminEmail, password = AuthApiFactory.SuperAdminPassword });
        return (await response.Content.ReadFromJsonAsync<Tokens>())!.AccessToken;
    }

    [Fact]
    public async Task The_Private_Showcase_Shows_Exactly_The_Grid_That_The_Public_Api_Serves_And_The_Server_Bills()
    {
        var grid = (await _client.GetFromJsonAsync<PublicGrid>("/api/v1/public/pricing-grid"))!;
        var page = await VitrineAsync();

        grid.Tiers.Should().HaveCount(4);
        foreach (var tier in grid.Tiers)
        {
            page.Should().Contain($"{Xof(tier.MonthlyXof)} / mois", $"la tranche {tier.Tier} doit afficher son tarif mensuel");
            page.Should().Contain($"{Xof(tier.AnnualXof)} / an", $"la tranche {tier.Tier} doit afficher son tarif annuel");
            page.Should().Contain($"/inscription?type=prive&amp;tranche={tier.Tier}".Replace("&amp;", "&"),
                $"la carte {tier.Tier} doit préremplir le formulaire d'inscription");
        }
    }

    [Fact]
    public async Task The_Showcase_Offers_The_Grand_Groupe_Above_The_Last_Standard_Tier()
    {
        var page = await VitrineAsync();

        page.Should().Contain("Plus de 400 élèves").And.Contain("Sur devis");
    }

    [Fact]
    public async Task The_Old_Per_Cycle_Private_Prices_Are_No_Longer_Displayed()
    {
        var page = await VitrineAsync();

        // Libellés propres à l'ancienne grille privée (« Primaire uniquement » existe encore côté PUBLIC, par élève).
        page.Should().NotContain("Formules bicycles");
        page.Should().NotContain("Grands complexes");
        page.Should().NotContain("Deux cycles, une seule plateforme");
        page.Should().NotContain("1,2 million FCFA");
    }

    [Fact]
    public async Task A_Price_Changed_By_The_Super_Admin_Appears_On_The_Showcase_Immediately()
    {
        var token = await SuperAdminTokenAsync();

        async Task<HttpResponseMessage> Put(int monthly, int annual)
        {
            var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/admin/pricing-grid/Tier1_150")
            {
                Content = JsonContent.Create(new { monthlyXof = monthly, annualXof = annual, boardingSurchargeMonthlyXof = 0, boardingSurchargeAnnualXof = 0 })
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return await _client.SendAsync(request);
        }

        (await Put(11_000, 110_000)).StatusCode.Should().Be(HttpStatusCode.OK);
        try
        {
            var page = await VitrineAsync();

            page.Should().Contain("11 000 FCFA / mois").And.Contain("110 000 FCFA / an");
        }
        finally
        {
            await Put(10_000, 100_000);
        }
    }

    [Fact]
    public async Task A_Boarding_Surcharge_Is_Announced_Only_Once_The_Super_Admin_Sets_One()
    {
        (await VitrineAsync()).Should().NotContain("majoration");

        var token = await SuperAdminTokenAsync();
        var tiers = new[] { ("Tier_100", 5_000, 50_000), ("Tier1_150", 10_000, 100_000), ("Tier_250", 15_000, 150_000), ("Tier2_400", 25_000, 250_000) };

        async Task SetAll(int surchargeMonthly, int surchargeAnnual)
        {
            foreach (var (tier, monthly, annual) in tiers)
            {
                var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/admin/pricing-grid/{tier}")
                {
                    Content = JsonContent.Create(new
                    {
                        monthlyXof = monthly, annualXof = annual,
                        boardingSurchargeMonthlyXof = surchargeMonthly, boardingSurchargeAnnualXof = surchargeAnnual
                    })
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                (await _client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.OK);
            }
        }

        await SetAll(2_000, 20_000);
        try
        {
            (await VitrineAsync()).Should().Contain("majoration de 2 000 FCFA par mois");
        }
        finally
        {
            await SetAll(0, 0);
        }
    }

    [Fact]
    public void The_Public_Offers_Are_Per_Student_And_Have_No_Fixed_Billed_Amount()
    {
        var publicAudience = MarketingCatalog.Pricing.Single(a => a.Key == "public");

        publicAudience.Groups.SelectMany(g => g.Offers)
            .Should().OnlyContain(o => o.Unit.Contains("par élève") && o.Tiers.Count == 1);
    }

    [Fact]
    public void The_Private_Catalog_Holds_No_Amount_Any_More_Because_They_Come_From_The_Grid_In_The_Database()
    {
        var privateAudience = MarketingCatalog.Pricing.Single(a => a.Key == "prive");

        privateAudience.Groups.Should().BeEmpty();
    }
}
