using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace SamaEcole.FunctionalTests.ReportCards;

/// <summary>
/// Ticket JGK-G03 — /report-cards/generate contre un vrai PostgreSQL, à travers la vraie pile HTTP.
/// </summary>
public class ReportCardsEndpointsTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public ReportCardsEndpointsTests(AuthApiFactory factory)
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

    private async Task<(Guid StudentId, Guid ClassroomId, Guid TermId)> SeedGradedStudentAsync(string directeurToken, string enseignantToken)
    {
        var classroomResponse = await SendAsync(HttpMethod.Post, "/api/v1/classrooms", directeurToken,
            // Classe du SECONDAIRE : notes sur /20, tableau de bulletin complet (coefficients,
            // appréciations, rangée des distinctions). Le rendu Primaire /10 est couvert par
            // ReportCardDocumentTests et GetReportCardPdfTests.
            new { name = "3e A", level = "Collège", capacity = 40 });
        var classroom = (await classroomResponse.Content.ReadFromJsonAsync<ClassroomDto>())!;

        var subjectResponse = await SendAsync(HttpMethod.Post, "/api/v1/subjects", directeurToken,
            new { name = "Mathématiques", level = "Primaire", coefficient = 4 });
        var subject = (await subjectResponse.Content.ReadFromJsonAsync<SubjectDto>())!;

        var studentResponse = await SendAsync(HttpMethod.Post, "/api/v1/students", directeurToken,
            new { fullName = "Élève de test", birthDate = "2015-01-01", birthPlace = "Dakar", gender = "M", classroomId = classroom.Id });
        var student = (await studentResponse.Content.ReadFromJsonAsync<StudentDto>())!;

        var yearResponse = await SendAsync(HttpMethod.Post, "/api/v1/school-years", directeurToken,
            new { label = "2026-2027", startDate = "2026-10-01", endDate = "2027-06-30" });
        var year = (await yearResponse.Content.ReadFromJsonAsync<SchoolYearDto>())!;

        var termsResponse = await SendAsync(HttpMethod.Get, $"/api/v1/school-years/{year.Id}/terms", directeurToken);
        var terms = (await termsResponse.Content.ReadFromJsonAsync<List<TermDto>>())!;
        var termId = terms[0].Id;

        await SendAsync(HttpMethod.Post, "/api/v1/grades", enseignantToken,
            new { studentId = student.Id, subjectId = subject.Id, termId, evaluationType = "Devoir1", value = 15 });
        await SendAsync(HttpMethod.Post, "/api/v1/grades", enseignantToken,
            new { studentId = student.Id, subjectId = subject.Id, termId, evaluationType = "Composition", value = 17 });

        return (student.Id, classroom.Id, termId);
    }

    [Fact]
    public async Task Generating_A_Report_Card_Should_Return_A_Valid_Pdf()
    {
        var directeur = await DirecteurTokenAsync();
        var enseignant = await EnseignantTokenAsync();
        var (studentId, _, termId) = await SeedGradedStudentAsync(directeur, enseignant);

        var response = await SendAsync(HttpMethod.Post, "/api/v1/report-cards/generate", directeur,
            new { studentId, termId });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");

        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Should().NotBeEmpty();
        Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
    }

    [Fact]
    public async Task An_Enseignant_Can_Also_Generate_The_Report_Card()
    {
        var directeur = await DirecteurTokenAsync();
        var enseignant = await EnseignantTokenAsync();
        var (studentId, _, termId) = await SeedGradedStudentAsync(directeur, enseignant);

        var response = await SendAsync(HttpMethod.Post, "/api/v1/report-cards/generate", enseignant,
            new { studentId, termId });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_Secretary_Can_Generate_A_Report_Card()
    {
        // Le Secrétariat compose et télécharge les bulletins (ReportCardDownloadRoles) mais n'écrit
        // jamais les observations du conseil, restées réservées à Directeur/Enseignant.
        var directeur = await DirecteurTokenAsync();
        var enseignant = await EnseignantTokenAsync();
        var (studentId, _, termId) = await SeedGradedStudentAsync(directeur, enseignant);
        var secretaire = await SecretaireTokenAsync();

        var response = await SendAsync(HttpMethod.Post, "/api/v1/report-cards/generate", secretaire,
            new { studentId, termId });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Generating_For_An_Unknown_Student_Should_Return_404()
    {
        var directeur = await DirecteurTokenAsync();
        var enseignant = await EnseignantTokenAsync();
        var (_, _, termId) = await SeedGradedStudentAsync(directeur, enseignant);

        var response = await SendAsync(HttpMethod.Post, "/api/v1/report-cards/generate", directeur,
            new { studentId = Guid.NewGuid(), termId });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Posting_Without_A_Token_Should_Return_401()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/report-cards/generate", new
        {
            studentId = Guid.NewGuid(), termId = Guid.NewGuid()
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Downloading_The_Class_Zip_Should_Return_A_Valid_Archive()
    {
        var directeur = await DirecteurTokenAsync();
        var enseignant = await EnseignantTokenAsync();
        var (_, classroomId, termId) = await SeedGradedStudentAsync(directeur, enseignant);

        var response = await SendAsync(HttpMethod.Get,
            $"/api/v1/report-cards/class-bulletins/zip?classroomId={classroomId}&termId={termId}", directeur);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/zip");

        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Should().NotBeEmpty();
        // Signature de fichier ZIP standard ("PK\x03\x04").
        bytes[0].Should().Be(0x50);
        bytes[1].Should().Be(0x4B);
    }

    [Fact]
    public async Task Downloading_The_Class_Merged_Pdf_Should_Return_A_Valid_Pdf()
    {
        var directeur = await DirecteurTokenAsync();
        var enseignant = await EnseignantTokenAsync();
        var (_, classroomId, termId) = await SeedGradedStudentAsync(directeur, enseignant);

        var response = await SendAsync(HttpMethod.Get,
            $"/api/v1/report-cards/class-bulletins/merged-pdf?classroomId={classroomId}&termId={termId}", directeur);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");

        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Should().NotBeEmpty();
        Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
    }

    [Fact]
    public async Task A_Secretary_Can_Download_Class_Bulletins()
    {
        var directeur = await DirecteurTokenAsync();
        var enseignant = await EnseignantTokenAsync();
        var (_, classroomId, termId) = await SeedGradedStudentAsync(directeur, enseignant);
        var secretaire = await SecretaireTokenAsync();

        var zipResponse = await SendAsync(HttpMethod.Get,
            $"/api/v1/report-cards/class-bulletins/zip?classroomId={classroomId}&termId={termId}", secretaire);
        var pdfResponse = await SendAsync(HttpMethod.Get,
            $"/api/v1/report-cards/class-bulletins/merged-pdf?classroomId={classroomId}&termId={termId}", secretaire);

        zipResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        pdfResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_Secretary_Can_Edit_The_Report_Card_Remark()
    {
        // Comme le téléchargement, la saisie des observations du conseil est ouverte au Secrétariat
        // (ReportCardWriterRoles) — il assure le suivi administratif de la vie scolaire au même titre
        // que la direction.
        var directeur = await DirecteurTokenAsync();
        var enseignant = await EnseignantTokenAsync();
        var (studentId, _, termId) = await SeedGradedStudentAsync(directeur, enseignant);
        var secretaire = await SecretaireTokenAsync();

        var response = await SendAsync(HttpMethod.Put, "/api/v1/report-cards/remark", secretaire,
            new { studentId, termId, disciplinaryMention = (string?)null, councilDecision = (string?)null, observations = "RAS" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Getting_The_Remark_Surfaces_The_Distinction_The_A5_Engine_Would_Print()
    {
        // Rien n'a encore été saisi par le conseil : la réponse porte SuggestedDisciplinaryMention, la
        // distinction que le bulletin imprimerait faute de saisie (DisciplinaryMentionPolicy). L'élève
        // seedé a 15 et 17 sur un unique coefficient → moyenne 16 → seuil « Félicitations ». L'écran de
        // saisie s'en sert pour pré-cocher la distinction, que le conseil valide ou remplace.
        var directeur = await DirecteurTokenAsync();
        var enseignant = await EnseignantTokenAsync();
        var (studentId, _, termId) = await SeedGradedStudentAsync(directeur, enseignant);

        var response = await SendAsync(HttpMethod.Get,
            $"/api/v1/report-cards/remark?studentId={studentId}&termId={termId}", directeur);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var remark = (await response.Content.ReadFromJsonAsync<RemarkResponse>())!;
        remark.DisciplinaryMention.Should().BeNull("le conseil n'a encore rien saisi");
        remark.SuggestedDisciplinaryMention.Should().Be("Felicitations");
        remark.GeneralAverage.Should().Be(16m);
    }

    private record RemarkResponse(
        string? DisciplinaryMention, string? CouncilDecision, string? Observations,
        string? SuggestedDisciplinaryMention, decimal? GeneralAverage);

    [Fact]
    public async Task Downloading_Class_Bulletins_For_An_Unknown_Classroom_Should_Return_404()
    {
        var directeur = await DirecteurTokenAsync();
        var enseignant = await EnseignantTokenAsync();
        var (_, _, termId) = await SeedGradedStudentAsync(directeur, enseignant);

        var response = await SendAsync(HttpMethod.Get,
            $"/api/v1/report-cards/class-bulletins/zip?classroomId={Guid.NewGuid()}&termId={termId}", directeur);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Downloading_Class_Bulletins_For_An_Empty_Classroom_Should_Return_409()
    {
        var directeur = await DirecteurTokenAsync();
        var enseignant = await EnseignantTokenAsync();
        var (_, _, termId) = await SeedGradedStudentAsync(directeur, enseignant);

        var emptyClassroomResponse = await SendAsync(HttpMethod.Post, "/api/v1/classrooms", directeur,
            new { name = "3e B", level = "Collège", capacity = 40 });
        var emptyClassroom = (await emptyClassroomResponse.Content.ReadFromJsonAsync<ClassroomDto>())!;

        var response = await SendAsync(HttpMethod.Get,
            $"/api/v1/report-cards/class-bulletins/zip?classroomId={emptyClassroom.Id}&termId={termId}", directeur);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
