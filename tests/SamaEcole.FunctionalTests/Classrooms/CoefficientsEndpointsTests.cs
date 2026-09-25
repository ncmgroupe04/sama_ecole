using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace SamaEcole.FunctionalTests.Classrooms;

/// <summary>
/// Évolution N°4 — /coefficients et la série de la classe, de bout en bout contre un vrai PostgreSQL :
/// routes, rôles (lecture Directeur + Secrétariat, écriture Directeur seul), codes 200/204/403/409/422.
/// </summary>
public class CoefficientsEndpointsTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public CoefficientsEndpointsTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public Task InitializeAsync() => _factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private record Tokens(string AccessToken, int ExpiresIn);
    private record ClassroomCreated(Guid Id, string Name, string Level, int Capacity, string? Cycle, bool IsAccelerated, string? TargetLevel, string? Series);
    private record SubjectCreated(Guid Id);
    private record OverrideResult(Guid Id, Guid SubjectId, string? Series, Guid? ClassroomId, decimal Coefficient, uint RowVersion);
    private record GridRow(Guid SubjectId, string SubjectName, decimal BaseCoefficient, Guid? OverrideId, decimal? OverrideCoefficient,
        uint? RowVersion, decimal EffectiveCoefficient, string Source);
    private record Grid(Guid SchoolYearId, string SchoolYearLabel, bool YearHasGrades, List<GridRow> Rows);
    private record SeriesItem(string Code, string Label);

    private async Task<string> TokenAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!.AccessToken;
    }

    private Task<string> DirecteurAsync() => TokenAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);
    private Task<string> SecretaireAsync() => TokenAsync(AuthApiFactory.SecretaireEmail, AuthApiFactory.SecretairePassword);
    private Task<string> EnseignantAsync() => TokenAsync(AuthApiFactory.EnseignantEmail, AuthApiFactory.EnseignantPassword);

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await _client.SendAsync(request);
    }

    /// <summary>Année active, une classe Lycée série S2 et deux matières de lycée.</summary>
    private async Task<(ClassroomCreated Classroom, Guid Maths, Guid Francais)> SeedAsync(string directeur)
    {
        var year = await SendAsync(HttpMethod.Post, "/api/v1/school-years", directeur,
            new { label = "2026-2027", startDate = "2026-09-01", endDate = "2027-06-30" });
        year.StatusCode.Should().Be(HttpStatusCode.Created);

        var classroom = await SendAsync(HttpMethod.Post, "/api/v1/classrooms", directeur,
            new { name = "Terminale S2 A", level = "Lycée", capacity = 40, series = "s2" });
        classroom.StatusCode.Should().Be(HttpStatusCode.Created);

        var maths = await SendAsync(HttpMethod.Post, "/api/v1/subjects", directeur,
            new { name = "Mathématiques", level = "Lycée", coefficient = 4 });
        var francais = await SendAsync(HttpMethod.Post, "/api/v1/subjects", directeur,
            new { name = "Français", level = "Lycée", coefficient = 2 });

        return (
            (await classroom.Content.ReadFromJsonAsync<ClassroomCreated>())!,
            (await maths.Content.ReadFromJsonAsync<SubjectCreated>())!.Id,
            (await francais.Content.ReadFromJsonAsync<SubjectCreated>())!.Id);
    }

    [Fact]
    public async Task The_Classroom_Stores_A_Normalised_Series_And_Refuses_A_Series_Outside_The_Lycee()
    {
        var directeur = await DirecteurAsync();

        var ok = await SendAsync(HttpMethod.Post, "/api/v1/classrooms", directeur,
            new { name = "Terminale L2 A", level = "Lycée", capacity = 40, series = "l2" });
        ok.StatusCode.Should().Be(HttpStatusCode.Created);
        (await ok.Content.ReadFromJsonAsync<ClassroomCreated>())!.Series.Should().Be("L2");

        var refused = await SendAsync(HttpMethod.Post, "/api/v1/classrooms", directeur,
            new { name = "CM2 B", level = "Primaire", capacity = 40, series = "S2" });
        refused.StatusCode.Should().Be((HttpStatusCode)422);

        var unknown = await SendAsync(HttpMethod.Post, "/api/v1/classrooms", directeur,
            new { name = "Terminale X", level = "Lycée", capacity = 40, series = "S9" });
        unknown.StatusCode.Should().Be((HttpStatusCode)422);
    }

    [Fact]
    public async Task The_Director_Sets_Reads_And_Restores_A_Series_Coefficient()
    {
        var directeur = await DirecteurAsync();
        var (_, maths, _) = await SeedAsync(directeur);

        var put = await SendAsync(HttpMethod.Put, "/api/v1/coefficients", directeur,
            new { subjectId = maths, coefficient = 6, series = "S2" });
        put.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = (await put.Content.ReadFromJsonAsync<OverrideResult>())!;
        created.Coefficient.Should().Be(6m);

        var grid = await SendAsync(HttpMethod.Get, "/api/v1/coefficients?series=S2", directeur);
        grid.StatusCode.Should().Be(HttpStatusCode.OK);
        var row = (await grid.Content.ReadFromJsonAsync<Grid>())!.Rows.Single(r => r.SubjectId == maths);
        row.BaseCoefficient.Should().Be(4m);
        row.EffectiveCoefficient.Should().Be(6m);
        row.Source.Should().Be("Series");

        var restore = await SendAsync(HttpMethod.Delete,
            $"/api/v1/coefficients/{created.Id}?rowVersion={row.RowVersion}", directeur);
        restore.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var after = await SendAsync(HttpMethod.Get, "/api/v1/coefficients?series=S2", directeur);
        (await after.Content.ReadFromJsonAsync<Grid>())!.Rows.Single(r => r.SubjectId == maths)
            .EffectiveCoefficient.Should().Be(4m, "« Rétablir » ramène la valeur de la matière");
    }

    [Fact]
    public async Task The_Secretary_Can_Read_But_Never_Write_And_The_Teacher_Cannot_Even_Read()
    {
        var directeur = await DirecteurAsync();
        var (_, maths, _) = await SeedAsync(directeur);

        var secretaire = await SecretaireAsync();
        (await SendAsync(HttpMethod.Get, "/api/v1/coefficients?series=S2", secretaire)).StatusCode
            .Should().Be(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Get, "/api/v1/coefficients/catalog", secretaire)).StatusCode
            .Should().Be(HttpStatusCode.OK);

        var write = await SendAsync(HttpMethod.Put, "/api/v1/coefficients", secretaire,
            new { subjectId = maths, coefficient = 6, series = "S2" });
        write.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var carry = await SendAsync(HttpMethod.Post, "/api/v1/coefficients/carry-over", secretaire,
            new { fromSchoolYearId = Guid.NewGuid() });
        carry.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var enseignant = await EnseignantAsync();
        (await SendAsync(HttpMethod.Get, "/api/v1/coefficients?series=S2", enseignant)).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Invalid_Requests_Are_422_And_A_Stale_Row_Version_Is_409()
    {
        var directeur = await DirecteurAsync();
        var (_, maths, _) = await SeedAsync(directeur);

        (await SendAsync(HttpMethod.Put, "/api/v1/coefficients", directeur,
            new { subjectId = maths, coefficient = 0, series = "S2" })).StatusCode.Should().Be((HttpStatusCode)422);
        (await SendAsync(HttpMethod.Put, "/api/v1/coefficients", directeur,
            new { subjectId = maths, coefficient = 6, series = "S2", classroomId = Guid.NewGuid() })).StatusCode
            .Should().Be((HttpStatusCode)422, "deux portées à la fois");
        (await SendAsync(HttpMethod.Get, "/api/v1/coefficients", directeur)).StatusCode
            .Should().Be((HttpStatusCode)422, "aucune portée");

        var first = await SendAsync(HttpMethod.Put, "/api/v1/coefficients", directeur,
            new { subjectId = maths, coefficient = 6, series = "S2" });
        var created = (await first.Content.ReadFromJsonAsync<OverrideResult>())!;

        (await SendAsync(HttpMethod.Put, "/api/v1/coefficients", directeur,
            new { subjectId = maths, coefficient = 7, series = "S2", rowVersion = created.RowVersion })).StatusCode
            .Should().Be(HttpStatusCode.OK);

        (await SendAsync(HttpMethod.Put, "/api/v1/coefficients", directeur,
            new { subjectId = maths, coefficient = 9, series = "S2", rowVersion = created.RowVersion })).StatusCode
            .Should().Be(HttpStatusCode.Conflict, "le jeton lu est périmé");
    }

    [Fact]
    public async Task Apply_Template_Is_Director_Only_And_Materialises_The_Validated_National_Values()
    {
        var directeur = await DirecteurAsync();
        var (_, maths, francais) = await SeedAsync(directeur);

        var secretaire = await SecretaireAsync();
        (await SendAsync(HttpMethod.Post, "/api/v1/coefficients/apply-template", secretaire, new { series = "S2" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // TECH : aucune valeur nationale fournie — refusé clairement, rien d'inventé.
        (await SendAsync(HttpMethod.Post, "/api/v1/coefficients/apply-template", directeur, new { series = "TECH" }))
            .StatusCode.Should().Be((HttpStatusCode)422);

        var applied = await SendAsync(HttpMethod.Post, "/api/v1/coefficients/apply-template", directeur, new { series = "S2" });
        applied.StatusCode.Should().Be(HttpStatusCode.OK);

        var grid = (await (await SendAsync(HttpMethod.Get, "/api/v1/coefficients?series=S2", directeur))
            .Content.ReadFromJsonAsync<Grid>())!;
        grid.Rows.Single(r => r.SubjectId == maths).EffectiveCoefficient.Should().Be(5m, "Maths = 5 en S2");
        grid.Rows.Single(r => r.SubjectId == francais).EffectiveCoefficient.Should().Be(2m, "Français = 2 en S2");
        grid.Rows.Should().OnlyContain(r => r.BaseCoefficient > 0);
    }

    [Fact]
    public async Task The_Catalogue_Lists_The_Baccalaureate_Series_Then_The_Legacy_Codes()
    {
        var directeur = await DirecteurAsync();

        var response = await SendAsync(HttpMethod.Get, "/api/v1/coefficients/catalog", directeur);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<List<SeriesItem>>())!.Select(s => s.Code)
            .Should().Equal(
                "L1A", "L1B", "L'1", "L2", "S1", "S2", "S3", "S4", "S5", "STEG", "T1", "T2", "STIDD", "LA", "S1A", "S2A",
                "L1", "TECH");
    }
}
