using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace SamaEcole.FunctionalTests.Grades;

/// <summary>
/// POST /grades/import contre un vrai PostgreSQL, à travers la vraie pile HTTP — même permission que
/// POST /grades (Saisir = Enseignant seul, docs/Volume_7_Security.md « Notes »).
///
/// Le contrat central du ticket : le fichier est validé INTÉGRALEMENT avant la moindre écriture (une
/// seule ligne en erreur → rien n'est enregistré), et un matricule d'un élève d'une AUTRE classe que
/// celle visée par l'import est un motif de rejet à part entière.
/// </summary>
public class GradeImportEndpointsTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public GradeImportEndpointsTests(AuthApiFactory factory)
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
    private record ImportResultDto(int Created, int Updated, int Unchanged);
    private record ErrorResponseDto(string Code, string Message, Dictionary<string, string[]>? Details, string TraceId);
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

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> ImportAsync(
        string token, Guid classroomId, Guid subjectId, Guid termId, string evaluationType,
        string fileContent, string fileName = "notes.csv")
    {
        using var content = new MultipartFormDataContent
        {
            { new StringContent(classroomId.ToString()), "classroomId" },
            { new StringContent(subjectId.ToString()), "subjectId" },
            { new StringContent(termId.ToString()), "termId" },
            { new StringContent(evaluationType), "evaluationType" }
        };
        var fileBytes = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(fileContent));
        fileBytes.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(fileBytes, "file", fileName);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/grades/import") { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(request);
    }

    /// <summary>Une classe de deux élèves, une matière, l'année active (et son 1er trimestre).</summary>
    private async Task<(Guid ClassroomId, Guid SubjectId, Guid TermId, StudentDto Student1, StudentDto Student2)>
        SeedGradingContextAsync(string directeurToken)
    {
        var classroomResponse = await SendAsync(HttpMethod.Post, "/api/v1/classrooms", directeurToken,
            new { name = "CM2", level = "Primaire", capacity = 40 });
        var classroom = (await classroomResponse.Content.ReadFromJsonAsync<ClassroomDto>())!;

        var subjectResponse = await SendAsync(HttpMethod.Post, "/api/v1/subjects", directeurToken,
            new { name = "Mathématiques", level = "Primaire", coefficient = 4 });
        var subject = (await subjectResponse.Content.ReadFromJsonAsync<SubjectDto>())!;

        var student1Response = await SendAsync(HttpMethod.Post, "/api/v1/students", directeurToken,
            new { fullName = "Premier Élève", birthDate = "2015-01-01", gender = "M", classroomId = classroom.Id });
        var student1 = (await student1Response.Content.ReadFromJsonAsync<StudentDto>())!;

        var student2Response = await SendAsync(HttpMethod.Post, "/api/v1/students", directeurToken,
            new { fullName = "Second Élève", birthDate = "2015-02-01", gender = "F", classroomId = classroom.Id });
        var student2 = (await student2Response.Content.ReadFromJsonAsync<StudentDto>())!;

        var yearResponse = await SendAsync(HttpMethod.Post, "/api/v1/school-years", directeurToken,
            new { label = "2026-2027", startDate = "2026-10-01", endDate = "2027-06-30" });
        var year = (await yearResponse.Content.ReadFromJsonAsync<SchoolYearDto>())!;

        var termsResponse = await SendAsync(HttpMethod.Get, $"/api/v1/school-years/{year.Id}/terms", directeurToken);
        var terms = (await termsResponse.Content.ReadFromJsonAsync<List<TermDto>>())!;

        return (classroom.Id, subject.Id, terms[0].Id, student1, student2);
    }

    [Fact]
    public async Task A_Well_Formed_Csv_Should_Create_Grades_For_Every_Student()
    {
        var directeur = await DirecteurTokenAsync();
        var (classroomId, subjectId, termId, student1, student2) = await SeedGradingContextAsync(directeur);
        var enseignant = await EnseignantTokenAsync();

        var csv = $"Matricule;Note\n{student1.Matricule};15\n{student2.Matricule};12,5";
        var response = await ImportAsync(enseignant, classroomId, subjectId, termId, "Devoir", csv);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<ImportResultDto>())!;
        result.Created.Should().Be(2);
        result.Updated.Should().Be(0);

        var grades = await SendAsync(HttpMethod.Get,
            $"/api/v1/grades?classroomId={classroomId}&subjectId={subjectId}&termId={termId}", directeur);
        var rows = (await grades.Content.ReadFromJsonAsync<List<StudentGradeRowDto>>())!;
        rows.Should().ContainSingle(r => r.StudentId == student1.Id && r.Devoir!.Value == 15);
        rows.Should().ContainSingle(r => r.StudentId == student2.Id && r.Devoir!.Value == 12.5m,
            "la notation FR à virgule (\"12,5\") doit être comprise comme une note décimale");
    }

    [Fact]
    public async Task ReImporting_With_A_Different_Value_Should_Update_Instead_Of_Duplicating()
    {
        var directeur = await DirecteurTokenAsync();
        var (classroomId, subjectId, termId, student1, student2) = await SeedGradingContextAsync(directeur);
        var enseignant = await EnseignantTokenAsync();

        await ImportAsync(enseignant, classroomId, subjectId, termId, "Devoir",
            $"{student1.Matricule};10\n{student2.Matricule};10");

        var second = await ImportAsync(enseignant, classroomId, subjectId, termId, "Devoir",
            $"{student1.Matricule};14\n{student2.Matricule};10");

        second.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await second.Content.ReadFromJsonAsync<ImportResultDto>())!;
        result.Created.Should().Be(0);
        result.Updated.Should().Be(1);
        result.Unchanged.Should().Be(1);
    }

    [Fact]
    public async Task A_Directeur_Must_Not_Import_Grades()
    {
        var directeur = await DirecteurTokenAsync();
        var (classroomId, subjectId, termId, student1, _) = await SeedGradingContextAsync(directeur);

        var response = await ImportAsync(directeur, classroomId, subjectId, termId, "Devoir", $"{student1.Matricule};15");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_Unknown_Matricule_Should_Return_422_With_The_Faulty_Line_Identified()
    {
        var directeur = await DirecteurTokenAsync();
        var (classroomId, subjectId, termId, _, _) = await SeedGradingContextAsync(directeur);
        var enseignant = await EnseignantTokenAsync();

        var response = await ImportAsync(enseignant, classroomId, subjectId, termId, "Devoir", "ELEV-INCONNU-9999;15");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var error = (await response.Content.ReadFromJsonAsync<ErrorResponseDto>())!;
        error.Details.Should().ContainKey("Ligne 1");
    }

    [Fact]
    public async Task A_Matricule_Belonging_To_Another_Classroom_Must_Be_Rejected()
    {
        // Le cœur du ticket : une note ne doit jamais atterrir sur l'élève d'une autre classe parce
        // qu'un fichier a été importé sur le mauvais écran, ou contient une ligne erronée.
        var directeur = await DirecteurTokenAsync();
        var (classroomId, subjectId, termId, student1, _) = await SeedGradingContextAsync(directeur);

        // Une SECONDE classe, avec son propre élève — hors du périmètre de l'import ci-dessous.
        var otherClassroomResponse = await SendAsync(HttpMethod.Post, "/api/v1/classrooms", directeur,
            new { name = "CM1", level = "Primaire", capacity = 40 });
        var otherClassroom = (await otherClassroomResponse.Content.ReadFromJsonAsync<ClassroomDto>())!;
        var otherStudentResponse = await SendAsync(HttpMethod.Post, "/api/v1/students", directeur,
            new { fullName = "Élève d'une autre classe", birthDate = "2015-03-01", gender = "M", classroomId = otherClassroom.Id });
        var otherStudent = (await otherStudentResponse.Content.ReadFromJsonAsync<StudentDto>())!;

        var enseignant = await EnseignantTokenAsync();

        // Import sur la PREMIÈRE classe, avec le matricule d'un élève de la SECONDE.
        var response = await ImportAsync(enseignant, classroomId, subjectId, termId, "Devoir", $"{otherStudent.Matricule};15");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        // Rien n'a été écrit sur la classe visée : ni sur l'élève étranger (rejeté), ni sur le vrai
        // élève de la classe (jamais mentionné dans ce fichier, donc logiquement toujours vierge).
        var gradesTargetClass = await SendAsync(HttpMethod.Get,
            $"/api/v1/grades?classroomId={classroomId}&subjectId={subjectId}&termId={termId}", directeur);
        var rows = (await gradesTargetClass.Content.ReadFromJsonAsync<List<StudentGradeRowDto>>())!;
        rows.Should().ContainSingle(r => r.StudentId == student1.Id && r.Devoir == null);
    }

    [Fact]
    public async Task A_Duplicate_Matricule_Within_The_File_Should_Be_Rejected()
    {
        var directeur = await DirecteurTokenAsync();
        var (classroomId, subjectId, termId, student1, _) = await SeedGradingContextAsync(directeur);
        var enseignant = await EnseignantTokenAsync();

        var csv = $"{student1.Matricule};15\n{student1.Matricule};10";
        var response = await ImportAsync(enseignant, classroomId, subjectId, termId, "Devoir", csv);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_Grade_Above_The_Grading_Scale_Should_Be_Rejected()
    {
        var directeur = await DirecteurTokenAsync();
        var (classroomId, subjectId, termId, student1, _) = await SeedGradingContextAsync(directeur);
        var enseignant = await EnseignantTokenAsync();

        var response = await ImportAsync(enseignant, classroomId, subjectId, termId, "Devoir", $"{student1.Matricule};25");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task One_Invalid_Line_Must_Prevent_The_Whole_File_From_Being_Applied()
    {
        // La ligne 1 est valide, la ligne 2 porte un matricule inconnu : AUCUNE des deux ne doit être
        // enregistrée — validation intégrale avant écriture, jamais un import partiel.
        var directeur = await DirecteurTokenAsync();
        var (classroomId, subjectId, termId, student1, _) = await SeedGradingContextAsync(directeur);
        var enseignant = await EnseignantTokenAsync();

        var csv = $"{student1.Matricule};15\nELEV-INCONNU-9999;10";
        var response = await ImportAsync(enseignant, classroomId, subjectId, termId, "Devoir", csv);
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var grades = await SendAsync(HttpMethod.Get,
            $"/api/v1/grades?classroomId={classroomId}&subjectId={subjectId}&termId={termId}", directeur);
        var rows = (await grades.Content.ReadFromJsonAsync<List<StudentGradeRowDto>>())!;
        rows.Should().ContainSingle(r => r.StudentId == student1.Id && r.Devoir == null,
            "la ligne valide ne doit pas être enregistrée tant que le fichier contient une autre ligne en erreur");
    }

    [Fact]
    public async Task An_Unsupported_File_Extension_Should_Be_Rejected()
    {
        var directeur = await DirecteurTokenAsync();
        var (classroomId, subjectId, termId, student1, _) = await SeedGradingContextAsync(directeur);
        var enseignant = await EnseignantTokenAsync();

        var response = await ImportAsync(
            enseignant, classroomId, subjectId, termId, "Devoir", $"{student1.Matricule};15", fileName: "notes.docx");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Importing_Without_A_Token_Should_Return_401()
    {
        var directeur = await DirecteurTokenAsync();
        var (classroomId, subjectId, termId, student1, _) = await SeedGradingContextAsync(directeur);

        using var content = new MultipartFormDataContent
        {
            { new StringContent(classroomId.ToString()), "classroomId" },
            { new StringContent(subjectId.ToString()), "subjectId" },
            { new StringContent(termId.ToString()), "termId" },
            { new StringContent("Devoir"), "evaluationType" }
        };
        var fileBytes = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes($"{student1.Matricule};15"));
        content.Add(fileBytes, "file", "notes.csv");

        var response = await _client.PostAsync("/api/v1/grades/import", content);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
