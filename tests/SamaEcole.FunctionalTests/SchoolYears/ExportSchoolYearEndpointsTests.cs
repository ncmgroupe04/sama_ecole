using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace SamaEcole.FunctionalTests.SchoolYears;

/// <summary>
/// GET /school-years/{id}/export — export ZIP (élèves, paiements, classes) d'une année scolaire, de
/// bout en bout contre un vrai PostgreSQL. Critère : réservé au Directeur (croise données personnelles
/// et financières), et l'export reflète réellement ce qui a été inscrit/encaissé cette année-là.
/// </summary>
public class ExportSchoolYearEndpointsTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public ExportSchoolYearEndpointsTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public Task InitializeAsync() => _factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private const decimal Inscription = 10_000m;
    private const decimal Mensualite = 15_000m;
    private const int DefaultTuitionMonths = 9;
    private const decimal ExpectedTotal = Inscription + Mensualite * DefaultTuitionMonths;

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private record Tokens(string AccessToken, int ExpiresIn);
    private record ClassroomDto(Guid Id, string Name, string Level, int Capacity, int StudentCount);
    private record SchoolYearDto(Guid Id, string Label, DateOnly StartDate, DateOnly EndDate, bool IsActive, bool IsClosed);
    private record FeeCategoryDto(Guid Id, string Name, bool IsRecurring);
    private record Receipt(Guid EnrollmentId, string ReceiptNumber, string Matricule, decimal TotalDue);
    private record AuditLogEntry(Guid Id, string ActorFullName, string Module, string Action, bool Success, string? FailureReason, string? IpAddress, DateTimeOffset OccurredAt);
    private record PaginatedAuditLogs(List<AuditLogEntry> Items, int TotalCount, int Page, int PageSize);

    private async Task<string> TokenAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!.AccessToken;
    }

    private Task<string> DirecteurTokenAsync() => TokenAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);
    private Task<string> SecretaireTokenAsync() => TokenAsync(AuthApiFactory.SecretaireEmail, AuthApiFactory.SecretairePassword);
    private Task<string> FinanceTokenAsync() => TokenAsync(AuthApiFactory.FinanceEmail, AuthApiFactory.FinancePassword);

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await _client.SendAsync(request);
    }

    private async Task SeedFeeAsync(string token, string name, bool isRecurring, decimal amount)
    {
        var categoryResponse = await SendAsync(HttpMethod.Post, "/api/v1/finance/fee-categories", token,
            new { name, isRecurring });
        categoryResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var category = (await categoryResponse.Content.ReadFromJsonAsync<FeeCategoryDto>())!;

        var applyResponse = await SendAsync(HttpMethod.Post, "/api/v1/finance/fees/apply-standard", token,
            new { feeCategoryId = category.Id, amount, overwriteExisting = false });
        applyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>École prête, un élève inscrit et un versement encaissé : renvoie l'année scolaire à exporter.</summary>
    private async Task<Guid> SeedEnrolledAndPaidStudentAsync()
    {
        var directeur = await DirecteurTokenAsync();

        var classroomResponse = await SendAsync(HttpMethod.Post, "/api/v1/classrooms", directeur,
            new { name = "CM2", level = "Primaire", capacity = 40 });
        classroomResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var classroom = (await classroomResponse.Content.ReadFromJsonAsync<ClassroomDto>())!;

        var yearResponse = await SendAsync(HttpMethod.Post, "/api/v1/school-years", directeur, new
        {
            label = $"{Today.Year}-{Today.Year + 1}",
            startDate = Today.AddDays(-150).ToString("yyyy-MM-dd"),
            endDate = Today.AddDays(+120).ToString("yyyy-MM-dd")
        });
        yearResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var year = (await yearResponse.Content.ReadFromJsonAsync<SchoolYearDto>())!;

        await SeedFeeAsync(directeur, "Inscription", isRecurring: false, amount: Inscription);
        await SeedFeeAsync(directeur, "Mensualité", isRecurring: true, amount: Mensualite);

        var secretaire = await SecretaireTokenAsync();
        var enrollResponse = await SendAsync(HttpMethod.Post, "/api/v1/enrollments", secretaire, new
        {
            type = "NewEnrollment",
            classroomId = classroom.Id,
            fullName = "Awa Ndiaye",
            birthDate = "2015-05-20",
            birthPlace = "Dakar",
            gender = "F"
        });
        enrollResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var receipt = (await enrollResponse.Content.ReadFromJsonAsync<Receipt>())!;
        receipt.TotalDue.Should().Be(ExpectedTotal);

        var finance = await FinanceTokenAsync();
        var payment = await SendAsync(HttpMethod.Post, "/api/v1/finance/payments", finance,
            new { enrollmentId = receipt.EnrollmentId, amount = 50_000m, method = "MobileMoney" });
        payment.StatusCode.Should().Be(HttpStatusCode.Created);

        return year.Id;
    }

    private static Dictionary<string, string> ReadZipEntries(byte[] zipBytes)
    {
        using var stream = new MemoryStream(zipBytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var entries = new Dictionary<string, string>();
        foreach (var entry in archive.Entries)
        {
            using var entryStream = entry.Open();
            using var reader = new StreamReader(entryStream, Encoding.UTF8);
            entries[entry.Name] = reader.ReadToEnd();
        }

        return entries;
    }

    [Fact]
    public async Task Directeur_Should_Download_A_Zip_With_The_Three_Expected_Csv_Files()
    {
        var yearId = await SeedEnrolledAndPaidStudentAsync();
        var directeur = await DirecteurTokenAsync();

        var response = await SendAsync(HttpMethod.Get, $"/api/v1/school-years/{yearId}/export", directeur);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/zip");

        var entries = ReadZipEntries(await response.Content.ReadAsByteArrayAsync());

        entries.Should().ContainKey("eleves.csv");
        entries.Should().ContainKey("paiements.csv");
        entries.Should().ContainKey("classes.csv");

        entries["eleves.csv"].Should().Contain("Awa Ndiaye").And.Contain("CM2").And.Contain("Primaire");
        entries["paiements.csv"].Should().Contain("50000").And.Contain("Mobile Money");
        entries["classes.csv"].Should().Contain("CM2").And.Contain("1"); // effectif de 1 élève
    }

    [Fact]
    public async Task Exporting_Should_Produce_A_Corresponding_Audit_Entry()
    {
        var yearId = await SeedEnrolledAndPaidStudentAsync();
        var directeur = await DirecteurTokenAsync();

        var export = await SendAsync(HttpMethod.Get, $"/api/v1/school-years/{yearId}/export", directeur);
        export.StatusCode.Should().Be(HttpStatusCode.OK);

        var logsResponse = await SendAsync(HttpMethod.Get, "/api/v1/audit-logs", directeur);
        var logs = (await logsResponse.Content.ReadFromJsonAsync<PaginatedAuditLogs>())!;

        logs.Items.Should().ContainSingle(l => l.Module == "SchoolYears" && l.Action == "ExportSchoolYear" && l.Success);
    }

    [Theory]
    [InlineData(nameof(SecretaireTokenAsync))]
    [InlineData(nameof(FinanceTokenAsync))]
    public async Task Only_A_Directeur_May_Export_A_School_Year(string tokenFactoryName)
    {
        var yearId = await SeedEnrolledAndPaidStudentAsync();
        var token = tokenFactoryName switch
        {
            nameof(SecretaireTokenAsync) => await SecretaireTokenAsync(),
            nameof(FinanceTokenAsync) => await FinanceTokenAsync(),
            _ => throw new ArgumentOutOfRangeException(nameof(tokenFactoryName))
        };

        var response = await SendAsync(HttpMethod.Get, $"/api/v1/school-years/{yearId}/export", token);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Exporting_An_Unknown_School_Year_Should_Return_404()
    {
        var directeur = await DirecteurTokenAsync();

        var response = await SendAsync(HttpMethod.Get, $"/api/v1/school-years/{Guid.NewGuid()}/export", directeur);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Exporting_A_Year_With_No_Enrollments_Should_Produce_An_Empty_But_Valid_Zip()
    {
        var directeur = await DirecteurTokenAsync();

        var yearResponse = await SendAsync(HttpMethod.Post, "/api/v1/school-years", directeur, new
        {
            label = $"{Today.Year}-{Today.Year + 1}",
            startDate = Today.AddDays(-150).ToString("yyyy-MM-dd"),
            endDate = Today.AddDays(+120).ToString("yyyy-MM-dd")
        });
        yearResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var year = (await yearResponse.Content.ReadFromJsonAsync<SchoolYearDto>())!;

        var response = await SendAsync(HttpMethod.Get, $"/api/v1/school-years/{year.Id}/export", directeur);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var entries = ReadZipEntries(await response.Content.ReadAsByteArrayAsync());

        // Les 3 fichiers existent toujours, avec seulement leur ligne d'en-tête.
        entries["eleves.csv"].Trim().Should().Be("Matricule,Nom complet,Date de naissance,Genre,Classe,Niveau,Nom du tuteur,Téléphone du tuteur");
    }
}
