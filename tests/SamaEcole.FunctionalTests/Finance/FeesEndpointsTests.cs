using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace SamaEcole.FunctionalTests.Finance;

/// <summary>
/// Ticket JGK-F01 — paramétrage des frais, de bout en bout contre un vrai PostgreSQL.
///
/// On y vérifie les deux options du Volume 1 §7.4 (montant standard en masse, puis exceptions par
/// classe), le verrouillage optimiste de la règle #5 (deux éditions concurrentes → 409),
/// l'historisation, et les droits (Directeur en écriture, lecture ouverte).
/// </summary>
public class FeesEndpointsTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public FeesEndpointsTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public Task InitializeAsync() => _factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private record Tokens(string AccessToken, int ExpiresIn);
    private record ClassroomDto(Guid Id, string Name, string Level, int Capacity, int StudentCount);
    private record FeeCategoryDto(Guid Id, string Name, bool IsRecurring);
    private record ClassFeeDto(Guid Id, Guid FeeCategoryId, string FeeCategoryName, Guid ClassroomId,
        string ClassroomName, string Level, decimal Amount, uint RowVersion);
    private record ApplyResult(int Created, int Updated, int Skipped);
    private record UpdateResult(Guid Id, decimal Amount, uint RowVersion);
    private record HistoryDto(decimal? OldAmount, decimal NewAmount, Guid ChangedByUserId, string ChangedByName, DateTimeOffset ChangedAt);

    private async Task<string> AccessTokenAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!.AccessToken;
    }

    private Task<string> DirecteurTokenAsync() =>
        AccessTokenAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);

    private Task<string> SecretaireTokenAsync() =>
        AccessTokenAsync(AuthApiFactory.SecretaireEmail, AuthApiFactory.SecretairePassword);

    private Task<string> FinanceTokenAsync() =>
        AccessTokenAsync(AuthApiFactory.FinanceEmail, AuthApiFactory.FinancePassword);

    /// <summary>
    /// Seul geste du Directeur capable d'activer la délégation Finance (matrice d'autorisation
    /// "Photoshop") : PUT /schools/current/settings, comme EnableSecretaryDelegationAsync dans
    /// UpdateGradingScaleEndpointsTests pour le barème.
    /// </summary>
    private async Task EnableFinanceDelegationAsync(string directeurToken, bool allowModify, bool allowDelete)
    {
        var response = await SendAsync(HttpMethod.Put, "/api/v1/schools/current/settings", directeurToken, new
        {
            gradingScale = "20",
            studentMatriculeFormat = "ELEV-{YEAR}-{SEQ:4}",
            teacherMatriculeFormat = "ENS-{YEAR}-{SEQ:3}",
            autoLogoutMinutes = 10,
            dateFormat = "dd/MM/yyyy",
            tuitionMonthsPerYear = 9,
            allowSecretaryToManageGrading = false,
            allowFinanceToModifyFees = allowModify,
            allowFinanceToDeleteFees = allowDelete
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

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

    private async Task<FeeCategoryDto> CreateCategoryAsync(string token, string name, bool isRecurring = true)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/v1/finance/fee-categories", token,
            new { name, isRecurring });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<FeeCategoryDto>())!;
    }

    private async Task<ApplyResult> ApplyStandardAsync(string token, Guid categoryId, decimal amount, bool overwrite)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/v1/finance/fees/apply-standard", token,
            new { feeCategoryId = categoryId, amount, overwriteExisting = overwrite });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<ApplyResult>())!;
    }

    private async Task<List<ClassFeeDto>> ListFeesAsync(string token)
    {
        var response = await SendAsync(HttpMethod.Get, "/api/v1/finance/fees", token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<List<ClassFeeDto>>())!;
    }

    [Fact]
    public async Task Listing_Fees_Without_A_Token_Should_Return_401()
    {
        var response = await _client.GetAsync("/api/v1/finance/fees");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Applying_A_Standard_Should_Create_One_Fee_Per_Classroom()
    {
        var token = await DirecteurTokenAsync();
        await CreateClassroomAsync(token, "CI");
        await CreateClassroomAsync(token, "CP");
        var category = await CreateCategoryAsync(token, "Mensualité");

        var result = await ApplyStandardAsync(token, category.Id, 15000, overwrite: false);

        result.Created.Should().Be(2);
        result.Updated.Should().Be(0);

        var fees = await ListFeesAsync(token);
        fees.Should().HaveCount(2);
        fees.Should().OnlyContain(f => f.Amount == 15000);
    }

    [Fact]
    public async Task Applying_A_Standard_On_A_School_Without_Classrooms_Should_Return_422()
    {
        // Aucune classe : rien à facturer. On ne crée pas un barème sans cible.
        var token = await DirecteurTokenAsync();
        var category = await CreateCategoryAsync(token, "Inscription", isRecurring: false);

        var response = await SendAsync(HttpMethod.Post, "/api/v1/finance/fees/apply-standard", token,
            new { feeCategoryId = category.Id, amount = 15000m, overwriteExisting = false });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task An_Exception_Set_Per_Class_Should_Survive_A_Re_Applied_Standard()
    {
        var token = await DirecteurTokenAsync();
        await CreateClassroomAsync(token, "CI");
        await CreateClassroomAsync(token, "CP");
        var category = await CreateCategoryAsync(token, "Mensualité");

        await ApplyStandardAsync(token, category.Id, 15000, overwrite: false);

        // Exception : une classe passe à 20 000.
        var fees = await ListFeesAsync(token);
        var exception = fees.First();
        var put = await SendAsync(HttpMethod.Put, $"/api/v1/finance/fees/{exception.Id}", token,
            new { amount = 20000m, rowVersion = exception.RowVersion });
        put.StatusCode.Should().Be(HttpStatusCode.OK);

        // On rejoue le standard SANS écraser : l'exception doit être PRÉSERVÉE (Volume 1 §7.4).
        var result = await ApplyStandardAsync(token, category.Id, 18000, overwrite: false);
        result.Created.Should().Be(0);
        result.Updated.Should().Be(0);
        result.Skipped.Should().Be(2, "sans écrasement, les lignes existantes sont laissées telles quelles");

        var after = await ListFeesAsync(token);
        after.Single(f => f.Id == exception.Id).Amount.Should().Be(20000, "l'exception ne doit pas être effacée");
    }

    [Fact]
    public async Task Re_Applying_A_Standard_With_Overwrite_Should_Realign_Every_Class()
    {
        var token = await DirecteurTokenAsync();
        await CreateClassroomAsync(token, "CI");
        await CreateClassroomAsync(token, "CP");
        var category = await CreateCategoryAsync(token, "Mensualité");

        await ApplyStandardAsync(token, category.Id, 15000, overwrite: false);

        var fees = await ListFeesAsync(token);
        var exception = fees.First();
        var put = await SendAsync(HttpMethod.Put, $"/api/v1/finance/fees/{exception.Id}", token,
            new { amount = 20000m, rowVersion = exception.RowVersion });
        put.StatusCode.Should().Be(HttpStatusCode.OK);

        // Remise à plat assumée : tout revient au standard, exception comprise.
        var result = await ApplyStandardAsync(token, category.Id, 18000, overwrite: true);
        result.Updated.Should().Be(2, "les deux classes changent de montant (15 000 et 20 000 → 18 000)");

        var after = await ListFeesAsync(token);
        after.Should().OnlyContain(f => f.Amount == 18000);
    }

    [Fact]
    public async Task A_Stale_RowVersion_Should_Be_Refused_With_A_409()
    {
        var token = await DirecteurTokenAsync();
        await CreateClassroomAsync(token, "CI");
        var category = await CreateCategoryAsync(token, "Mensualité");
        await ApplyStandardAsync(token, category.Id, 15000, overwrite: false);

        var fee = (await ListFeesAsync(token)).Single();
        var staleVersion = fee.RowVersion;

        // Première édition : réussit et fait tourner le jeton.
        var first = await SendAsync(HttpMethod.Put, $"/api/v1/finance/fees/{fee.Id}", token,
            new { amount = 16000m, rowVersion = staleVersion });
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        // Seconde édition avec le jeton D'ORIGINE, désormais périmé : conflit, pas d'écrasement (règle #5).
        var second = await SendAsync(HttpMethod.Put, $"/api/v1/finance/fees/{fee.Id}", token,
            new { amount = 17000m, rowVersion = staleVersion });
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Le montant en base est bien celui de la première édition, intact.
        (await ListFeesAsync(token)).Single().Amount.Should().Be(16000);
    }

    [Fact]
    public async Task Updating_A_Fee_Should_Record_Its_History()
    {
        var token = await DirecteurTokenAsync();
        await CreateClassroomAsync(token, "CI");
        var category = await CreateCategoryAsync(token, "Mensualité");
        await ApplyStandardAsync(token, category.Id, 15000, overwrite: false);

        var fee = (await ListFeesAsync(token)).Single();
        var put = await SendAsync(HttpMethod.Put, $"/api/v1/finance/fees/{fee.Id}", token,
            new { amount = 18000m, rowVersion = fee.RowVersion });
        put.StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await SendAsync(HttpMethod.Get, $"/api/v1/finance/fees/{fee.Id}/history", token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var history = (await response.Content.ReadFromJsonAsync<List<HistoryDto>>())!;

        // Deux entrées, la plus récente d'abord : la modification (15 000 → 18 000) puis la création
        // (null → 15 000).
        history.Should().HaveCount(2);
        history[0].OldAmount.Should().Be(15000);
        history[0].NewAmount.Should().Be(18000);
        history[0].ChangedByName.Should().Be("Directeur de test");
        history[1].OldAmount.Should().BeNull("la première entrée est une création");
        history[1].NewAmount.Should().Be(15000);
    }

    [Fact]
    public async Task Updating_An_Unknown_Fee_Should_Return_404()
    {
        var token = await DirecteurTokenAsync();

        var response = await SendAsync(HttpMethod.Put, $"/api/v1/finance/fees/{Guid.NewGuid()}", token,
            new { amount = 15000m, rowVersion = (uint)1 });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Duplicate_Category_Name_Should_Return_409()
    {
        var token = await DirecteurTokenAsync();
        await CreateCategoryAsync(token, "Cantine");

        var duplicate = await SendAsync(HttpMethod.Post, "/api/v1/finance/fee-categories", token,
            new { name = "Cantine", isRecurring = false });

        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_Secretary_Must_Not_Configure_Fees()
    {
        // Le barème est un paramètre d'établissement : Directeur uniquement (Volume 7 §15, règle #4).
        var directeur = await DirecteurTokenAsync();
        await CreateClassroomAsync(directeur, "CI");
        var category = await CreateCategoryAsync(directeur, "Mensualité");
        await ApplyStandardAsync(directeur, category.Id, 15000, overwrite: false);
        var fee = (await ListFeesAsync(directeur)).Single();

        var secretaire = await SecretaireTokenAsync();

        var create = await SendAsync(HttpMethod.Post, "/api/v1/finance/fee-categories", secretaire,
            new { name = "Transport", isRecurring = false });
        create.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var apply = await SendAsync(HttpMethod.Post, "/api/v1/finance/fees/apply-standard", secretaire,
            new { feeCategoryId = category.Id, amount = 9000m, overwriteExisting = true });
        apply.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var update = await SendAsync(HttpMethod.Put, $"/api/v1/finance/fees/{fee.Id}", secretaire,
            new { amount = 9000m, rowVersion = fee.RowVersion });
        update.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_Secretary_Should_Still_Read_The_Fees()
    {
        // La lecture reste ouverte : le secrétariat compose les montants dus à l'inscription.
        var directeur = await DirecteurTokenAsync();
        await CreateClassroomAsync(directeur, "CI");
        var category = await CreateCategoryAsync(directeur, "Mensualité");
        await ApplyStandardAsync(directeur, category.Id, 15000, overwrite: false);

        var secretaire = await SecretaireTokenAsync();
        var fees = await ListFeesAsync(secretaire);

        fees.Should().ContainSingle().Which.Amount.Should().Be(15000);
    }

    // ---------------------------------------------------------- Délégation Finance (matrice "Photoshop")

    [Fact]
    public async Task A_Finance_Must_Not_Modify_A_Fee_By_Default()
    {
        var directeur = await DirecteurTokenAsync();
        await CreateClassroomAsync(directeur, "CI");
        var category = await CreateCategoryAsync(directeur, "Mensualité");
        await ApplyStandardAsync(directeur, category.Id, 15000, overwrite: false);
        var fee = (await ListFeesAsync(directeur)).Single();

        var finance = await FinanceTokenAsync();
        var response = await SendAsync(HttpMethod.Put, $"/api/v1/finance/fees/{fee.Id}", finance,
            new { amount = 18000m, rowVersion = fee.RowVersion });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_Finance_Can_Modify_A_Fee_Once_The_Directeur_Enables_Delegation()
    {
        var directeur = await DirecteurTokenAsync();
        await CreateClassroomAsync(directeur, "CI");
        var category = await CreateCategoryAsync(directeur, "Mensualité");
        await ApplyStandardAsync(directeur, category.Id, 15000, overwrite: false);
        var fee = (await ListFeesAsync(directeur)).Single();

        await EnableFinanceDelegationAsync(directeur, allowModify: true, allowDelete: false);

        var finance = await FinanceTokenAsync();
        var response = await SendAsync(HttpMethod.Put, $"/api/v1/finance/fees/{fee.Id}", finance,
            new { amount = 18000m, rowVersion = fee.RowVersion });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ListFeesAsync(directeur)).Single().Amount.Should().Be(18000);
    }

    [Fact]
    public async Task A_Finance_Loses_Modify_Access_Again_Once_The_Directeur_Disables_Delegation()
    {
        var directeur = await DirecteurTokenAsync();
        await CreateClassroomAsync(directeur, "CI");
        var category = await CreateCategoryAsync(directeur, "Mensualité");
        await ApplyStandardAsync(directeur, category.Id, 15000, overwrite: false);
        var fee = (await ListFeesAsync(directeur)).Single();

        await EnableFinanceDelegationAsync(directeur, allowModify: true, allowDelete: false);
        await EnableFinanceDelegationAsync(directeur, allowModify: false, allowDelete: false);

        var finance = await FinanceTokenAsync();
        var response = await SendAsync(HttpMethod.Put, $"/api/v1/finance/fees/{fee.Id}", finance,
            new { amount = 18000m, rowVersion = fee.RowVersion });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ---------------------------------------------------- Feature D : autonomie Finance (créer/appliquer)

    [Fact]
    public async Task A_Finance_Must_Not_Create_A_Fee_Category_By_Default()
    {
        var directeur = await DirecteurTokenAsync();
        var finance = await FinanceTokenAsync();

        var response = await SendAsync(HttpMethod.Post, "/api/v1/finance/fee-categories", finance,
            new { name = "Transport", isRecurring = false });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_Finance_Can_Create_A_Fee_Category_Once_The_Directeur_Enables_Delegation()
    {
        var directeur = await DirecteurTokenAsync();
        await EnableFinanceDelegationAsync(directeur, allowModify: true, allowDelete: false);

        var finance = await FinanceTokenAsync();
        var response = await SendAsync(HttpMethod.Post, "/api/v1/finance/fee-categories", finance,
            new { name = "Transport", isRecurring = false });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_Finance_Must_Not_Apply_A_Standard_Fee_By_Default()
    {
        var directeur = await DirecteurTokenAsync();
        await CreateClassroomAsync(directeur, "CI");
        var category = await CreateCategoryAsync(directeur, "Mensualité");

        var finance = await FinanceTokenAsync();
        var response = await SendAsync(HttpMethod.Post, "/api/v1/finance/fees/apply-standard", finance,
            new { feeCategoryId = category.Id, amount = 15000m, overwriteExisting = false });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_Finance_Can_Apply_A_Standard_Fee_Once_The_Directeur_Enables_Delegation()
    {
        var directeur = await DirecteurTokenAsync();
        await CreateClassroomAsync(directeur, "CI");
        var category = await CreateCategoryAsync(directeur, "Mensualité");
        await EnableFinanceDelegationAsync(directeur, allowModify: true, allowDelete: false);

        var finance = await FinanceTokenAsync();
        var response = await SendAsync(HttpMethod.Post, "/api/v1/finance/fees/apply-standard", finance,
            new { feeCategoryId = category.Id, amount = 15000m, overwriteExisting = false });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ListFeesAsync(directeur)).Should().ContainSingle(f => f.Amount == 15000);
    }

    [Fact]
    public async Task A_Delete_Only_Delegation_Must_Not_Grant_Create_Or_Apply_Standard_Access()
    {
        // Créer une catégorie / appliquer un standard façonnent le barème (même délégation que
        // Modifier) — la délégation SUPPRIMER, plus lourde de conséquences, ne doit pas suffire.
        var directeur = await DirecteurTokenAsync();
        await CreateClassroomAsync(directeur, "CI");
        var category = await CreateCategoryAsync(directeur, "Mensualité");
        await EnableFinanceDelegationAsync(directeur, allowModify: false, allowDelete: true);

        var finance = await FinanceTokenAsync();

        var create = await SendAsync(HttpMethod.Post, "/api/v1/finance/fee-categories", finance,
            new { name = "Transport", isRecurring = false });
        create.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var apply = await SendAsync(HttpMethod.Post, "/api/v1/finance/fees/apply-standard", finance,
            new { feeCategoryId = category.Id, amount = 15000m, overwriteExisting = false });
        apply.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_Directeur_Can_Still_Create_And_Apply_Regardless_Of_Delegation_Settings()
    {
        // Contre-épreuve : le Directeur ne doit jamais dépendre de sa propre délégation.
        var directeur = await DirecteurTokenAsync();
        await CreateClassroomAsync(directeur, "CI");

        var category = await CreateCategoryAsync(directeur, "Cantine");
        var result = await ApplyStandardAsync(directeur, category.Id, 12000, overwrite: false);

        result.Created.Should().Be(1);
    }

    [Fact]
    public async Task A_Directeur_Can_Delete_A_Fee()
    {
        var directeur = await DirecteurTokenAsync();
        await CreateClassroomAsync(directeur, "CI");
        var category = await CreateCategoryAsync(directeur, "Mensualité");
        await ApplyStandardAsync(directeur, category.Id, 15000, overwrite: false);
        var fee = (await ListFeesAsync(directeur)).Single();

        var response = await SendAsync(
            HttpMethod.Delete, $"/api/v1/finance/fees/{fee.Id}?rowVersion={fee.RowVersion}", directeur);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ListFeesAsync(directeur)).Should().BeEmpty("la ligne supprimée ne doit plus apparaître dans le barème");
    }

    [Fact]
    public async Task A_Finance_Must_Not_Delete_A_Fee_By_Default()
    {
        var directeur = await DirecteurTokenAsync();
        await CreateClassroomAsync(directeur, "CI");
        var category = await CreateCategoryAsync(directeur, "Mensualité");
        await ApplyStandardAsync(directeur, category.Id, 15000, overwrite: false);
        var fee = (await ListFeesAsync(directeur)).Single();

        var finance = await FinanceTokenAsync();
        var response = await SendAsync(
            HttpMethod.Delete, $"/api/v1/finance/fees/{fee.Id}?rowVersion={fee.RowVersion}", finance);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ListFeesAsync(directeur)).Should().ContainSingle("la Finance non déléguée ne doit rien supprimer");
    }

    [Fact]
    public async Task A_Finance_Can_Delete_A_Fee_Once_The_Directeur_Enables_Delegation()
    {
        var directeur = await DirecteurTokenAsync();
        await CreateClassroomAsync(directeur, "CI");
        var category = await CreateCategoryAsync(directeur, "Mensualité");
        await ApplyStandardAsync(directeur, category.Id, 15000, overwrite: false);
        var fee = (await ListFeesAsync(directeur)).Single();

        await EnableFinanceDelegationAsync(directeur, allowModify: false, allowDelete: true);

        var finance = await FinanceTokenAsync();
        var response = await SendAsync(
            HttpMethod.Delete, $"/api/v1/finance/fees/{fee.Id}?rowVersion={fee.RowVersion}", finance);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ListFeesAsync(directeur)).Should().BeEmpty();
    }

    [Fact]
    public async Task Deleting_A_Fee_With_A_Stale_RowVersion_Should_Return_409()
    {
        var directeur = await DirecteurTokenAsync();
        await CreateClassroomAsync(directeur, "CI");
        var category = await CreateCategoryAsync(directeur, "Mensualité");
        await ApplyStandardAsync(directeur, category.Id, 15000, overwrite: false);
        var fee = (await ListFeesAsync(directeur)).Single();
        var staleVersion = fee.RowVersion;

        // Une modification entre-temps fait tourner le jeton xmin.
        var put = await SendAsync(HttpMethod.Put, $"/api/v1/finance/fees/{fee.Id}", directeur,
            new { amount = 16000m, rowVersion = staleVersion });
        put.StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await SendAsync(
            HttpMethod.Delete, $"/api/v1/finance/fees/{fee.Id}?rowVersion={staleVersion}", directeur);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ListFeesAsync(directeur)).Should().ContainSingle("le conflit ne doit jamais entraîner une suppression silencieuse");
    }

    [Fact]
    public async Task Deleting_An_Unknown_Fee_Should_Return_404()
    {
        var directeur = await DirecteurTokenAsync();

        var response = await SendAsync(
            HttpMethod.Delete, $"/api/v1/finance/fees/{Guid.NewGuid()}?rowVersion=1", directeur);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_Directeur_Can_Delete_A_Fee_Category_Cascading_Its_Fees()
    {
        var directeur = await DirecteurTokenAsync();
        await CreateClassroomAsync(directeur, "CI");
        await CreateClassroomAsync(directeur, "CP");
        var category = await CreateCategoryAsync(directeur, "Mensualité");
        await ApplyStandardAsync(directeur, category.Id, 15000, overwrite: false);

        var response = await SendAsync(HttpMethod.Delete, $"/api/v1/finance/fee-categories/{category.Id}", directeur);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var categories = await SendAsync(HttpMethod.Get, "/api/v1/finance/fee-categories", directeur);
        (await categories.Content.ReadFromJsonAsync<List<FeeCategoryDto>>())!
            .Should().BeEmpty("la catégorie supprimée ne doit plus apparaître");

        (await ListFeesAsync(directeur)).Should()
            .BeEmpty("ses lignes de barème doivent disparaître avec elle (cascade en transaction)");
    }

    [Fact]
    public async Task A_Finance_Must_Not_Delete_A_Fee_Category_By_Default()
    {
        var directeur = await DirecteurTokenAsync();
        var category = await CreateCategoryAsync(directeur, "Cantine");

        var finance = await FinanceTokenAsync();
        var response = await SendAsync(HttpMethod.Delete, $"/api/v1/finance/fee-categories/{category.Id}", finance);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_Finance_Can_Delete_A_Fee_Category_Once_The_Directeur_Enables_Delegation()
    {
        var directeur = await DirecteurTokenAsync();
        var category = await CreateCategoryAsync(directeur, "Cantine");
        await EnableFinanceDelegationAsync(directeur, allowModify: false, allowDelete: true);

        var finance = await FinanceTokenAsync();
        var response = await SendAsync(HttpMethod.Delete, $"/api/v1/finance/fee-categories/{category.Id}", finance);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Deleting_An_Unknown_Fee_Category_Should_Return_404()
    {
        var directeur = await DirecteurTokenAsync();

        var response = await SendAsync(
            HttpMethod.Delete, $"/api/v1/finance/fee-categories/{Guid.NewGuid()}", directeur);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
