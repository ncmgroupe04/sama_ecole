using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.FunctionalTests.Common;
using SamaEcole.Infrastructure.Security;
using Xunit;

namespace SamaEcole.FunctionalTests.Boarding;

/// <summary>
/// Sorties et pointage de nuit (lot D) de bout en bout — HTTP → MediatR → PostgreSQL réel : parcours complet, codes 409/422,
/// rôles, garde de module et absence de fuite de la fiche médicale. L'isolation entre écoles est prouvée au niveau
/// intégration (rôle SQL bridé + RLS) : <c>LeaveQueriesTests</c> et <c>AttendanceTests</c>. Une classe de tests partage UN
/// conteneur : noms uniques, assertions par identifiant.
/// </summary>
public class LeavesAndAttendanceEndpointsTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private const string SurveillantEmail = "surveillant@sama-ecole.sn";
    private const string SurveillantPassword = "SurveillantMotdepasse!2026";
    private const string MedicalSecret = "Allergie sévère aux arachides (EpiPen)";

    private static readonly SemaphoreSlim YearLock = new(1, 1);
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public LeavesAndAttendanceEndpointsTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public async Task InitializeAsync()
    {
        await _factory.ResetTestUsersAsync();
        await _factory.SeedAsOwnerAsync(async owner =>
        {
            if (await owner.Users.AnyAsync(u => u.Email == SurveillantEmail))
            {
                return;
            }

            owner.Users.Add(new User
            {
                SchoolId = AuthApiFactory.EcoleId, Email = SurveillantEmail,
                PasswordHash = new IdentityPasswordHasher().Hash(SurveillantPassword),
                FullName = "Surveillant de test", Role = Role.Surveillant, Status = EntityStatus.Active
            });
        });
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private record Tokens(string AccessToken, int ExpiresIn);
    private record ApiError(string Code, string Message);
    private record DormitoryDto(Guid Id, string Name, uint RowVersion);
    private record BedDto(Guid Id, int BedNumber, string Status, uint RowVersion);
    private record RoomResult(Guid Id, string Name, List<BedDto> Beds, uint RowVersion);
    private record BoarderItem(Guid Id, string StudentName, bool IsActive, uint RowVersion);
    private record LeaveDto(
        Guid Id, Guid BoarderId, string Status, bool IsCompanionUnlisted, string AccompaniedBy, DateOnly? ActualReturnDate, uint RowVersion);
    private record LeavePage(List<LeaveDto> Items, int TotalCount, int Page, int PageSize);
    private record BoarderDetail(BoarderItem Boarder, List<LeaveDto> Leaves);
    private record SheetLine(Guid BoarderId, bool OnLeave, bool Recorded, bool? IsPresent, string? Note);
    private record Sheet(List<SheetLine> Lines);
    private record AttendanceLine(Guid BoarderId, string Outcome, string? Code);
    private record AttendanceResult(int Saved, int Rejected, List<AttendanceLine> Lines);

    private async Task<string> TokenAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!.AccessToken;
    }

    private Task<string> DirecteurAsync() => TokenAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);
    private Task<string> SecretaireAsync() => TokenAsync(AuthApiFactory.SecretaireEmail, AuthApiFactory.SecretairePassword);
    private Task<string> SurveillantAsync() => TokenAsync(SurveillantEmail, SurveillantPassword);
    private Task<string> EnseignantAsync() => TokenAsync(AuthApiFactory.EnseignantEmail, AuthApiFactory.EnseignantPassword);
    private Task<string> FinanceAsync() => TokenAsync(AuthApiFactory.FinanceEmail, AuthApiFactory.FinancePassword);

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string? token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await _client.SendAsync(request);
    }

    private async Task SetInternatAsync(string directeurToken, bool enabled)
    {
        var response = await SendAsync(HttpMethod.Put, "/api/v1/schools/current/settings", directeurToken, new
        {
            gradingScale = "20", studentMatriculeFormat = "ELEV-{YEAR}-{SEQ:4}", teacherMatriculeFormat = "ENS-{YEAR}-{SEQ:3}",
            autoLogoutMinutes = 10, dateFormat = "dd/MM/yyyy", tuitionMonthsPerYear = 9, allowSecretaryToManageGrading = false,
            isPedagogyEnabled = true, isFinanceEnabled = true, isInternatEnabled = enabled, isCoranModuleEnabled = false
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static string Unique(string name) => $"{name} #{Guid.NewGuid():N}"[..(name.Length + 8)];

    /// <summary>Une inscription pour un nouvel élève ; l'année active est créée ou ÉTENDUE pour couvrir aujourd'hui (le pointage l'exige).</summary>
    private async Task<Guid> SeedEnrollmentAsync()
    {
        var enrollmentId = Guid.CreateVersion7();
        await YearLock.WaitAsync();
        try
        {
            await _factory.SeedAsOwnerAsync(async owner =>
            {
                var school = AuthApiFactory.EcoleId;
                var year = await owner.SchoolYears.IgnoreQueryFilters().FirstOrDefaultAsync(y => y.SchoolId == school && y.IsActive);
                if (year is null)
                {
                    year = new SchoolYear
                    {
                        SchoolId = school, Label = "2026-2027", StartDate = new DateOnly(2026, 9, 1),
                        EndDate = new DateOnly(2027, 6, 30), IsActive = true
                    };
                    owner.SchoolYears.Add(year);
                }

                if (Today < year.StartDate)
                {
                    year.StartDate = Today;
                }

                if (Today > year.EndDate)
                {
                    year.EndDate = Today;
                }

                var classroom = await owner.Classrooms.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.SchoolId == school && c.Name == "CM2 Internat");
                if (classroom is null)
                {
                    classroom = new Classroom { SchoolId = school, Name = "CM2 Internat", Level = "Primaire", Capacity = 200 };
                    owner.Classrooms.Add(classroom);
                }

                var key = Guid.NewGuid().ToString("N")[..8];
                var student = new Student
                {
                    SchoolId = school, Matricule = $"M-{key}", FullName = $"Élève {key}", BirthDate = new DateOnly(2014, 1, 1),
                    BirthPlace = "Dakar", Gender = "M", ClassroomId = classroom.Id
                };
                owner.Students.Add(student);
                owner.Enrollments.Add(new Enrollment
                {
                    Id = enrollmentId, SchoolId = school, StudentId = student.Id, SchoolYearId = year.Id, ClassroomId = classroom.Id,
                    Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed, TotalDue = 15_000m,
                    ReceiptNumber = $"REC-{key}", EnrolledAt = DateTimeOffset.UtcNow
                });
            });
        }
        finally
        {
            YearLock.Release();
        }

        return enrollmentId;
    }

    private async Task<(DormitoryDto Dormitory, RoomResult Room)> CreateRoomAsync(string token, int beds = 2)
    {
        var d = await SendAsync(HttpMethod.Post, "/api/v1/boarding/dormitories", token, new { name = Unique("Pavillon"), gender = "Garcons" });
        d.StatusCode.Should().Be(HttpStatusCode.Created);
        var dormitory = (await d.Content.ReadFromJsonAsync<DormitoryDto>())!;
        var r = await SendAsync(HttpMethod.Post, "/api/v1/boarding/rooms", token, new { dormitoryId = dormitory.Id, name = "Chambre 1", bedCount = beds });
        r.StatusCode.Should().Be(HttpStatusCode.Created);
        return (dormitory, (await r.Content.ReadFromJsonAsync<RoomResult>())!);
    }

    private async Task<BoarderItem> AssignOkAsync(string token, Guid bed)
    {
        var enrollment = await SeedEnrollmentAsync();
        var response = await SendAsync(HttpMethod.Post, "/api/v1/boarding/assign-bed", token,
            new { enrollmentId = enrollment, regime = "Interne", bedId = bed, includeBoardingFee = false });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<BoarderItem>())!;
    }

    private static object LeaveBody(Guid boarder, string companion = "Oncle Inconnu", DateOnly? leave = null, DateOnly? expected = null) =>
        new
        {
            boarderId = boarder, leaveDate = leave ?? Today, expectedReturnDate = expected ?? Today.AddDays(2),
            reason = "Weekend", accompaniedBy = companion
        };

    private static async Task<string> ErrorCodeAsync(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<ApiError>())!.Code;

    // ------------------------------------------------------------------ Garde de module

    [Fact]
    public async Task Every_Leave_And_Attendance_Route_Returns_403_MODULE_DISABLED_While_The_Module_Is_Off()
    {
        var directeur = await DirecteurAsync();
        await SetInternatAsync(directeur, enabled: false);
        var id = Guid.NewGuid();

        var calls = new (HttpMethod Method, string Url, object? Body)[]
        {
            (HttpMethod.Post, "/api/v1/boarding/leaves", LeaveBody(id)),
            (HttpMethod.Put, $"/api/v1/boarding/leaves/{id}/return", new { rowVersion = 1 }),
            (HttpMethod.Get, "/api/v1/boarding/leaves", null),
            (HttpMethod.Post, "/api/v1/boarding/attendance", new { date = Today, dormitoryId = id, entries = Array.Empty<object>() }),
            (HttpMethod.Get, $"/api/v1/boarding/attendance?dormitoryId={id}&date={Today:yyyy-MM-dd}", null)
        };

        foreach (var (method, url, body) in calls)
        {
            var response = await SendAsync(method, url, directeur, body);
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden, url);
            (await ErrorCodeAsync(response)).Should().Be("MODULE_DISABLED", url);
        }
    }

    // ------------------------------------------------------------------ Sorties

    [Fact]
    public async Task A_Leave_Goes_Through_Its_Whole_Life_With_The_Documented_Conflicts()
    {
        var directeur = await DirecteurAsync();
        await SetInternatAsync(directeur, enabled: true);
        var (_, room) = await CreateRoomAsync(directeur);
        var stay = await AssignOkAsync(directeur, room.Beds[0].Id);

        // Fiche médicale renseignée : elle ne doit apparaître dans AUCUNE réponse du registre des sorties.
        var profile = await SendAsync(HttpMethod.Put, $"/api/v1/boarding/boarders/{stay.Id}/profile", directeur, new
        {
            medicalNotes = MedicalSecret, emergencyContactName = "Mme Diop", emergencyContactPhone = "77 123 45 67",
            allowedExitPersons = new[] { new { name = "Oumar Diop", relationship = "Oncle", phone = "76 123 45 67" } },
            rowVersion = stay.RowVersion
        });
        profile.StatusCode.Should().Be(HttpStatusCode.OK);

        var declared = await SendAsync(HttpMethod.Post, "/api/v1/boarding/leaves", directeur, LeaveBody(stay.Id, companion: "oumar  DIOP"));
        declared.StatusCode.Should().Be(HttpStatusCode.Created);
        var declaredJson = await declared.Content.ReadAsStringAsync();
        var leave = System.Text.Json.JsonSerializer.Deserialize<LeaveDto>(
            declaredJson, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        (leave.Status, leave.IsCompanionUnlisted).Should().Be(("Active", false), "l'accompagnateur figure sur la fiche (casse et espaces ignorés)");

        var second = await SendAsync(HttpMethod.Post, "/api/v1/boarding/leaves", directeur, LeaveBody(stay.Id));
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ErrorCodeAsync(second)).Should().Be("LEAVE_ALREADY_OPEN");

        var list = await SendAsync(HttpMethod.Get, $"/api/v1/boarding/leaves?boarderId={stay.Id}&status=Active", directeur);
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        var listJson = await list.Content.ReadAsStringAsync();
        listJson.Should().NotContain(MedicalSecret).And.NotContainEquivalentOf("medicalNotes");
        System.Text.Json.JsonSerializer.Deserialize<LeavePage>(listJson, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!
            .Items.Should().ContainSingle(l => l.Id == leave.Id);

        var detail = await SendAsync(HttpMethod.Get, $"/api/v1/boarding/boarders/{stay.Id}", directeur);
        (await detail.Content.ReadFromJsonAsync<BoarderDetail>())!.Leaves.Should().ContainSingle(l => l.Id == leave.Id);

        var onLeave = await SendAsync(HttpMethod.Get, "/api/v1/boarding/boarders?status=onLeave&search=" + Uri.EscapeDataString(stay.StudentName), directeur);
        onLeave.StatusCode.Should().Be(HttpStatusCode.OK);
        (await onLeave.Content.ReadAsStringAsync()).Should().Contain(stay.Id.ToString());

        var current = await SendAsync(HttpMethod.Get, $"/api/v1/boarding/boarders/{stay.Id}", directeur);
        var token = (await current.Content.ReadFromJsonAsync<BoarderDetail>())!.Boarder.RowVersion;
        var blocked = await SendAsync(HttpMethod.Delete, $"/api/v1/boarding/unassign-bed/{stay.Id}?rowVersion={token}", directeur);
        blocked.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ErrorCodeAsync(blocked)).Should().Be("LEAVE_IN_PROGRESS");

        var returned = await SendAsync(HttpMethod.Put, $"/api/v1/boarding/leaves/{leave.Id}/return", directeur, new { rowVersion = leave.RowVersion });
        returned.StatusCode.Should().Be(HttpStatusCode.OK);
        var back = (await returned.Content.ReadFromJsonAsync<LeaveDto>())!;
        (back.Status, back.ActualReturnDate).Should().Be(("Returned", Today));

        var again = await SendAsync(HttpMethod.Put, $"/api/v1/boarding/leaves/{leave.Id}/return", directeur, new { rowVersion = back.RowVersion });
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ErrorCodeAsync(again)).Should().Be("LEAVE_ALREADY_RETURNED");

        var ended = await SendAsync(HttpMethod.Delete, $"/api/v1/boarding/unassign-bed/{stay.Id}?rowVersion={token}", directeur);
        ended.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task An_Unlisted_Companion_Is_Accepted_And_Flagged()
    {
        var directeur = await DirecteurAsync();
        await SetInternatAsync(directeur, enabled: true);
        var (_, room) = await CreateRoomAsync(directeur);
        var stay = await AssignOkAsync(directeur, room.Beds[0].Id);

        var response = await SendAsync(HttpMethod.Post, "/api/v1/boarding/leaves", directeur, LeaveBody(stay.Id, companion: "Voisin de la famille"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        (await response.Content.ReadFromJsonAsync<LeaveDto>())!.IsCompanionUnlisted.Should().BeTrue();
    }

    [Fact]
    public async Task Invalid_Dates_And_Unknown_Resources_Return_422_And_404()
    {
        var directeur = await DirecteurAsync();
        await SetInternatAsync(directeur, enabled: true);
        var (dormitory, room) = await CreateRoomAsync(directeur);
        var stay = await AssignOkAsync(directeur, room.Beds[0].Id);

        var backwards = await SendAsync(HttpMethod.Post, "/api/v1/boarding/leaves", directeur,
            LeaveBody(stay.Id, leave: Today, expected: Today.AddDays(-1)));
        backwards.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        (await SendAsync(HttpMethod.Post, "/api/v1/boarding/leaves", directeur, LeaveBody(Guid.NewGuid())))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await SendAsync(HttpMethod.Put, $"/api/v1/boarding/leaves/{Guid.NewGuid()}/return", directeur, new { rowVersion = 1 }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        var futureNight = Today.AddDays(1);
        (await SendAsync(HttpMethod.Post, "/api/v1/boarding/attendance", directeur,
                new { date = futureNight, dormitoryId = dormitory.Id, entries = new[] { new { boarderId = stay.Id, isPresent = true } } }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await SendAsync(HttpMethod.Get, "/api/v1/boarding/leaves?status=nimporte", directeur))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "un statut inconnu échoue au binding de l'énumération avant le pipeline MediatR");
    }

    // ------------------------------------------------------------------ Pointage

    [Fact]
    public async Task The_Night_Roll_Call_Prefills_Leave_Absentees_And_Reports_Each_Line()
    {
        var directeur = await DirecteurAsync();
        await SetInternatAsync(directeur, enabled: true);
        var (dormitory, room) = await CreateRoomAsync(directeur, beds: 2);
        var away = await AssignOkAsync(directeur, room.Beds[0].Id);
        var home = await AssignOkAsync(directeur, room.Beds[1].Id);
        (await SendAsync(HttpMethod.Post, "/api/v1/boarding/leaves", directeur, LeaveBody(away.Id))).StatusCode.Should().Be(HttpStatusCode.Created);

        var sheet = await SendAsync(HttpMethod.Get, $"/api/v1/boarding/attendance?dormitoryId={dormitory.Id}&date={Today:yyyy-MM-dd}", directeur);
        sheet.StatusCode.Should().Be(HttpStatusCode.OK);
        var lines = (await sheet.Content.ReadFromJsonAsync<Sheet>())!.Lines;
        lines.Should().HaveCount(2);
        lines.Single(l => l.BoarderId == away.Id).Should().Match<SheetLine>(l => l.OnLeave && l.IsPresent == false && l.Note == "En permission");
        lines.Single(l => l.BoarderId == home.Id).Should().Match<SheetLine>(l => !l.OnLeave && l.IsPresent == null);

        var posted = await SendAsync(HttpMethod.Post, "/api/v1/boarding/attendance", directeur, new
        {
            date = Today, dormitoryId = dormitory.Id,
            entries = new object[]
            {
                new { boarderId = away.Id, isPresent = true },     // en sortie : refusé
                new { boarderId = home.Id, isPresent = true }
            }
        });
        posted.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await posted.Content.ReadFromJsonAsync<AttendanceResult>())!;
        (result.Saved, result.Rejected).Should().Be((1, 1));
        result.Lines.Single(l => l.BoarderId == away.Id).Code.Should().Be("BOARDER_ON_LEAVE");
        result.Lines.Single(l => l.BoarderId == home.Id).Outcome.Should().Be("Saved");

        var after = await SendAsync(HttpMethod.Get, $"/api/v1/boarding/attendance?dormitoryId={dormitory.Id}&date={Today:yyyy-MM-dd}", directeur);
        (await after.Content.ReadFromJsonAsync<Sheet>())!.Lines.Single(l => l.BoarderId == home.Id)
            .Should().Match<SheetLine>(l => l.Recorded && l.IsPresent == true);
    }

    // ------------------------------------------------------------------ Rôles

    [Fact]
    public async Task Director_Secretary_And_Supervisor_Can_Declare_And_Read_Leaves_And_Roll_Call()
    {
        var directeur = await DirecteurAsync();
        await SetInternatAsync(directeur, enabled: true);
        var (dormitory, room) = await CreateRoomAsync(directeur, beds: 3);

        foreach (var (token, bed) in new[] { (directeur, 0), (await SecretaireAsync(), 1), (await SurveillantAsync(), 2) })
        {
            var stay = await AssignOkAsync(token, room.Beds[bed].Id);
            (await SendAsync(HttpMethod.Post, "/api/v1/boarding/leaves", token, LeaveBody(stay.Id))).StatusCode.Should().Be(HttpStatusCode.Created);
            (await SendAsync(HttpMethod.Get, "/api/v1/boarding/leaves", token)).StatusCode.Should().Be(HttpStatusCode.OK);
            (await SendAsync(HttpMethod.Get, $"/api/v1/boarding/attendance?dormitoryId={dormitory.Id}&date={Today:yyyy-MM-dd}", token))
                .StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Theory]
    [InlineData("POST", "/api/v1/boarding/leaves")]
    [InlineData("PUT", "/api/v1/boarding/leaves/00000000-0000-0000-0000-000000000001/return")]
    [InlineData("GET", "/api/v1/boarding/leaves")]
    [InlineData("POST", "/api/v1/boarding/attendance")]
    [InlineData("GET", "/api/v1/boarding/attendance?dormitoryId=00000000-0000-0000-0000-000000000001&date=2026-10-12")]
    public async Task Teachers_Finance_And_Anonymous_Callers_Are_Refused_On_Every_Leave_And_Attendance_Route(string method, string url)
    {
        await SetInternatAsync(await DirecteurAsync(), enabled: true);
        object? body = method is "POST" or "PUT" ? new { } : null;

        foreach (var token in new[] { await EnseignantAsync(), await FinanceAsync() })
        {
            (await SendAsync(new HttpMethod(method), url, token, body)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        (await SendAsync(new HttpMethod(method), url, token: null, body)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
