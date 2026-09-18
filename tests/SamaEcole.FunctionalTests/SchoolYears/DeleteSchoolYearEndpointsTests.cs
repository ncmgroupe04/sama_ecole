using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.FunctionalTests.Common;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace SamaEcole.FunctionalTests.SchoolYears;

/// <summary>
/// DELETE /api/v1/school-years/{id} — de bout en bout (HTTP → MediatR → PostgreSQL réel).
///
/// Ce qui est prouvé ici, et qui ne l'est nulle part ailleurs :
///   * la garde de rôle (Directeur seul) et celle du mot de confirmation (le libellé EXACT) ;
///   * les DEUX régimes : en mode test l'année et ses données partent ; en mode réel, une année qui
///     porte des données est refusée (409 SCHOOL_YEAR_HAS_DATA) et une année vide est ARCHIVÉE ;
///   * la rebascule de l'année active quand c'est elle qu'on supprime.
///
/// L'isolation multi-tenant et l'ordre des clés étrangères sont couverts côté base par
/// DeleteSchoolYearTests (catégorie MultiTenant).
/// </summary>
public class DeleteSchoolYearEndpointsTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    public async Task InitializeAsync()
    {
        await factory.ResetTestUsersAsync();

        // Chaque test repart en mode test : les tests d'une classe partagent la fabrique, et un
        // WentLiveAt laissé par un test précédent ferait basculer le suivant dans l'autre régime.
        await SetLiveModeAsync(live: false);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private record Tokens(string AccessToken, string RefreshToken, int ExpiresIn);

    private record SchoolYearDto(
        Guid Id, string Label, DateOnly StartDate, DateOnly EndDate, bool IsActive, bool IsClosed);

    private record DeleteResult(
        string Label, bool WasActive, bool WasPurged, SchoolYearDto? NewActiveYear,
        bool RequiresActiveYearSelection, ResetSummary? Summary);

    private record ResetSummary(int TotalRowsDeleted);
    private record ApiError(string Code, string Message);

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    // Périodes relatives à aujourd'hui : des dates en dur finiraient par faire basculer « en cours »
    // en « terminée » et casseraient ces tests toutes seules.
    private static object CurrentYear(string label) => YearBody(label, -150, +90);
    private static object FutureYear(string label) => YearBody(label, +100, +340);

    private static object YearBody(string label, int startOffsetDays, int endOffsetDays) => new
    {
        label,
        startDate = Today.AddDays(startOffsetDays).ToString("yyyy-MM-dd"),
        endDate = Today.AddDays(endOffsetDays).ToString("yyyy-MM-dd")
    };

    private async Task<Tokens> LoginAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!;
    }

    private Task<Tokens> LoginAsDirecteurAsync() =>
        LoginAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string path, string accessToken, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await _client.SendAsync(request);
    }

    private Task<HttpResponseMessage> DeleteYearAsync(string token, Guid id, string confirmation) =>
        SendAsync(HttpMethod.Delete, $"/api/v1/school-years/{id}", token, new { confirmation });

    private async Task<SchoolYearDto> CreateYearAsync(string token, object body)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/v1/school-years", token, body);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<SchoolYearDto>())!;
    }

    private async Task<List<SchoolYearDto>> ListYearsAsync(string token)
    {
        var response = await SendAsync(HttpMethod.Get, "/api/v1/school-years", token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<List<SchoolYearDto>>())!;
    }

    private Task SetLiveModeAsync(bool live) =>
        factory.SeedAsOwnerAsync(async db =>
        {
            var school = await db.Schools.FirstAsync(s => s.Id == AuthApiFactory.EcoleId);
            school.WentLiveAt = live ? DateTimeOffset.UtcNow : null;
        });

    /// <summary>Rattache une inscription à l'année : de quoi la rendre « non vide » en mode réel.</summary>
    private Task SeedEnrollmentAsync(Guid schoolYearId) =>
        factory.SeedAsOwnerAsync(async db =>
        {
            var classroomId = Guid.NewGuid();
            var studentId = Guid.NewGuid();

            db.Classrooms.Add(new Classroom
            {
                Id = classroomId, SchoolId = AuthApiFactory.EcoleId, Name = $"CM2-{Guid.NewGuid():N}"[..12],
                Level = "Primaire", Capacity = 40
            });

            db.Students.Add(new Student
            {
                Id = studentId, SchoolId = AuthApiFactory.EcoleId,
                Matricule = $"ELEV-{Guid.NewGuid():N}"[..20], FullName = "Awa Fall",
                BirthDate = new DateOnly(2015, 3, 12), BirthPlace = "Dakar", Gender = "F",
                ClassroomId = classroomId
            });

            db.Enrollments.Add(new Enrollment
            {
                SchoolId = AuthApiFactory.EcoleId, StudentId = studentId, SchoolYearId = schoolYearId,
                ClassroomId = classroomId, Type = EnrollmentType.NewEnrollment,
                Status = EnrollmentStatus.Confirmed, TotalDue = 150_000m, AmountPaid = 0m,
                ReceiptNumber = $"REC-{Guid.NewGuid():N}"[..20], EnrolledAt = DateTimeOffset.UtcNow
            });

            await Task.CompletedTask;
        });

    [Fact]
    public async Task Deleting_A_Year_Must_Be_Refused_To_Anyone_But_The_Director()
    {
        var directeur = await LoginAsDirecteurAsync();
        var year = await CreateYearAsync(directeur.AccessToken, CurrentYear("2026-2027"));

        var secretaire = await LoginAsync(AuthApiFactory.SecretaireEmail, AuthApiFactory.SecretairePassword);

        var response = await DeleteYearAsync(secretaire.AccessToken, year.Id, "2026-2027");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ListYearsAsync(directeur.AccessToken)).Should().HaveCount(1, "rien ne doit avoir été supprimé");
    }

    [Fact]
    public async Task A_Wrong_Confirmation_Must_Be_Refused()
    {
        var directeur = await LoginAsDirecteurAsync();
        var year = await CreateYearAsync(directeur.AccessToken, CurrentYear("2026-2027"));

        // Le mot-clé générique des autres gardes ne marche PAS ici : c'est le libellé de l'année visée
        // qu'il faut recopier, précisément pour obliger à regarder la ligne qu'on supprime.
        var response = await DeleteYearAsync(directeur.AccessToken, year.Id, "PURGER");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ListYearsAsync(directeur.AccessToken)).Should().HaveCount(1);
    }

    [Fact]
    public async Task An_Unknown_Year_Must_Return_404()
    {
        var directeur = await LoginAsDirecteurAsync();

        var response = await DeleteYearAsync(directeur.AccessToken, Guid.NewGuid(), "2026-2027");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task In_Test_Mode_The_Year_And_Its_Data_Must_Be_Erased()
    {
        var directeur = await LoginAsDirecteurAsync();
        var current = await CreateYearAsync(directeur.AccessToken, CurrentYear("2026-2027"));
        var next = await CreateYearAsync(directeur.AccessToken, FutureYear("2027-2028"));

        await SeedEnrollmentAsync(next.Id);

        var response = await DeleteYearAsync(directeur.AccessToken, next.Id, "2027-2028");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = (await response.Content.ReadFromJsonAsync<DeleteResult>())!;
        result.WasPurged.Should().BeTrue("en mode test, l'année et ses données sont effacées");
        result.WasActive.Should().BeFalse();
        result.Summary!.TotalRowsDeleted.Should().BeGreaterThan(0);

        var years = await ListYearsAsync(directeur.AccessToken);
        years.Should().ContainSingle().Which.Id.Should().Be(current.Id);
    }

    [Fact]
    public async Task Deleting_The_Active_Year_Must_Switch_The_School_Over_To_Another_Open_Year()
    {
        var directeur = await LoginAsDirecteurAsync();

        // La première année créée est l'année active (JGK-C01) : c'est donc elle qu'on supprime.
        var current = await CreateYearAsync(directeur.AccessToken, CurrentYear("2026-2027"));
        var next = await CreateYearAsync(directeur.AccessToken, FutureYear("2027-2028"));

        current.IsActive.Should().BeTrue();

        var response = await DeleteYearAsync(directeur.AccessToken, current.Id, "2026-2027");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = (await response.Content.ReadFromJsonAsync<DeleteResult>())!;
        result.WasActive.Should().BeTrue();
        result.RequiresActiveYearSelection.Should().BeFalse();
        result.NewActiveYear!.Id.Should().Be(next.Id,
            "sans année précédente ouverte, l'établissement bascule sur l'année suivante");

        var years = await ListYearsAsync(directeur.AccessToken);
        years.Should().ContainSingle().Which.IsActive.Should().BeTrue(
            "un établissement sans année active ne peut plus rien inscrire");
    }

    [Fact]
    public async Task Deleting_The_Only_Year_Must_Ask_For_A_New_Active_Year()
    {
        var directeur = await LoginAsDirecteurAsync();
        var only = await CreateYearAsync(directeur.AccessToken, CurrentYear("2026-2027"));

        var response = await DeleteYearAsync(directeur.AccessToken, only.Id, "2026-2027");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = (await response.Content.ReadFromJsonAsync<DeleteResult>())!;
        result.NewActiveYear.Should().BeNull();
        result.RequiresActiveYearSelection.Should().BeTrue(
            "il ne restait aucune année à activer : l'écran doit le dire plutôt que laisser l'école muette");

        (await ListYearsAsync(directeur.AccessToken)).Should().BeEmpty();
    }

    [Fact]
    public async Task In_Live_Mode_A_Year_That_Carries_Data_Must_Be_Refused()
    {
        var directeur = await LoginAsDirecteurAsync();
        var current = await CreateYearAsync(directeur.AccessToken, CurrentYear("2026-2027"));

        await SeedEnrollmentAsync(current.Id);
        await SetLiveModeAsync(live: true);

        var response = await DeleteYearAsync(directeur.AccessToken, current.Id, "2026-2027");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var error = (await response.Content.ReadFromJsonAsync<ApiError>())!;
        error.Code.Should().Be("SCHOOL_YEAR_HAS_DATA");
        error.Message.Should().Contain("inscription",
            "le message doit dire CE QUI bloque, sinon le Directeur ne sait pas quoi faire");

        // Et l'année est toujours là, intacte.
        var years = await ListYearsAsync(directeur.AccessToken);
        years.Should().ContainSingle().Which.Id.Should().Be(current.Id);
    }

    [Fact]
    public async Task In_Live_Mode_An_Empty_Year_Must_Be_Archived()
    {
        var directeur = await LoginAsDirecteurAsync();
        var current = await CreateYearAsync(directeur.AccessToken, CurrentYear("2026-2027"));
        var next = await CreateYearAsync(directeur.AccessToken, FutureYear("2027-2028"));

        await SetLiveModeAsync(live: true);

        // Cas réel : une année préparée en double, ou créée avec un mauvais libellé, qui n'a jamais servi.
        var response = await DeleteYearAsync(directeur.AccessToken, next.Id, "2027-2028");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = (await response.Content.ReadFromJsonAsync<DeleteResult>())!;
        result.WasPurged.Should().BeFalse("en mode réel, l'année est archivée — jamais effacée");
        result.Summary.Should().BeNull();

        var years = await ListYearsAsync(directeur.AccessToken);
        years.Should().ContainSingle().Which.Id.Should().Be(current.Id);
    }
}
