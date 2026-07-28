using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ClosedXML.Excel;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace SamaEcole.FunctionalTests.Grades;

/// <summary>
/// GET /grades/sheet/export et POST /grades/sheet/import contre un vrai PostgreSQL, à travers la
/// vraie pile HTTP — même permission que POST /grades (Saisir = Directeur ou Enseignant,
/// docs/Volume_7_Security.md « Notes »).
///
/// Le contrat central du ticket : les colonnes sont reconnues par le NOM de leur en-tête, jamais par
/// leur position — un fichier dont les colonnes ont été réordonnées doit produire EXACTEMENT le même
/// résultat qu'un fichier dans l'ordre d'origine (<see cref="Columns_In_A_Different_Order_Must_Still_Map_To_The_Right_Evaluation"/>).
/// Le fichier est validé INTÉGRALEMENT avant la moindre écriture (une seule ligne en erreur → rien
/// n'est enregistré), et un matricule d'un élève d'une AUTRE classe que celle visée par l'import est un
/// motif de rejet à part entière.
/// </summary>
public class GradeImportEndpointsTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public GradeImportEndpointsTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public Task InitializeAsync() => _factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private record Tokens(string AccessToken, int ExpiresIn);
    private record ClassroomDto(Guid Id, string Name, string Level, int Capacity);
    private record SubjectDto(Guid Id, string Name, string Level, decimal Coefficient);
    private record SchoolYearDto(Guid Id, string Label, DateOnly StartDate, DateOnly EndDate, bool IsActive, bool IsClosed);
    private record TermDto(Guid Id, string Label, int Order, DateOnly StartDate, DateOnly EndDate);
    private record StudentDto(Guid Id, string Matricule);
    private record ImportResultDto(int StudentsMatched, int Created, int Updated, int Unchanged);
    private record ErrorResponseDto(string Code, string Message, Dictionary<string, string[]>? Details, string TraceId);
    private record GradeCellDto(Guid Id, decimal Value, uint RowVersion);
    private record StudentGradeRowDto(
        Guid StudentId, string Matricule, string FullName, GradeCellDto? Devoir1, GradeCellDto? Devoir2, GradeCellDto? Composition);

    private async Task<string> AccessTokenAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!.AccessToken;
    }

    private Task<string> DirecteurTokenAsync() =>
        AccessTokenAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);

    private Task<string> EnseignantTokenAsync() =>
        AccessTokenAsync(AuthApiFactory.EnseignantEmail, AuthApiFactory.EnseignantPassword);

    private Task<string> FinanceTokenAsync() =>
        AccessTokenAsync(AuthApiFactory.FinanceEmail, AuthApiFactory.FinancePassword);

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await _client.SendAsync(request);
    }

    /// <summary>Construit un classeur en mémoire — une ligne d'en-tête, puis une ligne par tableau de <paramref name="rows"/>.</summary>
    private static byte[] BuildXlsx(string[] headers, IEnumerable<string?[]> rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Notes");

        for (var col = 0; col < headers.Length; col++)
        {
            sheet.Cell(1, col + 1).Value = headers[col];
        }

        var rowNumber = 2;
        foreach (var row in rows)
        {
            for (var col = 0; col < row.Length; col++)
            {
                if (row[col] is { } value) sheet.Cell(rowNumber, col + 1).Value = value;
            }
            rowNumber++;
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private async Task<HttpResponseMessage> ImportSheetAsync(
        string token, Guid classroomId, Guid subjectId, Guid termId, bool dryRun,
        byte[] fileContent, string fileName = "notes.xlsx")
    {
        using var content = new MultipartFormDataContent
        {
            { new StringContent(classroomId.ToString()), "classroomId" },
            { new StringContent(subjectId.ToString()), "subjectId" },
            { new StringContent(termId.ToString()), "termId" },
            { new StringContent(dryRun.ToString()), "dryRun" }
        };
        var fileBytes = new ByteArrayContent(fileContent);
        fileBytes.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(fileBytes, "file", fileName);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/grades/sheet/import") { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(request);
    }

    /// <summary>Une classe de deux élèves, une matière, l'année active (et son 1er trimestre).</summary>
    private async Task<(Guid ClassroomId, Guid SubjectId, Guid TermId, StudentDto Student1, StudentDto Student2)>
        SeedGradingContextAsync(string directeurToken)
    {
        var classroomResponse = await SendAsync(HttpMethod.Post, "/api/v1/classrooms", directeurToken,
            // Classe du SECONDAIRE : les fichiers importés portent des notes sur /20 (voir GradesEndpointsTests).
            new { name = "3e A", level = "Collège", capacity = 40 });
        var classroom = (await classroomResponse.Content.ReadFromJsonAsync<ClassroomDto>())!;

        var subjectResponse = await SendAsync(HttpMethod.Post, "/api/v1/subjects", directeurToken,
            new { name = "Mathématiques", level = "Primaire", coefficient = 4 });
        var subject = (await subjectResponse.Content.ReadFromJsonAsync<SubjectDto>())!;

        var student1Response = await SendAsync(HttpMethod.Post, "/api/v1/students", directeurToken,
            new { fullName = "Premier Élève", birthDate = "2015-01-01", birthPlace = "Dakar", gender = "M", classroomId = classroom.Id });
        var student1 = (await student1Response.Content.ReadFromJsonAsync<StudentDto>())!;

        var student2Response = await SendAsync(HttpMethod.Post, "/api/v1/students", directeurToken,
            new { fullName = "Second Élève", birthDate = "2015-02-01", birthPlace = "Dakar", gender = "F", classroomId = classroom.Id });
        var student2 = (await student2Response.Content.ReadFromJsonAsync<StudentDto>())!;

        var yearResponse = await SendAsync(HttpMethod.Post, "/api/v1/school-years", directeurToken,
            new { label = "2026-2027", startDate = "2026-10-01", endDate = "2027-06-30" });
        var year = (await yearResponse.Content.ReadFromJsonAsync<SchoolYearDto>())!;

        var termsResponse = await SendAsync(HttpMethod.Get, $"/api/v1/school-years/{year.Id}/terms", directeurToken);
        var terms = (await termsResponse.Content.ReadFromJsonAsync<List<TermDto>>())!;

        return (classroom.Id, subject.Id, terms[0].Id, student1, student2);
    }

    private async Task<List<StudentGradeRowDto>> GetGradesAsync(string token, Guid classroomId, Guid subjectId, Guid termId)
    {
        var response = await SendAsync(HttpMethod.Get,
            $"/api/v1/grades?classroomId={classroomId}&subjectId={subjectId}&termId={termId}", token);
        return (await response.Content.ReadFromJsonAsync<List<StudentGradeRowDto>>())!;
    }

    [Fact]
    public async Task A_Well_Formed_Sheet_Should_Create_Grades_For_Every_Evaluation_And_Every_Student()
    {
        var directeur = await DirecteurTokenAsync();
        var (classroomId, subjectId, termId, student1, student2) = await SeedGradingContextAsync(directeur);
        var enseignant = await EnseignantTokenAsync();

        var headers = new[] { "Matricule", "Nom & Prénom", "Devoir 1", "Devoir 2", "Composition" };
        var file = BuildXlsx(headers,
        [
            [student1.Matricule, "Premier Élève", "15", "14", null],
            [student2.Matricule, "Second Élève", null, null, "12,5"]
        ]);

        var response = await ImportSheetAsync(enseignant, classroomId, subjectId, termId, dryRun: false, file);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<ImportResultDto>())!;
        result.StudentsMatched.Should().Be(2);
        result.Created.Should().Be(3); // devoir1 + devoir2 (élève 1) + composition (élève 2)
        result.Updated.Should().Be(0);

        var rows = await GetGradesAsync(directeur, classroomId, subjectId, termId);
        rows.Should().ContainSingle(r => r.StudentId == student1.Id && r.Devoir1!.Value == 15 && r.Devoir2!.Value == 14 && r.Composition == null);
        rows.Should().ContainSingle(r => r.StudentId == student2.Id && r.Composition!.Value == 12.5m && r.Devoir1 == null,
            "la notation FR à virgule (\"12,5\") doit être comprise comme une note décimale");
    }

    [Fact]
    public async Task Columns_In_A_Different_Order_Must_Still_Map_To_The_Right_Evaluation()
    {
        // Le cœur du ticket : l'ORDRE des colonnes ne doit avoir aucune importance, seul le NOM de
        // l'en-tête gouverne le mappage — sinon une simple réorganisation du fichier inverserait des notes.
        var directeur = await DirecteurTokenAsync();
        var (classroomId, subjectId, termId, student1, student2) = await SeedGradingContextAsync(directeur);
        var enseignant = await EnseignantTokenAsync();

        // Composition avant Devoir 2 avant Devoir 1 avant Matricule — ordre volontairement inversé, et
        // les lignes des deux élèves volontairement permutées par rapport à l'ordre "naturel".
        var headers = new[] { "Composition", "Devoir 2", "Devoir 1", "Matricule" };
        var file = BuildXlsx(headers,
        [
            ["8", "18", "17", student2.Matricule],
            ["6", "12", "11", student1.Matricule]
        ]);

        var response = await ImportSheetAsync(enseignant, classroomId, subjectId, termId, dryRun: false, file);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var rows = await GetGradesAsync(directeur, classroomId, subjectId, termId);
        rows.Should().ContainSingle(r =>
            r.StudentId == student1.Id && r.Devoir1!.Value == 11 && r.Devoir2!.Value == 12 && r.Composition!.Value == 6);
        rows.Should().ContainSingle(r =>
            r.StudentId == student2.Id && r.Devoir1!.Value == 17 && r.Devoir2!.Value == 18 && r.Composition!.Value == 8);
    }

    [Fact]
    public async Task A_Dry_Run_Should_Report_The_Result_Without_Writing_Anything()
    {
        var directeur = await DirecteurTokenAsync();
        var (classroomId, subjectId, termId, student1, _) = await SeedGradingContextAsync(directeur);
        var enseignant = await EnseignantTokenAsync();

        var file = BuildXlsx(["Matricule", "Devoir 1"], [[student1.Matricule, "15"]]);

        var response = await ImportSheetAsync(enseignant, classroomId, subjectId, termId, dryRun: true, file);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<ImportResultDto>())!;
        result.StudentsMatched.Should().Be(1);
        result.Created.Should().Be(1);

        var rows = await GetGradesAsync(directeur, classroomId, subjectId, termId);
        rows.Should().ContainSingle(r => r.StudentId == student1.Id && r.Devoir1 == null,
            "l'aperçu ne doit rien écrire, même quand tout le fichier est valide");
    }

    [Fact]
    public async Task ReImporting_With_A_Different_Value_Should_Update_Instead_Of_Duplicating()
    {
        var directeur = await DirecteurTokenAsync();
        var (classroomId, subjectId, termId, student1, student2) = await SeedGradingContextAsync(directeur);
        var enseignant = await EnseignantTokenAsync();

        var headers = new[] { "Matricule", "Devoir 1" };
        await ImportSheetAsync(enseignant, classroomId, subjectId, termId, dryRun: false,
            BuildXlsx(headers, [[student1.Matricule, "10"], [student2.Matricule, "10"]]));

        var second = await ImportSheetAsync(enseignant, classroomId, subjectId, termId, dryRun: false,
            BuildXlsx(headers, [[student1.Matricule, "14"], [student2.Matricule, "10"]]));

        second.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await second.Content.ReadFromJsonAsync<ImportResultDto>())!;
        result.Created.Should().Be(0);
        result.Updated.Should().Be(1);
        result.Unchanged.Should().Be(1);
    }

    [Fact]
    public async Task A_Directeur_Can_Import_Grades()
    {
        var directeur = await DirecteurTokenAsync();
        var (classroomId, subjectId, termId, student1, _) = await SeedGradingContextAsync(directeur);

        var response = await ImportSheetAsync(directeur, classroomId, subjectId, termId, dryRun: false,
            BuildXlsx(["Matricule", "Devoir 1"], [[student1.Matricule, "15"]]));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<ImportResultDto>())!;
        result.Created.Should().Be(1);
    }

    [Fact]
    public async Task An_Unknown_Matricule_Should_Return_422_With_The_Faulty_Line_Identified()
    {
        var directeur = await DirecteurTokenAsync();
        var (classroomId, subjectId, termId, _, _) = await SeedGradingContextAsync(directeur);
        var enseignant = await EnseignantTokenAsync();

        var response = await ImportSheetAsync(enseignant, classroomId, subjectId, termId, dryRun: false,
            BuildXlsx(["Matricule", "Devoir 1"], [["ELEV-INCONNU-9999", "15"]]));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var error = (await response.Content.ReadFromJsonAsync<ErrorResponseDto>())!;
        error.Details.Should().ContainKey("Ligne 2", "la ligne 1 est l'en-tête, les données commencent à la ligne 2");
    }

    [Fact]
    public async Task A_Matricule_Belonging_To_Another_Classroom_Must_Be_Rejected()
    {
        // Le cœur du ticket : une note ne doit jamais atterrir sur l'élève d'une autre classe parce
        // qu'un fichier a été importé sur le mauvais écran, ou contient une ligne erronée.
        var directeur = await DirecteurTokenAsync();
        var (classroomId, subjectId, termId, student1, _) = await SeedGradingContextAsync(directeur);

        // Une SECONDE classe, avec son propre élève — hors du périmètre de l'import ci-dessous.
        var otherClassroomResponse = await SendAsync(HttpMethod.Post, "/api/v1/classrooms", directeur,
            new { name = "3e B", level = "Collège", capacity = 40 });
        var otherClassroom = (await otherClassroomResponse.Content.ReadFromJsonAsync<ClassroomDto>())!;
        var otherStudentResponse = await SendAsync(HttpMethod.Post, "/api/v1/students", directeur,
            new { fullName = "Élève d'une autre classe", birthDate = "2015-03-01", birthPlace = "Dakar", gender = "M", classroomId = otherClassroom.Id });
        var otherStudent = (await otherStudentResponse.Content.ReadFromJsonAsync<StudentDto>())!;

        var enseignant = await EnseignantTokenAsync();

        // Import sur la PREMIÈRE classe, avec le matricule d'un élève de la SECONDE.
        var response = await ImportSheetAsync(enseignant, classroomId, subjectId, termId, dryRun: false,
            BuildXlsx(["Matricule", "Devoir 1"], [[otherStudent.Matricule, "15"]]));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        // Rien n'a été écrit sur la classe visée : ni sur l'élève étranger (rejeté), ni sur le vrai
        // élève de la classe (jamais mentionné dans ce fichier, donc logiquement toujours vierge).
        var rows = await GetGradesAsync(directeur, classroomId, subjectId, termId);
        rows.Should().ContainSingle(r => r.StudentId == student1.Id && r.Devoir1 == null);
    }

    [Fact]
    public async Task A_Duplicate_Matricule_Within_The_File_Should_Be_Rejected()
    {
        var directeur = await DirecteurTokenAsync();
        var (classroomId, subjectId, termId, student1, _) = await SeedGradingContextAsync(directeur);
        var enseignant = await EnseignantTokenAsync();

        var response = await ImportSheetAsync(enseignant, classroomId, subjectId, termId, dryRun: false,
            BuildXlsx(["Matricule", "Devoir 1"], [[student1.Matricule, "15"], [student1.Matricule, "10"]]));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_Grade_Above_The_Grading_Scale_Should_Be_Rejected()
    {
        var directeur = await DirecteurTokenAsync();
        var (classroomId, subjectId, termId, student1, _) = await SeedGradingContextAsync(directeur);
        var enseignant = await EnseignantTokenAsync();

        var response = await ImportSheetAsync(enseignant, classroomId, subjectId, termId, dryRun: false,
            BuildXlsx(["Matricule", "Devoir 1"], [[student1.Matricule, "25"]]));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task One_Invalid_Line_Must_Prevent_The_Whole_File_From_Being_Applied()
    {
        // La ligne 2 est valide, la ligne 3 porte un matricule inconnu : AUCUNE des deux ne doit être
        // enregistrée — validation intégrale avant écriture, jamais un import partiel.
        var directeur = await DirecteurTokenAsync();
        var (classroomId, subjectId, termId, student1, _) = await SeedGradingContextAsync(directeur);
        var enseignant = await EnseignantTokenAsync();

        var response = await ImportSheetAsync(enseignant, classroomId, subjectId, termId, dryRun: false,
            BuildXlsx(["Matricule", "Devoir 1"], [[student1.Matricule, "15"], ["ELEV-INCONNU-9999", "10"]]));
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var rows = await GetGradesAsync(directeur, classroomId, subjectId, termId);
        rows.Should().ContainSingle(r => r.StudentId == student1.Id && r.Devoir1 == null,
            "la ligne valide ne doit pas être enregistrée tant que le fichier contient une autre ligne en erreur");
    }

    [Fact]
    public async Task A_File_Without_A_Matricule_Header_Should_Be_Rejected()
    {
        // Le format LARGE exige la ligne d'en-tête : sans elle, aucun moyen fiable de savoir quelle
        // colonne est le matricule et lesquelles sont les notes — jamais une hypothèse de position.
        var directeur = await DirecteurTokenAsync();
        var (classroomId, subjectId, termId, student1, _) = await SeedGradingContextAsync(directeur);
        var enseignant = await EnseignantTokenAsync();

        var response = await ImportSheetAsync(enseignant, classroomId, subjectId, termId, dryRun: false,
            BuildXlsx(["Colonne A", "Colonne B"], [[student1.Matricule, "15"]]));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task An_Unsupported_File_Extension_Should_Be_Rejected()
    {
        var directeur = await DirecteurTokenAsync();
        var (classroomId, subjectId, termId, student1, _) = await SeedGradingContextAsync(directeur);
        var enseignant = await EnseignantTokenAsync();

        var response = await ImportSheetAsync(enseignant, classroomId, subjectId, termId, dryRun: false,
            BuildXlsx(["Matricule", "Devoir 1"], [[student1.Matricule, "15"]]), fileName: "notes.docx");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Importing_Without_A_Token_Should_Return_401()
    {
        var directeur = await DirecteurTokenAsync();
        var (classroomId, subjectId, termId, student1, _) = await SeedGradingContextAsync(directeur);

        using var content = new MultipartFormDataContent
        {
            { new StringContent(classroomId.ToString()), "classroomId" },
            { new StringContent(subjectId.ToString()), "subjectId" },
            { new StringContent(termId.ToString()), "termId" },
            { new StringContent("false"), "dryRun" }
        };
        var fileBytes = new ByteArrayContent(BuildXlsx(["Matricule", "Devoir 1"], [[student1.Matricule, "15"]]));
        content.Add(fileBytes, "file", "notes.xlsx");

        var response = await _client.PostAsync("/api/v1/grades/sheet/import", content);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Exporting_Returns_A_Valid_Xlsx_Prefilled_With_Existing_Grades()
    {
        var directeur = await DirecteurTokenAsync();
        var (classroomId, subjectId, termId, student1, _) = await SeedGradingContextAsync(directeur);
        var enseignant = await EnseignantTokenAsync();

        await ImportSheetAsync(enseignant, classroomId, subjectId, termId, dryRun: false,
            BuildXlsx(["Matricule", "Devoir 1"], [[student1.Matricule, "15"]]));

        var response = await SendAsync(HttpMethod.Get,
            $"/api/v1/grades/sheet/export?classroomId={classroomId}&subjectId={subjectId}&termId={termId}", directeur);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");

        var bytes = await response.Content.ReadAsByteArrayAsync();
        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var sheet = workbook.Worksheets.First();
        var cellsText = sheet.CellsUsed().Select(c => c.GetString());
        cellsText.Should().Contain(student1.Matricule);
    }

    [Fact]
    public async Task A_Finance_Must_Not_Export_The_Grade_Sheet()
    {
        var directeur = await DirecteurTokenAsync();
        var (classroomId, subjectId, termId, _, _) = await SeedGradingContextAsync(directeur);
        var finance = await FinanceTokenAsync();

        var response = await SendAsync(HttpMethod.Get,
            $"/api/v1/grades/sheet/export?classroomId={classroomId}&subjectId={subjectId}&termId={termId}", finance);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
