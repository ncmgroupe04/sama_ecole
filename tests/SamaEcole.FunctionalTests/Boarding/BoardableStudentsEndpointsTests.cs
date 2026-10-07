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
/// Recherche des élèves à affecter (lot G) de bout en bout — HTTP → MediatR → PostgreSQL réel : rôles, garde de module, et le parcours « externe →
/// pensionnaire → transfert » qui prouve que la recherche renvoie le jeton du SÉJOUR, directement utilisable par <c>assign-bed</c>. Une classe de tests
/// partage UN conteneur : noms uniques, assertions par identifiant.
/// </summary>
public class BoardableStudentsEndpointsTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private const string SurveillantEmail = "surveillant@sama-ecole.sn";
    private const string SurveillantPassword = "SurveillantMotdepasse!2026";

    private static readonly SemaphoreSlim YearLock = new(1, 1);
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public BoardableStudentsEndpointsTests(AuthApiFactory factory)
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
    private record BoarderItem(Guid Id, string StudentName, string Matricule, bool IsActive, uint RowVersion);

    private async Task<(DormitoryDto Dormitory, RoomResult Room)> CreateRoomAsync(string token, int beds = 2)
    {
        var d = await SendAsync(HttpMethod.Post, "/api/v1/boarding/dormitories", token, new { name = Unique("Pavillon"), gender = "Garcons" });
        d.StatusCode.Should().Be(HttpStatusCode.Created);
        var dormitory = (await d.Content.ReadFromJsonAsync<DormitoryDto>())!;
        var r = await SendAsync(HttpMethod.Post, "/api/v1/boarding/rooms", token, new { dormitoryId = dormitory.Id, name = "Chambre 1", bedCount = beds });
        r.StatusCode.Should().Be(HttpStatusCode.Created);
        return (dormitory, (await r.Content.ReadFromJsonAsync<RoomResult>())!);
    }

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

    /// <summary>Une inscription pour un nouvel élève ; l'année active est créée ou ÉTENDUE pour couvrir aujourd'hui (le registre l'exige).</summary>
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

    private record Candidate(
        Guid StudentId, Guid EnrollmentId, string Matricule, string FullName, string ClassroomName, Guid? BoarderId, string? Regime,
        Guid? BedId, int? BedNumber, string? RoomName, string? DormitoryName, uint? RowVersion);

    private async Task<HttpResponseMessage> SearchAsync(string? term, string? token) =>
        await SendAsync(HttpMethod.Get, "/api/v1/boarding/boardable-students" + (term is null ? string.Empty : "?term=" + Uri.EscapeDataString(term)), token);

    private async Task<List<Candidate>> SearchOkAsync(string term, string token)
    {
        var response = await SearchAsync(term, token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<List<Candidate>>())!;
    }

    /// <summary>Un élève inscrit et sa clé de recherche (le matricule est « M-{clé} », le nom « Élève {clé} »).</summary>
    private async Task<(Guid Enrollment, string Key)> SeedStudentAsync()
    {
        var enrollment = await SeedEnrollmentAsync();
        string key = string.Empty;
        await _factory.SeedAsOwnerAsync(async owner =>
        {
            key = (await owner.Enrollments.IgnoreQueryFilters().Where(e => e.Id == enrollment)
                .Join(owner.Students.IgnoreQueryFilters(), e => e.StudentId, s => s.Id, (e, s) => s.Matricule).SingleAsync())[2..];
        });
        return (enrollment, key);
    }

    // ------------------------------------------------------------------ Rôles et garde de module

    [Fact]
    public async Task Director_Secretary_And_Supervisor_Can_Search()
    {
        var directeur = await DirecteurAsync();
        await SetInternatAsync(directeur, enabled: true);

        foreach (var token in new[] { directeur, await SecretaireAsync(), await SurveillantAsync() })
        {
            (await SearchAsync("zz", token)).StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task Teachers_Finance_And_Anonymous_Callers_Are_Refused()
    {
        await SetInternatAsync(await DirecteurAsync(), enabled: true);

        foreach (var token in new[] { await EnseignantAsync(), await FinanceAsync() })
        {
            (await SearchAsync("zz", token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        (await SearchAsync("zz", token: null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_Route_Returns_403_MODULE_DISABLED_While_The_Module_Is_Off()
    {
        var directeur = await DirecteurAsync();
        await SetInternatAsync(directeur, enabled: false);

        var response = await SearchAsync("zz", directeur);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadFromJsonAsync<ApiError>())!.Code.Should().Be("MODULE_DISABLED");
    }

    // ------------------------------------------------------------------ Parcours

    [Fact]
    public async Task A_Short_Or_Missing_Term_Returns_An_Empty_List()
    {
        var directeur = await DirecteurAsync();
        await SetInternatAsync(directeur, enabled: true);

        foreach (var term in new[] { null, "", "a" })
        {
            var response = await SearchAsync(term, directeur);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            (await response.Content.ReadFromJsonAsync<List<Candidate>>())!.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task The_Candidate_Goes_From_External_To_Boarder_And_Its_Token_Drives_A_Transfer()
    {
        var directeur = await DirecteurAsync();
        await SetInternatAsync(directeur, enabled: true);
        var (_, room) = await CreateRoomAsync(directeur, beds: 2);
        var (enrollment, key) = await SeedStudentAsync();

        // 1. Pas encore de séjour : externe.
        var before = (await SearchOkAsync(key, directeur)).Should().ContainSingle().Subject;
        (before.EnrollmentId, before.BoarderId, before.Regime, before.RowVersion).Should().Be((enrollment, (Guid?)null, (string?)null, (uint?)null));

        // 2. Affectation : la recherche décrit désormais le séjour.
        var assign = await SendAsync(HttpMethod.Post, "/api/v1/boarding/assign-bed", directeur,
            new { enrollmentId = enrollment, regime = "Interne", bedId = room.Beds[0].Id, includeBoardingFee = false });
        assign.StatusCode.Should().Be(HttpStatusCode.OK);
        var stay = (await assign.Content.ReadFromJsonAsync<BoarderItem>())!;

        var after = (await SearchOkAsync(key, directeur)).Should().ContainSingle().Subject;
        (after.BoarderId, after.Regime, after.BedId, after.BedNumber, after.RoomName).Should().Be((stay.Id, "Interne", room.Beds[0].Id, 1, "Chambre 1"));
        after.DormitoryName.Should().NotBeNullOrWhiteSpace();
        after.RowVersion.Should().Be(stay.RowVersion, "le jeton est celui du séjour, directement réutilisable par assign-bed");

        // 3. Le jeton renvoyé par la recherche suffit à un transfert : aucun second appel pour le lire.
        var transfer = await SendAsync(HttpMethod.Post, "/api/v1/boarding/assign-bed", directeur,
            new { enrollmentId = enrollment, regime = "Interne", bedId = room.Beds[1].Id, includeBoardingFee = false, rowVersion = after.RowVersion });
        transfer.StatusCode.Should().Be(HttpStatusCode.OK);
        (await transfer.Content.ReadFromJsonAsync<BoarderItem>())!.Id.Should().Be(stay.Id, "un transfert conserve le même séjour");
    }
}
