using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace SamaEcole.FunctionalTests.Students;

/// <summary>
/// POST /api/v1/students/import (et son modèle GET .../import/template) contre un vrai PostgreSQL, à
/// travers la vraie pile HTTP.
///
/// Le contrat central : RIEN n'est jamais écrit tant que le fichier n'est pas intégralement valide
/// (dryRun=true = aperçu pur ; dryRun=false = confirmation, tout ou rien) — la garantie que même une
/// coupure réseau pendant un import massif (rentrée scolaire) ne laisse jamais un import à moitié
/// appliqué.
/// </summary>
public class StudentImportEndpointsTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public StudentImportEndpointsTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public Task InitializeAsync() => _factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private record Tokens(string AccessToken, int ExpiresIn);
    private record ClassroomDto(Guid Id, string Name, string Level, int Capacity);
    private record ErrorResponseDto(string Code, string Message, Dictionary<string, string[]>? Details, string TraceId);
    private record ImportRowResultDto(int RowNumber, bool IsValid, string FullName, string BirthDate,
        string BirthPlace, string Gender, string ClassroomName, string GuardianName, string GuardianPhone,
        Dictionary<string, string> FieldErrors);
    private record ImportResultDto(bool DryRun, bool Committed, int TotalRows, int ValidRows, int InvalidRows,
        int Created, List<ImportRowResultDto> Rows);
    private record StudentListItemDto(Guid Id, string Matricule, string FullName, DateOnly BirthDate,
        string? BirthPlace, string Gender, Guid ClassroomId, string ClassroomName);
    private record PaginatedStudentsDto(List<StudentListItemDto> Items, int TotalCount, int Page, int PageSize);

    private const string Header = "Nom complet;Date de naissance;Lieu de naissance;Genre;Classe;Tuteur;Téléphone";

    private async Task<string> AccessTokenAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!.AccessToken;
    }

    private Task<string> DirecteurTokenAsync() =>
        AccessTokenAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);

    private Task<string> FinanceTokenAsync() =>
        AccessTokenAsync(AuthApiFactory.FinanceEmail, AuthApiFactory.FinancePassword);

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await _client.SendAsync(request);
    }

    private async Task<ClassroomDto> CreateClassroomAsync(string token, string name)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/v1/classrooms", token,
            new { name, level = "Primaire", capacity = 40 });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ClassroomDto>())!;
    }

    private async Task<HttpResponseMessage> ImportAsync(
        string token, string csvContent, bool dryRun, string fileName = "eleves.csv")
    {
        using var content = new MultipartFormDataContent
        {
            { new StringContent(dryRun.ToString()), "dryRun" }
        };
        var fileBytes = new ByteArrayContent(Encoding.UTF8.GetBytes(csvContent));
        fileBytes.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(fileBytes, "file", fileName);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/students/import") { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(request);
    }

    private async Task<int> StudentCountAsync(string token)
    {
        var response = await SendAsync(HttpMethod.Get, "/api/v1/students?page=1&pageSize=1", token);
        var page = (await response.Content.ReadFromJsonAsync<PaginatedStudentsDto>())!;
        return page.TotalCount;
    }

    [Fact]
    public async Task A_Dry_Run_On_A_Well_Formed_File_Reports_Every_Row_Valid_And_Writes_Nothing()
    {
        var directeur = await DirecteurTokenAsync();
        var classroom = await CreateClassroomAsync(directeur, "CM2 A");
        var before = await StudentCountAsync(directeur);

        var csv = $"{Header}\nAwa Ndiaye;12/03/2015;Dakar;F;{classroom.Name};Moussa Ndiaye;+221771234567\n" +
                  $"Modou Diop;01/06/2015;Thiès;M;{classroom.Name};;";

        var response = await ImportAsync(directeur, csv, dryRun: true);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<ImportResultDto>())!;
        result.DryRun.Should().BeTrue();
        result.Committed.Should().BeFalse();
        result.TotalRows.Should().Be(2);
        result.ValidRows.Should().Be(2);
        result.InvalidRows.Should().Be(0);
        result.Created.Should().Be(0);

        (await StudentCountAsync(directeur)).Should().Be(before, "un aperçu (dryRun) ne doit jamais écrire en base");
    }

    [Fact]
    public async Task Confirming_A_Fully_Valid_File_Creates_Every_Student_Across_Their_Respective_Classes()
    {
        // Le cœur du ticket : une classe PAR LIGNE, pas un import limité à une seule classe — une
        // rentrée scolaire mélange plusieurs classes dans le même fichier.
        var directeur = await DirecteurTokenAsync();
        var classA = await CreateClassroomAsync(directeur, "CM2 A");
        var classB = await CreateClassroomAsync(directeur, "CM2 B");

        var csv = $"{Header}\n" +
                  $"Awa Ndiaye;12/03/2015;Dakar;F;{classA.Name};Moussa Ndiaye;+221771234567\n" +
                  $"Modou Diop;01/06/2015;Thiès;M;{classB.Name};;";

        var response = await ImportAsync(directeur, csv, dryRun: false);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<ImportResultDto>())!;
        result.Committed.Should().BeTrue();
        result.Created.Should().Be(2);

        var listResponse = await SendAsync(HttpMethod.Get, "/api/v1/students?page=1&pageSize=50", directeur);
        var page = (await listResponse.Content.ReadFromJsonAsync<PaginatedStudentsDto>())!;
        page.Items.Should().ContainSingle(s => s.FullName == "Awa Ndiaye" && s.ClassroomId == classA.Id);
        page.Items.Should().ContainSingle(s => s.FullName == "Modou Diop" && s.ClassroomId == classB.Id);
        page.Items.Should().OnlyContain(s => !string.IsNullOrEmpty(s.Matricule), "le matricule doit être généré à la confirmation");
    }

    [Fact]
    public async Task An_Invalid_Birth_Date_Is_Reported_On_The_Correct_Field_Without_Rejecting_Other_Rows()
    {
        var directeur = await DirecteurTokenAsync();
        var classroom = await CreateClassroomAsync(directeur, "CM2 A");

        var csv = $"{Header}\n" +
                  $"Awa Ndiaye;pas-une-date;Dakar;F;{classroom.Name};;\n" +
                  $"Modou Diop;01/06/2015;Thiès;M;{classroom.Name};;";

        var response = await ImportAsync(directeur, csv, dryRun: true);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<ImportResultDto>())!;
        result.InvalidRows.Should().Be(1);
        result.ValidRows.Should().Be(1, "une ligne en erreur ne doit jamais empêcher l'aperçu des autres");

        var badRow = result.Rows.Single(r => r.FullName == "Awa Ndiaye");
        badRow.IsValid.Should().BeFalse();
        badRow.FieldErrors.Should().ContainKey("birthDate");
        badRow.FieldErrors.Should().NotContainKey("fullName", "seul le champ fautif doit être signalé");

        var goodRow = result.Rows.Single(r => r.FullName == "Modou Diop");
        goodRow.IsValid.Should().BeTrue();
        goodRow.FieldErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task A_Missing_Birth_Place_Is_Rejected()
    {
        // Intégration directe avec la feature E (BirthPlace obligatoire) : l'import ne doit pas
        // permettre de contourner la règle par un fichier qui omet la colonne.
        var directeur = await DirecteurTokenAsync();
        var classroom = await CreateClassroomAsync(directeur, "CM2 A");

        var csv = $"{Header}\nAwa Ndiaye;12/03/2015;;F;{classroom.Name};;";

        var response = await ImportAsync(directeur, csv, dryRun: true);

        var result = (await response.Content.ReadFromJsonAsync<ImportResultDto>())!;
        result.Rows.Single().FieldErrors.Should().ContainKey("birthPlace");
    }

    [Fact]
    public async Task An_Unknown_Classroom_Name_Is_Reported_With_A_Helpful_Message()
    {
        var directeur = await DirecteurTokenAsync();
        await CreateClassroomAsync(directeur, "CM2 A"); // Existe, mais pas celle visée par la ligne ci-dessous.

        var csv = $"{Header}\nAwa Ndiaye;12/03/2015;Dakar;F;Classe Inexistante;;";

        var response = await ImportAsync(directeur, csv, dryRun: true);

        var result = (await response.Content.ReadFromJsonAsync<ImportResultDto>())!;
        var row = result.Rows.Single();
        row.IsValid.Should().BeFalse();
        row.FieldErrors.Should().ContainKey("classroomName");
        row.FieldErrors["classroomName"].Should().Contain("Classe Inexistante");
    }

    [Fact]
    public async Task An_Invalid_Gender_Is_Rejected()
    {
        var directeur = await DirecteurTokenAsync();
        var classroom = await CreateClassroomAsync(directeur, "CM2 A");

        var csv = $"{Header}\nAwa Ndiaye;12/03/2015;Dakar;X;{classroom.Name};;";

        var response = await ImportAsync(directeur, csv, dryRun: true);

        var result = (await response.Content.ReadFromJsonAsync<ImportResultDto>())!;
        result.Rows.Single().FieldErrors.Should().ContainKey("gender");
    }

    [Fact]
    public async Task An_Invalid_Guardian_Email_Is_Rejected()
    {
        const string header = "Nom complet;Date de naissance;Lieu de naissance;Genre;Classe;Tuteur;Téléphone;E-mail;Adresse";
        var directeur = await DirecteurTokenAsync();
        var classroom = await CreateClassroomAsync(directeur, "CM2 A");

        var csv = $"{header}\nAwa Ndiaye;12/03/2015;Dakar;F;{classroom.Name};;;pas-un-email;";

        var response = await ImportAsync(directeur, csv, dryRun: true);

        var result = (await response.Content.ReadFromJsonAsync<ImportResultDto>())!;
        result.Rows.Single().FieldErrors.Should().ContainKey("guardianEmail");
    }

    [Fact]
    public async Task Confirming_A_File_With_An_Invalid_Row_Rejects_The_Whole_Batch_And_Writes_Nothing()
    {
        // AGENTS.md règle #5 / le cœur de la demande "réseau" : jamais un import partiel. La ligne 1 est
        // valide, la ligne 2 porte une classe inconnue — AUCUNE des deux ne doit être enregistrée.
        var directeur = await DirecteurTokenAsync();
        var classroom = await CreateClassroomAsync(directeur, "CM2 A");
        var before = await StudentCountAsync(directeur);

        var csv = $"{Header}\n" +
                  $"Awa Ndiaye;12/03/2015;Dakar;F;{classroom.Name};;\n" +
                  $"Modou Diop;01/06/2015;Thiès;M;Classe Inconnue;;";

        var response = await ImportAsync(directeur, csv, dryRun: false);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var error = (await response.Content.ReadFromJsonAsync<ErrorResponseDto>())!;
        error.Details.Should().ContainKey("Ligne 3");

        (await StudentCountAsync(directeur)).Should().Be(before, "aucune ligne ne doit être écrite si le fichier contient la moindre erreur");
    }

    [Fact]
    public async Task A_Dry_Run_Never_Consumes_A_Matricule_Sequence_Number()
    {
        // Si l'aperçu appelait le générateur de matricule, le PREMIER élève réellement créé ensuite
        // porterait un numéro déjà "sauté" — un trou dans la numérotation officielle (AGENTS.md règle #3).
        var directeur = await DirecteurTokenAsync();
        var classroom = await CreateClassroomAsync(directeur, "CM2 A");

        var csv = $"{Header}\nAwa Ndiaye;12/03/2015;Dakar;F;{classroom.Name};;";
        await ImportAsync(directeur, csv, dryRun: true);
        await ImportAsync(directeur, csv, dryRun: true); // Un second aperçu, pour bonne mesure.

        var createResponse = await SendAsync(HttpMethod.Post, "/api/v1/students", directeur, new
        {
            fullName = "Premier Élève Réel", birthDate = "2015-01-01", birthPlace = "Dakar",
            gender = "M", classroomId = classroom.Id
        });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<Dictionary<string, object>>();

        created!["matricule"].ToString().Should().Contain("0001", "aucun aperçu ne doit avoir consommé de numéro avant cette toute première création réelle");
    }

    [Fact]
    public async Task A_Finance_Must_Not_Import_Students()
    {
        var directeur = await DirecteurTokenAsync();
        var classroom = await CreateClassroomAsync(directeur, "CM2 A");
        var finance = await FinanceTokenAsync();

        var csv = $"{Header}\nAwa Ndiaye;12/03/2015;Dakar;F;{classroom.Name};;";
        var response = await ImportAsync(finance, csv, dryRun: true);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_Unsupported_File_Extension_Should_Be_Rejected()
    {
        var directeur = await DirecteurTokenAsync();
        var classroom = await CreateClassroomAsync(directeur, "CM2 A");

        var csv = $"{Header}\nAwa Ndiaye;12/03/2015;Dakar;F;{classroom.Name};;";
        var response = await ImportAsync(directeur, csv, dryRun: true, fileName: "eleves.docx");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_File_Beyond_The_Row_Limit_Should_Be_Rejected_Before_Any_Row_Level_Validation()
    {
        var directeur = await DirecteurTokenAsync();
        var classroom = await CreateClassroomAsync(directeur, "CM2 A");

        var sb = new StringBuilder(Header);
        for (var i = 0; i < 1001; i++)
        {
            sb.Append($"\nÉlève {i};12/03/2015;Dakar;F;{classroom.Name};;");
        }

        var response = await ImportAsync(directeur, sb.ToString(), dryRun: true);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Importing_Without_A_Token_Should_Return_401()
    {
        var csv = $"{Header}\nAwa Ndiaye;12/03/2015;Dakar;F;CM2 A;;";

        using var content = new MultipartFormDataContent { { new StringContent("true"), "dryRun" } };
        var fileBytes = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        content.Add(fileBytes, "file", "eleves.csv");

        var response = await _client.PostAsync("/api/v1/students/import", content);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Downloading_The_Template_Returns_A_Valid_Xlsx()
    {
        var directeur = await DirecteurTokenAsync();
        await CreateClassroomAsync(directeur, "CM2 A"); // La classe doit apparaître dans le modèle.

        var response = await SendAsync(HttpMethod.Get, "/api/v1/students/import/template", directeur);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");

        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Should().NotBeEmpty();
        // Un .xlsx est une archive ZIP : signature "PK" en tête, suffisant pour prouver un fichier non
        // corrompu sans dépendre de ClosedXML côté test pour le relire.
        bytes[0].Should().Be((byte)'P');
        bytes[1].Should().Be((byte)'K');
    }

    [Fact]
    public async Task A_Finance_Must_Not_Download_The_Import_Template()
    {
        var directeur = await DirecteurTokenAsync();
        var finance = await FinanceTokenAsync();

        var response = await SendAsync(HttpMethod.Get, "/api/v1/students/import/template", finance);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
