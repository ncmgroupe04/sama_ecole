using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using SamaEcole.FunctionalTests.Common;
using Xunit;

namespace SamaEcole.FunctionalTests.Reports;

/// <summary>
/// Évolution N°7 à travers la vraie pile HTTP : la matrice de droits des nouvelles routes (règles du conseil,
/// rapports institutionnels, programmes) tient-elle, et le référentiel des programmes s'écrit-il et se relit-il ?
/// </summary>
public class Evolution7EndpointsTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public Evolution7EndpointsTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public Task InitializeAsync() => _factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private record Tokens(string AccessToken, int ExpiresIn);
    private record IdDto(Guid Id);
    private record UnitDto(Guid Id, string Title, string? Section, int Order);
    private record UnitsDto(string? GradeLevel, bool HasTemplate, List<UnitDto> Units);
    private record AddedDto(int Added);
    private record CouncilRulesDto(decimal FelicitationsMin, decimal HonorRollMin, decimal EncouragementsMin);
    private record NormRowDto(Guid? SubjectId, string SubjectName, decimal? TemplateHours, decimal? SchoolHours, decimal? EffectiveHours);
    private record NormsDto(string GradeLevel, string? Series, bool HasTemplate, List<NormRowDto> Rows);

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

    private async Task<Guid> CreateSubjectAsync(string token)
    {
        var level = $"Col-{Guid.NewGuid():N}"[..20];
        var response = await SendAsync(HttpMethod.Post, "/api/v1/subjects", token, new { name = "Mathématiques", level, coefficient = 4 });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<IdDto>())!.Id;
    }

    /// <summary>Matière de LYCÉE au nom unique : l'écran des volumes ne montre que les matières du cycle du niveau.</summary>
    private async Task<Guid> CreateLyceeSubjectAsync(string token)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/v1/subjects", token,
            new { name = $"Option {Guid.NewGuid():N}"[..20], level = "Lycée", coefficient = 2 });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<IdDto>())!.Id;
    }

    [Fact]
    public async Task The_Director_Writes_A_Programme_That_Teachers_Can_Read()
    {
        var directeur = await DirecteurAsync();
        var subjectId = await CreateSubjectAsync(directeur);

        var import = await SendAsync(HttpMethod.Post, "/api/v1/syllabus/import-template", directeur, new { subjectId, gradeLevel = "Troisième" });
        import.StatusCode.Should().Be(HttpStatusCode.OK);
        (await import.Content.ReadFromJsonAsync<AddedDto>())!.Added.Should().BeGreaterThan(0);

        var add = await SendAsync(HttpMethod.Post, "/api/v1/syllabus/units", directeur,
            new { subjectId, gradeLevel = "Troisième", section = "Révisions", titles = new[] { "Préparation au BFEM" } });
        add.StatusCode.Should().Be(HttpStatusCode.OK);

        var read = await SendAsync(HttpMethod.Get, $"/api/v1/syllabus/units?subjectId={subjectId}&gradeLevel=Troisi%C3%A8me", await EnseignantAsync());
        read.StatusCode.Should().Be(HttpStatusCode.OK);
        var units = (await read.Content.ReadFromJsonAsync<UnitsDto>())!;
        units.HasTemplate.Should().BeTrue();
        units.Units.Last().Title.Should().Be("Préparation au BFEM", "les chapitres ajoutés viennent à la suite de la trame");
    }

    [Fact]
    public async Task An_Unknown_Grade_Is_A_Validation_Error()
    {
        var directeur = await DirecteurAsync();
        var subjectId = await CreateSubjectAsync(directeur);

        var response = await SendAsync(HttpMethod.Post, "/api/v1/syllabus/units", directeur,
            new { subjectId, gradeLevel = "Master 2", titles = new[] { "Chapitre" } });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Only_The_Director_Writes_A_Programme()
    {
        var subjectId = await CreateSubjectAsync(await DirecteurAsync());
        var body = new { subjectId, gradeLevel = "Troisième", titles = new[] { "Chapitre" } };

        (await SendAsync(HttpMethod.Post, "/api/v1/syllabus/units", await SecretaireAsync(), body)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Post, "/api/v1/syllabus/units", await EnseignantAsync(), body)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_Coverage_Dashboard_Is_For_Director_And_Secretariat()
    {
        (await SendAsync(HttpMethod.Get, "/api/v1/syllabus/coverage", await SecretaireAsync())).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Get, "/api/v1/syllabus/coverage", await EnseignantAsync())).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Age_Norms_Are_Read_By_The_Secretariat_But_Changed_By_The_Director_Only()
    {
        var secretaire = await SecretaireAsync();

        (await SendAsync(HttpMethod.Get, "/api/v1/institutional/age-norms", secretaire)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Put, "/api/v1/institutional/age-norms/CI", secretaire, new { minAge = 5, maxAge = 9 }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Get, "/api/v1/institutional/age-norms", await EnseignantAsync()))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Council_Rules_Default_To_The_National_Thresholds_And_Only_The_Director_Changes_Them()
    {
        var response = await SendAsync(HttpMethod.Get, "/api/v1/report-cards/council-rules", await DirecteurAsync());
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var rules = (await response.Content.ReadFromJsonAsync<CouncilRulesDto>())!;
        rules.FelicitationsMin.Should().Be(14);
        rules.HonorRollMin.Should().Be(12);
        rules.EncouragementsMin.Should().Be(12);

        (await SendAsync(HttpMethod.Put, "/api/v1/report-cards/council-rules", await SecretaireAsync(), new { felicitationsMin = 15 }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_Director_Sets_A_Weekly_Volume_That_The_Secretariat_Reads()
    {
        var directeur = await DirecteurAsync();
        var subjectId = await CreateLyceeSubjectAsync(directeur);

        var put = await SendAsync(HttpMethod.Put, "/api/v1/hour-volumes/norms", directeur,
            new { gradeLevel = "Terminale", series = "s2", subjectId, weeklyHours = 4.5m });
        put.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var read = await SendAsync(HttpMethod.Get, "/api/v1/hour-volumes/norms?gradeLevel=Terminale&series=S2", await SecretaireAsync());
        read.StatusCode.Should().Be(HttpStatusCode.OK);
        var norms = (await read.Content.ReadFromJsonAsync<NormsDto>())!;
        norms.Series.Should().Be("S2", "la série est normalisée");
        norms.Rows.Should().Contain(r => r.SubjectId == subjectId && r.SchoolHours == 4.5m && r.EffectiveHours == 4.5m);
    }

    [Fact]
    public async Task Weekly_Volumes_Are_Validated_And_Reserved_To_The_Director()
    {
        var directeur = await DirecteurAsync();
        var subjectId = await CreateLyceeSubjectAsync(directeur);

        (await SendAsync(HttpMethod.Put, "/api/v1/hour-volumes/norms", directeur,
            new { gradeLevel = "Terminale", series = "Z9", subjectId, weeklyHours = 4 })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await SendAsync(HttpMethod.Put, "/api/v1/hour-volumes/norms", directeur,
            new { gradeLevel = "Terminale", subjectId, weeklyHours = 2.1m })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await SendAsync(HttpMethod.Put, "/api/v1/hour-volumes/norms", await SecretaireAsync(),
            new { gradeLevel = "Terminale", subjectId, weeklyHours = 4 })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_Compliance_Check_Is_For_Director_And_Secretariat()
    {
        (await SendAsync(HttpMethod.Get, "/api/v1/hour-volumes/compliance", await SecretaireAsync())).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Get, "/api/v1/hour-volumes/compliance", await EnseignantAsync())).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Get, $"/api/v1/hour-volumes/compliance?classroomId={Guid.NewGuid()}", await DirecteurAsync()))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
