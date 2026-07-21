using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace SamaEcole.FunctionalTests.Grades;

/// <summary>
/// Ticket JGK-G02 — /grades/calculate et /grades/mentions contre un vrai PostgreSQL.
/// </summary>
public class GradeSummaryEndpointsTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public GradeSummaryEndpointsTests(AuthApiFactory factory)
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
    private record SubjectGradeDto(Guid SubjectId, string SubjectName, decimal? Devoir, decimal? Composition, decimal Average, decimal Coefficient, decimal WeightedPoints);
    private record GradeSummaryDto(Guid StudentId, Guid TermId, List<SubjectGradeDto> Subjects, decimal TotalCoefficients, decimal TotalPoints, decimal GeneralAverage, string? Mention);
    private record MentionDto(Guid? Id, string Label, decimal MinAverage);

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

    private async Task<(Guid StudentId, Guid SubjectId, Guid TermId)> SeedGradingContextAsync(string directeurToken)
    {
        var classroomResponse = await SendAsync(HttpMethod.Post, "/api/v1/classrooms", directeurToken,
            // Classe du SECONDAIRE : notes sur /20 ET moyenne PONDÉRÉE par coefficients — le primaire
            // calcule, lui, une moyenne simple sur /10 (GetGradeSummaryQueryHandler).
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

        return (student.Id, subject.Id, terms[0].Id);
    }

    /// <summary>Seul geste du Directeur capable d'activer la délégation (ticket JGK-G02) : PUT /schools/current/settings.</summary>
    private async Task EnableSecretaryDelegationAsync(string directeurToken)
    {
        var response = await SendAsync(HttpMethod.Put, "/api/v1/schools/current/settings", directeurToken, new
        {
            gradingScale = "20",
            studentMatriculeFormat = "ELEV-{YEAR}-{SEQ:4}",
            teacherMatriculeFormat = "ENS-{YEAR}-{SEQ:3}",
            autoLogoutMinutes = 10,
            dateFormat = "dd/MM/yyyy",
            tuitionMonthsPerYear = 9,
            allowSecretaryToManageGrading = true
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Calculate_Returns_The_Weighted_General_Average_And_Mention()
    {
        var directeur = await DirecteurTokenAsync();
        var (studentId, subjectId, termId) = await SeedGradingContextAsync(directeur);
        var enseignant = await EnseignantTokenAsync();

        await SendAsync(HttpMethod.Post, "/api/v1/grades", enseignant,
            new { studentId, subjectId, termId, evaluationType = "Devoir", value = 16 });
        await SendAsync(HttpMethod.Post, "/api/v1/grades", enseignant,
            new { studentId, subjectId, termId, evaluationType = "Composition", value = 18 });

        var response = await SendAsync(
            HttpMethod.Get, $"/api/v1/grades/calculate?studentId={studentId}&termId={termId}", directeur);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var summary = (await response.Content.ReadFromJsonAsync<GradeSummaryDto>())!;

        summary.Subjects.Should().ContainSingle().Which.Average.Should().Be(17);
        summary.GeneralAverage.Should().Be(17);
        summary.Mention.Should().Be("Excellent");
    }

    [Fact]
    public async Task A_Secretary_Must_Not_Read_The_Grade_Summary()
    {
        var directeur = await DirecteurTokenAsync();
        var (studentId, _, termId) = await SeedGradingContextAsync(directeur);
        var secretaire = await SecretaireTokenAsync();

        var response = await SendAsync(
            HttpMethod.Get, $"/api/v1/grades/calculate?studentId={studentId}&termId={termId}", secretaire);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Listing_Mentions_Without_Customization_Returns_The_Five_Defaults()
    {
        var directeur = await DirecteurTokenAsync();

        var response = await SendAsync(HttpMethod.Get, "/api/v1/grades/mentions", directeur);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var mentions = (await response.Content.ReadFromJsonAsync<List<MentionDto>>())!;
        mentions.Should().HaveCount(5);
        mentions.Should().Contain(m => m.Label == "Excellent" && m.MinAverage == 16);
    }

    [Fact]
    public async Task A_Directeur_Can_Create_A_Custom_Mention()
    {
        var directeur = await DirecteurTokenAsync();

        var response = await SendAsync(HttpMethod.Post, "/api/v1/grades/mentions", directeur,
            new { label = "Mention Maison", minAverage = 5 });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var mention = (await response.Content.ReadFromJsonAsync<MentionDto>())!;
        mention.Label.Should().Be("Mention Maison");
    }

    [Fact]
    public async Task A_Secretariat_Must_Not_Create_A_Mention_By_Default()
    {
        // Ticket JGK-G02 : la délégation est FACULTATIVE, fermée tant que le Directeur ne l'a pas
        // explicitement activée (SchoolSettingsDefaults.AllowSecretaryToManageGrading = false).
        var secretaire = await SecretaireTokenAsync();

        var response = await SendAsync(HttpMethod.Post, "/api/v1/grades/mentions", secretaire,
            new { label = "Mention Secrétariat", minAverage = 5 });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_Secretariat_Can_Create_A_Custom_Mention_Once_The_Directeur_Enables_Delegation()
    {
        var directeur = await DirecteurTokenAsync();
        await EnableSecretaryDelegationAsync(directeur);

        var secretaire = await SecretaireTokenAsync();

        var response = await SendAsync(HttpMethod.Post, "/api/v1/grades/mentions", secretaire,
            new { label = "Mention Secrétariat", minAverage = 5 });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_Secretariat_Can_Read_The_Mentions_Even_Without_Delegation_Enabled()
    {
        // Nécessaire pour composer les bulletins : la LECTURE des mentions n'est jamais conditionnée
        // par la délégation, contrairement à l'écriture (GradesController.MentionReadRoles).
        var secretaire = await SecretaireTokenAsync();

        var response = await SendAsync(HttpMethod.Get, "/api/v1/grades/mentions", secretaire);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_Enseignant_Must_Not_Create_A_Mention_Even_With_Delegation_Enabled()
    {
        // Configurer les mentions relève des paramètres d'établissement, réservés au Directeur et,
        // si délégué, au Secrétariat (docs/Volume_7_Security.md « Paramètres de l'école ») — la
        // délégation ne concerne QUE le Secrétariat, elle n'ouvre rien à l'Enseignant.
        var directeur = await DirecteurTokenAsync();
        await EnableSecretaryDelegationAsync(directeur);

        var enseignant = await EnseignantTokenAsync();

        var response = await SendAsync(HttpMethod.Post, "/api/v1/grades/mentions", enseignant,
            new { label = "Mention Maison", minAverage = 5 });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Creating_A_Duplicate_Mention_Label_Returns_409()
    {
        var directeur = await DirecteurTokenAsync();

        await SendAsync(HttpMethod.Post, "/api/v1/grades/mentions", directeur, new { label = "Top", minAverage = 18 });
        var duplicate = await SendAsync(HttpMethod.Post, "/api/v1/grades/mentions", directeur, new { label = "Top", minAverage = 10 });

        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
