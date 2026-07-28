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
/// Ticket JGK-R01 — GET /api/v1/reports/dashboard contre un vrai PostgreSQL. Vérifie les permissions
/// (Directeur + Super Admin uniquement) et l'EXACTITUDE des agrégats (effectifs par genre, enseignants
/// actifs, taux de présence du mois, jours restants d'abonnement) sur un jeu de données connu.
/// </summary>
public class ReportsDashboardEndpointsTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public ReportsDashboardEndpointsTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public Task InitializeAsync() => _factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private record Tokens(string AccessToken, int ExpiresIn);
    private record EnrollmentStats(int Total, int Boys, int Girls);
    private record SubscriptionSummary(string Plan, string Status, DateOnly? ExpiresAt, int? DaysRemaining);
    private record DirectorDashboard(EnrollmentStats Enrollments, int ActiveTeachers, decimal? AttendanceRate, SubscriptionSummary? Subscription);

    private async Task<string> AccessTokenAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!.AccessToken;
    }

    private async Task<HttpResponseMessage> GetDashboardAsync(string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/reports/dashboard");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(request);
    }

    private static readonly Guid EcoleId = AuthApiFactory.EcoleId;

    /// <summary>
    /// Sème le scénario du tableau de bord : année active, une classe, 1 garçon + 1 fille inscrits,
    /// 1 enseignant actif, un appel du jour (1 présent + 1 retard = 100 % de présence), et un
    /// abonnement actif expirant dans 30 jours.
    /// </summary>
    private async Task SeedDashboardScenarioAsync()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var yearId = Guid.CreateVersion7();
        var classroomId = Guid.CreateVersion7();
        var subjectId = Guid.CreateVersion7();
        var boyId = Guid.CreateVersion7();
        var girlId = Guid.CreateVersion7();
        var sheetId = Guid.CreateVersion7();

        await _factory.SeedAsOwnerAsync(async db =>
        {
            db.SchoolYears.Add(new SchoolYear
            {
                Id = yearId, SchoolId = EcoleId, Label = "2026-2027",
                StartDate = today.AddMonths(-1), EndDate = today.AddMonths(8), IsActive = true
            });

            db.Classrooms.Add(new Classroom { Id = classroomId, SchoolId = EcoleId, Name = "CM2 A", Level = "Primaire", Capacity = 40 });
            db.Subjects.Add(new Subject { Id = subjectId, SchoolId = EcoleId, Name = "Mathématiques", Level = "Primaire", Coefficient = 4 });

            db.Students.AddRange(
                new Student { Id = boyId, SchoolId = EcoleId, Matricule = "ELEV-2026-0001", FullName = "Modou Diop", BirthDate = new DateOnly(2015, 1, 1), BirthPlace = "Dakar", Gender = "M", ClassroomId = classroomId },
                new Student { Id = girlId, SchoolId = EcoleId, Matricule = "ELEV-2026-0002", FullName = "Awa Fall", BirthDate = new DateOnly(2015, 2, 2), BirthPlace = "Dakar", Gender = "F", ClassroomId = classroomId });

            db.Enrollments.AddRange(
                new Enrollment { SchoolId = EcoleId, StudentId = boyId, SchoolYearId = yearId, ClassroomId = classroomId, Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed, TotalDue = 100000, ReceiptNumber = "REC-2026-0001", EnrolledAt = DateTimeOffset.UtcNow },
                new Enrollment { SchoolId = EcoleId, StudentId = girlId, SchoolYearId = yearId, ClassroomId = classroomId, Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed, TotalDue = 100000, ReceiptNumber = "REC-2026-0002", EnrolledAt = DateTimeOffset.UtcNow });

            db.Teachers.Add(new Teacher { SchoolId = EcoleId, Matricule = "ENS-2026-001", FullName = "Fatou Sarr", Email = "fatou@sama-ecole.sn", BirthDate = new DateOnly(1985, 4, 12), Status = EntityStatus.Active });

            db.AttendanceSheets.Add(new AttendanceSheet
            {
                Id = sheetId, SchoolId = EcoleId, ClassroomId = classroomId, SubjectId = subjectId,
                SchoolYearId = yearId, Date = today, Period = "Matin", TakenByUserId = AuthApiFactory.DirecteurId
            });
            db.StudentAttendances.AddRange(
                new StudentAttendance { SchoolId = EcoleId, AttendanceSheetId = sheetId, StudentId = boyId, Status = AttendanceStatus.Present, LateMinutes = 0 },
                new StudentAttendance { SchoolId = EcoleId, AttendanceSheetId = sheetId, StudentId = girlId, Status = AttendanceStatus.Late, LateMinutes = 10 });

            db.Subscriptions.Add(new Subscription
            {
                SchoolId = EcoleId, Plan = SubscriptionPlan.Standard,
                Status = SubscriptionStatus.Active, ExpiresAt = today.AddDays(30)
            });
        });
    }

    [Fact]
    public async Task Dashboard_Without_A_Token_Should_Return_401()
    {
        var response = await _client.GetAsync("/api/v1/reports/dashboard");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(nameof(Role.Secretariat))]
    [InlineData(nameof(Role.Finance))]
    [InlineData(nameof(Role.Enseignant))]
    public async Task Non_Director_Roles_Should_Be_Forbidden(string role)
    {
        var (email, password) = role switch
        {
            nameof(Role.Secretariat) => (AuthApiFactory.SecretaireEmail, AuthApiFactory.SecretairePassword),
            nameof(Role.Finance) => (AuthApiFactory.FinanceEmail, AuthApiFactory.FinancePassword),
            _ => (AuthApiFactory.EnseignantEmail, AuthApiFactory.EnseignantPassword)
        };
        var token = await AccessTokenAsync(email, password);

        var response = await GetDashboardAsync(token);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Director_Sees_Accurate_Aggregates()
    {
        await SeedDashboardScenarioAsync();
        var token = await AccessTokenAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);

        var response = await GetDashboardAsync(token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dashboard = (await response.Content.ReadFromJsonAsync<DirectorDashboard>())!;

        dashboard.Enrollments.Total.Should().Be(2);
        dashboard.Enrollments.Boys.Should().Be(1);
        dashboard.Enrollments.Girls.Should().Be(1);

        dashboard.ActiveTeachers.Should().Be(1);

        // 1 présent + 1 retard sur 2 lignes = 100 % (le retard est une présence tardive).
        dashboard.AttendanceRate.Should().Be(1.0m);

        dashboard.Subscription.Should().NotBeNull();
        dashboard.Subscription!.Status.Should().Be(nameof(SubscriptionStatus.Active));
        dashboard.Subscription.DaysRemaining.Should().Be(30);
    }

    [Fact]
    public async Task Attendance_Rate_Should_Count_Late_As_Present_But_Not_Absences()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var yearId = Guid.CreateVersion7();
        var classroomId = Guid.CreateVersion7();
        var subjectId = Guid.CreateVersion7();
        var s1 = Guid.CreateVersion7();
        var s2 = Guid.CreateVersion7();
        var s3 = Guid.CreateVersion7();
        var sheetId = Guid.CreateVersion7();

        await _factory.SeedAsOwnerAsync(async db =>
        {
            db.SchoolYears.Add(new SchoolYear { Id = yearId, SchoolId = EcoleId, Label = "2026-2027", StartDate = today.AddMonths(-1), EndDate = today.AddMonths(8), IsActive = true });
            db.Classrooms.Add(new Classroom { Id = classroomId, SchoolId = EcoleId, Name = "CM2 A", Level = "Primaire", Capacity = 40 });
            db.Subjects.Add(new Subject { Id = subjectId, SchoolId = EcoleId, Name = "Maths", Level = "Primaire", Coefficient = 4 });
            db.Students.AddRange(
                new Student { Id = s1, SchoolId = EcoleId, Matricule = "ELEV-2026-0001", FullName = "A", BirthDate = new DateOnly(2015, 1, 1), BirthPlace = "Dakar", Gender = "M", ClassroomId = classroomId },
                new Student { Id = s2, SchoolId = EcoleId, Matricule = "ELEV-2026-0002", FullName = "B", BirthDate = new DateOnly(2015, 1, 1), BirthPlace = "Dakar", Gender = "F", ClassroomId = classroomId },
                new Student { Id = s3, SchoolId = EcoleId, Matricule = "ELEV-2026-0003", FullName = "C", BirthDate = new DateOnly(2015, 1, 1), BirthPlace = "Dakar", Gender = "M", ClassroomId = classroomId });
            db.AttendanceSheets.Add(new AttendanceSheet { Id = sheetId, SchoolId = EcoleId, ClassroomId = classroomId, SubjectId = subjectId, SchoolYearId = yearId, Date = today, Period = "Matin", TakenByUserId = AuthApiFactory.DirecteurId });
            // 1 présent + 1 retard + 1 absence non justifiée = 2/3 de présence.
            db.StudentAttendances.AddRange(
                new StudentAttendance { SchoolId = EcoleId, AttendanceSheetId = sheetId, StudentId = s1, Status = AttendanceStatus.Present, LateMinutes = 0 },
                new StudentAttendance { SchoolId = EcoleId, AttendanceSheetId = sheetId, StudentId = s2, Status = AttendanceStatus.Late, LateMinutes = 5 },
                new StudentAttendance { SchoolId = EcoleId, AttendanceSheetId = sheetId, StudentId = s3, Status = AttendanceStatus.UnjustifiedAbsence, LateMinutes = 0 });
        });

        var token = await AccessTokenAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);
        var response = await GetDashboardAsync(token);
        var dashboard = (await response.Content.ReadFromJsonAsync<DirectorDashboard>())!;

        dashboard.AttendanceRate.Should().Be(0.6667m);
    }

    [Fact]
    public async Task Attendance_Rate_Should_Be_Null_When_No_Attendance_This_Month()
    {
        // Aucun appel semé : le taux n'existe pas encore, il vaut null (et l'UI affiche « — »).
        var token = await AccessTokenAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);

        var response = await GetDashboardAsync(token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dashboard = (await response.Content.ReadFromJsonAsync<DirectorDashboard>())!;

        dashboard.AttendanceRate.Should().BeNull();
        dashboard.Enrollments.Total.Should().Be(0);
        dashboard.Subscription.Should().BeNull();
    }

    [Fact]
    public async Task Super_Admin_Is_Allowed_But_Sees_No_Tenant_Data()
    {
        // Le Super Admin n'a pas d'école : la RLS lui ferme toutes les tables tenant. L'accès est
        // autorisé (200), mais les agrégats sont vides — jamais les données d'une école prise au hasard.
        await SeedDashboardScenarioAsync();
        var token = await AccessTokenAsync(AuthApiFactory.SuperAdminEmail, AuthApiFactory.SuperAdminPassword);

        var response = await GetDashboardAsync(token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dashboard = (await response.Content.ReadFromJsonAsync<DirectorDashboard>())!;

        dashboard.Enrollments.Total.Should().Be(0);
        dashboard.ActiveTeachers.Should().Be(0);
        dashboard.AttendanceRate.Should().BeNull();
        dashboard.Subscription.Should().BeNull();
    }
}
