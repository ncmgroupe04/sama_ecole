using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace SamaEcole.FunctionalTests.Teachers;

/// <summary>
/// Ticket JGK-D04 — /teachers/{id} et /teachers/{id}/assignments contre un vrai PostgreSQL.
/// </summary>
public class TeacherAssignmentsEndpointsTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public TeacherAssignmentsEndpointsTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public Task InitializeAsync() => _factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private record Tokens(string AccessToken, int ExpiresIn);
    private record SubjectDto(Guid Id, string Name, string Level, decimal Coefficient);
    private record ClassroomDto(Guid Id, string Name, string Level, int Capacity);
    private record TeacherResult(Guid Id, string Matricule);
    private record AssignmentDto(Guid Id, string ClassroomName, string SubjectName, Guid SchoolYearId, string SchoolYearLabel, bool IsActiveSchoolYear);
    private record TeacherProfileDto(
        Guid Id, string Matricule, string FullName, string Email, string? Phone, string Status,
        List<string> Subjects, List<AssignmentDto> Assignments);

    private async Task<string> AccessTokenAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!.AccessToken;
    }

    private Task<string> DirecteurTokenAsync() =>
        AccessTokenAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);

    private Task<string> FinanceTokenAsync() =>
        AccessTokenAsync(AuthApiFactory.FinanceEmail, AuthApiFactory.FinancePassword);

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await _client.SendAsync(request);
    }

    private async Task<Guid> CreateSubjectAsync(string token, string name)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/v1/subjects", token,
            new { name, level = "Primaire", coefficient = 4 });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<SubjectDto>())!.Id;
    }

    private async Task<Guid> CreateClassroomAsync(string token, string name)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/v1/classrooms", token,
            new { name, level = "Primaire", capacity = 30 });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ClassroomDto>())!.Id;
    }

    /// <summary>La toute première année scolaire d'une école devient active d'office.</summary>
    private async Task EnsureActiveSchoolYearAsync(string token)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/v1/school-years", token,
            new { label = "2026-2027", startDate = "2026-10-01", endDate = "2027-06-30" });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private async Task<Guid> CreateTeacherAsync(string token, string fullName, string email, Guid subjectId)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/v1/teachers", token,
            new { fullName, email, subjectIds = new[] { subjectId } });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<TeacherResult>())!.Id;
    }

    [Fact]
    public async Task Getting_An_Unknown_Teacher_Should_Return_404()
    {
        var directeur = await DirecteurTokenAsync();

        var response = await SendAsync(HttpMethod.Get, $"/api/v1/teachers/{Guid.NewGuid()}", directeur);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Assigning_An_Unknown_Teacher_Should_Return_404()
    {
        var directeur = await DirecteurTokenAsync();
        await EnsureActiveSchoolYearAsync(directeur);
        var subjectId = await CreateSubjectAsync(directeur, "Mathématiques");
        var classroomId = await CreateClassroomAsync(directeur, "CM2 A");

        var response = await SendAsync(HttpMethod.Post, $"/api/v1/teachers/{Guid.NewGuid()}/assignments", directeur,
            new { classroomId, subjectId });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Assigned_Teacher_Should_Appear_In_Its_Profile_With_The_Active_School_Year()
    {
        var directeur = await DirecteurTokenAsync();
        await EnsureActiveSchoolYearAsync(directeur);
        var subjectId = await CreateSubjectAsync(directeur, "Mathématiques");
        var classroomId = await CreateClassroomAsync(directeur, "CM2 A");
        var teacherId = await CreateTeacherAsync(directeur, "Moussa Ndiaye", "moussa.ndiaye@sama-ecole.sn", subjectId);

        var assignResponse = await SendAsync(HttpMethod.Post, $"/api/v1/teachers/{teacherId}/assignments", directeur,
            new { classroomId, subjectId });
        assignResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var profileResponse = await SendAsync(HttpMethod.Get, $"/api/v1/teachers/{teacherId}", directeur);
        profileResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var profile = (await profileResponse.Content.ReadFromJsonAsync<TeacherProfileDto>())!;

        profile.Subjects.Should().Contain("Mathématiques");
        profile.Assignments.Should().ContainSingle(a =>
            a.ClassroomName == "CM2 A" && a.SubjectName == "Mathématiques" &&
            a.SchoolYearLabel == "2026-2027" && a.IsActiveSchoolYear);
    }

    [Fact]
    public async Task Assigning_The_Same_Teacher_Twice_To_The_Same_Classroom_And_Subject_Should_Return_409()
    {
        var directeur = await DirecteurTokenAsync();
        await EnsureActiveSchoolYearAsync(directeur);
        var subjectId = await CreateSubjectAsync(directeur, "Mathématiques");
        var classroomId = await CreateClassroomAsync(directeur, "CM2 A");
        var teacherId = await CreateTeacherAsync(directeur, "Moussa Ndiaye", "moussa.ndiaye2@sama-ecole.sn", subjectId);

        await SendAsync(HttpMethod.Post, $"/api/v1/teachers/{teacherId}/assignments", directeur,
            new { classroomId, subjectId });

        var duplicate = await SendAsync(HttpMethod.Post, $"/api/v1/teachers/{teacherId}/assignments", directeur,
            new { classroomId, subjectId });

        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Assigning_An_Unknown_Classroom_Should_Return_422()
    {
        var directeur = await DirecteurTokenAsync();
        await EnsureActiveSchoolYearAsync(directeur);
        var subjectId = await CreateSubjectAsync(directeur, "Mathématiques");
        var teacherId = await CreateTeacherAsync(directeur, "Moussa Ndiaye", "moussa.ndiaye3@sama-ecole.sn", subjectId);

        var response = await SendAsync(HttpMethod.Post, $"/api/v1/teachers/{teacherId}/assignments", directeur,
            new { classroomId = Guid.NewGuid(), subjectId });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Assigning_Without_An_Active_School_Year_Should_Return_422()
    {
        // Aucune école scolaire créée du tout dans ce test : ni active, ni inactive.
        var directeur = await DirecteurTokenAsync();
        var subjectId = await CreateSubjectAsync(directeur, "Mathématiques");
        var classroomId = await CreateClassroomAsync(directeur, "CM2 A");
        var teacherId = await CreateTeacherAsync(directeur, "Moussa Ndiaye", "moussa.ndiaye4@sama-ecole.sn", subjectId);

        var response = await SendAsync(HttpMethod.Post, $"/api/v1/teachers/{teacherId}/assignments", directeur,
            new { classroomId, subjectId });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Finance_Must_Not_Assign_A_Teacher()
    {
        var directeur = await DirecteurTokenAsync();
        await EnsureActiveSchoolYearAsync(directeur);
        var subjectId = await CreateSubjectAsync(directeur, "Mathématiques");
        var classroomId = await CreateClassroomAsync(directeur, "CM2 A");
        var teacherId = await CreateTeacherAsync(directeur, "Moussa Ndiaye", "moussa.ndiaye5@sama-ecole.sn", subjectId);

        var finance = await FinanceTokenAsync();
        var response = await SendAsync(HttpMethod.Post, $"/api/v1/teachers/{teacherId}/assignments", finance,
            new { classroomId, subjectId });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Finance_Must_Not_See_A_Teacher_Profile()
    {
        var directeur = await DirecteurTokenAsync();
        var subjectId = await CreateSubjectAsync(directeur, "Mathématiques");
        var teacherId = await CreateTeacherAsync(directeur, "Moussa Ndiaye", "moussa.ndiaye6@sama-ecole.sn", subjectId);

        var finance = await FinanceTokenAsync();
        var response = await SendAsync(HttpMethod.Get, $"/api/v1/teachers/{teacherId}", finance);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
