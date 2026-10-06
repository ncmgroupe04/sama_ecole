using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Xunit;

namespace SamaEcole.FunctionalTests.Internat;

/// <summary>
/// Tableau de bord de la mémorisation et bulletin coranique PDF par le vrai pipeline HTTP : authentification, garde de
/// module, rôles, et surtout le parcours complet — le Directeur crée un élève et un Oustaz lié, l'affecte, l'Oustaz saisit
/// l'avancement avec SON compte, puis la Direction le retrouve dans le tableau de bord et tire le bulletin PDF, que
/// l'Oustaz ne peut tirer que pour ses propres élèves.
/// </summary>
public class ProgressEndpointsTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    public Task InitializeAsync() => factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private record Tokens(string AccessToken, string RefreshToken, int ExpiresIn);
    private record ApiError(string Code, string Message);
    private record IdResult(Guid Id);
    private record InstructorResult(Guid Id, uint RowVersion);
    private record Band(int Min, int Max, int StudentCount);
    private record Stagnant(Guid StudentId, string FullName, string InstructorName, string? LastEvaluatedAt, int? DaysSinceEvaluation);
    private record Halqa(Guid InstructorId, string InstructorName, int StudentCount, int StagnantCount);
    private record Dashboard(
        int StaleDays, int StudentsInHalqa, int UnassignedStudents, decimal AverageProgressPercent, int CompletedHizbs,
        List<Band> Bands, List<Halqa> Halqas, int StagnantCount, List<Stagnant> Stagnant);

    private async Task<string> LoginAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!.AccessToken;
    }

    private Task<string> DirecteurAsync() => LoginAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);
    private Task<string> EnseignantAsync() => LoginAsync(AuthApiFactory.EnseignantEmail, AuthApiFactory.EnseignantPassword);
    private Task<string> SecretaireAsync() => LoginAsync(AuthApiFactory.SecretaireEmail, AuthApiFactory.SecretairePassword);
    private Task<string> FinanceAsync() => LoginAsync(AuthApiFactory.FinanceEmail, AuthApiFactory.FinancePassword);

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? accessToken, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await _client.SendAsync(request);
    }

    private async Task EnableInternatAsync(string directeurToken)
    {
        var response = await SendAsync(HttpMethod.Put, "/api/v1/schools/current/settings", directeurToken, new
        {
            gradingScale = "20",
            studentMatriculeFormat = "ELEV-{YEAR}-{SEQ:4}",
            teacherMatriculeFormat = "ENS-{YEAR}-{SEQ:3}",
            autoLogoutMinutes = 10,
            dateFormat = "dd/MM/yyyy",
            tuitionMonthsPerYear = 9,
            allowSecretaryToManageGrading = false,
            isPedagogyEnabled = true,
            isFinanceEnabled = true,
            isInternatEnabled = true,
            isCoranModuleEnabled = false
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private const string DashboardUrl = "/api/v1/internat/progress-dashboard";
    private static string ReportUrl(Guid student) => $"/api/v1/internat/students/{student}/hizb-report/pdf";

    private async Task<Dashboard> GetDashboardAsync(string token, string query = "")
    {
        var response = await SendAsync(HttpMethod.Get, DashboardUrl + query, token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Dashboard>())!;
    }

    private async Task<Guid> CreateClassroomAsync(string directeur)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/v1/classrooms", directeur, new { name = "Halqa du matin", level = "Primaire", capacity = 30 });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<IdResult>())!.Id;
    }

    private async Task<Guid> CreateStudentAsync(string directeur, string name, Guid classroom)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/v1/students", directeur,
            new { fullName = name, birthDate = "2015-03-12", birthPlace = "Touba", gender = "M", classroomId = classroom });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<IdResult>())!.Id;
    }

    private async Task<Guid> CreateInstructorAsync(string directeur, string name, Guid? userId = null)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/v1/internat/instructors", directeur, new { fullName = name, userId });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<InstructorResult>())!.Id;
    }

    private async Task AssignAsync(string directeur, Guid instructor, params Guid[] students)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/v1/internat/students/assign-instructor", directeur,
            new { instructorId = instructor, studentIds = students });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Every_Route_Requires_Authentication()
    {
        (await SendAsync(HttpMethod.Get, DashboardUrl, null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await SendAsync(HttpMethod.Get, ReportUrl(Guid.NewGuid()), null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_Routes_Are_Locked_By_The_Internat_Module_Gate_By_Default()
    {
        var response = await SendAsync(HttpMethod.Get, DashboardUrl, await DirecteurAsync());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadFromJsonAsync<ApiError>())!.Code.Should().Be("MODULE_DISABLED");
    }

    [Fact]
    public async Task Only_The_Direction_Reads_The_Dashboard()
    {
        await EnableInternatAsync(await DirecteurAsync());

        (await SendAsync(HttpMethod.Get, DashboardUrl, await DirecteurAsync())).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Get, DashboardUrl, await SecretaireAsync())).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Get, DashboardUrl, await EnseignantAsync())).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Get, DashboardUrl, await FinanceAsync())).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_Empty_School_Gets_An_Empty_Dashboard_And_The_Threshold_Is_Clamped()
    {
        var directeur = await DirecteurAsync();
        await EnableInternatAsync(directeur);

        var d = await GetDashboardAsync(directeur, "?staleDays=0");

        d.StaleDays.Should().Be(1, "un seuil de 0 jour serait absurde : borné à 1");
        d.StudentsInHalqa.Should().Be(0);
        d.Halqas.Should().BeEmpty();
        d.Stagnant.Should().BeEmpty();
        d.Bands.Should().HaveCount(6);
    }

    [Fact]
    public async Task The_Report_Of_An_Unknown_Student_Is_Not_Found()
    {
        var directeur = await DirecteurAsync();
        await EnableInternatAsync(directeur);

        (await SendAsync(HttpMethod.Get, ReportUrl(Guid.NewGuid()), directeur)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_Dashboard_And_Report_Pages_Are_Served_With_Their_Scripts()
    {
        var response = await _client.GetAsync("/suivi-coranique");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("x-data=\"progressDashboardPage()\"").And.Contain("/js/progress-dashboard.js");
    }

    [Fact]
    public async Task Full_Journey_The_Oustaz_Records_Progress_Then_The_Direction_Sees_It_And_Prints_The_Report()
    {
        var directeur = await DirecteurAsync();
        await EnableInternatAsync(directeur);
        var enseignant = await EnseignantAsync();

        var classroom = await CreateClassroomAsync(directeur);
        var mine = await CreateStudentAsync(directeur, "Awa Diop", classroom);
        var neverEvaluated = await CreateStudentAsync(directeur, "Binta Fall", classroom);
        var theirs = await CreateStudentAsync(directeur, "Cheikh Ndiaye", classroom);

        var oustaz = await CreateInstructorAsync(directeur, "Serigne Modou", AuthApiFactory.EnseignantId);
        var other = await CreateInstructorAsync(directeur, "Autre Oustaz");
        await AssignAsync(directeur, oustaz, mine, neverEvaluated);
        await AssignAsync(directeur, other, theirs);

        // L'Oustaz saisit un Hizb complet et un Hizb à moitié, avec SON compte.
        (await SendAsync(HttpMethod.Put, $"/api/v1/internat/students/{mine}/hizb-progress", enseignant,
            new { hizbNumber = 1, completedQuarters = 4, rating = 5, rowVersion = (uint?)null })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Put, $"/api/v1/internat/students/{mine}/hizb-progress", enseignant,
            new { hizbNumber = 2, completedQuarters = 2, rating = 3, rowVersion = (uint?)null })).StatusCode.Should().Be(HttpStatusCode.OK);

        // La Direction le retrouve.
        var d = await GetDashboardAsync(directeur);
        d.StudentsInHalqa.Should().Be(3);
        d.CompletedHizbs.Should().Be(1);
        d.Halqas.Select(h => h.InstructorName).Should().BeEquivalentTo("Serigne Modou", "Autre Oustaz");
        d.Halqas.Single(h => h.InstructorId == oustaz).StudentCount.Should().Be(2);
        d.Bands.Sum(b => b.StudentCount).Should().Be(3);

        // Alertes : les deux élèves jamais évalués (Binta, Cheikh) ; Awa vient d'être évaluée.
        d.Stagnant.Select(s => s.StudentId).Should().BeEquivalentTo(new[] { neverEvaluated, theirs });
        d.Stagnant.Should().OnlyContain(s => s.LastEvaluatedAt == null && s.DaysSinceEvaluation == null);

        // Le bulletin PDF de l'élève : un vrai PDF, pour la Direction comme pour l'Oustaz de cet élève.
        foreach (var token in new[] { directeur, enseignant })
        {
            var pdf = await SendAsync(HttpMethod.Get, ReportUrl(mine), token);
            pdf.StatusCode.Should().Be(HttpStatusCode.OK);
            pdf.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
            var bytes = await pdf.Content.ReadAsByteArrayAsync();
            Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
            bytes.Length.Should().BeGreaterThan(3000);
        }

        // L'Oustaz ne tire pas le bulletin d'un élève qui n'est pas dans sa Halqa.
        (await SendAsync(HttpMethod.Get, ReportUrl(theirs), enseignant)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        // La Direction, si.
        (await SendAsync(HttpMethod.Get, ReportUrl(theirs), directeur)).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
