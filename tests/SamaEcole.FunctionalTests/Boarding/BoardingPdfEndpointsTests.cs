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
using SamaEcole.UnitTests.Boarding;
using Xunit;

namespace SamaEcole.FunctionalTests.Boarding;

/// <summary>
/// Fiche pensionnaire et registre du pavillon en PDF (lot E) de bout en bout — HTTP → MediatR → PostgreSQL réel → QuestPDF.
/// La confidentialité est contrôlée sur le TEXTE du PDF reçu (extraction PdfPig), jamais sur ses octets : le flux est
/// compressé, une recherche d'octets ne trouverait jamais rien et un test « absent » passerait à vide. Chaque « absent » a
/// donc son jumeau « présent » sur le même document. Une classe de tests partage UN conteneur : noms uniques.
/// </summary>
public class BoardingPdfEndpointsTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private const string SurveillantEmail = "surveillant@sama-ecole.sn";
    private const string SurveillantPassword = "SurveillantMotdepasse!2026";
    private const string MedicalSecret = "Allergie sévère aux arachides (EpiPen)";
    private const string EmergencyPhone = "77 123 45 67";
    private const string SupervisorPhone = "77 000 11 22";
    private const string SupervisorName = "M. Ndiaye";

    private static readonly SemaphoreSlim YearLock = new(1, 1);
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public BoardingPdfEndpointsTests(AuthApiFactory factory)
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

    /// <summary>Un pavillon (avec surveillant ET son téléphone), une chambre, un pensionnaire dont la fiche médicale et le contact sont renseignés.</summary>
    private async Task<(DormitoryDto Dormitory, BoarderItem Stay)> SeedBoarderAsync(string directeur)
    {
        var d = await SendAsync(HttpMethod.Post, "/api/v1/boarding/dormitories", directeur, new
        {
            name = Unique("Pavillon"), gender = "Garcons", supervisorName = SupervisorName, supervisorPhone = SupervisorPhone
        });
        d.StatusCode.Should().Be(HttpStatusCode.Created);
        var dormitory = (await d.Content.ReadFromJsonAsync<DormitoryDto>())!;

        var r = await SendAsync(HttpMethod.Post, "/api/v1/boarding/rooms", directeur, new { dormitoryId = dormitory.Id, name = "Chambre 1", bedCount = 2 });
        r.StatusCode.Should().Be(HttpStatusCode.Created);
        var room = (await r.Content.ReadFromJsonAsync<RoomResult>())!;

        var enrollment = await SeedEnrollmentAsync();
        var assigned = await SendAsync(HttpMethod.Post, "/api/v1/boarding/assign-bed", directeur,
            new { enrollmentId = enrollment, regime = "Interne", bedId = room.Beds[0].Id, includeBoardingFee = false });
        assigned.StatusCode.Should().Be(HttpStatusCode.OK);
        var stay = (await assigned.Content.ReadFromJsonAsync<BoarderItem>())!;

        var profile = await SendAsync(HttpMethod.Put, $"/api/v1/boarding/boarders/{stay.Id}/profile", directeur, new
        {
            medicalNotes = MedicalSecret, emergencyContactName = "Mme Diop", emergencyContactPhone = EmergencyPhone,
            allowedExitPersons = new[] { new { name = "Oumar Diop", relationship = "Oncle", phone = "76 123 45 67" } },
            rowVersion = stay.RowVersion
        });
        profile.StatusCode.Should().Be(HttpStatusCode.OK);

        return (dormitory, stay);
    }

    private async Task<(HttpResponseMessage Response, byte[] Bytes, string Text)> PdfAsync(string url, string? token)
    {
        var response = await SendAsync(HttpMethod.Get, url, token);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        return (response, bytes, response.StatusCode == HttpStatusCode.OK ? PdfText.Extract(bytes) : string.Empty);
    }

    private static string SheetUrl(Guid boarder) => $"/api/v1/boarding/boarders/{boarder}/pdf";
    private static string RegisterUrl(Guid dormitory, DateOnly? date = null) =>
        $"/api/v1/boarding/dormitories/{dormitory}/register-pdf" + (date is { } d ? $"?date={d:yyyy-MM-dd}" : string.Empty);

    private static async Task<string> ErrorCodeAsync(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<ApiError>())!.Code;

    // ------------------------------------------------------------------ Fiche : confidentialité

    [Fact]
    public async Task The_Sheet_Shows_The_Medical_Notes_To_Director_And_Supervisor_And_Hides_Them_From_The_Secretary()
    {
        var directeur = await DirecteurAsync();
        await SetInternatAsync(directeur, enabled: true);
        var (_, stay) = await SeedBoarderAsync(directeur);

        foreach (var (token, role, sees) in new[]
                 {
                     (directeur, "Directeur", true), (await SurveillantAsync(), "Surveillant", true), (await SecretaireAsync(), "Secretariat", false)
                 })
        {
            var (response, bytes, text) = await PdfAsync(SheetUrl(stay.Id), token);

            response.StatusCode.Should().Be(HttpStatusCode.OK, role);
            // Jumeau : le MÊME document porte l'identité et le contact d'urgence — l'absence qui suit n'est pas celle d'un PDF vide.
            text.Should().Contain(stay.StudentName, role).And.Contain(stay.Matricule, role).And.Contain(EmergencyPhone, role);
            if (sees)
            {
                text.Should().Contain(MedicalSecret, role).And.ContainEquivalentOf("fiche médicale", role);
            }
            else
            {
                text.Should().NotContain("Allergie", role).And.NotContain("EpiPen", role).And.NotContainEquivalentOf("fiche médicale", role);
            }

            System.Text.Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
        }
    }

    // ------------------------------------------------------------------ Registre : confidentialité

    [Fact]
    public async Task The_Register_Never_Carries_Health_Data_Or_Phone_Numbers_For_Any_Role()
    {
        var directeur = await DirecteurAsync();
        await SetInternatAsync(directeur, enabled: true);
        var (dormitory, stay) = await SeedBoarderAsync(directeur);

        foreach (var (token, role) in new[] { (directeur, "Directeur"), (await SurveillantAsync(), "Surveillant"), (await SecretaireAsync(), "Secretariat") })
        {
            var (response, _, text) = await PdfAsync(RegisterUrl(dormitory.Id), token);

            response.StatusCode.Should().Be(HttpStatusCode.OK, role);
            // Jumeau : le registre contient bien ses lignes et le nom du surveillant (sans son téléphone).
            text.Should().Contain(stay.StudentName, role).And.Contain(stay.Matricule, role)
                .And.Contain(dormitory.Name, role).And.Contain(SupervisorName, role);
            text.Should().NotContain(MedicalSecret, role).And.NotContain("Allergie", role).And.NotContainEquivalentOf("fiche médicale", role)
                .And.NotContain(EmergencyPhone, role).And.NotContain(SupervisorPhone, role).And.NotContain("770001122", role)
                .And.NotContain("76 123 45 67", role, "le téléphone d'une personne habilitée non plus");
        }
    }

    // ------------------------------------------------------------------ En-têtes, statuts, rôles, garde de module

    [Fact]
    public async Task Both_Documents_Are_Inline_Pdfs_With_A_Sanitized_File_Name()
    {
        var directeur = await DirecteurAsync();
        await SetInternatAsync(directeur, enabled: true);
        var (dormitory, stay) = await SeedBoarderAsync(directeur);

        var (sheet, sheetBytes, _) = await PdfAsync(SheetUrl(stay.Id), directeur);
        var (register, registerBytes, _) = await PdfAsync(RegisterUrl(dormitory.Id, Today), directeur);

        foreach (var (response, bytes) in new[] { (sheet, sheetBytes), (register, registerBytes) })
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
            System.Text.Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
            var disposition = response.Content.Headers.ContentDisposition!.ToString();
            disposition.Should().StartWith("inline").And.Contain(".pdf");
            disposition.Should().NotContain(" #", "pas d'espace ni de dièse dans le nom de fichier");
        }

        sheet.Content.Headers.ContentDisposition!.FileName.Should().Contain("Fiche-Pensionnaire-");
        register.Content.Headers.ContentDisposition!.FileName.Should().Contain("Registre-Pavillon-").And.Contain($"{Today:yyyy-MM-dd}");
    }

    [Fact]
    public async Task Unknown_Resources_Return_404_And_A_Future_Register_Date_Returns_422()
    {
        var directeur = await DirecteurAsync();
        await SetInternatAsync(directeur, enabled: true);
        var (dormitory, _) = await SeedBoarderAsync(directeur);

        (await SendAsync(HttpMethod.Get, SheetUrl(Guid.NewGuid()), directeur)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await SendAsync(HttpMethod.Get, RegisterUrl(Guid.NewGuid()), directeur)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await SendAsync(HttpMethod.Get, RegisterUrl(dormitory.Id, Today.AddDays(1)), directeur))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Theory]
    [InlineData("sheet")]
    [InlineData("register")]
    public async Task Teachers_Finance_And_Anonymous_Callers_Are_Refused(string document)
    {
        var directeur = await DirecteurAsync();
        await SetInternatAsync(directeur, enabled: true);
        var url = document == "sheet" ? SheetUrl(Guid.NewGuid()) : RegisterUrl(Guid.NewGuid());

        foreach (var token in new[] { await EnseignantAsync(), await FinanceAsync() })
        {
            (await SendAsync(HttpMethod.Get, url, token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        (await SendAsync(HttpMethod.Get, url, token: null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Both_Routes_Return_403_MODULE_DISABLED_While_The_Module_Is_Off()
    {
        var directeur = await DirecteurAsync();
        await SetInternatAsync(directeur, enabled: false);

        foreach (var url in new[] { SheetUrl(Guid.NewGuid()), RegisterUrl(Guid.NewGuid()) })
        {
            var response = await SendAsync(HttpMethod.Get, url, directeur);
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden, url);
            (await ErrorCodeAsync(response)).Should().Be("MODULE_DISABLED", url);
        }
    }

    // ------------------------------------------------------------------ Audit

    [Fact]
    public async Task Printing_Is_Audited_And_The_Audit_Entry_Never_Carries_The_Document_Content()
    {
        var directeur = await DirecteurAsync();
        await SetInternatAsync(directeur, enabled: true);
        var (dormitory, stay) = await SeedBoarderAsync(directeur);

        (await SendAsync(HttpMethod.Get, SheetUrl(stay.Id), directeur)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Get, RegisterUrl(dormitory.Id), directeur)).StatusCode.Should().Be(HttpStatusCode.OK);

        List<AuditLog> entries = [];
        await _factory.SeedAsOwnerAsync(async owner =>
        {
            entries = await owner.AuditLogs.IgnoreQueryFilters().AsNoTracking()
                .Where(a => a.Module == "Boarding" && (a.Action == "GetBoarderSheetPdf" || a.Action == "GetDormitoryRegisterPdf"))
                .ToListAsync();
        });

        entries.Select(e => e.Action).Should().Contain(["GetBoarderSheetPdf", "GetDormitoryRegisterPdf"]);
        entries.Should().OnlyContain(e => e.Success);
        entries.Select(e => $"{e.Module} {e.Action} {e.FailureReason}").Should()
            .NotContain(line => line.Contains(MedicalSecret) || line.Contains("Allergie") || line.Contains(EmergencyPhone));
    }
}
