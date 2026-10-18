using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace SamaEcole.FunctionalTests.Deletion;

/// <summary>
/// Suppression guidée des classes, matières et enseignants, de bout en bout contre un vrai PostgreSQL (spec 2026-10-17) :
/// la suppression directe énumère les liens, la rupture est explicite, les liens non rompables sont refusés, le
/// Finance n'a aucun accès, un élément inconnu est introuvable.
/// </summary>
public class GuidedDeletionEndpointsTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public GuidedDeletionEndpointsTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public Task InitializeAsync() => _factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private record Tokens(string AccessToken, int ExpiresIn);
    private record Created(Guid Id);
    private record ClassroomItem(Guid Id, string Name, uint RowVersion);
    private record SubjectItem(Guid Id, string Name, uint RowVersion);
    private record TeacherProfile(Guid Id, uint RowVersion);
    private record FeeCategoryDto(Guid Id);
    private record Receipt(Guid EnrollmentId, string Matricule);
    private record Link(string Kind, string Label, int Count, bool Breakable, string BreakAction);
    private record Impact(bool CanDelete, bool FinancialBlock, string? Message, List<Link> Links);
    private record ApiError(string Code, string Message);

    private async Task<string> TokenAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!.AccessToken;
    }

    private Task<string> DirecteurTokenAsync() => TokenAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);
    private Task<string> SecretaireTokenAsync() => TokenAsync(AuthApiFactory.SecretaireEmail, AuthApiFactory.SecretairePassword);
    private Task<string> FinanceTokenAsync() => TokenAsync(AuthApiFactory.FinanceEmail, AuthApiFactory.FinancePassword);

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await _client.SendAsync(request);
    }

    private async Task<Guid> CreateClassroomAsync(string token, string name)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/v1/classrooms", token, new { name, level = "Primaire", capacity = 40 });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<Created>())!.Id;
    }

    private async Task<uint> ClassroomVersionAsync(string token, Guid id)
    {
        var list = await SendAsync(HttpMethod.Get, "/api/v1/classrooms", token);
        return (await list.Content.ReadFromJsonAsync<List<ClassroomItem>>())!.Single(c => c.Id == id).RowVersion;
    }

    private async Task<Guid> CreateSubjectAsync(string token, string name)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/v1/subjects", token, new { name, level = "Primaire", coefficient = 4 });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<Created>())!.Id;
    }

    private async Task<uint> SubjectVersionAsync(string token, Guid id)
    {
        var list = await SendAsync(HttpMethod.Get, "/api/v1/subjects?pageSize=100", token);
        var body = await list.Content.ReadAsStringAsync();
        using var doc = System.Text.Json.JsonDocument.Parse(body);
        var items = doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array
            ? doc.RootElement
            : doc.RootElement.GetProperty("items");
        foreach (var item in items.EnumerateArray())
        {
            if (item.GetProperty("id").GetGuid() == id) return item.GetProperty("rowVersion").GetUInt32();
        }
        throw new InvalidOperationException($"Matière {id} absente de la liste.");
    }

    private async Task<Guid> CreateTeacherAsync(string token, string fullName, string email, IEnumerable<Guid> subjectIds)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/v1/teachers", token,
            new { fullName, email, birthDate = "1985-04-12", subjectIds });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<Created>())!.Id;
    }

    private async Task<uint> TeacherVersionAsync(string token, Guid id)
    {
        var profile = await SendAsync(HttpMethod.Get, $"/api/v1/teachers/{id}", token);
        return (await profile.Content.ReadFromJsonAsync<TeacherProfile>())!.RowVersion;
    }

    /// <summary>Année, frais et un élève inscrit dans la classe donnée.</summary>
    private async Task EnrollStudentAsync(string directeur, Guid classroomId)
    {
        (await SendAsync(HttpMethod.Post, "/api/v1/school-years", directeur, new
        {
            label = $"{Today.Year}-{Today.Year + 1}",
            startDate = Today.AddDays(-150).ToString("yyyy-MM-dd"),
            endDate = Today.AddDays(+120).ToString("yyyy-MM-dd")
        })).StatusCode.Should().Be(HttpStatusCode.Created);

        foreach (var (name, recurring, amount) in new[] { ("Inscription", false, 10_000m), ("Mensualité", true, 15_000m) })
        {
            var category = await SendAsync(HttpMethod.Post, "/api/v1/finance/fee-categories", directeur, new { name, isRecurring = recurring });
            category.StatusCode.Should().Be(HttpStatusCode.Created);
            var id = (await category.Content.ReadFromJsonAsync<FeeCategoryDto>())!.Id;
            (await SendAsync(HttpMethod.Post, "/api/v1/finance/fees/apply-standard", directeur,
                new { feeCategoryId = id, amount, overwriteExisting = false })).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var secretaire = await SecretaireTokenAsync();
        (await SendAsync(HttpMethod.Post, "/api/v1/enrollments", secretaire, new
        {
            type = "NewEnrollment", classroomId, fullName = "Awa Ndiaye", birthDate = "2015-05-20", birthPlace = "Dakar", gender = "F"
        })).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // ------------------------------------------------------------------------------------------ Classes

    [Fact]
    public async Task A_Classroom_With_Enrolled_Students_Lists_Its_Links_And_Cannot_Be_Unlinked()
    {
        var directeur = await DirecteurTokenAsync();
        var classroom = await CreateClassroomAsync(directeur, "CM2 Chargée");
        await EnrollStudentAsync(directeur, classroom);
        var version = await ClassroomVersionAsync(directeur, classroom);

        var blocked = await SendAsync(HttpMethod.Delete, $"/api/v1/classrooms/{classroom}?rowVersion={version}", directeur);
        blocked.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var error = (await blocked.Content.ReadFromJsonAsync<ApiError>())!;
        error.Code.Should().Be("DEPENDENCIES_EXIST");
        error.Message.Should().Contain("1 élève rattaché").And.Contain("1 inscription").And.Contain("Vous devez rompre ces liens");

        var impact = (await (await SendAsync(HttpMethod.Get, $"/api/v1/classrooms/{classroom}/deletion-impact", directeur))
            .Content.ReadFromJsonAsync<Impact>())!;
        impact.CanDelete.Should().BeFalse();
        impact.Links.Single(l => l.Kind == "Student").Breakable.Should().BeFalse("on transfère les élèves ou on supprime leur fiche");

        var unlink = await SendAsync(HttpMethod.Post, $"/api/v1/classrooms/{classroom}/unlink-dependencies", directeur);
        unlink.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await unlink.Content.ReadFromJsonAsync<ApiError>())!.Code.Should().Be("DEPENDENCIES_NOT_BREAKABLE");
    }

    [Fact]
    public async Task An_Empty_Classroom_Is_Analyzed_As_Deletable_Then_Deleted()
    {
        var directeur = await DirecteurTokenAsync();
        var classroom = await CreateClassroomAsync(directeur, "CE1 Vide");
        var version = await ClassroomVersionAsync(directeur, classroom);

        var impact = (await (await SendAsync(HttpMethod.Get, $"/api/v1/classrooms/{classroom}/deletion-impact", directeur))
            .Content.ReadFromJsonAsync<Impact>())!;
        impact.CanDelete.Should().BeTrue();

        (await SendAsync(HttpMethod.Delete, $"/api/v1/classrooms/{classroom}?rowVersion={version}", directeur))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ------------------------------------------------------------------------------------------ Matières

    [Fact]
    public async Task A_Subject_Taught_By_A_Teacher_Is_Blocked_Then_Unlinked_Then_Deleted()
    {
        var directeur = await DirecteurTokenAsync();
        var subject = await CreateSubjectAsync(directeur, "Histoire");
        await CreateTeacherAsync(directeur, "Mame Diarra", "mame.diarra@sama-ecole.sn", [subject]);
        var version = await SubjectVersionAsync(directeur, subject);

        var blocked = await SendAsync(HttpMethod.Delete, $"/api/v1/subjects/{subject}?rowVersion={version}", directeur);
        blocked.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var error = (await blocked.Content.ReadFromJsonAsync<ApiError>())!;
        error.Code.Should().Be("DEPENDENCIES_EXIST");
        error.Message.Should().Contain("1 enseignant qualifié");

        (await SendAsync(HttpMethod.Post, $"/api/v1/subjects/{subject}/unlink-dependencies", directeur))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Delete, $"/api/v1/subjects/{subject}?rowVersion={version}", directeur))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ------------------------------------------------------------------------------------------ Enseignants

    [Fact]
    public async Task A_Teacher_Qualified_For_A_Subject_Is_Deletable_Directly_Because_Qualifications_Belong_To_The_Profile()
    {
        var directeur = await DirecteurTokenAsync();
        var subject = await CreateSubjectAsync(directeur, "Géographie");
        var teacher = await CreateTeacherAsync(directeur, "Ibra Sow", "ibra.sow@sama-ecole.sn", [subject]);
        var version = await TeacherVersionAsync(directeur, teacher);

        var impact = (await (await SendAsync(HttpMethod.Get, $"/api/v1/teachers/{teacher}/deletion-impact", directeur))
            .Content.ReadFromJsonAsync<Impact>())!;
        impact.CanDelete.Should().BeTrue("les matières enseignées font partie de la fiche : elles partent avec elle");

        (await SendAsync(HttpMethod.Delete, $"/api/v1/teachers/{teacher}?rowVersion={version}", directeur))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task A_Teacher_With_An_Assignment_Is_Blocked_Then_Unlinked_Then_Deleted()
    {
        var directeur = await DirecteurTokenAsync();
        var classroom = await CreateClassroomAsync(directeur, "CM1 B");
        var subject = await CreateSubjectAsync(directeur, "Sciences");
        var teacher = await CreateTeacherAsync(directeur, "Fatou Gaye", "fatou.gaye@sama-ecole.sn", [subject]);
        (await SendAsync(HttpMethod.Post, "/api/v1/school-years", directeur, new
        {
            label = $"{Today.Year}-{Today.Year + 1}",
            startDate = Today.AddDays(-150).ToString("yyyy-MM-dd"),
            endDate = Today.AddDays(+120).ToString("yyyy-MM-dd")
        })).StatusCode.Should().Be(HttpStatusCode.Created);
        (await SendAsync(HttpMethod.Post, $"/api/v1/teachers/{teacher}/assignments", directeur, new { classroomId = classroom, subjectId = subject }))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        var version = await TeacherVersionAsync(directeur, teacher);

        var blocked = await SendAsync(HttpMethod.Delete, $"/api/v1/teachers/{teacher}?rowVersion={version}", directeur);
        blocked.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var error = (await blocked.Content.ReadFromJsonAsync<ApiError>())!;
        error.Code.Should().Be("DEPENDENCIES_EXIST");
        error.Message.Should().Contain("1 affectation classe/matière").And.StartWith("Cet enseignant est actuellement rattaché");

        (await SendAsync(HttpMethod.Post, $"/api/v1/teachers/{teacher}/unlink-dependencies", directeur))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Delete, $"/api/v1/teachers/{teacher}?rowVersion={version}", directeur))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ------------------------------------------------------------------------------------------ Rôles et inconnus

    [Fact]
    public async Task Finance_Cannot_Use_The_Deletion_Routes_Of_Classrooms_Subjects_And_Teachers()
    {
        var directeur = await DirecteurTokenAsync();
        var classroom = await CreateClassroomAsync(directeur, "6e A");
        var subject = await CreateSubjectAsync(directeur, "Anglais");
        var teacher = await CreateTeacherAsync(directeur, "Awa Ba", "awa.ba@sama-ecole.sn", [subject]);
        var finance = await FinanceTokenAsync();

        foreach (var resource in new[] { $"classrooms/{classroom}", $"subjects/{subject}", $"teachers/{teacher}" })
        {
            (await SendAsync(HttpMethod.Get, $"/api/v1/{resource}/deletion-impact", finance)).StatusCode.Should().Be(HttpStatusCode.Forbidden, resource);
            (await SendAsync(HttpMethod.Post, $"/api/v1/{resource}/unlink-dependencies", finance)).StatusCode.Should().Be(HttpStatusCode.Forbidden, resource);
        }
    }

    [Fact]
    public async Task The_Deletion_Routes_Return_404_For_Unknown_Elements()
    {
        var directeur = await DirecteurTokenAsync();

        foreach (var resource in new[] { "classrooms", "subjects", "teachers" })
        {
            (await SendAsync(HttpMethod.Get, $"/api/v1/{resource}/{Guid.NewGuid()}/deletion-impact", directeur))
                .StatusCode.Should().Be(HttpStatusCode.NotFound, resource);
            (await SendAsync(HttpMethod.Post, $"/api/v1/{resource}/{Guid.NewGuid()}/unlink-dependencies", directeur))
                .StatusCode.Should().Be(HttpStatusCode.NotFound, resource);
        }
    }
}
