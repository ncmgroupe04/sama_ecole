using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.FunctionalTests.Common;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace SamaEcole.FunctionalTests.Reports;

/// <summary>
/// Ticket JGK-R03 — GET /api/v1/reports/attendance/export contre un vrai PostgreSQL. Vérifie les deux
/// formats (PDF/CSV), les restrictions de rôle, le refus d'un classId falsifié et d'un format inconnu.
/// </summary>
public class ExportAttendanceEndpointsTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public ExportAttendanceEndpointsTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public Task InitializeAsync() => _factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static readonly Guid EcoleId = AuthApiFactory.EcoleId;
    private static readonly Guid ClasseA = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000a3");
    private static readonly Guid AwaId = Guid.Parse("cccccccc-0000-0000-0000-0000000000d1");
    private static readonly Guid ModouId = Guid.Parse("cccccccc-0000-0000-0000-0000000000d2");

    private const string Period = "startDate=2026-07-01&endDate=2026-07-31";

    private record Tokens(string AccessToken, int ExpiresIn);

    private async Task<string> AccessTokenAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!.AccessToken;
    }

    private Task<string> DirecteurTokenAsync() => AccessTokenAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);

    private async Task<HttpResponseMessage> ExportAsync(string token, string query)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/reports/attendance/export?{query}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(request);
    }

    /// <summary>
    /// Awa : Présent, Présent, Retard(5) → 3 appels, taux 100 %. Modou : Présent, Absent NJ, Absent J
    /// → 3 appels, taux 33,33 %. Moyenne classe = 4/6 = 66,67 %.
    /// </summary>
    private async Task SeedScenarioAsync()
    {
        var yearId = Guid.CreateVersion7();
        var subjectId = Guid.CreateVersion7();

        await _factory.SeedAsOwnerAsync(async db =>
        {
            db.SchoolYears.Add(new SchoolYear { Id = yearId, SchoolId = EcoleId, Label = "2026-2027", StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true });
            db.Classrooms.Add(new Classroom { Id = ClasseA, SchoolId = EcoleId, Name = "CM2 A", Level = "Primaire", Capacity = 40 });
            db.Subjects.Add(new Subject { Id = subjectId, SchoolId = EcoleId, Name = "Maths", Level = "Primaire", Coefficient = 4 });
            db.Students.AddRange(
                new Student { Id = AwaId, SchoolId = EcoleId, Matricule = "ELEV-2026-0001", FullName = "Awa Fall", BirthDate = new DateOnly(2015, 1, 1), Gender = "F", ClassroomId = ClasseA },
                new Student { Id = ModouId, SchoolId = EcoleId, Matricule = "ELEV-2026-0002", FullName = "Modou Diop", BirthDate = new DateOnly(2015, 1, 1), Gender = "M", ClassroomId = ClasseA });

            AddSheet(db, subjectId, yearId, new DateOnly(2026, 7, 10),
                (AwaId, AttendanceStatus.Present, 0), (ModouId, AttendanceStatus.Present, 0));
            AddSheet(db, subjectId, yearId, new DateOnly(2026, 7, 11),
                (AwaId, AttendanceStatus.Present, 0), (ModouId, AttendanceStatus.UnjustifiedAbsence, 0));
            AddSheet(db, subjectId, yearId, new DateOnly(2026, 7, 12),
                (AwaId, AttendanceStatus.Late, 5), (ModouId, AttendanceStatus.JustifiedAbsence, 0));
        });
    }

    private static void AddSheet(
        SamaEcole.Persistence.ApplicationDbContext db, Guid subjectId, Guid yearId,
        DateOnly date, params (Guid StudentId, AttendanceStatus Status, int LateMinutes)[] lines)
    {
        var sheetId = Guid.CreateVersion7();
        db.AttendanceSheets.Add(new AttendanceSheet
        {
            Id = sheetId, SchoolId = EcoleId, ClassroomId = ClasseA, SubjectId = subjectId,
            SchoolYearId = yearId, Date = date, Period = "Matin", TakenByUserId = AuthApiFactory.DirecteurId
        });
        foreach (var (studentId, status, lateMinutes) in lines)
        {
            db.StudentAttendances.Add(new StudentAttendance
            {
                SchoolId = EcoleId, AttendanceSheetId = sheetId, StudentId = studentId, Status = status, LateMinutes = lateMinutes
            });
        }
    }

    [Fact]
    public async Task Export_Without_A_Token_Should_Return_401()
    {
        var response = await _client.GetAsync($"/api/v1/reports/attendance/export?{Period}&format=pdf");
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

        var response = await ExportAsync(token, $"{Period}&format=pdf");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Pdf_Export_Should_Return_A_Pdf_File()
    {
        await SeedScenarioAsync();
        var token = await DirecteurTokenAsync();

        var response = await ExportAsync(token, $"{Period}&format=pdf");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        response.Content.Headers.ContentDisposition!.FileName.Should().EndWith(".pdf");

        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Length.Should().BeGreaterThan(100);
        Encoding.ASCII.GetString(bytes, 0, 4).Should().Be("%PDF");
    }

    [Fact]
    public async Task Csv_Export_Should_Return_Csv_With_Computed_Values()
    {
        await SeedScenarioAsync();
        var token = await DirecteurTokenAsync();

        var response = await ExportAsync(token, $"{Period}&format=csv");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        response.Content.Headers.ContentDisposition!.FileName.Should().EndWith(".csv");

        var csv = await response.Content.ReadAsStringAsync();

        csv.Should().Contain("Matricule;Nom;Classe;Appels;Présents;Retards;Minutes de retard;Absences justifiées;Absences non justifiées;Taux de présence (%)");
        // Awa : 3 appels, 2 présents, 1 retard, 5 min, taux 100 %.
        csv.Should().Contain("ELEV-2026-0001;Awa Fall;CM2 A;3;2;1;5;0;0;100");
        // Modou : 3 appels, 1 présent, 1 abs. justifiée, 1 abs. non justifiée, taux 33,33 %.
        csv.Should().Contain("ELEV-2026-0002;Modou Diop;CM2 A;3;1;0;0;1;1;33,33");
    }

    [Fact]
    public async Task Secretary_Is_Allowed_To_Export()
    {
        await SeedScenarioAsync();
        var token = await AccessTokenAsync(AuthApiFactory.SecretaireEmail, AuthApiFactory.SecretairePassword);

        var response = await ExportAsync(token, $"{Period}&format=csv");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_ClassId_From_Another_School_Should_Return_422()
    {
        await SeedScenarioAsync();
        var token = await DirecteurTokenAsync();

        var response = await ExportAsync(token, $"{Period}&format=pdf&classId={Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task An_Unknown_Format_Should_Return_422()
    {
        var token = await DirecteurTokenAsync();

        var response = await ExportAsync(token, $"{Period}&format=xlsx");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task End_Before_Start_Should_Return_422()
    {
        var token = await DirecteurTokenAsync();

        var response = await ExportAsync(token, "startDate=2026-07-31&endDate=2026-07-01&format=csv");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }
}
