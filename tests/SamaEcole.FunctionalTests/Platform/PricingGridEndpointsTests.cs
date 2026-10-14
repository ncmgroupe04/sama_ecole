using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Xunit;

namespace SamaEcole.FunctionalTests.Platform;

/// <summary>
/// Grille tarifaire par tranche d'effectif : lecture publique anonyme, édition réservée au Super Admin,
/// et prise en compte immédiate d'un nouveau prix. La grille de départ (5 000 / 10 000 / 15 000 / 25 000
/// FCFA par mois, annuel = 10 mois) est semée par la migration AddQuotaTierPrices.
/// </summary>
public class PricingGridEndpointsTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    public Task InitializeAsync() => factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private record Tokens(string AccessToken, string RefreshToken, int ExpiresIn);
    private record TierDto(string Tier, int MaxStudents, int SoftLimit, decimal MonthlyXof, decimal AnnualXof,
        decimal BoardingSurchargeMonthlyXof, decimal BoardingSurchargeAnnualXof);
    private record PublicGrid(string Currency, int QuoteAboveStudents, List<TierDto> Tiers);
    private record AdminTier(TierDto Price, DateTimeOffset? UpdatedAt, string? UpdatedBy);

    private async Task<string> LoginAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!.AccessToken;
    }

    private Task<string> SuperAdminTokenAsync() => LoginAsync(AuthApiFactory.SuperAdminEmail, AuthApiFactory.SuperAdminPassword);

    private Task<HttpResponseMessage> PutAsync(string? token, string tier, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/admin/pricing-grid/{tier}") { Content = JsonContent.Create(body) };
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return _client.SendAsync(request);
    }

    [Fact]
    public async Task The_Public_Grid_Is_Readable_Anonymously_With_The_Starting_Prices()
    {
        var response = await _client.GetAsync("/api/v1/public/pricing-grid");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var grid = (await response.Content.ReadFromJsonAsync<PublicGrid>())!;
        grid.Currency.Should().Be("XOF");
        grid.QuoteAboveStudents.Should().Be(400);
        grid.Tiers.Select(t => (t.Tier, t.MaxStudents, t.SoftLimit, t.MonthlyXof, t.AnnualXof)).Should().Equal(
            ("Tier_100", 100, 110, 5_000m, 50_000m),
            ("Tier1_150", 150, 160, 10_000m, 100_000m),
            ("Tier_250", 250, 260, 15_000m, 150_000m),
            ("Tier2_400", 400, 420, 25_000m, 250_000m));
    }

    [Fact]
    public async Task Editing_The_Grid_Is_Refused_To_Anonymous_Callers_And_To_A_Director()
    {
        var body = new { monthlyXof = 6_000, annualXof = 60_000, boardingSurchargeMonthlyXof = 0, boardingSurchargeAnnualXof = 0 };

        (await PutAsync(null, "Tier_100", body)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var director = await LoginAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);
        (await PutAsync(director, "Tier_100", body)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var adminList = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/pricing-grid");
        adminList.Headers.Authorization = new AuthenticationHeaderValue("Bearer", director);
        (await _client.SendAsync(adminList)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_Super_Admin_Can_Change_A_Price_And_The_Public_Grid_Follows_Immediately()
    {
        var token = await SuperAdminTokenAsync();

        var update = await PutAsync(token, "Tier_100",
            new { monthlyXof = 6_000, annualXof = 60_000, boardingSurchargeMonthlyXof = 1_000, boardingSurchargeAnnualXof = 10_000 });

        update.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = (await update.Content.ReadFromJsonAsync<AdminTier>())!;
        updated.Price.MonthlyXof.Should().Be(6_000);
        updated.UpdatedAt.Should().NotBeNull();
        updated.UpdatedBy.Should().NotBeNullOrEmpty();

        var grid = (await _client.GetFromJsonAsync<PublicGrid>("/api/v1/public/pricing-grid"))!;
        var tier = grid.Tiers.Single(t => t.Tier == "Tier_100");
        (tier.MonthlyXof, tier.AnnualXof, tier.BoardingSurchargeMonthlyXof, tier.BoardingSurchargeAnnualXof)
            .Should().Be((6_000m, 60_000m, 1_000m, 10_000m));

        // Les autres tranches ne bougent pas.
        grid.Tiers.Single(t => t.Tier == "Tier2_400").MonthlyXof.Should().Be(25_000);

        // Remet la grille de départ : la base du fixture est partagée par les tests de cette classe.
        (await PutAsync(token, "Tier_100",
            new { monthlyXof = 5_000, annualXof = 50_000, boardingSurchargeMonthlyXof = 0, boardingSurchargeAnnualXof = 0 }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("Tier3_800")]
    [InlineData("Tier4_Custom")]
    public async Task A_Tier_On_Quote_Cannot_Be_Priced(string tier)
    {
        var token = await SuperAdminTokenAsync();

        var response = await PutAsync(token, tier,
            new { monthlyXof = 40_000, annualXof = 400_000, boardingSurchargeMonthlyXof = 0, boardingSurchargeAnnualXof = 0 });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_Yearly_Price_Above_Twelve_Monthly_Payments_Is_Refused_And_Nothing_Changes()
    {
        var token = await SuperAdminTokenAsync();

        var response = await PutAsync(token, "Tier_250",
            new { monthlyXof = 15_000, annualXof = 200_000, boardingSurchargeMonthlyXof = 0, boardingSurchargeAnnualXof = 0 });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var grid = (await _client.GetFromJsonAsync<PublicGrid>("/api/v1/public/pricing-grid"))!;
        grid.Tiers.Single(t => t.Tier == "Tier_250").AnnualXof.Should().Be(150_000);
    }

    [Fact]
    public async Task The_Admin_List_Exposes_The_Four_Priced_Tiers_To_The_Super_Admin()
    {
        var token = await SuperAdminTokenAsync();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/pricing-grid");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var tiers = (await response.Content.ReadFromJsonAsync<List<AdminTier>>())!;
        tiers.Select(t => t.Price.Tier).Should().Equal("Tier_100", "Tier1_150", "Tier_250", "Tier2_400");
    }
}
