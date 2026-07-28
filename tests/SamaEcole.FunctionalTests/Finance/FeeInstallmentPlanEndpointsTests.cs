using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace SamaEcole.FunctionalTests.Finance;

/// <summary>
/// Étape 5 — échéanciers personnalisés, de bout en bout contre un vrai PostgreSQL.
///
/// Le critère central : la somme des échéances doit égaler EXACTEMENT le montant dû de l'inscription,
/// et un plan personnalisé doit primer sur la synthèse mensuelle uniforme dans GetStudentBalanceQuery
/// dès qu'il existe (InstallmentScheduleCalculator).
/// </summary>
public class FeeInstallmentPlanEndpointsTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public FeeInstallmentPlanEndpointsTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public Task InitializeAsync() => _factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private const decimal Inscription = 10_000m;
    private const decimal Mensualite = 15_000m;
    private const int DefaultTuitionMonths = 9;
    private const decimal ExpectedTotal = Inscription + Mensualite * DefaultTuitionMonths; // 145 000

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private record Tokens(string AccessToken, int ExpiresIn);
    private record ClassroomDto(Guid Id, string Name, string Level, int Capacity, int StudentCount);
    private record FeeCategoryDto(Guid Id, string Name, bool IsRecurring);
    private record Receipt(Guid EnrollmentId, string Matricule, decimal TotalDue);
    private record InstallmentDto(string Id, string Designation, decimal Amount, decimal AmountPaid, decimal RemainingDue, DateTimeOffset DueDate, string Status);
    private record StudentBalanceDto(Guid EnrollmentId, Guid StudentId, decimal TotalDue, decimal AmountPaid, List<InstallmentDto> Installments);
    private record StudentSearchItem(Guid Id, string Matricule, string FullName);
    private record StudentSearchPage(List<StudentSearchItem> Items);
    private record ApplyToClassResult(int AppliedCount, int SkippedCount);

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
        var categoryResponse = await SendAsync(HttpMethod.Post, "/api/v1/finance/fee-categories", token, new { name, isRecurring });
        categoryResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var category = (await categoryResponse.Content.ReadFromJsonAsync<FeeCategoryDto>())!;

        var applyResponse = await SendAsync(HttpMethod.Post, "/api/v1/finance/fees/apply-standard", token,
            new { feeCategoryId = category.Id, amount, overwriteExisting = false });
        applyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>Prépare une école prête à inscrire : une classe, une année active, un barème complet.</summary>
    private async Task<Guid> SeedEnrollableSchoolAsync(string directeurToken)
    {
        var classroomResponse = await SendAsync(HttpMethod.Post, "/api/v1/classrooms", directeurToken,
            new { name = "CM2", level = "Primaire", capacity = 40 });
        classroomResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var classroom = (await classroomResponse.Content.ReadFromJsonAsync<ClassroomDto>())!;

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

    private async Task<Receipt> EnrollAsync(string secretaireToken, Guid classroomId, string fullName)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/v1/enrollments", secretaireToken, new
        {
            type = "NewEnrollment",
            classroomId,
            fullName,
            birthDate = "2015-05-20",
            birthPlace = "Dakar",
            gender = "F"
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<Receipt>())!;
    }

    private async Task<Guid> FindStudentIdByMatriculeAsync(string token, string matricule)
    {
        var response = await SendAsync(HttpMethod.Get, $"/api/v1/students?page=1&pageSize=10&search={matricule}", token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = (await response.Content.ReadFromJsonAsync<StudentSearchPage>())!;
        return page.Items.Single().Id;
    }

    private async Task<StudentBalanceDto> GetBalanceAsync(string token, Guid studentId)
    {
        var response = await SendAsync(HttpMethod.Get, $"/api/v1/finance/students/{studentId}/balance", token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<StudentBalanceDto>())!;
    }

    [Fact]
    public async Task Creating_A_Well_Formed_Plan_Replaces_The_Derived_Monthly_Schedule_On_The_Balance()
    {
        var directeur = await DirecteurTokenAsync();
        var classroomId = await SeedEnrollableSchoolAsync(directeur);
        var secretaire = await SecretaireTokenAsync();
        var receipt = await EnrollAsync(secretaire, classroomId, "Awa Échéancier");

        receipt.TotalDue.Should().Be(ExpectedTotal);

        var createResponse = await SendAsync(
            HttpMethod.Post, $"/api/v1/finance/enrollments/{receipt.EnrollmentId}/installment-plan", directeur,
            new
            {
                reason = "Accord négocié avec la famille",
                installments = new object[]
                {
                    new { label = "Versement 1", amount = 60_000m, dueDate = Today.ToString("yyyy-MM-dd") },
                    new { label = "Versement 2", amount = 85_000m, dueDate = Today.AddMonths(2).ToString("yyyy-MM-dd") }
                }
            });

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var studentId = await FindStudentIdByMatriculeAsync(directeur, receipt.Matricule);
        var balance = await GetBalanceAsync(directeur, studentId);

        // Le plan personnalisé (2 échéances) remplace la synthèse mensuelle uniforme (10 lignes :
        // 1 inscription + 9 mensualités) — preuve qu'InstallmentScheduleCalculator a bien priorisé le plan.
        balance.Installments.Should().HaveCount(2);
        balance.Installments.Select(i => i.Designation).Should().BeEquivalentTo(["Versement 1", "Versement 2"]);
        balance.Installments.Sum(i => i.Amount).Should().Be(ExpectedTotal);
    }

    [Fact]
    public async Task A_Plan_Whose_Sum_Does_Not_Match_The_Total_Due_Is_Rejected()
    {
        var directeur = await DirecteurTokenAsync();
        var classroomId = await SeedEnrollableSchoolAsync(directeur);
        var secretaire = await SecretaireTokenAsync();
        var receipt = await EnrollAsync(secretaire, classroomId, "Modou Somme Fausse");

        var response = await SendAsync(
            HttpMethod.Post, $"/api/v1/finance/enrollments/{receipt.EnrollmentId}/installment-plan", directeur,
            new
            {
                reason = (string?)null,
                installments = new object[]
                {
                    new { label = "Versement unique", amount = 1_000m, dueDate = Today.ToString("yyyy-MM-dd") }
                }
            });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Secretaire_Cannot_Create_An_Installment_Plan()
    {
        var directeur = await DirecteurTokenAsync();
        var classroomId = await SeedEnrollableSchoolAsync(directeur);
        var secretaire = await SecretaireTokenAsync();
        var receipt = await EnrollAsync(secretaire, classroomId, "Fatou Non Autorisée");

        var response = await SendAsync(
            HttpMethod.Post, $"/api/v1/finance/enrollments/{receipt.EnrollmentId}/installment-plan", secretaire,
            new
            {
                reason = (string?)null,
                installments = new object[]
                {
                    new { label = "Versement unique", amount = ExpectedTotal, dueDate = Today.ToString("yyyy-MM-dd") }
                }
            });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_Second_Plan_On_The_Same_Enrollment_Replaces_The_First_On_The_Balance()
    {
        var directeur = await DirecteurTokenAsync();
        var classroomId = await SeedEnrollableSchoolAsync(directeur);
        var secretaire = await SecretaireTokenAsync();
        var receipt = await EnrollAsync(secretaire, classroomId, "Ibrahima Renégociation");

        async Task<HttpResponseMessage> PostPlan(string label) => await SendAsync(
            HttpMethod.Post, $"/api/v1/finance/enrollments/{receipt.EnrollmentId}/installment-plan", directeur,
            new
            {
                reason = (string?)null,
                installments = new object[] { new { label, amount = ExpectedTotal, dueDate = Today.ToString("yyyy-MM-dd") } }
            });

        (await PostPlan("Premier accord")).StatusCode.Should().Be(HttpStatusCode.Created);
        (await PostPlan("Second accord")).StatusCode.Should().Be(HttpStatusCode.Created);

        var studentId = await FindStudentIdByMatriculeAsync(directeur, receipt.Matricule);
        var balance = await GetBalanceAsync(directeur, studentId);

        balance.Installments.Should().ContainSingle().Which.Designation.Should().Be("Second accord");
    }

    [Fact]
    public async Task Applying_A_Template_To_A_Classroom_Gives_Every_Active_Enrollment_Its_Own_Plan()
    {
        var directeur = await DirecteurTokenAsync();
        var classroomId = await SeedEnrollableSchoolAsync(directeur);
        var secretaire = await SecretaireTokenAsync();

        var receiptA = await EnrollAsync(secretaire, classroomId, "Awa Classe Template");
        var receiptB = await EnrollAsync(secretaire, classroomId, "Baba Classe Template");

        var response = await SendAsync(
            HttpMethod.Post, $"/api/v1/finance/classrooms/{classroomId}/installment-plan", directeur,
            new
            {
                reason = "Facilité de fin d'année",
                template = new object[]
                {
                    new { label = "Versement 1", percentage = 0.4m, offsetDays = 0 },
                    new { label = "Versement 2", percentage = 0.3m, offsetDays = 30 },
                    new { label = "Versement 3", percentage = 0.3m, offsetDays = 60 }
                }
            });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<ApplyToClassResult>())!;
        result.AppliedCount.Should().Be(2);
        result.SkippedCount.Should().Be(0);

        var studentIdA = await FindStudentIdByMatriculeAsync(directeur, receiptA.Matricule);
        var balanceA = await GetBalanceAsync(directeur, studentIdA);

        balanceA.Installments.Should().HaveCount(3);
        balanceA.Installments.Sum(i => i.Amount).Should().Be(ExpectedTotal, "le dernier versement absorbe l'écart d'arrondi");

        var studentIdB = await FindStudentIdByMatriculeAsync(directeur, receiptB.Matricule);
        var balanceB = await GetBalanceAsync(directeur, studentIdB);
        balanceB.Installments.Should().HaveCount(3);
    }
}
