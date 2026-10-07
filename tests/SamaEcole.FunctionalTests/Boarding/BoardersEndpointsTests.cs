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
/// Pensionnaires (lot C) de bout en bout — HTTP → MediatR → PostgreSQL réel : affectation, transfert, fin de séjour,
/// liste, fiche, profil, et surtout le MASQUAGE de la fiche médicale par rôle. Une classe de tests partage UN conteneur :
/// noms uniques, assertions par identifiant.
/// </summary>
public class BoardersEndpointsTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private const string SurveillantEmail = "surveillant@sama-ecole.sn";
    private const string SurveillantPassword = "SurveillantMotdepasse!2026";
    private const string MedicalSecret = "Allergie sévère aux arachides (EpiPen)";

    private static readonly SemaphoreSlim YearLock = new(1, 1);

    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public BoardersEndpointsTests(AuthApiFactory factory)
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
    private record BoarderItem(
        Guid Id, string StudentName, string Regime, bool IsActive, Guid? BedId, int? BedNumber, string? RoomName, uint RowVersion);
    private record ExitPerson(string Name, string Relationship, string Phone);
    private record BoarderDetail(
        BoarderItem Boarder, string? MedicalNotes, string? EmergencyContactName, List<ExitPerson> AllowedExitPersons);
    private record PageResult(List<BoarderItem> Items, int TotalCount, int Page, int PageSize);

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

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
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

    /// <summary>Une inscription (année active + classe créées au besoin) pour un nouvel élève du genre demandé.</summary>
    private async Task<Guid> SeedEnrollmentAsync(string gender)
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
                    BirthPlace = "Dakar", Gender = gender, ClassroomId = classroom.Id
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

    private async Task<(DormitoryDto Dormitory, RoomResult Room)> CreateRoomAsync(string token, string gender, int beds = 2)
    {
        var d = await SendAsync(HttpMethod.Post, "/api/v1/boarding/dormitories", token, new { name = Unique("Pavillon"), gender });
        d.StatusCode.Should().Be(HttpStatusCode.Created);
        var dormitory = (await d.Content.ReadFromJsonAsync<DormitoryDto>())!;
        var r = await SendAsync(HttpMethod.Post, "/api/v1/boarding/rooms", token, new { dormitoryId = dormitory.Id, name = "Chambre 1", bedCount = beds });
        r.StatusCode.Should().Be(HttpStatusCode.Created);
        return (dormitory, (await r.Content.ReadFromJsonAsync<RoomResult>())!);
    }

    private Task<HttpResponseMessage> AssignAsync(string token, Guid enrollment, string regime, Guid? bed, uint? rowVersion = null, bool fee = false) =>
        SendAsync(HttpMethod.Post, "/api/v1/boarding/assign-bed", token,
            new { enrollmentId = enrollment, regime, bedId = bed, includeBoardingFee = fee, rowVersion });

    private async Task<BoarderItem> AssignOkAsync(string token, Guid enrollment, Guid bed, uint? rowVersion = null)
    {
        var response = await AssignAsync(token, enrollment, "Interne", bed, rowVersion);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<BoarderItem>())!;
    }

    private async Task<BoarderDetail> GetDetailAsync(string token, Guid id)
    {
        var response = await SendAsync(HttpMethod.Get, $"/api/v1/boarding/boarders/{id}", token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<BoarderDetail>())!;
    }

    private static async Task<string> ErrorCodeAsync(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<ApiError>())!.Code;

    // ------------------------------------------------------------------ Garde de module

    [Fact]
    public async Task Every_Boarder_Route_Returns_403_MODULE_DISABLED_While_The_Module_Is_Off()
    {
        var directeur = await DirecteurAsync();
        await SetInternatAsync(directeur, enabled: false);

        var response = await SendAsync(HttpMethod.Get, "/api/v1/boarding/boarders", directeur);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ErrorCodeAsync(response)).Should().Be("MODULE_DISABLED");
    }

    // ------------------------------------------------------------------ Parcours

    [Fact]
    public async Task The_Director_Can_Assign_List_Transfer_Update_The_Profile_And_End_A_Stay()
    {
        var directeur = await DirecteurAsync();
        await SetInternatAsync(directeur, enabled: true);
        var (_, room) = await CreateRoomAsync(directeur, "Garcons");
        var enrollment = await SeedEnrollmentAsync("M");

        var stay = await AssignOkAsync(directeur, enrollment, room.Beds[0].Id);
        (stay.Regime, stay.IsActive, stay.BedNumber, stay.RoomName).Should().Be(("Interne", true, 1, "Chambre 1"));

        var list = await SendAsync(HttpMethod.Get, "/api/v1/boarding/boarders?search=" + Uri.EscapeDataString(stay.StudentName), directeur);
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        (await list.Content.ReadFromJsonAsync<PageResult>())!.Items.Should().ContainSingle(i => i.Id == stay.Id);

        var profile = await SendAsync(HttpMethod.Put, $"/api/v1/boarding/boarders/{stay.Id}/profile", directeur, new
        {
            medicalNotes = MedicalSecret, emergencyContactName = "Mme Diop", emergencyContactPhone = "77 123 45 67",
            allowedExitPersons = new[] { new { name = "Oumar Diop", relationship = "Oncle", phone = "76 123 45 67" } },
            rowVersion = stay.RowVersion
        });
        profile.StatusCode.Should().Be(HttpStatusCode.OK);
        var detail = (await profile.Content.ReadFromJsonAsync<BoarderDetail>())!;
        (detail.MedicalNotes, detail.EmergencyContactName).Should().Be((MedicalSecret, "Mme Diop"));
        detail.AllowedExitPersons.Should().ContainSingle().Which.Name.Should().Be("Oumar Diop");

        var moved = await AssignOkAsync(directeur, enrollment, room.Beds[1].Id, detail.Boarder.RowVersion);
        (moved.Id, moved.BedNumber).Should().Be((stay.Id, 2));

        var end = await SendAsync(HttpMethod.Delete, $"/api/v1/boarding/unassign-bed/{stay.Id}?rowVersion={moved.RowVersion}", directeur);
        end.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var ended = await SendAsync(HttpMethod.Get, "/api/v1/boarding/boarders?status=ended&search=" + Uri.EscapeDataString(stay.StudentName), directeur);
        var closed = (await ended.Content.ReadFromJsonAsync<PageResult>())!.Items.Should().ContainSingle(i => i.Id == stay.Id).Subject;
        (closed.IsActive, closed.BedId).Should().Be((false, (Guid?)null));
    }

    [Fact]
    public async Task A_Stale_RowVersion_On_A_Transfer_Returns_409()
    {
        var directeur = await DirecteurAsync();
        await SetInternatAsync(directeur, enabled: true);
        var (_, room) = await CreateRoomAsync(directeur, "Garcons", beds: 3);
        var enrollment = await SeedEnrollmentAsync("M");
        var first = await AssignOkAsync(directeur, enrollment, room.Beds[0].Id);
        await AssignOkAsync(directeur, enrollment, room.Beds[1].Id, first.RowVersion);   // le jeton change

        var stale = await AssignAsync(directeur, enrollment, "Interne", room.Beds[2].Id, first.RowVersion);

        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_Bed_Already_Taken_Returns_409_BED_UNAVAILABLE()
    {
        var directeur = await DirecteurAsync();
        await SetInternatAsync(directeur, enabled: true);
        var (_, room) = await CreateRoomAsync(directeur, "Garcons");
        await AssignOkAsync(directeur, await SeedEnrollmentAsync("M"), room.Beds[0].Id);

        var second = await AssignAsync(directeur, await SeedEnrollmentAsync("M"), "Interne", room.Beds[0].Id);

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ErrorCodeAsync(second)).Should().Be("BED_UNAVAILABLE");
    }

    [Fact]
    public async Task Maintenance_Wrong_Gender_And_An_Unknown_Regime_Are_Refused_With_422()
    {
        var directeur = await DirecteurAsync();
        await SetInternatAsync(directeur, enabled: true);
        var (_, room) = await CreateRoomAsync(directeur, "Garcons");
        var boy = await SeedEnrollmentAsync("M");
        var girl = await SeedEnrollmentAsync("F");

        (await SendAsync(HttpMethod.Put, $"/api/v1/boarding/beds/{room.Beds[1].Id}/status", directeur,
            new { status = "Maintenance", rowVersion = room.Beds[1].RowVersion })).StatusCode.Should().Be(HttpStatusCode.OK);

        (await AssignAsync(directeur, boy, "Interne", room.Beds[1].Id)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await AssignAsync(directeur, girl, "Interne", room.Beds[0].Id)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var unknown = await SendAsync(HttpMethod.Post, "/api/v1/boarding/assign-bed", directeur,
            new { enrollmentId = boy, regime = 99, bedId = room.Beds[0].Id, includeBoardingFee = false });
        unknown.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_Half_Boarder_Is_Assigned_Without_A_Bed()
    {
        var directeur = await DirecteurAsync();
        await SetInternatAsync(directeur, enabled: true);

        var response = await AssignAsync(directeur, await SeedEnrollmentAsync("F"), "DemiPensionnaire", bed: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var stay = (await response.Content.ReadFromJsonAsync<BoarderItem>())!;
        (stay.Regime, stay.BedId).Should().Be(("DemiPensionnaire", (Guid?)null));
    }

    // ------------------------------------------------------------------ Fiche médicale et rôles

    [Fact]
    public async Task The_Medical_Notes_Are_Hidden_From_The_Secretary_And_Visible_To_The_Surveillant()
    {
        var directeur = await DirecteurAsync();
        await SetInternatAsync(directeur, enabled: true);
        var (_, room) = await CreateRoomAsync(directeur, "Garcons");
        var stay = await AssignOkAsync(directeur, await SeedEnrollmentAsync("M"), room.Beds[0].Id);
        (await SendAsync(HttpMethod.Put, $"/api/v1/boarding/boarders/{stay.Id}/profile", directeur,
            new { medicalNotes = MedicalSecret, allowedExitPersons = Array.Empty<object>(), rowVersion = stay.RowVersion }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await GetDetailAsync(await SecretaireAsync(), stay.Id)).MedicalNotes.Should().BeNull();
        (await GetDetailAsync(await SurveillantAsync(), stay.Id)).MedicalNotes.Should().Be(MedicalSecret);
        (await GetDetailAsync(directeur, stay.Id)).MedicalNotes.Should().Be(MedicalSecret);

        // Le texte médical ne doit jamais apparaître dans le corps brut renvoyé au Secrétariat.
        var raw = await (await SendAsync(HttpMethod.Get, $"/api/v1/boarding/boarders/{stay.Id}", await SecretaireAsync()))
            .Content.ReadAsStringAsync();
        raw.Should().NotContain("arachides");
    }

    [Fact]
    public async Task The_Secretary_Cannot_Write_Medical_Notes_But_Can_Update_The_Rest_Without_Erasing_Them()
    {
        var directeur = await DirecteurAsync();
        await SetInternatAsync(directeur, enabled: true);
        var (_, room) = await CreateRoomAsync(directeur, "Garcons");
        var stay = await AssignOkAsync(directeur, await SeedEnrollmentAsync("M"), room.Beds[0].Id);
        var written = await SendAsync(HttpMethod.Put, $"/api/v1/boarding/boarders/{stay.Id}/profile", directeur,
            new { medicalNotes = MedicalSecret, allowedExitPersons = Array.Empty<object>(), rowVersion = stay.RowVersion });
        var token = (await written.Content.ReadFromJsonAsync<BoarderDetail>())!.Boarder.RowVersion;
        var secretaire = await SecretaireAsync();

        var forbidden = await SendAsync(HttpMethod.Put, $"/api/v1/boarding/boarders/{stay.Id}/profile", secretaire,
            new { medicalNotes = "Falsification", allowedExitPersons = Array.Empty<object>(), rowVersion = token });
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var allowed = await SendAsync(HttpMethod.Put, $"/api/v1/boarding/boarders/{stay.Id}/profile", secretaire,
            new { emergencyContactName = "Mme Ba", allowedExitPersons = Array.Empty<object>(), rowVersion = token });
        allowed.StatusCode.Should().Be(HttpStatusCode.OK);

        var after = await GetDetailAsync(directeur, stay.Id);
        (after.MedicalNotes, after.EmergencyContactName).Should().Be((MedicalSecret, "Mme Ba"));
    }

    [Theory]
    [InlineData("GET", "/api/v1/boarding/boarders")]
    [InlineData("POST", "/api/v1/boarding/assign-bed")]
    [InlineData("DELETE", "/api/v1/boarding/unassign-bed/00000000-0000-0000-0000-000000000001?rowVersion=0")]
    [InlineData("GET", "/api/v1/boarding/boarders/00000000-0000-0000-0000-000000000001")]
    [InlineData("PUT", "/api/v1/boarding/boarders/00000000-0000-0000-0000-000000000001/profile")]
    public async Task Teachers_And_Finance_Get_403_On_Every_Boarder_Route(string method, string url)
    {
        await SetInternatAsync(await DirecteurAsync(), enabled: true);
        object? body = method is "POST" or "PUT" ? new { } : null;

        foreach (var token in new[] { await EnseignantAsync(), await FinanceAsync() })
        {
            (await SendAsync(new HttpMethod(method), url, token, body)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
    }

    [Fact]
    public async Task The_Page_Size_Is_Capped_At_100_And_An_Unknown_Status_Is_Refused()
    {
        var directeur = await DirecteurAsync();
        await SetInternatAsync(directeur, enabled: true);

        var big = await SendAsync(HttpMethod.Get, "/api/v1/boarding/boarders?pageSize=1000", directeur);
        big.StatusCode.Should().Be(HttpStatusCode.OK);
        (await big.Content.ReadFromJsonAsync<PageResult>())!.PageSize.Should().Be(100);

        (await SendAsync(HttpMethod.Get, "/api/v1/boarding/boarders?status=nimporte", directeur))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }
}
