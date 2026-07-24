using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using SamaEcole.Domain.Enums;
using SamaEcole.FunctionalTests.Common;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace SamaEcole.FunctionalTests.Enrollments;

/// <summary>
/// Ticket JGK-E01 — inscriptions, de bout en bout contre un vrai PostgreSQL.
///
/// Le critère central du ticket : le service Finance ne peut PAS composer un montant dû. On le
/// prouve par le test d'autorisation négatif obligatoire (Finance → 403 sur POST /enrollments).
/// Le chemin nominal (Secrétariat → 201, matricule généré, montant calculé, reçu relisible) vérifie
/// que l'inscription relie bien année active, classe, matricule et barème.
/// </summary>
public class EnrollmentsEndpointsTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public EnrollmentsEndpointsTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public Task InitializeAsync() => _factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private const decimal Inscription = 10_000m;
    private const decimal Mensualite = 15_000m;
    private const int DefaultTuitionMonths = 9; // aucune ligne de réglages semée → défaut.
    private const decimal ExpectedTotal = Inscription + Mensualite * DefaultTuitionMonths;

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private record Tokens(string AccessToken, int ExpiresIn);
    private record ClassroomDto(Guid Id, string Name, string Level, int Capacity, int StudentCount);
    private record FeeCategoryDto(Guid Id, string Name, bool IsRecurring);
    private record ReceiptLine(string Designation, bool IsRecurring, decimal UnitAmount, int Months, decimal LineTotal);
    private record CollectedLine(string Designation, bool IsRecurring, int Months, decimal Amount);
    private record Receipt(
        Guid EnrollmentId, string ReceiptNumber, string SchoolName, string? SchoolPhone, string? SchoolCity,
        string Matricule, string StudentFullName, string ClassroomName, string ClassroomLevel,
        string SchoolYearLabel, string Type, string Status, DateTimeOffset EnrolledAt,
        List<ReceiptLine> Lines, decimal TotalDue,
        List<CollectedLine> CollectedLines, decimal TotalCollected, decimal RemainingBalance, string? PaymentMethod);

    // Miroir PARTIEL de GetStudentDetailQuery : seuls les champs utiles pour retrouver l'inscription et
    // son jeton xmin (AcademicHistoryEntryDto.RowVersion) — les champs JSON non repris (Identity,
    // Grades, Payments, GradingScale...) sont simplement ignorés par la désérialisation.
    private record AcademicHistoryEntry(Guid EnrollmentId, string Status, uint RowVersion);
    private record StudentDetail(List<AcademicHistoryEntry> AcademicHistory);
    private record StudentSearchItem(Guid Id, string Matricule, string FullName);
    private record StudentSearchPage(List<StudentSearchItem> Items);

    private async Task<string> TokenAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!.AccessToken;
    }

    private Task<string> DirecteurTokenAsync() =>
        TokenAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);
    private Task<string> SecretaireTokenAsync() =>
        TokenAsync(AuthApiFactory.SecretaireEmail, AuthApiFactory.SecretairePassword);
    private Task<string> FinanceTokenAsync() =>
        TokenAsync(AuthApiFactory.FinanceEmail, AuthApiFactory.FinancePassword);

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await _client.SendAsync(request);
    }

    /// <summary>Prépare une école prête à inscrire : une classe, une année active, un barème complet.</summary>
    private async Task<Guid> SeedEnrollableSchoolAsync(string directeurToken)
    {
        var classroomResponse = await SendAsync(HttpMethod.Post, "/api/v1/classrooms", directeurToken,
            new { name = "CM2", level = "Primaire", capacity = 40 });
        classroomResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var classroom = (await classroomResponse.Content.ReadFromJsonAsync<ClassroomDto>())!;

        // Première année de l'école → active d'office (JGK-C01). Dates relatives : l'année reste « en cours ».
        var yearResponse = await SendAsync(HttpMethod.Post, "/api/v1/school-years", directeurToken, new
        {
            label = $"{Today.Year}-{Today.Year + 1}",
            startDate = Today.AddDays(-150).ToString("yyyy-MM-dd"),
            endDate = Today.AddDays(+120).ToString("yyyy-MM-dd")
        });
        yearResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        await SeedFeeAsync(directeurToken, "Inscription", isRecurring: false, amount: Inscription);
        await SeedFeeAsync(directeurToken, "Mensualité", isRecurring: true, amount: Mensualite);

        return classroom.Id;
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

    private static object NewEnrollmentBody(Guid classroomId, string fullName) => new
    {
        type = "NewEnrollment",
        classroomId,
        fullName,
        birthDate = "2015-05-20",
        birthPlace = "Dakar",
        gender = "F"
    };

    /// <summary>
    /// Liste les catégories de frais de l'école courante — utilisé par le test de ventilation pour
    /// récupérer les IDs réels des catégories semées dans SeedEnrollableSchoolAsync.
    /// </summary>
    private async Task<List<FeeCategoryDto>> ListFeeCategoriesAsync(string token)
    {
        var response = await SendAsync(HttpMethod.Get, "/api/v1/finance/fee-categories", token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<List<FeeCategoryDto>>())!;
    }

    /// <summary>
    /// Convertit un objet anonyme en <see cref="Dictionary{TKey,TValue}"/> pour pouvoir lui ajouter
    /// des clés (collectedFees, paymentMethod) sans abandonner le typage structurel sur le noyau du body.
    /// </summary>
    private static Dictionary<string, object?> ToDictionary(object anonymous) =>
        anonymous.GetType().GetProperties()
            .ToDictionary(p => char.ToLowerInvariant(p.Name[0]) + p.Name[1..], p => p.GetValue(anonymous));

    /// <summary>
    /// Le reçu (Receipt) porte le matricule mais pas l'identifiant brut de l'élève : on le retrouve par
    /// recherche (GetStudentsQuery accepte nom OU matricule), comme le fait
    /// ClassroomsEndpointsTests pour retrouver un élève fraîchement créé.
    /// </summary>
    private async Task<Guid> FindStudentIdByMatriculeAsync(string token, string matricule)
    {
        var response = await SendAsync(
            HttpMethod.Get, $"/api/v1/students?page=1&pageSize=10&search={matricule}", token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var page = (await response.Content.ReadFromJsonAsync<StudentSearchPage>())!;
        return page.Items.Single().Id;
    }

    /// <summary>
    /// Jeton xmin d'une inscription (AGENTS.md règle #5) : nécessaire à CancelEnrollmentCommand et
    /// ChangeEnrollmentStatusCommand, lu depuis l'historique scolaire de l'élève (GET /students/{id}),
    /// comme documenté sur AcademicHistoryEntryDto.RowVersion — il n'existe nulle part ailleurs
    /// (le reçu d'inscription ne le porte pas).
    /// </summary>
    private async Task<AcademicHistoryEntry> FetchAcademicHistoryEntryAsync(
        string token, Guid studentId, Guid enrollmentId)
    {
        var response = await SendAsync(HttpMethod.Get, $"/api/v1/students/{studentId}", token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var detail = (await response.Content.ReadFromJsonAsync<StudentDetail>())!;
        return detail.AcademicHistory.Single(e => e.EnrollmentId == enrollmentId);
    }

    /// <summary>
    /// Encaisse un versement partiel — utilisé UNIQUEMENT pour faire tourner le xmin d'une inscription
    /// avant un test de conflit sur ChangeEnrollmentStatusCommand (RecordPayment ne bloque aucune
    /// transition de statut, contrairement à CancelEnrollmentCommand qui rejette explicitement toute
    /// inscription déjà encaissée — voir TouchEnrollmentRowVersionAsync dans AuthApiFactory pour le cas
    /// symétrique côté annulation).
    /// </summary>
    private async Task RecordPaymentAsync(string financeToken, Guid enrollmentId, decimal amount)
    {
        var sessionResponse = await SendAsync(HttpMethod.Post, "/api/v1/finance/sessions", financeToken, new { openingBalance = 0m });
        if (!sessionResponse.IsSuccessStatusCode && sessionResponse.StatusCode != HttpStatusCode.Conflict)
            sessionResponse.EnsureSuccessStatusCode();

        var response = await SendAsync(HttpMethod.Post, "/api/v1/finance/payments", financeToken,
            new { enrollmentId, amount, method = "Cash", category = "Tuition" });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    /// <summary>Noms des élèves renvoyés par GET /students pour une recherche donnée, filtre d'année optionnel.</summary>
    private async Task<List<string>> GetStudentNamesAsync(string token, string search, bool activeYearOnly)
    {
        var url = $"/api/v1/students?page=1&pageSize=50&search={search}";
        if (activeYearOnly) url += "&activeYearOnly=true";

        var response = await SendAsync(HttpMethod.Get, url, token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var page = (await response.Content.ReadFromJsonAsync<StudentSearchPage>())!;
        return page.Items.Select(i => i.FullName).ToList();
    }

    [Fact]
    public async Task The_Students_List_Filters_On_The_Active_Year_Only_When_Asked()
    {
        // GetStudentsQuery.ActiveYearOnly (écran Élèves) : la liste doit pouvoir se borner à l'effectif
        // INSCRIT pour l'année active — inscriptions, notes et finances travaillent sur cet exercice —
        // sans jamais retirer de l'annuaire complet un élève créé hors inscription (parcours « importer
        // un effectif puis inscrire »). On prouve les deux comportements sur la MÊME donnée.
        var directeur = await DirecteurTokenAsync();
        var classroomId = await SeedEnrollableSchoolAsync(directeur);

        // A — inscrit sur l'année active : POST /enrollments crée l'élève ET son inscription.
        var secretaire = await SecretaireTokenAsync();
        var enrollResponse = await SendAsync(
            HttpMethod.Post, "/api/v1/enrollments", secretaire, NewEnrollmentBody(classroomId, "Awa Filtre Active"));
        enrollResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        // B — élève créé DIRECTEMENT, sans aucune inscription : l'annuaire le connaît, l'année active non.
        var createResponse = await SendAsync(HttpMethod.Post, "/api/v1/students", directeur, new
        {
            fullName = "Modou Filtre Direct",
            birthDate = "2015-05-20",
            birthPlace = "Dakar",
            gender = "M",
            classroomId
        });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        // Filtré (activeYearOnly=true) : seul l'inscrit de l'année active remonte.
        var activeOnly = await GetStudentNamesAsync(directeur, "Filtre", activeYearOnly: true);
        activeOnly.Should().Contain("Awa Filtre Active");
        activeOnly.Should().NotContain("Modou Filtre Direct", "un élève sans inscription sur l'année active en est absent");

        // Défaut de l'API (annuaire complet) : les deux remontent — la rétrocompatibilité est préservée.
        var all = await GetStudentNamesAsync(directeur, "Filtre", activeYearOnly: false);
        all.Should().Contain("Awa Filtre Active");
        all.Should().Contain("Modou Filtre Direct");
    }

    [Fact]
    public async Task Finance_Must_Not_Be_Allowed_To_Create_An_Enrollment()
    {
        // Critère du ticket : le service Finance ne compose jamais un montant dû (règle #4).
        // Le contrôle de rôle précède tout traitement — inutile même de semer le barème.
        var finance = await FinanceTokenAsync();

        var response = await SendAsync(HttpMethod.Post, "/api/v1/enrollments", finance,
            NewEnrollmentBody(Guid.NewGuid(), "Tentative Interdite"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_Secretariat_Creates_An_Enrollment_With_The_Computed_Total_And_A_Readable_Receipt()
    {
        var directeur = await DirecteurTokenAsync();
        var classroomId = await SeedEnrollableSchoolAsync(directeur);

        var secretaire = await SecretaireTokenAsync();
        var createResponse = await SendAsync(HttpMethod.Post, "/api/v1/enrollments", secretaire,
            NewEnrollmentBody(classroomId, "Awa Ndiaye"));

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var receipt = (await createResponse.Content.ReadFromJsonAsync<Receipt>())!;

        receipt.Matricule.Should().NotBeNullOrWhiteSpace("le matricule est généré à l'enregistrement");
        receipt.ReceiptNumber.Should().MatchRegex(@"^REC-\d{4}-\d{4}$", "le numéro de reçu officiel est généré à l'enregistrement (JGK-E02)");
        receipt.StudentFullName.Should().Be("Awa Ndiaye");
        receipt.Type.Should().Be("NewEnrollment");
        receipt.TotalDue.Should().Be(ExpectedTotal, "frais ponctuel + mensualité × 9 mois (réglage par défaut)");
        receipt.Lines.Should().HaveCount(2);
        receipt.Lines.Single(l => l.IsRecurring).Months.Should().Be(DefaultTuitionMonths);

        // Le reçu se relit à l'identique (réimpression) — même montant, mêmes lignes figées.
        var receiptResponse = await SendAsync(
            HttpMethod.Get, $"/api/v1/enrollments/{receipt.EnrollmentId}/receipt", secretaire);
        receiptResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var reread = (await receiptResponse.Content.ReadFromJsonAsync<Receipt>())!;

        reread.TotalDue.Should().Be(ExpectedTotal);
        reread.Matricule.Should().Be(receipt.Matricule);
        reread.Lines.Should().HaveCount(2);
    }

    [Fact]
    public async Task An_Enrollment_Without_Any_Collection_Should_Not_Create_A_Payment()
    {
        // Dossier ouvert sans versement : le dû annuel est bien figé, mais le reçu n'atteste de rien
        // — total encaissé à 0, aucun mode de règlement, et surtout aucune écriture de caisse.
        var directeur = await DirecteurTokenAsync();
        var classroomId = await SeedEnrollableSchoolAsync(directeur);

        var secretaire = await SecretaireTokenAsync();
        var createResponse = await SendAsync(HttpMethod.Post, "/api/v1/enrollments", secretaire,
            NewEnrollmentBody(classroomId, "Awa Ndiaye"));

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var receipt = (await createResponse.Content.ReadFromJsonAsync<Receipt>())!;

        receipt.TotalCollected.Should().Be(0);
        receipt.CollectedLines.Should().BeEmpty();
        receipt.PaymentMethod.Should().BeNull("aucun versement : aucun mode de règlement à afficher");
        receipt.RemainingBalance.Should().Be(ExpectedTotal);
    }

    [Fact]
    public async Task Fees_Collected_At_Enrollment_Should_Be_Itemised_On_The_Receipt()
    {
        // Cas courant au guichet : le tuteur règle les droits d'inscription ET la première mensualité.
        // Le reçu doit ventiler CES deux lignes, et son total être exactement la somme encaissée —
        // jamais le dû annuel.
        var directeur = await DirecteurTokenAsync();
        var classroomId = await SeedEnrollableSchoolAsync(directeur);
        var categories = await ListFeeCategoriesAsync(directeur);

        var secretaire = await SecretaireTokenAsync();
        var body = new Dictionary<string, object?>(ToDictionary(NewEnrollmentBody(classroomId, "Awa Ndiaye")))
        {
            ["collectedFees"] = categories.Select(c => new { feeCategoryId = c.Id, months = 1 }).ToList(),
            ["paymentMethod"] = "MobileMoney"
        };

        var createResponse = await SendAsync(HttpMethod.Post, "/api/v1/enrollments", secretaire, body);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var receipt = (await createResponse.Content.ReadFromJsonAsync<Receipt>())!;

        receipt.CollectedLines.Should().HaveCount(2, "une ligne par frais réellement réglé");
        receipt.CollectedLines.Single(l => l.IsRecurring).Months
            .Should().Be(1, "une seule mensualité est réglée le jour de l'inscription");
        receipt.TotalCollected.Should().Be(Inscription + Mensualite,
            "le reçu n'atteste que de l'encaissement du jour, pas des 9 mensualités dues");
        receipt.TotalDue.Should().Be(ExpectedTotal);
        receipt.RemainingBalance.Should().Be(ExpectedTotal - (Inscription + Mensualite));
        receipt.PaymentMethod.Should().Be("MobileMoney");

        // Réimpression : la ventilation est figée, elle ne se recalcule pas sur l'état courant.
        var reread = await SendAsync(
            HttpMethod.Get, $"/api/v1/enrollments/{receipt.EnrollmentId}/receipt", secretaire);
        reread.StatusCode.Should().Be(HttpStatusCode.OK);
        var rereadReceipt = (await reread.Content.ReadFromJsonAsync<Receipt>())!;

        rereadReceipt.TotalCollected.Should().Be(Inscription + Mensualite);
        rereadReceipt.CollectedLines.Should().HaveCount(2);
        rereadReceipt.PaymentMethod.Should().Be("MobileMoney", "le mode de règlement est relu du versement");
    }

    [Fact]
    public async Task The_Official_Receipt_Can_Be_Downloaded_As_A_Pdf()
    {
        // Ticket JGK-E02 : le reçu officiel se télécharge en PDF. On vérifie qu'un vrai PDF sort du
        // pipeline (en-tête magique), avec le bon type MIME et un nom de fichier portant le numéro
        // officiel — la fidélité visuelle à la référence relève de la revue à l'œil.
        var directeur = await DirecteurTokenAsync();
        var classroomId = await SeedEnrollableSchoolAsync(directeur);

        var secretaire = await SecretaireTokenAsync();
        var createResponse = await SendAsync(HttpMethod.Post, "/api/v1/enrollments", secretaire,
            NewEnrollmentBody(classroomId, "Awa Ndiaye"));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var receipt = (await createResponse.Content.ReadFromJsonAsync<Receipt>())!;

        var pdfResponse = await SendAsync(
            HttpMethod.Get, $"/api/v1/enrollments/{receipt.EnrollmentId}/receipt/pdf", secretaire);

        pdfResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        pdfResponse.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        pdfResponse.Content.Headers.ContentDisposition!.FileName.Should().Contain(receipt.ReceiptNumber);

        var bytes = await pdfResponse.Content.ReadAsByteArrayAsync();
        bytes.Should().NotBeEmpty();
        Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-", "l'en-tête magique d'un fichier PDF");
    }

    [Fact]
    public async Task Downloading_The_Pdf_Of_An_Unknown_Enrollment_Returns_404()
    {
        var secretaire = await SecretaireTokenAsync();
        var response = await SendAsync(
            HttpMethod.Get, $"/api/v1/enrollments/{Guid.NewGuid()}/receipt/pdf", secretaire);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Enrolling_Without_An_Active_Year_Is_Rejected()
    {
        // Classe et barème existent, mais aucune année scolaire active : rien à rattacher → 422.
        var directeur = await DirecteurTokenAsync();
        var classroomResponse = await SendAsync(HttpMethod.Post, "/api/v1/classrooms", directeur,
            new { name = "CI", level = "Primaire", capacity = 40 });
        var classroom = (await classroomResponse.Content.ReadFromJsonAsync<ClassroomDto>())!;

        var secretaire = await SecretaireTokenAsync();
        var response = await SendAsync(HttpMethod.Post, "/api/v1/enrollments", secretaire,
            NewEnrollmentBody(classroom.Id, "Sans Année"));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Creating_An_Enrollment_Without_A_Token_Should_Return_401()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/enrollments",
            NewEnrollmentBody(Guid.NewGuid(), "Anonyme"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ---------------------------------------------------------------- DELETE /enrollments/{id} (Cancel)

    [Fact]
    public async Task A_Secretariat_Can_Cancel_An_Enrollment_Without_Soft_Deleting_It()
    {
        var directeur = await DirecteurTokenAsync();
        var classroomId = await SeedEnrollableSchoolAsync(directeur);
        var secretaire = await SecretaireTokenAsync();

        var createResponse = await SendAsync(HttpMethod.Post, "/api/v1/enrollments", secretaire,
            NewEnrollmentBody(classroomId, "Aminata Diallo"));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var receipt = (await createResponse.Content.ReadFromJsonAsync<Receipt>())!;

        var studentId = await FindStudentIdByMatriculeAsync(secretaire, receipt.Matricule);
        var history = await FetchAcademicHistoryEntryAsync(secretaire, studentId, receipt.EnrollmentId);

        var response = await SendAsync(HttpMethod.Delete,
            $"/api/v1/enrollments/{receipt.EnrollmentId}?rowVersion={history.RowVersion}", secretaire);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // CancelEnrollmentCommand documente explicitement l'absence de soft delete : l'historique
        // scolaire doit rester visible (Status = Cancelled), jamais masqué par le Global Query Filter
        // comme le serait un IsDeleted = true (AGENTS.md règle #6 s'applique différemment ici : on
        // n'efface rien, on requalifie le statut — voir le commentaire de classe de la commande).
        var enrollment = await _factory.GetEnrollmentAsync(receipt.EnrollmentId);
        enrollment.Should().NotBeNull();
        enrollment!.Status.Should().Be(EnrollmentStatus.Cancelled);
        enrollment.IsDeleted.Should().BeFalse(
            "l'annulation est un changement de statut, pas un soft delete : l'historique doit rester lisible");
    }

    [Fact]
    public async Task Finance_Must_Not_Be_Allowed_To_Cancel_An_Enrollment()
    {
        var directeur = await DirecteurTokenAsync();
        var classroomId = await SeedEnrollableSchoolAsync(directeur);
        var secretaire = await SecretaireTokenAsync();

        var createResponse = await SendAsync(HttpMethod.Post, "/api/v1/enrollments", secretaire,
            NewEnrollmentBody(classroomId, "Omar Sylla"));
        var receipt = (await createResponse.Content.ReadFromJsonAsync<Receipt>())!;
        var studentId = await FindStudentIdByMatriculeAsync(secretaire, receipt.Matricule);
        var history = await FetchAcademicHistoryEntryAsync(secretaire, studentId, receipt.EnrollmentId);

        var finance = await FinanceTokenAsync();
        var response = await SendAsync(HttpMethod.Delete,
            $"/api/v1/enrollments/{receipt.EnrollmentId}?rowVersion={history.RowVersion}", finance);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _factory.GetEnrollmentAsync(receipt.EnrollmentId))!.Status.Should().Be(EnrollmentStatus.Confirmed);
    }

    [Fact]
    public async Task Cancelling_An_Unknown_Enrollment_Should_Return_404()
    {
        var secretaire = await SecretaireTokenAsync();

        var response = await SendAsync(
            HttpMethod.Delete, $"/api/v1/enrollments/{Guid.NewGuid()}?rowVersion=1", secretaire);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Cancelling_An_Enrollment_With_A_Stale_RowVersion_Should_Return_409()
    {
        var directeur = await DirecteurTokenAsync();
        var classroomId = await SeedEnrollableSchoolAsync(directeur);
        var secretaire = await SecretaireTokenAsync();

        var createResponse = await SendAsync(HttpMethod.Post, "/api/v1/enrollments", secretaire,
            NewEnrollmentBody(classroomId, "Rokhaya Thiam"));
        var receipt = (await createResponse.Content.ReadFromJsonAsync<Receipt>())!;
        var studentId = await FindStudentIdByMatriculeAsync(secretaire, receipt.Matricule);
        var staleVersion = (await FetchAcademicHistoryEntryAsync(secretaire, studentId, receipt.EnrollmentId)).RowVersion;

        // Fait tourner xmin SANS créer de paiement ni changer le statut (voir le commentaire de
        // TouchEnrollmentRowVersionAsync) : un Payment ferait échouer ce Cancel pour la MAUVAISE raison
        // (règle métier « paiement déjà encaissé »), pas pour un jeton simplement périmé.
        await _factory.TouchEnrollmentRowVersionAsync(receipt.EnrollmentId);

        var response = await SendAsync(HttpMethod.Delete,
            $"/api/v1/enrollments/{receipt.EnrollmentId}?rowVersion={staleVersion}", secretaire);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await _factory.GetEnrollmentAsync(receipt.EnrollmentId))!.Status.Should().Be(
            EnrollmentStatus.Confirmed, "le conflit ne doit jamais entraîner une annulation silencieuse");
    }

    // ---------------------------------------------------------------- POST /enrollments/{id}/status

    [Fact]
    public async Task A_Secretariat_Can_Declare_A_Dropout()
    {
        var directeur = await DirecteurTokenAsync();
        var classroomId = await SeedEnrollableSchoolAsync(directeur);
        var secretaire = await SecretaireTokenAsync();

        var createResponse = await SendAsync(HttpMethod.Post, "/api/v1/enrollments", secretaire,
            NewEnrollmentBody(classroomId, "Lamine Faye"));
        var receipt = (await createResponse.Content.ReadFromJsonAsync<Receipt>())!;
        var studentId = await FindStudentIdByMatriculeAsync(secretaire, receipt.Matricule);
        var history = await FetchAcademicHistoryEntryAsync(secretaire, studentId, receipt.EnrollmentId);

        var response = await SendAsync(HttpMethod.Post, $"/api/v1/enrollments/{receipt.EnrollmentId}/status",
            secretaire, new { newStatus = "DroppedOut", rowVersion = history.RowVersion });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var enrollment = await _factory.GetEnrollmentAsync(receipt.EnrollmentId);
        enrollment!.Status.Should().Be(EnrollmentStatus.DroppedOut);
        enrollment.IsDeleted.Should().BeFalse("un abandon n'est jamais un soft delete : notes et paiements restent intacts");
    }

    [Fact]
    public async Task Finance_Must_Not_Be_Allowed_To_Change_An_Enrollment_Status()
    {
        var directeur = await DirecteurTokenAsync();
        var classroomId = await SeedEnrollableSchoolAsync(directeur);
        var secretaire = await SecretaireTokenAsync();

        var createResponse = await SendAsync(HttpMethod.Post, "/api/v1/enrollments", secretaire,
            NewEnrollmentBody(classroomId, "Coumba Gning"));
        var receipt = (await createResponse.Content.ReadFromJsonAsync<Receipt>())!;
        var studentId = await FindStudentIdByMatriculeAsync(secretaire, receipt.Matricule);
        var history = await FetchAcademicHistoryEntryAsync(secretaire, studentId, receipt.EnrollmentId);

        var finance = await FinanceTokenAsync();
        var response = await SendAsync(HttpMethod.Post, $"/api/v1/enrollments/{receipt.EnrollmentId}/status",
            finance, new { newStatus = "DroppedOut", rowVersion = history.RowVersion });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _factory.GetEnrollmentAsync(receipt.EnrollmentId))!.Status.Should().Be(EnrollmentStatus.Confirmed);
    }

    [Fact]
    public async Task Changing_The_Status_Of_An_Unknown_Enrollment_Should_Return_404()
    {
        var secretaire = await SecretaireTokenAsync();

        var response = await SendAsync(HttpMethod.Post, $"/api/v1/enrollments/{Guid.NewGuid()}/status",
            secretaire, new { newStatus = "DroppedOut", rowVersion = 1u });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Changing_The_Status_With_A_Stale_RowVersion_Should_Return_409()
    {
        var directeur = await DirecteurTokenAsync();
        var classroomId = await SeedEnrollableSchoolAsync(directeur);
        var secretaire = await SecretaireTokenAsync();

        var createResponse = await SendAsync(HttpMethod.Post, "/api/v1/enrollments", secretaire,
            NewEnrollmentBody(classroomId, "Babacar Niang"));
        var receipt = (await createResponse.Content.ReadFromJsonAsync<Receipt>())!;
        var studentId = await FindStudentIdByMatriculeAsync(secretaire, receipt.Matricule);
        var staleVersion = (await FetchAcademicHistoryEntryAsync(secretaire, studentId, receipt.EnrollmentId)).RowVersion;

        // Un encaissement partiel concurrent fait tourner xmin sans bloquer la transition de statut
        // (contrairement à CancelEnrollmentCommand, ChangeEnrollmentStatusCommandHandler ne vérifie
        // aucun paiement) — voir RecordPaymentAsync.
        var finance = await FinanceTokenAsync();
        await RecordPaymentAsync(finance, receipt.EnrollmentId, 20_000m);

        var response = await SendAsync(HttpMethod.Post, $"/api/v1/enrollments/{receipt.EnrollmentId}/status",
            secretaire, new { newStatus = "DroppedOut", rowVersion = staleVersion });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await _factory.GetEnrollmentAsync(receipt.EnrollmentId))!.Status.Should().Be(
            EnrollmentStatus.Confirmed, "le conflit ne doit jamais entraîner un changement de statut silencieux");
    }
}
