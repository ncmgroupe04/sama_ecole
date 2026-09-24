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
/// POST (Saisir) est ouvert au Directeur, au Secrétariat et à l'Enseignant. PUT (Corriger) l'est aussi,
/// l'Enseignant étant borné dans le Handler par la fenêtre GradeEditWindowDays et par la propriété de
/// la note (Évolution N°1, GradeEditPolicy — le détail du délai est testé en intégration avec une
/// horloge simulée). DELETE (Annuler) reste réservé au Directeur et au Secrétariat.
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
    private record ClassroomDto(Guid Id, string Name, string Level, int Capacity, string? Cycle);
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
    /// <param name="level">
    /// Niveau de la classe, dont se déduit le CYCLE (ClassroomCycle) et donc le barème. « Collège » par
    /// défaut : la majorité de ces tests saisissent des notes sur /20, qu'une classe « Primaire »
    /// (plafonnée à /10) refuserait en 422.
    /// </param>
    private async Task<(Guid StudentId, Guid SubjectId, Guid TermId)> SeedGradingContextAsync(
        string directeurToken, string level = "Collège")
    {
        var classroomResponse = await SendAsync(HttpMethod.Post, "/api/v1/classrooms", directeurToken,
            new { name = "3e A", level, capacity = 40 });
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

        var response = await CreateGradeAsync(enseignant, studentId, subjectId, termId, "Devoir1", 15);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var grade = (await response.Content.ReadFromJsonAsync<GradeDto>())!;
        grade.Value.Should().Be(15);
        grade.RowVersion.Should().NotBe(0u);
    }

    [Fact]
    public async Task A_Directeur_Can_Enter_A_New_Grade()
    {
        // « Saisir » est ouvert au Directeur et à l'Enseignant (docs/Volume_7_Security.md « Notes »).
        var directeur = await DirecteurTokenAsync();
        var (studentId, subjectId, termId) = await SeedGradingContextAsync(directeur);

        var response = await CreateGradeAsync(directeur, studentId, subjectId, termId, "Devoir1", 15);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var grade = (await response.Content.ReadFromJsonAsync<GradeDto>())!;
        grade.Value.Should().Be(15);
        grade.RowVersion.Should().NotBe(0u);
    }

    [Fact]
    public async Task A_Secretary_Can_Enter_Grades_For_Any_Class()
    {
        // Évolution N°1 : le Secrétariat saisit, sans périmètre par classe.
        var directeur = await DirecteurTokenAsync();
        var (studentId, subjectId, termId) = await SeedGradingContextAsync(directeur);
        var secretaire = await SecretaireTokenAsync();

        var response = await CreateGradeAsync(secretaire, studentId, subjectId, termId, "Devoir1", 13);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_Finance_User_Must_Not_Enter_Grades()
    {
        var directeur = await DirecteurTokenAsync();
        var (studentId, subjectId, termId) = await SeedGradingContextAsync(directeur);
        var finance = await AccessTokenAsync(AuthApiFactory.FinanceEmail, AuthApiFactory.FinancePassword);

        var response = await CreateGradeAsync(finance, studentId, subjectId, termId, "Devoir1", 13);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_Grade_Above_The_Grading_Scale_Should_Return_422()
    {
        var directeur = await DirecteurTokenAsync();
        var (studentId, subjectId, termId) = await SeedGradingContextAsync(directeur);
        var enseignant = await EnseignantTokenAsync();

        // Barème par défaut de l'école : 20 (SchoolSettingsDefaults.GradingScale).
        var response = await CreateGradeAsync(enseignant, studentId, subjectId, termId, "Devoir1", 25);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>
    /// Barème du PRIMAIRE : /10, et non le /20 du secondaire. 15 est une note parfaitement valide au
    /// collège — c'est le cycle de la classe, pas un réglage d'école, qui doit la faire refuser ici.
    /// Le serveur reste l'autorité : l'attribut max du champ de saisie n'est qu'un confort.
    /// </summary>
    [Theory]
    [InlineData("Primaire")]
    [InlineData("Maternelle")]
    public async Task A_Grade_Above_Ten_Should_Return_422_For_A_Simplified_Grading_Cycle(string level)
    {
        var directeur = await DirecteurTokenAsync();
        var (studentId, subjectId, termId) = await SeedGradingContextAsync(directeur, level);
        var enseignant = await EnseignantTokenAsync();

        var response = await CreateGradeAsync(enseignant, studentId, subjectId, termId, "Devoir1", 15);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>Contre-épreuve du test ci-dessus : sous le plafond /10, la saisie passe normalement.</summary>
    [Theory]
    [InlineData("Primaire")]
    [InlineData("Maternelle")]
    public async Task A_Grade_Within_Ten_Should_Be_Accepted_For_A_Simplified_Grading_Cycle(string level)
    {
        var directeur = await DirecteurTokenAsync();
        var (studentId, subjectId, termId) = await SeedGradingContextAsync(directeur, level);
        var enseignant = await EnseignantTokenAsync();

        var response = await CreateGradeAsync(enseignant, studentId, subjectId, termId, "Devoir1", 8.5m);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    /// <summary>
    /// Le cycle doit VOYAGER jusqu'au client : c'est `cycle` qui pilote l'affichage « /10 » ou « /20 »
    /// et l'attribut max du champ de saisie (wwwroot/js/grades.js, SIMPLIFIED_GRADING_CYCLES). Une
    /// classe créée « Primaire » mais renvoyée sur College afficherait un /20 trompeur, suivi d'un 422
    /// que rien n'annonçait — la régression corrigée le 2026-08-03 côté seeder.
    /// </summary>
    [Theory]
    [InlineData("Primaire", "Primaire")]
    [InlineData("Maternelle", "Maternelle")]
    [InlineData("Collège", "College")]
    [InlineData("Lycée", "Lycee")]
    public async Task A_Classroom_Should_Expose_The_Cycle_Derived_From_Its_Level(string level, string expectedCycle)
    {
        var directeur = await DirecteurTokenAsync();

        var response = await SendAsync(HttpMethod.Post, "/api/v1/classrooms", directeur,
            new { name = $"Classe {level}", level, capacity = 30 });
        var classroom = (await response.Content.ReadFromJsonAsync<ClassroomDto>())!;

        classroom.Cycle.Should().Be(expectedCycle);
    }

    [Fact]
    public async Task An_Unknown_Student_Should_Return_422()
    {
        var directeur = await DirecteurTokenAsync();
        var (_, subjectId, termId) = await SeedGradingContextAsync(directeur);
        var enseignant = await EnseignantTokenAsync();

        var response = await CreateGradeAsync(enseignant, Guid.NewGuid(), subjectId, termId, "Devoir1", 12);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Devoir_And_Composition_Are_Independent_Entries_For_The_Same_Subject()
    {
        var directeur = await DirecteurTokenAsync();
        var (studentId, subjectId, termId) = await SeedGradingContextAsync(directeur);
        var enseignant = await EnseignantTokenAsync();

        var devoir = await CreateGradeAsync(enseignant, studentId, subjectId, termId, "Devoir1", 10);
        var composition = await CreateGradeAsync(enseignant, studentId, subjectId, termId, "Composition", 18);

        devoir.StatusCode.Should().Be(HttpStatusCode.Created);
        composition.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task An_Enseignant_Can_Correct_His_Own_Grade_Inside_The_Default_Window()
    {
        // Évolution N°1 : l'auteur corrige sa note pendant GradeEditWindowDays (7 jours par défaut).
        // Le refus hors fenêtre et le refus « ni auteur ni affecté » sont testés en intégration
        // (GradeCorrectionTests), où l'horloge est simulée.
        var directeur = await DirecteurTokenAsync();
        var (studentId, subjectId, termId) = await SeedGradingContextAsync(directeur);
        var enseignant = await EnseignantTokenAsync();

        var created = await CreateGradeAsync(enseignant, studentId, subjectId, termId, "Devoir1", 12);
        var grade = (await created.Content.ReadFromJsonAsync<GradeDto>())!;

        var corrected = await SendAsync(HttpMethod.Put, $"/api/v1/grades/{grade.Id}", enseignant,
            new { value = 14, rowVersion = grade.RowVersion });

        corrected.StatusCode.Should().Be(HttpStatusCode.OK);
        (await corrected.Content.ReadFromJsonAsync<GradeDto>())!.Value.Should().Be(14);
    }

    [Fact]
    public async Task An_Enseignant_Cannot_Correct_A_Grade_Entered_By_The_Directeur_When_Not_Assigned()
    {
        // Ni auteur (la note est celle du Directeur), ni affecté à la classe/matière : 403.
        var directeur = await DirecteurTokenAsync();
        var (studentId, subjectId, termId) = await SeedGradingContextAsync(directeur);
        var enseignant = await EnseignantTokenAsync();

        var created = await CreateGradeAsync(directeur, studentId, subjectId, termId, "Devoir1", 12);
        var grade = (await created.Content.ReadFromJsonAsync<GradeDto>())!;

        var corrected = await SendAsync(HttpMethod.Put, $"/api/v1/grades/{grade.Id}", enseignant,
            new { value = 14, rowVersion = grade.RowVersion });

        corrected.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_Directeur_Can_Correct_A_Grade_Entered_By_The_Enseignant()
    {
        var directeur = await DirecteurTokenAsync();
        var (studentId, subjectId, termId) = await SeedGradingContextAsync(directeur);
        var enseignant = await EnseignantTokenAsync();

        var created = await CreateGradeAsync(enseignant, studentId, subjectId, termId, "Devoir1", 12);
        var grade = (await created.Content.ReadFromJsonAsync<GradeDto>())!;

        var corrected = await SendAsync(HttpMethod.Put, $"/api/v1/grades/{grade.Id}", directeur,
            new { value = 16, rowVersion = grade.RowVersion });

        corrected.StatusCode.Should().Be(HttpStatusCode.OK);
        (await corrected.Content.ReadFromJsonAsync<GradeDto>())!.Value.Should().Be(16);
    }

    [Fact]
    public async Task A_Secretary_Can_Correct_A_Grade()
    {
        // Contrôle strict de la matrice d'autorisation "Photoshop" : le Secrétariat corrige/annule
        // une note déjà saisie, à la place de l'Enseignant qui en perd le droit une fois enregistrée.
        var directeur = await DirecteurTokenAsync();
        var (studentId, subjectId, termId) = await SeedGradingContextAsync(directeur);
        var enseignant = await EnseignantTokenAsync();

        var created = await CreateGradeAsync(enseignant, studentId, subjectId, termId, "Devoir1", 12);
        var grade = (await created.Content.ReadFromJsonAsync<GradeDto>())!;

        var secretaire = await SecretaireTokenAsync();
        var response = await SendAsync(HttpMethod.Put, $"/api/v1/grades/{grade.Id}", secretaire,
            new { value = 16, rowVersion = grade.RowVersion });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<GradeDto>())!.Value.Should().Be(16);
    }

    [Fact]
    public async Task A_Secretary_Can_View_The_Grades_List()
    {
        var directeur = await DirecteurTokenAsync();
        var (studentId, subjectId, termId) = await SeedGradingContextAsync(directeur);
        var enseignant = await EnseignantTokenAsync();
        await CreateGradeAsync(enseignant, studentId, subjectId, termId, "Devoir1", 12);

        var secretaire = await SecretaireTokenAsync();
        var classroomsResponse = await SendAsync(HttpMethod.Get, "/api/v1/classrooms", secretaire);
        var classroomId = (await classroomsResponse.Content.ReadFromJsonAsync<List<ClassroomDto>>())!.Single().Id;

        var response = await SendAsync(HttpMethod.Get,
            $"/api/v1/grades?classroomId={classroomId}&subjectId={subjectId}&termId={termId}", secretaire);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Correcting_A_Grade_With_A_Stale_Row_Version_Should_Return_409()
    {
        var directeur = await DirecteurTokenAsync();
        var (studentId, subjectId, termId) = await SeedGradingContextAsync(directeur);
        var enseignant = await EnseignantTokenAsync();

        var created = await CreateGradeAsync(enseignant, studentId, subjectId, termId, "Devoir1", 12);
        var grade = (await created.Content.ReadFromJsonAsync<GradeDto>())!;

        // Le Directeur corrige — la version en base bascule.
        await SendAsync(HttpMethod.Put, $"/api/v1/grades/{grade.Id}", directeur,
            new { value = 16, rowVersion = grade.RowVersion });

        // Le Secrétariat, resté sur l'ancienne lecture, tente de corriger avec le jeton PÉRIMÉ.
        var secretaire = await SecretaireTokenAsync();
        var conflict = await SendAsync(HttpMethod.Put, $"/api/v1/grades/{grade.Id}", secretaire,
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

    // ---------------------------------------------------------- Annulation (DELETE) — matrice "Photoshop"

    [Fact]
    public async Task A_Directeur_Can_Delete_A_Grade()
    {
        var directeur = await DirecteurTokenAsync();
        var (studentId, subjectId, termId) = await SeedGradingContextAsync(directeur);
        var enseignant = await EnseignantTokenAsync();

        var created = await CreateGradeAsync(enseignant, studentId, subjectId, termId, "Devoir1", 12);
        var grade = (await created.Content.ReadFromJsonAsync<GradeDto>())!;

        var response = await SendAsync(
            HttpMethod.Delete, $"/api/v1/grades/{grade.Id}?rowVersion={grade.RowVersion}", directeur);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task A_Secretary_Can_Delete_A_Grade()
    {
        var directeur = await DirecteurTokenAsync();
        var (studentId, subjectId, termId) = await SeedGradingContextAsync(directeur);
        var enseignant = await EnseignantTokenAsync();

        var created = await CreateGradeAsync(enseignant, studentId, subjectId, termId, "Devoir1", 12);
        var grade = (await created.Content.ReadFromJsonAsync<GradeDto>())!;

        var secretaire = await SecretaireTokenAsync();
        var response = await SendAsync(
            HttpMethod.Delete, $"/api/v1/grades/{grade.Id}?rowVersion={grade.RowVersion}", secretaire);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task An_Enseignant_Must_Not_Delete_A_Grade_Even_Their_Own()
    {
        var directeur = await DirecteurTokenAsync();
        var (studentId, subjectId, termId) = await SeedGradingContextAsync(directeur);
        var enseignant = await EnseignantTokenAsync();

        var created = await CreateGradeAsync(enseignant, studentId, subjectId, termId, "Devoir1", 12);
        var grade = (await created.Content.ReadFromJsonAsync<GradeDto>())!;

        var response = await SendAsync(
            HttpMethod.Delete, $"/api/v1/grades/{grade.Id}?rowVersion={grade.RowVersion}", enseignant);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Deleting_A_Grade_With_A_Stale_Row_Version_Should_Return_409()
    {
        var directeur = await DirecteurTokenAsync();
        var (studentId, subjectId, termId) = await SeedGradingContextAsync(directeur);
        var enseignant = await EnseignantTokenAsync();

        var created = await CreateGradeAsync(enseignant, studentId, subjectId, termId, "Devoir1", 12);
        var grade = (await created.Content.ReadFromJsonAsync<GradeDto>())!;
        var staleVersion = grade.RowVersion;

        // Une correction entre-temps fait tourner le jeton xmin.
        await SendAsync(HttpMethod.Put, $"/api/v1/grades/{grade.Id}", directeur,
            new { value = 16, rowVersion = staleVersion });

        var response = await SendAsync(
            HttpMethod.Delete, $"/api/v1/grades/{grade.Id}?rowVersion={staleVersion}", directeur);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Deleting_An_Unknown_Grade_Should_Return_404()
    {
        var directeur = await DirecteurTokenAsync();

        var response = await SendAsync(
            HttpMethod.Delete, $"/api/v1/grades/{Guid.NewGuid()}?rowVersion=1", directeur);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Posting_Without_A_Token_Should_Return_401()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/grades", new
        {
            studentId = Guid.NewGuid(), subjectId = Guid.NewGuid(), termId = Guid.NewGuid(),
            evaluationType = "Devoir1", value = 12
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
