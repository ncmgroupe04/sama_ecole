using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace SamaEcole.FunctionalTests.Grades;

/// <summary>
/// GET /api/v1/grades?classroomId=&amp;subjectId=&amp;termId= — écran de saisie des notes (tickets
/// JGK-G01/G02). Alimente le tableau classe/matière/trimestre de /notes : chaque élève avec sa note
/// Devoir/Composition déjà saisie (ou null si rien n'a encore été entré).
/// </summary>
public class ClassGradesEndpointsTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public ClassGradesEndpointsTests(AuthApiFactory factory)
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
    private record GradeCellDto(Guid Id, decimal Value, uint RowVersion);
    private record StudentGradeRowDto(Guid StudentId, string Matricule, string FullName, GradeCellDto? Devoir, GradeCellDto? Composition);

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

    private async Task<(Guid ClassroomId, Guid StudentId, Guid SubjectId, Guid TermId)> SeedGradingContextAsync(string directeurToken)
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

        return (classroom.Id, student.Id, subject.Id, terms[0].Id);
    }

    private static string ListUrl(Guid classroomId, Guid subjectId, Guid termId) =>
        $"/api/v1/grades?classroomId={classroomId}&subjectId={subjectId}&termId={termId}";

    [Fact]
    public async Task Listing_Before_Any_Grade_Is_Entered_Returns_The_Student_With_Null_Cells()
    {
        var directeur = await DirecteurTokenAsync();
        var (classroomId, studentId, subjectId, termId) = await SeedGradingContextAsync(directeur);

        var response = await SendAsync(HttpMethod.Get, ListUrl(classroomId, subjectId, termId), directeur);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = (await response.Content.ReadFromJsonAsync<List<StudentGradeRowDto>>())!;
        rows.Should().ContainSingle().Which.StudentId.Should().Be(studentId);
        rows[0].Devoir.Should().BeNull();
        rows[0].Composition.Should().BeNull();
    }

    [Fact]
    public async Task Listing_After_Entering_Grades_Reflects_Their_Value_And_Row_Version()
    {
        var directeur = await DirecteurTokenAsync();
        var (classroomId, studentId, subjectId, termId) = await SeedGradingContextAsync(directeur);
        var enseignant = await EnseignantTokenAsync();

        await SendAsync(HttpMethod.Post, "/api/v1/grades", enseignant,
            new { studentId, subjectId, termId, evaluationType = "Devoir", value = 15 });

        var response = await SendAsync(HttpMethod.Get, ListUrl(classroomId, subjectId, termId), enseignant);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var row = (await response.Content.ReadFromJsonAsync<List<StudentGradeRowDto>>())!.Single();
        row.Devoir.Should().NotBeNull();
        row.Devoir!.Value.Should().Be(15);
        row.Devoir.RowVersion.Should().NotBe(0u);
        row.Composition.Should().BeNull();
    }

    [Fact]
    public async Task A_Secretary_Can_List_Class_Grades()
    {
        // Matrice d'autorisation "Photoshop" : le Secrétariat peut désormais corriger/annuler une
        // note déjà saisie (GradesController.UpdateGradeRoles), il doit donc pouvoir VOIR la grille —
        // contrairement à l'ancienne règle, où seuls Directeur et Enseignant y avaient accès.
        var directeur = await DirecteurTokenAsync();
        var (classroomId, _, subjectId, termId) = await SeedGradingContextAsync(directeur);
        var secretaire = await SecretaireTokenAsync();

        var response = await SendAsync(HttpMethod.Get, ListUrl(classroomId, subjectId, termId), secretaire);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Listing_With_An_Unknown_Classroom_Returns_404()
    {
        var directeur = await DirecteurTokenAsync();
        var (_, _, subjectId, termId) = await SeedGradingContextAsync(directeur);

        var response = await SendAsync(HttpMethod.Get, ListUrl(Guid.NewGuid(), subjectId, termId), directeur);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Listing_Without_A_Token_Should_Return_401()
    {
        var response = await _client.GetAsync(ListUrl(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
