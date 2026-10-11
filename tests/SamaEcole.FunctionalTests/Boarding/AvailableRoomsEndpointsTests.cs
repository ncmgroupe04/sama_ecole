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
/// Chambres disponibles (lot F0) de bout en bout — HTTP → MediatR → PostgreSQL réel : rôles, garde de module, et le parcours « créer une chambre →
/// la voir proposée avec ses places → affecter des élèves → le nombre de places baisse, puis la chambre disparaît quand elle est pleine ».
/// Une classe de tests partage UN conteneur : noms uniques, assertions par identifiant.
/// </summary>
public class AvailableRoomsEndpointsTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private const string SurveillantEmail = "surveillant@sama-ecole.sn";
    private const string SurveillantPassword = "SurveillantMotdepasse!2026";

    private static readonly SemaphoreSlim YearLock = new(1, 1);
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public AvailableRoomsEndpointsTests(AuthApiFactory factory)
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
    private record AvailableRoom(Guid RoomId, string RoomName, Guid DormitoryId, string DormitoryName, string Gender, int FreeBeds);

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

    private async Task<RoomResult> CreateRoomAsync(string token, int beds)
    {
        var d = await SendAsync(HttpMethod.Post, "/api/v1/boarding/dormitories", token, new { name = Unique("Pavillon"), gender = "Garcons" });
        d.StatusCode.Should().Be(HttpStatusCode.Created);
        var dormitory = (await d.Content.ReadFromJsonAsync<DormitoryDto>())!;
        var r = await SendAsync(HttpMethod.Post, "/api/v1/boarding/rooms", token, new { dormitoryId = dormitory.Id, name = "Chambre 1", bedCount = beds });
        r.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await r.Content.ReadFromJsonAsync<RoomResult>())!;
    }

    /// <summary>Une inscription pour un nouvel élève ; l'année active est créée ou ÉTENDUE pour couvrir aujourd'hui.</summary>
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

    private Task<HttpResponseMessage> ListAsync(string? token) => SendAsync(HttpMethod.Get, "/api/v1/boarding/available-rooms", token);

    private async Task<List<AvailableRoom>> ListOkAsync(string token)
    {
        var response = await ListAsync(token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<List<AvailableRoom>>())!;
    }

    // ------------------------------------------------------------------ Rôles et garde de module

    [Fact]
    public async Task Director_Secretary_And_Supervisor_Can_List()
    {
        var directeur = await DirecteurAsync();
        await SetInternatAsync(directeur, enabled: true);

        foreach (var token in new[] { directeur, await SecretaireAsync(), await SurveillantAsync() })
        {
            (await ListAsync(token)).StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task Teachers_Finance_And_Anonymous_Callers_Are_Refused()
    {
        await SetInternatAsync(await DirecteurAsync(), enabled: true);

        foreach (var token in new[] { await EnseignantAsync(), await FinanceAsync() })
        {
            (await ListAsync(token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        (await ListAsync(token: null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_Route_Returns_403_MODULE_DISABLED_While_The_Module_Is_Off()
    {
        var directeur = await DirecteurAsync();
        await SetInternatAsync(directeur, enabled: false);

        var response = await ListAsync(directeur);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadFromJsonAsync<ApiError>())!.Code.Should().Be("MODULE_DISABLED");
    }

    // ------------------------------------------------------------------ Parcours

    [Fact]
    public async Task A_New_Room_Is_Offered_With_Its_Places_Which_Drop_As_Students_Are_Assigned_Until_The_Room_Disappears()
    {
        var directeur = await DirecteurAsync();
        await SetInternatAsync(directeur, enabled: true);
        var room = await CreateRoomAsync(directeur, beds: 2);

        (await ListOkAsync(directeur)).Should().ContainSingle(r => r.RoomId == room.Id).Which.FreeBeds.Should().Be(2);

        foreach (var (bed, expectedFree) in new[] { (room.Beds[0], 1), (room.Beds[1], 0) })
        {
            var assign = await SendAsync(HttpMethod.Post, "/api/v1/boarding/assign-bed", directeur, new
            {
                enrollmentId = await SeedEnrollmentAsync(), regime = "Interne", bedId = bed.Id, includeBoardingFee = false, rowVersion = (uint?)null
            });
            assign.StatusCode.Should().Be(HttpStatusCode.OK, await assign.Content.ReadAsStringAsync());

            var rooms = await ListOkAsync(directeur);
            if (expectedFree == 0)
            {
                rooms.Should().NotContain(r => r.RoomId == room.Id, "une chambre pleine n'est plus proposée");
            }
            else
            {
                rooms.Should().ContainSingle(r => r.RoomId == room.Id).Which.FreeBeds.Should().Be(expectedFree);
            }
        }
    }

    [Fact]
    public async Task A_Bed_Put_In_Maintenance_Reduces_The_Offered_Places()
    {
        var directeur = await DirecteurAsync();
        await SetInternatAsync(directeur, enabled: true);
        var room = await CreateRoomAsync(directeur, beds: 2);

        var maintenance = await SendAsync(HttpMethod.Put, $"/api/v1/boarding/beds/{room.Beds[0].Id}/status", directeur,
            new { status = "Maintenance", rowVersion = room.Beds[0].RowVersion });
        maintenance.StatusCode.Should().Be(HttpStatusCode.OK, await maintenance.Content.ReadAsStringAsync());

        (await ListOkAsync(directeur)).Should().ContainSingle(r => r.RoomId == room.Id).Which.FreeBeds.Should().Be(1);
    }
}
