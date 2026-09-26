using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.FunctionalTests.Common;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace SamaEcole.FunctionalTests.Reports;

/// <summary>
/// Ticket JGK-R02 — GET /api/v1/reports/attendance contre un vrai PostgreSQL. Vérifie les permissions
/// (Directeur/Secrétariat/Super Admin ; Finance et Enseignant exclus), l'EXACTITUDE des statistiques
/// par élève, le filtre par classe, et le refus d'un classId hors périmètre (falsification).
/// </summary>
public class AttendanceReportEndpointsTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public AttendanceReportEndpointsTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public Task InitializeAsync() => _factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static readonly Guid EcoleId = AuthApiFactory.EcoleId;

    // Classes connues du scénario, pour filtrer et assert.
    private static readonly Guid ClasseA = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000aa");
    private static readonly Guid ClasseB = Guid.Parse("bbbbbbbb-0000-0000-0000-0000000000bb");
    private static readonly Guid AwaId = Guid.Parse("cccccccc-0000-0000-0000-0000000000c1");
    private static readonly Guid ModouId = Guid.Parse("cccccccc-0000-0000-0000-0000000000c2");
    private static readonly Guid FatouId = Guid.Parse("cccccccc-0000-0000-0000-0000000000c3");

    private record Tokens(string AccessToken, int ExpiresIn);
    private record ReportRow(Guid StudentId, string Matricule, string FullName, Guid ClassroomId, string ClassroomName,
        int TotalCalls, int Present, int Late, int JustifiedAbsences, int UnjustifiedAbsences, int TotalLateMinutes, decimal AttendanceRate);
    private record Report(DateOnly StartDate, DateOnly EndDate, Guid? ClassId, decimal? AverageAttendanceRate,
        List<ReportRow> Students, int TotalCount, int Page, int PageSize);

    private async Task<string> AccessTokenAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!.AccessToken;
    }

    private async Task<HttpResponseMessage> GetReportAsync(string token, string query)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/reports/attendance?{query}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(request);
    }

    private Task<string> DirecteurTokenAsync() => AccessTokenAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);

    /// <summary>
    /// Scénario : deux classes. Classe A (Awa, Modou) a 3 appels dans la période + 1 HORS période ;
    /// Classe B (Fatou) a 1 appel. Statuts choisis pour des taux exacts et vérifiables.
    /// </summary>
    private async Task SeedScenarioAsync()
    {
        var yearId = Guid.CreateVersion7();
        var subjectId = Guid.CreateVersion7();

        await _factory.SeedAsOwnerAsync(db =>
        {
            db.SchoolYears.Add(new SchoolYear { Id = yearId, SchoolId = EcoleId, Label = "2026-2027", StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true });
            db.Classrooms.AddRange(
                new Classroom { Id = ClasseA, SchoolId = EcoleId, Name = "CM2 A", Level = "Primaire", Capacity = 40 },
                new Classroom { Id = ClasseB, SchoolId = EcoleId, Name = "CM1 B", Level = "Primaire", Capacity = 40 });
            db.Subjects.Add(new Subject { Id = subjectId, SchoolId = EcoleId, Name = "Maths", Level = "Primaire", Coefficient = 4 });
            db.Students.AddRange(
                new Student { Id = AwaId, SchoolId = EcoleId, Matricule = "ELEV-2026-0001", FullName = "Awa Fall", BirthDate = new DateOnly(2015, 1, 1), BirthPlace = "Dakar", Gender = "F", ClassroomId = ClasseA },
                new Student { Id = ModouId, SchoolId = EcoleId, Matricule = "ELEV-2026-0002", FullName = "Modou Diop", BirthDate = new DateOnly(2015, 1, 1), BirthPlace = "Dakar", Gender = "M", ClassroomId = ClasseA },
                new Student { Id = FatouId, SchoolId = EcoleId, Matricule = "ELEV-2026-0003", FullName = "Fatou Sarr", BirthDate = new DateOnly(2015, 1, 1), BirthPlace = "Dakar", Gender = "F", ClassroomId = ClasseB });

            // Classe A — 3 appels dans la période (10, 11, 12 juillet).
            AddSheet(db, EcoleId, ClasseA, subjectId, yearId, new DateOnly(2026, 7, 10),
                (AwaId, AttendanceStatus.Present, 0), (ModouId, AttendanceStatus.Present, 0));
            AddSheet(db, EcoleId, ClasseA, subjectId, yearId, new DateOnly(2026, 7, 11),
                (AwaId, AttendanceStatus.Present, 0), (ModouId, AttendanceStatus.UnjustifiedAbsence, 0));
            AddSheet(db, EcoleId, ClasseA, subjectId, yearId, new DateOnly(2026, 7, 12),
                (AwaId, AttendanceStatus.Late, 5), (ModouId, AttendanceStatus.JustifiedAbsence, 0));

            // Classe A — 1 appel HORS période (1er août) : ne doit PAS être compté.
            AddSheet(db, EcoleId, ClasseA, subjectId, yearId, new DateOnly(2026, 8, 1),
                (AwaId, AttendanceStatus.UnjustifiedAbsence, 0), (ModouId, AttendanceStatus.UnjustifiedAbsence, 0));

            // Classe B — 1 appel dans la période.
            AddSheet(db, EcoleId, ClasseB, subjectId, yearId, new DateOnly(2026, 7, 10),
                (FatouId, AttendanceStatus.Present, 0));

            return Task.CompletedTask;
        });
    }

    private static void AddSheet(
        SamaEcole.Persistence.ApplicationDbContext db, Guid schoolId, Guid classroomId, Guid subjectId, Guid yearId,
        DateOnly date, params (Guid StudentId, AttendanceStatus Status, int LateMinutes)[] lines)
    {
        var sheetId = Guid.CreateVersion7();
        db.AttendanceSheets.Add(new AttendanceSheet
        {
            Id = sheetId, SchoolId = schoolId, ClassroomId = classroomId, SubjectId = subjectId,
            SchoolYearId = yearId, Date = date, Period = "Matin", TakenByUserId = AuthApiFactory.DirecteurId
        });
        foreach (var (studentId, status, lateMinutes) in lines)
        {
            db.StudentAttendances.Add(new StudentAttendance
            {
                SchoolId = schoolId, AttendanceSheetId = sheetId, StudentId = studentId, Status = status, LateMinutes = lateMinutes
            });
        }
    }

    private const string Period = "startDate=2026-07-01&endDate=2026-07-31";

    [Fact]
    public async Task Report_Without_A_Token_Should_Return_401()
    {
        var response = await _client.GetAsync($"/api/v1/reports/attendance?{Period}");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(nameof(Role.Finance))]
    [InlineData(nameof(Role.Enseignant))]
    public async Task Finance_And_Enseignant_Should_Be_Forbidden(string role)
    {
        var (email, password) = role == nameof(Role.Finance)
            ? (AuthApiFactory.FinanceEmail, AuthApiFactory.FinancePassword)
            : (AuthApiFactory.EnseignantEmail, AuthApiFactory.EnseignantPassword);
        var token = await AccessTokenAsync(email, password);

        var response = await GetReportAsync(token, Period);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Secretary_Is_Allowed()
    {
        var token = await AccessTokenAsync(AuthApiFactory.SecretaireEmail, AuthApiFactory.SecretairePassword);

        var response = await GetReportAsync(token, Period);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Global_Report_Should_Compute_Per_Student_And_Average_Rates()
    {
        await SeedScenarioAsync();
        var token = await DirecteurTokenAsync();

        var response = await GetReportAsync(token, Period);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var report = (await response.Content.ReadFromJsonAsync<Report>())!;

        report.TotalCount.Should().Be(3);

        // Moyenne globale : 7 lignes dans la période, 5 « présentes » (Awa 3 + Modou 1 + Fatou 1) = 0,7143.
        report.AverageAttendanceRate.Should().Be(0.7143m);

        var awa = report.Students.Single(s => s.StudentId == AwaId);
        awa.TotalCalls.Should().Be(3); // l'appel du 1er août est HORS période, donc exclu
        awa.Present.Should().Be(2);
        awa.Late.Should().Be(1);
        awa.TotalLateMinutes.Should().Be(5);
        awa.AttendanceRate.Should().Be(1.0m); // (2 présents + 1 retard) / 3

        var modou = report.Students.Single(s => s.StudentId == ModouId);
        modou.UnjustifiedAbsences.Should().Be(1);
        modou.JustifiedAbsences.Should().Be(1);
        modou.Present.Should().Be(1);
        modou.AttendanceRate.Should().Be(0.3333m); // 1 / 3
    }

    [Fact]
    public async Task Filtering_By_Class_Should_Scope_Students_And_Average()
    {
        await SeedScenarioAsync();
        var token = await DirecteurTokenAsync();

        var response = await GetReportAsync(token, $"{Period}&classId={ClasseA}");
        var report = (await response.Content.ReadFromJsonAsync<Report>())!;

        report.TotalCount.Should().Be(2); // Awa + Modou, pas Fatou (classe B)
        report.Students.Should().NotContain(s => s.StudentId == FatouId);
        // Classe A : 6 lignes, 4 présentes (Awa 3 + Modou 1) = 0,6667.
        report.AverageAttendanceRate.Should().Be(0.6667m);
    }

    [Fact]
    public async Task A_ClassId_From_Another_School_Should_Return_422()
    {
        await SeedScenarioAsync();
        var token = await DirecteurTokenAsync();

        // Un identifiant de classe inexistant dans l'école courante : refus explicite (anti-falsification),
        // pas un rapport vide.
        var response = await GetReportAsync(token, $"{Period}&classId={Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task End_Before_Start_Should_Return_422()
    {
        var token = await DirecteurTokenAsync();

        var response = await GetReportAsync(token, "startDate=2026-07-31&endDate=2026-07-01");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Empty_Period_Should_Report_No_Students_And_Null_Average()
    {
        await SeedScenarioAsync();
        var token = await DirecteurTokenAsync();

        // Une période sans aucun appel (février) : zéro élève, taux moyen null.
        var response = await GetReportAsync(token, "startDate=2026-02-01&endDate=2026-02-28");
        var report = (await response.Content.ReadFromJsonAsync<Report>())!;

        report.TotalCount.Should().Be(0);
        report.Students.Should().BeEmpty();
        report.AverageAttendanceRate.Should().BeNull();
    }
}
