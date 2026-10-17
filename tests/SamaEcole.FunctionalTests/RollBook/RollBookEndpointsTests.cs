using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace SamaEcole.FunctionalTests.RollBook;

/// <summary>
/// Cahier de roulement (Franco-Arabe Bloc 5b) — /roll-book à travers la vraie pile HTTP : grille mensuelle, saisie
/// en lot (statut nul = retour au calcul), statistiques, permissions par rôle et portée de l'Enseignant.
/// Les dates sont définies RELATIVEMENT à aujourd'hui pour ne pas vieillir.
/// </summary>
public class RollBookEndpointsTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private const string Url = "/api/v1/roll-book";

    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public RollBookEndpointsTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public Task InitializeAsync() => _factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private record Tokens(string AccessToken, int ExpiresIn);
    private record IdResult(Guid Id);
    private record CountsDto(int Present, int Late, int PartialAbsence, int JustifiedAbsence, int UnjustifiedAbsence, int Recorded, int LateMinutes);
    private record CellDto(DateOnly Date, string HalfDay, string Mark, string Source, int LateMinutes, string? Reason, Guid? EntryId);
    private record StudentDto(Guid StudentId, string FullName, List<CellDto> Cells, CountsDto Counts);
    private record DayDto(DateOnly Date, bool IsClassDay, string? OffReason);
    private record GridDto(Guid ClassroomId, string ClassroomName, string Month, List<DayDto> Days, int ClassDays, List<StudentDto> Students, CountsDto Totals);
    private record StatsStudentDto(Guid StudentId, string FullName, CountsDto Counts);
    private record StatsDto(string Scope, List<StatsStudentDto> Students);
    private record UpsertResult(int Created, int Updated, int Reverted);

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);
    private static readonly string ThisMonth = Today.ToString("yyyy-MM");

    /// <summary>Le jour ouvré (lundi→samedi) le plus récent, aujourd'hui compris : jamais dans le futur, jamais un dimanche.</summary>
    private static readonly DateOnly WorkingDay = LastWorkingDay(Today);

    private static DateOnly LastWorkingDay(DateOnly day)
    {
        while (day.DayOfWeek == DayOfWeek.Sunday) day = day.AddDays(-1);
        return day;
    }

    private async Task<string> TokenAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!.AccessToken;
    }

    private Task<string> DirecteurAsync() => TokenAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await _client.SendAsync(request);
    }

    private async Task<(Guid Classroom, Guid Subject, Guid S1, Guid S2)> SeedAsync(string directeur)
    {
        // Année en cours, définie relativement à aujourd'hui (la grille du mois courant doit tomber dedans).
        (await SendAsync(HttpMethod.Post, "/api/v1/school-years", directeur, new
        {
            label = $"{Today.Year}-{Today.Year + 1}-R",
            startDate = Today.AddDays(-150).ToString("yyyy-MM-dd"),
            endDate = Today.AddDays(150).ToString("yyyy-MM-dd")
        })).StatusCode.Should().Be(HttpStatusCode.Created);

        var subject = await CreateAsync(directeur, "/api/v1/subjects", new { name = "Mathématiques", level = "Primaire", coefficient = 4 });
        var classroom = await CreateAsync(directeur, "/api/v1/classrooms", new { name = "CM2 A", level = "Primaire", capacity = 30 });
        var s1 = await CreateAsync(directeur, "/api/v1/students",
            new { fullName = "Awa Fall", birthDate = "2015-03-12", birthPlace = "Dakar", gender = "F", classroomId = classroom });
        var s2 = await CreateAsync(directeur, "/api/v1/students",
            new { fullName = "Modou Diop", birthDate = "2015-03-12", birthPlace = "Dakar", gender = "M", classroomId = classroom });
        return (classroom, subject, s1, s2);
    }

    private async Task<Guid> CreateAsync(string token, string url, object body)
    {
        var response = await SendAsync(HttpMethod.Post, url, token, body);
        response.StatusCode.Should().Be(HttpStatusCode.Created, url);
        return (await response.Content.ReadFromJsonAsync<IdResult>())!.Id;
    }

    private static object Cell(Guid student, string half, string? status, int minutes = 0, string? reason = null, DateOnly? date = null) =>
        new { studentId = student, date = (date ?? WorkingDay).ToString("yyyy-MM-dd"), halfDay = half, status, lateMinutes = minutes, reason };

    private async Task<GridDto> GridAsync(string token, Guid classroom, string? month = null)
    {
        var response = await SendAsync(HttpMethod.Get, $"{Url}?classroomId={classroom}&month={month ?? WorkingDay.ToString("yyyy-MM")}", token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<GridDto>())!;
    }

    [Fact]
    public async Task Directeur_Fills_Reads_And_Reverts_The_Roll_Book()
    {
        var directeur = await DirecteurAsync();
        var (classroom, _, s1, s2) = await SeedAsync(directeur);

        // Grille vide : deux élèves, aucune case renseignée, des jours de classe.
        var empty = await GridAsync(directeur, classroom);
        empty.Students.Should().HaveCount(2);
        empty.Students.Should().OnlyContain(s => s.Cells.Count == 0);
        empty.ClassDays.Should().BeGreaterThan(0);
        empty.Days.Count.Should().Be(DateTime.DaysInMonth(WorkingDay.Year, WorkingDay.Month));

        // Saisie en lot : matin et après-midi de deux élèves.
        var saved = await SendAsync(HttpMethod.Put, $"{Url}/entries", directeur, new
        {
            classroomId = classroom,
            entries = new[]
            {
                Cell(s1, "Morning", "Present"),
                Cell(s1, "Afternoon", "Late", 12, "Bus en panne"),
                Cell(s2, "Morning", "UnjustifiedAbsence"),
                Cell(s2, "Afternoon", "JustifiedAbsence", reason: "Certificat médical")
            }
        });
        saved.StatusCode.Should().Be(HttpStatusCode.OK);
        (await saved.Content.ReadFromJsonAsync<UpsertResult>())!.Should().Be(new UpsertResult(4, 0, 0));

        // Relecture depuis la base.
        var grid = await GridAsync(directeur, classroom);
        var awa = grid.Students.Single(s => s.StudentId == s1);
        awa.Cells.Should().Contain(c => c.HalfDay == "Afternoon" && c.Mark == "Late" && c.LateMinutes == 12 && c.Reason == "Bus en panne" && c.Source == "Entry");
        awa.Counts.Recorded.Should().Be(2);

        var modou = grid.Students.Single(s => s.StudentId == s2);
        modou.Counts.UnjustifiedAbsence.Should().Be(1);
        modou.Counts.JustifiedAbsence.Should().Be(1);

        // Retour au calcul d'une case (statut nul) : elle disparaît de la grille.
        var reverted = await SendAsync(HttpMethod.Put, $"{Url}/entries", directeur,
            new { classroomId = classroom, entries = new[] { Cell(s2, "Morning", null) } });
        (await reverted.Content.ReadFromJsonAsync<UpsertResult>())!.Reverted.Should().Be(1);
        (await GridAsync(directeur, classroom)).Students.Single(s => s.StudentId == s2).Cells.Should().ContainSingle();

        // Retour au calcul par identifiant de saisie (DELETE) : 204, puis 404.
        var entryId = (await GridAsync(directeur, classroom)).Students.Single(s => s.StudentId == s2).Cells.Single().EntryId;
        (await SendAsync(HttpMethod.Delete, $"{Url}/entries/{entryId}", directeur)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await SendAsync(HttpMethod.Delete, $"{Url}/entries/{entryId}", directeur)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Stats_Give_Rates_Per_Student_And_Refuse_Bad_Parameters()
    {
        var directeur = await DirecteurAsync();
        var (classroom, _, s1, s2) = await SeedAsync(directeur);
        await SendAsync(HttpMethod.Put, $"{Url}/entries", directeur, new
        {
            classroomId = classroom,
            entries = new[] { Cell(s1, "Morning", "Present"), Cell(s2, "Morning", "UnjustifiedAbsence") }
        });

        var ok = await SendAsync(HttpMethod.Get,
            $"{Url}/stats?scope=class&classroomId={classroom}&month={WorkingDay:yyyy-MM}", directeur);
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        var stats = (await ok.Content.ReadFromJsonAsync<StatsDto>())!;
        stats.Scope.Should().Be("class");
        stats.Students.Single(s => s.StudentId == s1).Counts.Present.Should().Be(1);
        stats.Students.Single(s => s.StudentId == s2).Counts.UnjustifiedAbsence.Should().Be(1);

        (await SendAsync(HttpMethod.Get, $"{Url}/stats?scope=level&month={WorkingDay:yyyy-MM}", directeur))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Paramètres invalides : portée inconnue, aucune période, deux périodes.
        (await SendAsync(HttpMethod.Get, $"{Url}/stats?scope=school&month={ThisMonth}", directeur)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await SendAsync(HttpMethod.Get, $"{Url}/stats?scope=level", directeur)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await SendAsync(HttpMethod.Get, $"{Url}/stats?scope=level&month={ThisMonth}&termId={Guid.NewGuid()}", directeur))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_Future_Date_Or_A_Student_Of_Another_Class_Is_Rejected_And_Nothing_Is_Written()
    {
        var directeur = await DirecteurAsync();
        var (classroom, _, s1, _) = await SeedAsync(directeur);
        var other = await CreateAsync(directeur, "/api/v1/classrooms", new { name = "CM1 B", level = "Primaire", capacity = 30 });
        var stranger = await CreateAsync(directeur, "/api/v1/students",
            new { fullName = "Eli Gaye", birthDate = "2015-03-12", birthPlace = "Dakar", gender = "M", classroomId = other });

        var future = await SendAsync(HttpMethod.Put, $"{Url}/entries", directeur, new
        {
            classroomId = classroom, entries = new[] { Cell(s1, "Morning", "Present", date: Today.AddDays(2)) }
        });
        future.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var wrongClass = await SendAsync(HttpMethod.Put, $"{Url}/entries", directeur, new
        {
            classroomId = classroom, entries = new[] { Cell(s1, "Morning", "Present"), Cell(stranger, "Morning", "Present") }
        });
        wrongClass.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        (await GridAsync(directeur, classroom)).Students.Should().OnlyContain(s => s.Cells.Count == 0, "le lot est atomique");
    }

    [Fact]
    public async Task Late_Minutes_On_A_Non_Late_Status_Are_Rejected()
    {
        var directeur = await DirecteurAsync();
        var (classroom, _, s1, _) = await SeedAsync(directeur);

        var response = await SendAsync(HttpMethod.Put, $"{Url}/entries", directeur,
            new { classroomId = classroom, entries = new[] { Cell(s1, "Morning", "Present", minutes: 15) } });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_Month_Outside_Any_School_Year_Gives_422()
    {
        var directeur = await DirecteurAsync();
        var (classroom, _, _, _) = await SeedAsync(directeur);

        var response = await SendAsync(HttpMethod.Get, $"{Url}?classroomId={classroom}&month=2001-01", directeur);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_Bad_Month_Format_Gives_422()
    {
        var directeur = await DirecteurAsync();

        (await SendAsync(HttpMethod.Get, $"{Url}?classroomId={Guid.NewGuid()}&month=novembre", directeur))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Secretariat_Can_Tend_The_Roll_Book_And_Read_Stats()
    {
        var directeur = await DirecteurAsync();
        var (classroom, _, s1, _) = await SeedAsync(directeur);
        var secretaire = await TokenAsync(AuthApiFactory.SecretaireEmail, AuthApiFactory.SecretairePassword);

        (await SendAsync(HttpMethod.Put, $"{Url}/entries", secretaire,
            new { classroomId = classroom, entries = new[] { Cell(s1, "Morning", "Present") } })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Get, $"{Url}/stats?scope=level&month={WorkingDay:yyyy-MM}", secretaire)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ------------------------------------------------------------------------------------ Export PDF

    private async Task<HttpResponseMessage> PdfAsync(string token, Guid classroom, string query) =>
        await SendAsync(HttpMethod.Get, $"{Url}/pdf?classroomId={classroom}&{query}", token);

    [Fact]
    public async Task Directeur_Exports_The_Monthly_Cahier_As_An_Inline_French_Pdf()
    {
        var directeur = await DirecteurAsync();
        var (classroom, _, s1, _) = await SeedAsync(directeur);
        await SendAsync(HttpMethod.Put, $"{Url}/entries", directeur,
            new { classroomId = classroom, entries = new[] { Cell(s1, "Morning", "Present") } });

        // « fr » explicite, puis sans langue : une école hors module Franco-Arabe reçoit le français.
        foreach (var query in new[] { $"month={WorkingDay:yyyy-MM}&lang=fr", $"month={WorkingDay:yyyy-MM}" })
        {
            var response = await PdfAsync(directeur, classroom, query);

            response.StatusCode.Should().Be(HttpStatusCode.OK, query);
            response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf", query);
            response.Content.Headers.ContentDisposition!.DispositionType.Should().Be("inline");
            response.Content.Headers.ContentDisposition.FileName.Should().StartWith("Cahier-de-roulement-CM2-A-");
            (await response.Content.ReadAsByteArrayAsync()).Take(5).Should().Equal("%PDF-"u8.ToArray(), query);
        }
    }

    [Fact]
    public async Task Arabic_And_Bilingual_Exports_Are_Refused_For_A_School_Without_The_Franco_Arabic_Module()
    {
        var directeur = await DirecteurAsync();
        var (classroom, _, _, _) = await SeedAsync(directeur);

        // L'école de test n'active pas le module : le rendu arabe ou bilingue est refusé (422), jamais généré à moitié.
        // Le rendu arabe et bilingue d'une école Franco-Arabe est couvert par RollBookPdfTests (PostgreSQL réel).
        foreach (var lang in new[] { "ar", "bi" })
        {
            (await PdfAsync(directeur, classroom, $"month={WorkingDay:yyyy-MM}&lang={lang}")).StatusCode
                .Should().Be(HttpStatusCode.UnprocessableEntity, lang);
        }
    }

    [Fact]
    public async Task Secretariat_Can_Export_The_Cahier()
    {
        var directeur = await DirecteurAsync();
        var (classroom, _, _, _) = await SeedAsync(directeur);
        var secretaire = await TokenAsync(AuthApiFactory.SecretaireEmail, AuthApiFactory.SecretairePassword);

        (await PdfAsync(secretaire, classroom, $"month={WorkingDay:yyyy-MM}&lang=fr")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_Pdf_Needs_Exactly_One_Period_And_A_Known_Language()
    {
        var directeur = await DirecteurAsync();
        var (classroom, _, _, _) = await SeedAsync(directeur);

        (await PdfAsync(directeur, classroom, "lang=fr")).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "aucune période");
        (await PdfAsync(directeur, classroom, $"month={ThisMonth}&termId={Guid.NewGuid()}")).StatusCode
            .Should().Be(HttpStatusCode.UnprocessableEntity, "deux périodes");
        (await PdfAsync(directeur, classroom, $"month={ThisMonth}&lang=en")).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await PdfAsync(directeur, classroom, "month=novembre")).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await PdfAsync(directeur, classroom, "month=2001-01")).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "hors de toute année scolaire");
        (await PdfAsync(directeur, Guid.NewGuid(), $"month={ThisMonth}")).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "classe inconnue");
        (await PdfAsync(directeur, classroom, $"termId={Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "période d'évaluation inconnue");
    }

    [Fact]
    public async Task Teacher_Finance_And_Anonymous_Callers_Cannot_Export_The_Cahier()
    {
        var directeur = await DirecteurAsync();
        var (classroom, _, _, _) = await SeedAsync(directeur);
        var query = $"month={WorkingDay:yyyy-MM}&lang=fr";

        var enseignant = await TokenAsync(AuthApiFactory.EnseignantEmail, AuthApiFactory.EnseignantPassword);
        var finance = await TokenAsync(AuthApiFactory.FinanceEmail, AuthApiFactory.FinancePassword);

        (await PdfAsync(enseignant, classroom, query)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "l'Enseignant saisit le cahier mais ne l'exporte pas");
        (await PdfAsync(finance, classroom, query)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _client.GetAsync($"{Url}/pdf?classroomId={classroom}&{query}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Finance_And_Anonymous_Callers_Are_Rejected()
    {
        var directeur = await DirecteurAsync();
        var (classroom, _, s1, _) = await SeedAsync(directeur);
        var finance = await TokenAsync(AuthApiFactory.FinanceEmail, AuthApiFactory.FinancePassword);

        (await SendAsync(HttpMethod.Get, $"{Url}?classroomId={classroom}&month={ThisMonth}", finance)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Put, $"{Url}/entries", finance,
            new { classroomId = classroom, entries = new[] { Cell(s1, "Morning", "Present") } })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Get, $"{Url}/stats?scope=level&month={ThisMonth}", finance)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await _client.GetAsync($"{Url}?classroomId={classroom}&month={ThisMonth}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_Teacher_Is_Limited_To_His_Classes_And_Cannot_Read_Stats()
    {
        var directeur = await DirecteurAsync();
        var (classroom, subject, s1, _) = await SeedAsync(directeur);
        var enseignant = await TokenAsync(AuthApiFactory.EnseignantEmail, AuthApiFactory.EnseignantPassword);

        // Compte non rattaché à une fiche enseignant : refus.
        (await SendAsync(HttpMethod.Get, $"{Url}?classroomId={classroom}&month={WorkingDay:yyyy-MM}", enseignant))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Rattaché et affecté à la classe : la grille et la saisie lui sont ouvertes.
        var teacher = await CreateAsync(directeur, "/api/v1/teachers", new
        {
            fullName = "Enseignant de test", email = "prof.lie@sama-ecole.sn", birthDate = "1985-04-12",
            subjectIds = new[] { subject }, userId = AuthApiFactory.EnseignantId
        });
        (await SendAsync(HttpMethod.Post, $"/api/v1/teachers/{teacher}/assignments", directeur, new { classroomId = classroom, subjectId = subject }))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        (await SendAsync(HttpMethod.Get, $"{Url}?classroomId={classroom}&month={WorkingDay:yyyy-MM}", enseignant)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Put, $"{Url}/entries", enseignant,
            new { classroomId = classroom, entries = new[] { Cell(s1, "Morning", "Present") } })).StatusCode.Should().Be(HttpStatusCode.OK);

        // Les statistiques restent à la direction et au secrétariat.
        (await SendAsync(HttpMethod.Get, $"{Url}/stats?scope=level&month={WorkingDay:yyyy-MM}", enseignant))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Une autre classe, où il n'enseigne pas : refus.
        var other = await CreateAsync(directeur, "/api/v1/classrooms", new { name = "CM1 B", level = "Primaire", capacity = 30 });
        (await SendAsync(HttpMethod.Get, $"{Url}?classroomId={other}&month={WorkingDay:yyyy-MM}", enseignant))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
