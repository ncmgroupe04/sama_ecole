using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace SamaEcole.FunctionalTests.Teachers;

/// <summary>
/// POST /api/v1/teachers/import (et son modèle GET .../import/template) contre un vrai PostgreSQL, à
/// travers la vraie pile HTTP — même contrat que StudentImportEndpointsTests : RIEN n'est jamais écrit
/// tant que le fichier n'est pas intégralement valide (dryRun=true = aperçu pur ; dryRun=false =
/// confirmation, tout ou rien).
/// </summary>
public class TeacherImportEndpointsTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public TeacherImportEndpointsTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public Task InitializeAsync() => _factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private record Tokens(string AccessToken, int ExpiresIn);
    private record SubjectDto(Guid Id, string Name, string Level, decimal Coefficient, uint RowVersion);
    private record ErrorResponseDto(string Code, string Message, Dictionary<string, string[]>? Details, string TraceId);
    private record ImportRowResultDto(int RowNumber, bool IsValid, string FullName, string Email, string Phone,
        string BirthDate, string BirthPlace, string Subjects, Dictionary<string, string> FieldErrors);
    private record ImportResultDto(bool DryRun, bool Committed, int TotalRows, int ValidRows, int InvalidRows,
        int Created, List<ImportRowResultDto> Rows);
    private record TeacherListItemDto(Guid Id, string Matricule, string FullName, string Email, string? Phone,
        string? PhotoUrl, string? PhotoDisplayUrl, string Status, List<string> Subjects);
    private record PaginatedTeachersDto(List<TeacherListItemDto> Items, int TotalCount, int Page, int PageSize);

    private const string Header = "Nom complet;Email;Téléphone;Date de naissance;Lieu de naissance;Matières";

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

    private async Task<SubjectDto> CreateSubjectAsync(string token, string name, string level)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/v1/subjects", token,
            new { name, level, coefficient = 1 });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<SubjectDto>())!;
    }

    private async Task<HttpResponseMessage> ImportAsync(
        string token, string csvContent, bool dryRun, string fileName = "enseignants.csv")
    {
        using var content = new MultipartFormDataContent
        {
            { new StringContent(dryRun.ToString()), "dryRun" }
        };
        var fileBytes = new ByteArrayContent(Encoding.UTF8.GetBytes(csvContent));
        fileBytes.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(fileBytes, "file", fileName);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/teachers/import") { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(request);
    }

    private async Task<int> TeacherCountAsync(string token)
    {
        var response = await SendAsync(HttpMethod.Get, "/api/v1/teachers?page=1&pageSize=1", token);
        var page = (await response.Content.ReadFromJsonAsync<PaginatedTeachersDto>())!;
        return page.TotalCount;
    }

    [Fact]
    public async Task A_Dry_Run_On_A_Well_Formed_File_Reports_Every_Row_Valid_And_Writes_Nothing()
    {
        var directeur = await DirecteurTokenAsync();
        var maths = await CreateSubjectAsync(directeur, "Mathématiques", "Primaire");
        var before = await TeacherCountAsync(directeur);

        var csv = $"{Header}\n" +
                  $"Moussa Fall;moussa.fall@example.com;771234567;15/05/1985;Dakar;{maths.Name} ({maths.Level})\n" +
                  $"Awa Ndiaye;awa.ndiaye@example.com;781234567;01/01/1980;Thiès;{maths.Name} ({maths.Level})";

        var response = await ImportAsync(directeur, csv, dryRun: true);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<ImportResultDto>())!;
        result.DryRun.Should().BeTrue();
        result.Committed.Should().BeFalse();
        result.TotalRows.Should().Be(2);
        result.ValidRows.Should().Be(2);
        result.InvalidRows.Should().Be(0);
        result.Created.Should().Be(0);

        (await TeacherCountAsync(directeur)).Should().Be(before, "un aperçu (dryRun) ne doit jamais écrire en base");
    }

    [Fact]
    public async Task Confirming_A_Fully_Valid_File_Creates_Every_Teacher_With_Their_Subjects()
    {
        var directeur = await DirecteurTokenAsync();
        var maths = await CreateSubjectAsync(directeur, "Mathématiques", "Primaire");
        var anglais = await CreateSubjectAsync(directeur, "Anglais", "Primaire");

        var csv = $"{Header}\n" +
                  $"Moussa Fall;moussa.fall@example.com;771234567;15/05/1985;Dakar;{maths.Name} ({maths.Level}), {anglais.Name} ({anglais.Level})";

        var response = await ImportAsync(directeur, csv, dryRun: false);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<ImportResultDto>())!;
        result.Committed.Should().BeTrue();
        result.Created.Should().Be(1);

        var listResponse = await SendAsync(HttpMethod.Get, "/api/v1/teachers?page=1&pageSize=50", directeur);
        var page = (await listResponse.Content.ReadFromJsonAsync<PaginatedTeachersDto>())!;
        var created = page.Items.Should().ContainSingle(t => t.FullName == "Moussa Fall").Subject;
        created.Subjects.Should().Contain(["Mathématiques", "Anglais"]);
        created.Matricule.Should().NotBeNullOrEmpty("le matricule doit être généré à la confirmation");
    }

    [Fact]
    public async Task An_Invalid_Birth_Date_Is_Reported_On_The_Correct_Field_Without_Rejecting_Other_Rows()
    {
        var directeur = await DirecteurTokenAsync();
        var maths = await CreateSubjectAsync(directeur, "Mathématiques", "Primaire");

        var csv = $"{Header}\n" +
                  $"Moussa Fall;moussa.fall@example.com;771234567;pas-une-date;Dakar;{maths.Name}\n" +
                  $"Awa Ndiaye;awa.ndiaye@example.com;781234567;01/01/1980;Thiès;{maths.Name}";

        var response = await ImportAsync(directeur, csv, dryRun: true);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<ImportResultDto>())!;
        result.InvalidRows.Should().Be(1);
        result.ValidRows.Should().Be(1, "une ligne en erreur ne doit jamais empêcher l'aperçu des autres");

        var badRow = result.Rows.Single(r => r.FullName == "Moussa Fall");
        badRow.IsValid.Should().BeFalse();
        badRow.FieldErrors.Should().ContainKey("birthDate");
        badRow.FieldErrors.Should().NotContainKey("email", "seul le champ fautif doit être signalé");

        var goodRow = result.Rows.Single(r => r.FullName == "Awa Ndiaye");
        goodRow.IsValid.Should().BeTrue();
        goodRow.FieldErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task An_Invalid_Email_Is_Rejected()
    {
        var directeur = await DirecteurTokenAsync();
        var maths = await CreateSubjectAsync(directeur, "Mathématiques", "Primaire");

        var csv = $"{Header}\nMoussa Fall;pas-un-email;771234567;15/05/1985;Dakar;{maths.Name}";

        var response = await ImportAsync(directeur, csv, dryRun: true);

        var result = (await response.Content.ReadFromJsonAsync<ImportResultDto>())!;
        result.Rows.Single().FieldErrors.Should().ContainKey("email");
    }

    [Fact]
    public async Task An_Invalid_Phone_Is_Rejected()
    {
        var directeur = await DirecteurTokenAsync();
        var maths = await CreateSubjectAsync(directeur, "Mathématiques", "Primaire");

        var csv = $"{Header}\nMoussa Fall;moussa.fall@example.com;123;15/05/1985;Dakar;{maths.Name}";

        var response = await ImportAsync(directeur, csv, dryRun: true);

        var result = (await response.Content.ReadFromJsonAsync<ImportResultDto>())!;
        result.Rows.Single().FieldErrors.Should().ContainKey("phone");
    }

    [Fact]
    public async Task An_Unknown_Subject_Name_Is_Reported_With_A_Helpful_Message()
    {
        var directeur = await DirecteurTokenAsync();
        await CreateSubjectAsync(directeur, "Mathématiques", "Primaire"); // Existe, mais pas celle visée ci-dessous.

        var csv = $"{Header}\nMoussa Fall;moussa.fall@example.com;771234567;15/05/1985;Dakar;Matière Inexistante";

        var response = await ImportAsync(directeur, csv, dryRun: true);

        var result = (await response.Content.ReadFromJsonAsync<ImportResultDto>())!;
        var row = result.Rows.Single();
        row.IsValid.Should().BeFalse();
        row.FieldErrors.Should().ContainKey("subjects");
        row.FieldErrors["subjects"].Should().Contain("Matière Inexistante");
    }

    [Fact]
    public async Task A_Subject_Name_Shared_By_Two_Levels_Requires_Disambiguation()
    {
        // Domain.Entities.Subject : la clé est (Niveau, Nom) — un même nom peut exister à deux niveaux.
        // Sans précision du niveau, la ligne doit être rejetée avec un message explicite, jamais un choix
        // arbitraire de l'un des deux.
        var directeur = await DirecteurTokenAsync();
        await CreateSubjectAsync(directeur, "Mathématiques", "Primaire");
        await CreateSubjectAsync(directeur, "Mathématiques", "Collège");

        var csv = $"{Header}\nMoussa Fall;moussa.fall@example.com;771234567;15/05/1985;Dakar;Mathématiques";

        var response = await ImportAsync(directeur, csv, dryRun: true);

        var result = (await response.Content.ReadFromJsonAsync<ImportResultDto>())!;
        var row = result.Rows.Single();
        row.IsValid.Should().BeFalse();
        row.FieldErrors.Should().ContainKey("subjects");
    }

    [Fact]
    public async Task An_Email_Already_Used_By_An_Existing_Teacher_Is_Rejected_As_A_Duplicate()
    {
        var directeur = await DirecteurTokenAsync();
        var maths = await CreateSubjectAsync(directeur, "Mathématiques", "Primaire");

        var firstCsv = $"{Header}\nMoussa Fall;moussa.fall@example.com;771234567;15/05/1985;Dakar;{maths.Name}";
        (await ImportAsync(directeur, firstCsv, dryRun: false)).StatusCode.Should().Be(HttpStatusCode.OK);

        var duplicateCsv = $"{Header}\nMoussa Faux;moussa.fall@example.com;781234567;01/01/1980;Thiès;{maths.Name}";
        var response = await ImportAsync(directeur, duplicateCsv, dryRun: true);

        var result = (await response.Content.ReadFromJsonAsync<ImportResultDto>())!;
        result.Rows.Single().FieldErrors.Should().ContainKey("email");
    }

    [Fact]
    public async Task A_Phone_Repeated_Within_The_Same_File_Is_Rejected_As_A_Duplicate()
    {
        var directeur = await DirecteurTokenAsync();
        var maths = await CreateSubjectAsync(directeur, "Mathématiques", "Primaire");

        var csv = $"{Header}\n" +
                  $"Moussa Fall;moussa.fall@example.com;771234567;15/05/1985;Dakar;{maths.Name}\n" +
                  $"Awa Ndiaye;awa.ndiaye@example.com;771234567;01/01/1980;Thiès;{maths.Name}";

        var response = await ImportAsync(directeur, csv, dryRun: true);

        var result = (await response.Content.ReadFromJsonAsync<ImportResultDto>())!;
        result.InvalidRows.Should().Be(1);
        result.Rows.Single(r => r.FullName == "Awa Ndiaye").FieldErrors.Should().ContainKey("phone");
    }

    [Fact]
    public async Task Confirming_A_File_With_An_Invalid_Row_Rejects_The_Whole_Batch_And_Writes_Nothing()
    {
        var directeur = await DirecteurTokenAsync();
        var maths = await CreateSubjectAsync(directeur, "Mathématiques", "Primaire");
        var before = await TeacherCountAsync(directeur);

        var csv = $"{Header}\n" +
                  $"Moussa Fall;moussa.fall@example.com;771234567;15/05/1985;Dakar;{maths.Name}\n" +
                  $"Awa Ndiaye;awa.ndiaye@example.com;781234567;01/01/1980;Thiès;Matière Inconnue";

        var response = await ImportAsync(directeur, csv, dryRun: false);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var error = (await response.Content.ReadFromJsonAsync<ErrorResponseDto>())!;
        error.Details.Should().ContainKey("Ligne 3");

        (await TeacherCountAsync(directeur)).Should().Be(before, "aucune ligne ne doit être écrite si le fichier contient la moindre erreur");
    }

    [Fact]
    public async Task A_Dry_Run_Never_Consumes_A_Matricule_Sequence_Number()
    {
        // Ne suppose PAS que le compteur parte de zéro (d'autres tests de cette classe créent déjà de
        // vrais enseignants avant celui-ci, sans que la table matricule_sequences ne soit réinitialisée
        // entre chaque test — voir AuthApiFactory.ResetTestUsersAsync) : on encadre les deux aperçus par
        // deux créations RÉELLES et on vérifie que le numéro n'a avancé que d'UN cran, pas de trois.
        var directeur = await DirecteurTokenAsync();
        var maths = await CreateSubjectAsync(directeur, "Mathématiques", "Primaire");

        var beforeResponse = await SendAsync(HttpMethod.Post, "/api/v1/teachers", directeur, new
        {
            fullName = "Enseignant Avant", email = "avant@example.com", birthDate = "1980-01-01",
            subjectIds = new[] { maths.Id }
        });
        beforeResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var before = await beforeResponse.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        var beforeSequence = ExtractMatriculeSequence(before!["matricule"].ToString()!);

        var csv = $"{Header}\nMoussa Fall;moussa.fall@example.com;771234567;15/05/1985;Dakar;{maths.Name}";
        await ImportAsync(directeur, csv, dryRun: true);
        await ImportAsync(directeur, csv, dryRun: true); // Un second aperçu, pour bonne mesure.

        var afterResponse = await SendAsync(HttpMethod.Post, "/api/v1/teachers", directeur, new
        {
            fullName = "Enseignant Après", email = "apres@example.com", birthDate = "1980-01-01",
            subjectIds = new[] { maths.Id }
        });
        afterResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var after = await afterResponse.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        var afterSequence = ExtractMatriculeSequence(after!["matricule"].ToString()!);

        afterSequence.Should().Be(beforeSequence + 1, "aucun aperçu ne doit avoir consommé de numéro entre les deux créations réelles");
    }

    private static int ExtractMatriculeSequence(string matricule) =>
        int.Parse(matricule.Split('-').Last());

    [Fact]
    public async Task A_Finance_Must_Not_Import_Teachers()
    {
        var directeur = await DirecteurTokenAsync();
        var maths = await CreateSubjectAsync(directeur, "Mathématiques", "Primaire");
        var finance = await FinanceTokenAsync();

        var csv = $"{Header}\nMoussa Fall;moussa.fall@example.com;771234567;15/05/1985;Dakar;{maths.Name}";
        var response = await ImportAsync(finance, csv, dryRun: true);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_Unsupported_File_Extension_Should_Be_Rejected()
    {
        var directeur = await DirecteurTokenAsync();
        var maths = await CreateSubjectAsync(directeur, "Mathématiques", "Primaire");

        var csv = $"{Header}\nMoussa Fall;moussa.fall@example.com;771234567;15/05/1985;Dakar;{maths.Name}";
        var response = await ImportAsync(directeur, csv, dryRun: true, fileName: "enseignants.docx");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_File_Beyond_The_Row_Limit_Should_Be_Rejected_Before_Any_Row_Level_Validation()
    {
        var directeur = await DirecteurTokenAsync();
        var maths = await CreateSubjectAsync(directeur, "Mathématiques", "Primaire");

        var sb = new StringBuilder(Header);
        for (var i = 0; i < 1001; i++)
        {
            sb.Append($"\nEnseignant {i};enseignant{i}@example.com;771234567;15/05/1985;Dakar;{maths.Name}");
        }

        var response = await ImportAsync(directeur, sb.ToString(), dryRun: true);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Importing_Without_A_Token_Should_Return_401()
    {
        var csv = $"{Header}\nMoussa Fall;moussa.fall@example.com;771234567;15/05/1985;Dakar;Maths";

        using var content = new MultipartFormDataContent { { new StringContent("true"), "dryRun" } };
        var fileBytes = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        content.Add(fileBytes, "file", "enseignants.csv");

        var response = await _client.PostAsync("/api/v1/teachers/import", content);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Downloading_The_Template_Returns_A_Valid_Xlsx()
    {
        var directeur = await DirecteurTokenAsync();
        await CreateSubjectAsync(directeur, "Mathématiques", "Primaire"); // Doit apparaître dans le modèle.

        var response = await SendAsync(HttpMethod.Get, "/api/v1/teachers/import/template", directeur);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");

        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Should().NotBeEmpty();
        bytes[0].Should().Be((byte)'P');
        bytes[1].Should().Be((byte)'K');
    }

    [Fact]
    public async Task A_Finance_Must_Not_Download_The_Import_Template()
    {
        var directeur = await DirecteurTokenAsync();
        var finance = await FinanceTokenAsync();

        var response = await SendAsync(HttpMethod.Get, "/api/v1/teachers/import/template", finance);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
