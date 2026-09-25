using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace SamaEcole.FunctionalTests.Attendance;

/// <summary>
/// Ticket JGK-D06 — /attendance contre un vrai PostgreSQL, à travers la vraie pile HTTP. Couvre le
/// flux complet (roster → soumission → consultation), les permissions par rôle, et surtout la portée
/// de l'enseignant (il ne fait l'appel que pour ses classes/matières assignées).
/// </summary>
public class AttendanceEndpointsTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public AttendanceEndpointsTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public Task InitializeAsync() => _factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>Date de l'appel : aujourd'hui — l'appel ne se fait jamais pour un jour futur (validé côté commande).</summary>
    private static readonly string CallDate = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");

    private record Tokens(string AccessToken, int ExpiresIn);
    private record SubjectDto(Guid Id, string Name, string Level, decimal Coefficient);
    private record ClassroomDto(Guid Id, string Name, string Level, int Capacity);
    private record StudentResult(Guid Id, string Matricule);
    private record TeacherResult(Guid Id, string Matricule);
    private record RosterRow(Guid StudentId, string Matricule, string FullName, string? Status, int LateMinutes);
    private record RosterDto(Guid ClassroomId, string ClassroomName, Guid SubjectId, string SubjectName, DateOnly Date, string Period, bool AlreadySubmitted, List<RosterRow> Students);
    private record SubmitResult(Guid Id, int StudentCount);
    private record LineDto(Guid StudentId, string Matricule, string FullName, string Status, int LateMinutes);
    private record SheetDto(Guid Id, Guid ClassroomId, string ClassroomName, Guid SubjectId, string SubjectName, Guid SchoolYearId, string SchoolYearLabel, DateOnly Date, string Period, List<LineDto> Lines);

    private async Task<string> AccessTokenAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!.AccessToken;
    }

    private Task<string> DirecteurTokenAsync() => AccessTokenAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);
    private Task<string> FinanceTokenAsync() => AccessTokenAsync(AuthApiFactory.FinanceEmail, AuthApiFactory.FinancePassword);
    private Task<string> EnseignantTokenAsync() => AccessTokenAsync(AuthApiFactory.EnseignantEmail, AuthApiFactory.EnseignantPassword);

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await _client.SendAsync(request);
    }

    private async Task<Guid> CreateSubjectAsync(string token, string name)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/v1/subjects", token, new { name, level = "Primaire", coefficient = 4 });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<SubjectDto>())!.Id;
    }

    private async Task<Guid> CreateClassroomAsync(string token, string name)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/v1/classrooms", token, new { name, level = "Primaire", capacity = 30 });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ClassroomDto>())!.Id;
    }

    private async Task<Guid> CreateStudentAsync(string token, string fullName, Guid classroomId)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/v1/students", token,
            new { fullName, birthDate = "2015-03-12", birthPlace = "Dakar", gender = "F", classroomId });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<StudentResult>())!.Id;
    }

    private async Task EnsureActiveSchoolYearAsync(string token)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/v1/school-years", token,
            new { label = "2026-2027", startDate = "2026-10-01", endDate = "2027-06-30" });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    /// <summary>Décor commun : année active, matière, classe, deux élèves.</summary>
    private async Task<(Guid SubjectId, Guid ClassroomId, Guid Student1, Guid Student2)> SeedClassAsync(string directeur)
    {
        await EnsureActiveSchoolYearAsync(directeur);
        var subjectId = await CreateSubjectAsync(directeur, "Mathématiques");
        var classroomId = await CreateClassroomAsync(directeur, "CM2 A");
        var s1 = await CreateStudentAsync(directeur, "Awa Fall", classroomId);
        var s2 = await CreateStudentAsync(directeur, "Modou Diop", classroomId);
        return (subjectId, classroomId, s1, s2);
    }

    /// <summary>Rattache le compte Enseignant semé à une fiche et l'assigne à la classe/matière (année active).</summary>
    private async Task LinkAndAssignSeededTeacherAsync(string directeur, Guid classroomId, Guid subjectId)
    {
        var createResponse = await SendAsync(HttpMethod.Post, "/api/v1/teachers", directeur, new
        {
            fullName = "Enseignant de test",
            email = "prof.lie@sama-ecole.sn",
            birthDate = "1985-04-12",
            subjectIds = new[] { subjectId },
            userId = AuthApiFactory.EnseignantId
        });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var teacherId = (await createResponse.Content.ReadFromJsonAsync<TeacherResult>())!.Id;

        var assignResponse = await SendAsync(HttpMethod.Post, $"/api/v1/teachers/{teacherId}/assignments", directeur,
            new { classroomId, subjectId });
        assignResponse.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private static object Entry(Guid studentId, string status, int lateMinutes = 0)
        => new { studentId, status, lateMinutes };

    [Fact]
    public async Task Submitting_Without_A_Token_Should_Return_401()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/attendance", new { });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Directeur_Can_Initialize_Submit_And_Consult_An_Attendance_Sheet()
    {
        var directeur = await DirecteurTokenAsync();
        var (subjectId, classroomId, s1, s2) = await SeedClassAsync(directeur);

        // 1. Roster : les deux élèves, aucune fiche encore.
        var rosterResponse = await SendAsync(HttpMethod.Get,
            $"/api/v1/attendance/roster?classroomId={classroomId}&subjectId={subjectId}&date={CallDate}&period=Matin", directeur);
        rosterResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var roster = (await rosterResponse.Content.ReadFromJsonAsync<RosterDto>())!;
        roster.AlreadySubmitted.Should().BeFalse();
        roster.Students.Should().HaveCount(2);

        // 2. Soumission de l'appel.
        var submitResponse = await SendAsync(HttpMethod.Post, "/api/v1/attendance", directeur, new
        {
            classroomId,
            subjectId,
            date = CallDate,
            period = "Matin",
            entries = new[]
            {
                Entry(s1, "Present"),
                Entry(s2, "Late", 10)
            }
        });
        submitResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var submit = (await submitResponse.Content.ReadFromJsonAsync<SubmitResult>())!;
        submit.StudentCount.Should().Be(2);

        // 3. Consultation : la fiche avec les deux statuts.
        var sheetResponse = await SendAsync(HttpMethod.Get, $"/api/v1/attendance/{submit.Id}", directeur);
        sheetResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var sheet = (await sheetResponse.Content.ReadFromJsonAsync<SheetDto>())!;
        sheet.Lines.Should().HaveCount(2);
        sheet.Lines.Should().Contain(l => l.StudentId == s2 && l.Status == "Late" && l.LateMinutes == 10);
        sheet.SchoolYearLabel.Should().Be("2026-2027");
    }

    private record SlotDto(Guid SlotId, Guid SubjectId, string SubjectName, Guid TeacherId, string TeacherName, string Start, string End, string Label, Guid? SheetId);

    /// <summary>
    /// Évolution N°5 — appel par créneau de bout en bout : la liste des cours du jour, l'appel sans période
    /// saisie (dérivée du cours), un créneau qui ne convient pas refusé en 422, et la vue restreinte d'un Enseignant.
    /// </summary>
    [Fact]
    public async Task An_Attendance_Sheet_Can_Be_Taken_On_A_Schedule_Slot_And_Derives_Its_Period()
    {
        var directeur = await DirecteurTokenAsync();
        var (subjectId, classroomId, s1, _) = await SeedClassAsync(directeur);

        // Un jour ouvré de la semaine par défaut (lundi → samedi) : le dimanche, on prend la veille.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var date = today.DayOfWeek == DayOfWeek.Sunday ? today.AddDays(-1) : today;

        var teacherResponse = await SendAsync(HttpMethod.Post, "/api/v1/teachers", directeur, new
        {
            fullName = "Enseignant de test", email = "prof.creneau@sama-ecole.sn", birthDate = "1985-04-12",
            subjectIds = new[] { subjectId }, userId = AuthApiFactory.EnseignantId
        });
        teacherResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var teacherId = (await teacherResponse.Content.ReadFromJsonAsync<TeacherResult>())!.Id;
        (await SendAsync(HttpMethod.Post, $"/api/v1/teachers/{teacherId}/assignments", directeur, new { classroomId, subjectId }))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var slotResponse = await SendAsync(HttpMethod.Post, "/api/v1/schedules", directeur, new
        {
            teacherId, classroomId, subjectId, dayOfWeek = (int)date.DayOfWeek, startTime = "08:00:00", endTime = "10:00:00"
        });
        slotResponse.EnsureSuccessStatusCode();

        // 1. Les cours du jour.
        var slots = (await (await SendAsync(HttpMethod.Get, $"/api/v1/attendance/slots?classroomId={classroomId}&date={date:yyyy-MM-dd}", directeur))
            .Content.ReadFromJsonAsync<List<SlotDto>>())!;
        var slot = slots.Should().ContainSingle().Subject;
        slot.Label.Should().Be("08:00-10:00");
        slot.SheetId.Should().BeNull();

        // 2. Un créneau qui ne convient pas (mauvaise matière) : 422, rien d'écrit.
        var otherSubject = await CreateSubjectAsync(directeur, "Français");
        (await SendAsync(HttpMethod.Post, "/api/v1/attendance", directeur, new
        {
            classroomId, subjectId = otherSubject, date = date.ToString("yyyy-MM-dd"), scheduleSlotId = slot.SlotId,
            entries = new[] { Entry(s1, "Present") }
        })).StatusCode.Should().Be((HttpStatusCode)422);

        // 3. L'appel SANS période : elle est dérivée du cours.
        var submit = await SendAsync(HttpMethod.Post, "/api/v1/attendance", directeur, new
        {
            classroomId, subjectId, date = date.ToString("yyyy-MM-dd"), scheduleSlotId = slot.SlotId,
            entries = new[] { Entry(s1, "Present") }
        });
        submit.StatusCode.Should().Be(HttpStatusCode.Created);
        var sheetId = (await submit.Content.ReadFromJsonAsync<SubmitResult>())!.Id;
        (await (await SendAsync(HttpMethod.Get, $"/api/v1/attendance/{sheetId}", directeur))
            .Content.ReadFromJsonAsync<SheetDto>())!.Period.Should().Be("08:00-10:00");

        // 4. Le cours porte désormais sa fiche ; le même créneau ne se ressaisit pas (409).
        var after = (await (await SendAsync(HttpMethod.Get, $"/api/v1/attendance/slots?classroomId={classroomId}&date={date:yyyy-MM-dd}", directeur))
            .Content.ReadFromJsonAsync<List<SlotDto>>())!;
        after.Single().SheetId.Should().Be(sheetId);
        (await SendAsync(HttpMethod.Post, "/api/v1/attendance", directeur, new
        {
            classroomId, subjectId, date = date.ToString("yyyy-MM-dd"), scheduleSlotId = slot.SlotId,
            entries = new[] { Entry(s1, "Present") }
        })).StatusCode.Should().Be(HttpStatusCode.Conflict);

        // 5. L'Enseignant titulaire voit son cours.
        var enseignant = await EnseignantTokenAsync();
        var mine = (await (await SendAsync(HttpMethod.Get, $"/api/v1/attendance/slots?classroomId={classroomId}&date={date:yyyy-MM-dd}", enseignant))
            .Content.ReadFromJsonAsync<List<SlotDto>>())!;
        mine.Should().ContainSingle();
    }

    [Fact]
    public async Task Roster_Should_Reflect_An_Already_Submitted_Sheet()
    {
        var directeur = await DirecteurTokenAsync();
        var (subjectId, classroomId, s1, s2) = await SeedClassAsync(directeur);

        await SendAsync(HttpMethod.Post, "/api/v1/attendance", directeur, new
        {
            classroomId, subjectId, date = CallDate, period = "Matin",
            entries = new[] { Entry(s1, "UnjustifiedAbsence"), Entry(s2, "Present") }
        });

        var rosterResponse = await SendAsync(HttpMethod.Get,
            $"/api/v1/attendance/roster?classroomId={classroomId}&subjectId={subjectId}&date={CallDate}&period=Matin", directeur);
        var roster = (await rosterResponse.Content.ReadFromJsonAsync<RosterDto>())!;

        roster.AlreadySubmitted.Should().BeTrue();
        roster.Students.Should().Contain(r => r.StudentId == s1 && r.Status == "UnjustifiedAbsence");
    }

    [Fact]
    public async Task Submitting_The_Same_Sheet_Twice_Should_Return_409()
    {
        var directeur = await DirecteurTokenAsync();
        var (subjectId, classroomId, s1, _) = await SeedClassAsync(directeur);

        var body = new
        {
            classroomId, subjectId, date = CallDate, period = "Matin",
            entries = new[] { Entry(s1, "Present") }
        };

        (await SendAsync(HttpMethod.Post, "/api/v1/attendance", directeur, body)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await SendAsync(HttpMethod.Post, "/api/v1/attendance", directeur, body)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Submitting_A_Late_Status_Without_Minutes_Should_Return_422()
    {
        var directeur = await DirecteurTokenAsync();
        var (subjectId, classroomId, s1, _) = await SeedClassAsync(directeur);

        var response = await SendAsync(HttpMethod.Post, "/api/v1/attendance", directeur, new
        {
            classroomId, subjectId, date = CallDate, period = "Matin",
            entries = new[] { Entry(s1, "Late", 0) }
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Submitting_A_Student_From_Another_Class_Should_Return_422()
    {
        var directeur = await DirecteurTokenAsync();
        var (subjectId, classroomId, _, _) = await SeedClassAsync(directeur);

        // Élève d'une AUTRE classe.
        var otherClass = await CreateClassroomAsync(directeur, "CM1 B");
        var outsider = await CreateStudentAsync(directeur, "Intrus", otherClass);

        var response = await SendAsync(HttpMethod.Post, "/api/v1/attendance", directeur, new
        {
            classroomId, subjectId, date = CallDate, period = "Matin",
            entries = new[] { Entry(outsider, "Present") }
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Submitting_Without_An_Active_School_Year_Should_Return_422()
    {
        var directeur = await DirecteurTokenAsync();
        // Pas d'année active : on crée classe/matière/élève sans EnsureActiveSchoolYearAsync.
        var subjectId = await CreateSubjectAsync(directeur, "Mathématiques");
        var classroomId = await CreateClassroomAsync(directeur, "CM2 A");
        var s1 = await CreateStudentAsync(directeur, "Awa Fall", classroomId);

        var response = await SendAsync(HttpMethod.Post, "/api/v1/attendance", directeur, new
        {
            classroomId, subjectId, date = CallDate, period = "Matin",
            entries = new[] { Entry(s1, "Present") }
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task An_Assigned_Teacher_Can_Submit_The_Attendance_Of_Their_Class()
    {
        var directeur = await DirecteurTokenAsync();
        var (subjectId, classroomId, s1, s2) = await SeedClassAsync(directeur);
        await LinkAndAssignSeededTeacherAsync(directeur, classroomId, subjectId);

        var enseignant = await EnseignantTokenAsync();
        var response = await SendAsync(HttpMethod.Post, "/api/v1/attendance", enseignant, new
        {
            classroomId, subjectId, date = CallDate, period = "Matin",
            entries = new[] { Entry(s1, "Present"), Entry(s2, "JustifiedAbsence") }
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_Teacher_Not_Assigned_To_The_Class_Should_Be_Forbidden()
    {
        var directeur = await DirecteurTokenAsync();
        var (subjectId, classroomId, s1, _) = await SeedClassAsync(directeur);
        await LinkAndAssignSeededTeacherAsync(directeur, classroomId, subjectId);

        // Une AUTRE classe/matière, à laquelle l'enseignant n'est pas assigné.
        var otherSubject = await CreateSubjectAsync(directeur, "Français");
        var otherClass = await CreateClassroomAsync(directeur, "CM1 B");
        var otherStudent = await CreateStudentAsync(directeur, "Fatou Sarr", otherClass);

        var enseignant = await EnseignantTokenAsync();
        var response = await SendAsync(HttpMethod.Post, "/api/v1/attendance", enseignant, new
        {
            classroomId = otherClass, subjectId = otherSubject, date = CallDate, period = "Matin",
            entries = new[] { Entry(otherStudent, "Present") }
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_Enseignant_Without_A_Linked_Teacher_Record_Should_Be_Forbidden()
    {
        var directeur = await DirecteurTokenAsync();
        var (subjectId, classroomId, s1, _) = await SeedClassAsync(directeur);
        // Volontairement AUCUN rattachement Teacher↔User ici.

        var enseignant = await EnseignantTokenAsync();
        var response = await SendAsync(HttpMethod.Post, "/api/v1/attendance", enseignant, new
        {
            classroomId, subjectId, date = CallDate, period = "Matin",
            entries = new[] { Entry(s1, "Present") }
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Finance_Must_Not_Submit_An_Attendance_Sheet()
    {
        var directeur = await DirecteurTokenAsync();
        var (subjectId, classroomId, s1, _) = await SeedClassAsync(directeur);

        var finance = await FinanceTokenAsync();
        var response = await SendAsync(HttpMethod.Post, "/api/v1/attendance", finance, new
        {
            classroomId, subjectId, date = CallDate, period = "Matin",
            entries = new[] { Entry(s1, "Present") }
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Finance_Must_Not_Consult_An_Attendance_Sheet()
    {
        var directeur = await DirecteurTokenAsync();
        var (subjectId, classroomId, s1, _) = await SeedClassAsync(directeur);

        var submitResponse = await SendAsync(HttpMethod.Post, "/api/v1/attendance", directeur, new
        {
            classroomId, subjectId, date = CallDate, period = "Matin",
            entries = new[] { Entry(s1, "Present") }
        });
        var sheetId = (await submitResponse.Content.ReadFromJsonAsync<SubmitResult>())!.Id;

        var finance = await FinanceTokenAsync();
        var response = await SendAsync(HttpMethod.Get, $"/api/v1/attendance/{sheetId}", finance);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Consulting_An_Unknown_Sheet_Should_Return_404()
    {
        var directeur = await DirecteurTokenAsync();
        var response = await SendAsync(HttpMethod.Get, $"/api/v1/attendance/{Guid.NewGuid()}", directeur);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
