using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace SamaEcole.FunctionalTests.Grades;

/// <summary>
/// Ticket JGK-G01 — /grades contre un vrai PostgreSQL, à travers la vraie pile HTTP.
///
/// Deux endpoints, deux permissions (docs/Volume_7_Security.md « Notes ») : POST (Saisir) est réservé
/// à l'Enseignant, PUT (Modifier) est ouvert au Directeur ET à l'Enseignant.
/// </summary>
public class GradesEndpointsTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public GradesEndpointsTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public Task InitializeAsync() => _factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private record Tokens(string AccessToken, int ExpiresIn);
    private record ClassroomDto(Guid Id, string Name, string Level, int Capacity);
    private record SubjectDto(Guid Id, string Name, string Level, decimal Coefficient);
    private record SchoolYearDto(Guid Id, string Label, DateOnly StartDate, DateOnly EndDate, bool IsActive, bool IsClosed);
    private record TermDto(Guid Id, string Label, int Order, DateOnly StartDate, DateOnly EndDate);
    private record StudentDto(Guid Id, string Matricule);
    private record GradeDto(Guid Id, decimal Value, uint RowVersion);

    private async Task<string> AccessTokenAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!.AccessToken;
    }

    private Task<string> DirecteurTokenAsync() =>
        AccessTokenAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);

    private Task<string> EnseignantTokenAsync() =>
        AccessTokenAsync(AuthApiFactory.EnseignantEmail, AuthApiFactory.EnseignantPassword);

    private Task<string> SecretaireTokenAsync() =>
        AccessTokenAsync(AuthApiFactory.SecretaireEmail, AuthApiFactory.SecretairePassword);

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await _client.SendAsync(request);
    }

    private Task<HttpResponseMessage> CreateGradeAsync(
        string token, Guid studentId, Guid subjectId, Guid termId, string evaluationType, decimal value) =>
        SendAsync(HttpMethod.Post, "/api/v1/grades", token, new { studentId, subjectId, termId, evaluationType, value });

    /// <summary>Classe, matière, élève, année (et donc ses 3 trimestres auto-générés) — le décor complet.</summary>
    private async Task<(Guid StudentId, Guid SubjectId, Guid TermId)> SeedGradingContextAsync(string directeurToken)
    {
        var classroomResponse = await SendAsync(HttpMethod.Post, "/api/v1/classrooms", directeurToken,
            new { name = "CM2", level = "Primaire", capacity = 40 });
        var classroom = (await classroomResponse.Content.ReadFromJsonAsync<ClassroomDto>())!;

        var subjectResponse = await SendAsync(HttpMethod.Post, "/api/v1/subjects", directeurToken,
            new { name = "Mathématiques", level = "Primaire", coefficient = 4 });
        var subject = (await subjectResponse.Content.ReadFromJsonAsync<SubjectDto>())!;

        var studentResponse = await SendAsync(HttpMethod.Post, "/api/v1/students", directeurToken,
            new { fullName = "Élève de test", birthDate = "2015-01-01", gender = "M", classroomId = classroom.Id });
        var student = (await studentResponse.Content.ReadFromJsonAsync<StudentDto>())!;

        var yearResponse = await SendAsync(HttpMethod.Post, "/api/v1/school-years", directeurToken,
            new { label = "2026-2027", startDate = "2026-10-01", endDate = "2027-06-30" });
        var year = (await yearResponse.Content.ReadFromJsonAsync<SchoolYearDto>())!;

        var termsResponse = await SendAsync(HttpMethod.Get, $"/api/v1/school-years/{year.Id}/terms", directeurToken);
        var terms = (await termsResponse.Content.ReadFromJsonAsync<List<TermDto>>())!;

        return (student.Id, subject.Id, terms[0].Id);
    }

    [Fact]
    public async Task Creating_A_School_Year_Should_Generate_Three_Terms_Covering_The_Whole_Period()
    {
        var directeur = await DirecteurTokenAsync();

        var yearResponse = await SendAsync(HttpMethod.Post, "/api/v1/school-years", directeur,
            new { label = "2026-2027", startDate = "2026-10-01", endDate = "2027-06-30" });
        var year = (await yearResponse.Content.ReadFromJsonAsync<SchoolYearDto>())!;

        var termsResponse = await SendAsync(HttpMethod.Get, $"/api/v1/school-years/{year.Id}/terms", directeur);
        var terms = (await termsResponse.Content.ReadFromJsonAsync<List<TermDto>>())!;

        terms.Should().HaveCount(3);
        terms.Select(t => t.Order).Should().BeEquivalentTo([1, 2, 3]);
        terms[0].StartDate.Should().Be(year.StartDate);
        terms[^1].EndDate.Should().Be(year.EndDate);
    }

    [Fact]
    public async Task An_Enseignant_Entering_A_New_Grade_Should_Return_201_With_A_Row_Version()
    {
        var directeur = await DirecteurTokenAsync();
        var (studentId, subjectId, termId) = await SeedGradingContextAsync(directeur);
        var enseignant = await EnseignantTokenAsync();

        var response = await CreateGradeAsync(enseignant, studentId, subjectId, termId, "Devoir", 15);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var grade = (await response.Content.ReadFromJsonAsync<GradeDto>())!;
        grade.Value.Should().Be(15);
        grade.RowVersion.Should().NotBe(0u);
    }

    [Fact]
    public async Task A_Directeur_Must_Not_Enter_A_New_Grade()
    {
        // « Saisir » est réservé à l'Enseignant (docs/Volume_7_Security.md « Notes ») : le Directeur ne
        // peut que corriger une note déjà saisie, jamais en créer une lui-même.
        var directeur = await DirecteurTokenAsync();
        var (studentId, subjectId, termId) = await SeedGradingContextAsync(directeur);

        var response = await CreateGradeAsync(directeur, studentId, subjectId, termId, "Devoir", 15);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_Secretary_Must_Not_Enter_Grades()
    {
        var directeur = await DirecteurTokenAsync();
        var (studentId, subjectId, termId) = await SeedGradingContextAsync(directeur);
        var secretaire = await SecretaireTokenAsync();

        var response = await CreateGradeAsync(secretaire, studentId, subjectId, termId, "Devoir", 13);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_Grade_Above_The_Grading_Scale_Should_Return_422()
    {
        var directeur = await DirecteurTokenAsync();
        var (studentId, subjectId, termId) = await SeedGradingContextAsync(directeur);
        var enseignant = await EnseignantTokenAsync();

        // Barème par défaut de l'école : 20 (SchoolSettingsDefaults.GradingScale).
        var response = await CreateGradeAsync(enseignant, studentId, subjectId, termId, "Devoir", 25);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task An_Unknown_Student_Should_Return_422()
    {
        var directeur = await DirecteurTokenAsync();
        var (_, subjectId, termId) = await SeedGradingContextAsync(directeur);
        var enseignant = await EnseignantTokenAsync();

        var response = await CreateGradeAsync(enseignant, Guid.NewGuid(), subjectId, termId, "Devoir", 12);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Devoir_And_Composition_Are_Independent_Entries_For_The_Same_Subject()
    {
        var directeur = await DirecteurTokenAsync();
        var (studentId, subjectId, termId) = await SeedGradingContextAsync(directeur);
        var enseignant = await EnseignantTokenAsync();

        var devoir = await CreateGradeAsync(enseignant, studentId, subjectId, termId, "Devoir", 10);
        var composition = await CreateGradeAsync(enseignant, studentId, subjectId, termId, "Composition", 18);

        devoir.StatusCode.Should().Be(HttpStatusCode.Created);
        composition.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task An_Enseignant_Correcting_Their_Own_Grade_With_The_Fresh_Row_Version_Should_Succeed()
    {
        var directeur = await DirecteurTokenAsync();
        var (studentId, subjectId, termId) = await SeedGradingContextAsync(directeur);
        var enseignant = await EnseignantTokenAsync();

        var created = await CreateGradeAsync(enseignant, studentId, subjectId, termId, "Devoir", 12);
        var grade = (await created.Content.ReadFromJsonAsync<GradeDto>())!;

        var corrected = await SendAsync(HttpMethod.Put, $"/api/v1/grades/{grade.Id}", enseignant,
            new { value = 14, rowVersion = grade.RowVersion });

        corrected.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await corrected.Content.ReadFromJsonAsync<GradeDto>())!;
        result.Value.Should().Be(14);
    }

    [Fact]
    public async Task A_Directeur_Can_Correct_A_Grade_Entered_By_The_Enseignant()
    {
        // « Modifier » est ouvert aux DEUX rôles (docs/Volume_7_Security.md « Notes ») : contrairement
        // à la saisie, la correction n'est pas réservée à l'Enseignant.
        var directeur = await DirecteurTokenAsync();
        var (studentId, subjectId, termId) = await SeedGradingContextAsync(directeur);
        var enseignant = await EnseignantTokenAsync();

        var created = await CreateGradeAsync(enseignant, studentId, subjectId, termId, "Devoir", 12);
        var grade = (await created.Content.ReadFromJsonAsync<GradeDto>())!;

        var corrected = await SendAsync(HttpMethod.Put, $"/api/v1/grades/{grade.Id}", directeur,
            new { value = 16, rowVersion = grade.RowVersion });

        corrected.StatusCode.Should().Be(HttpStatusCode.OK);
        (await corrected.Content.ReadFromJsonAsync<GradeDto>())!.Value.Should().Be(16);
    }

    [Fact]
    public async Task A_Secretary_Must_Not_Correct_A_Grade()
    {
        var directeur = await DirecteurTokenAsync();
        var (studentId, subjectId, termId) = await SeedGradingContextAsync(directeur);
        var enseignant = await EnseignantTokenAsync();

        var created = await CreateGradeAsync(enseignant, studentId, subjectId, termId, "Devoir", 12);
        var grade = (await created.Content.ReadFromJsonAsync<GradeDto>())!;

        var secretaire = await SecretaireTokenAsync();
        var response = await SendAsync(HttpMethod.Put, $"/api/v1/grades/{grade.Id}", secretaire,
            new { value = 16, rowVersion = grade.RowVersion });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Correcting_A_Grade_With_A_Stale_Row_Version_Should_Return_409()
    {
        var directeur = await DirecteurTokenAsync();
        var (studentId, subjectId, termId) = await SeedGradingContextAsync(directeur);
        var enseignant = await EnseignantTokenAsync();

        var created = await CreateGradeAsync(enseignant, studentId, subjectId, termId, "Devoir", 12);
        var grade = (await created.Content.ReadFromJsonAsync<GradeDto>())!;

        // Le Directeur corrige — la version en base bascule.
        await SendAsync(HttpMethod.Put, $"/api/v1/grades/{grade.Id}", directeur,
            new { value = 16, rowVersion = grade.RowVersion });

        // L'Enseignant, resté sur l'ancienne lecture, tente de corriger avec le jeton PÉRIMÉ.
        var conflict = await SendAsync(HttpMethod.Put, $"/api/v1/grades/{grade.Id}", enseignant,
            new { value = 20, rowVersion = grade.RowVersion });

        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Correcting_An_Unknown_Grade_Should_Return_404()
    {
        var directeur = await DirecteurTokenAsync();

        var response = await SendAsync(HttpMethod.Put, $"/api/v1/grades/{Guid.NewGuid()}", directeur,
            new { value = 12, rowVersion = 0u });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Posting_Without_A_Token_Should_Return_401()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/grades", new
        {
            studentId = Guid.NewGuid(), subjectId = Guid.NewGuid(), termId = Guid.NewGuid(),
            evaluationType = "Devoir", value = 12
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
