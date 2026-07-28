using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Xunit;

namespace SamaEcole.FunctionalTests.Grades;

/// <summary>
/// Ticket JGK-G02 — mentions personnalisables du bulletin. Jusqu'ici seules la lecture et la création
/// existaient : une erreur de saisie obligeait à supprimer puis recréer TOUTE la liste (voir la note
/// d'UI "dès votre première mention ajoutée, cette liste par défaut n'est plus utilisée"). PATCH/DELETE
/// permettent de corriger une seule mention sans ce détour. Critères testés : écriture réservée au
/// Directeur (Secrétariat seulement si délégué) ; suppression LOGIQUE (AGENTS.md règle #6, jamais
/// physique) ; un libellé supprimé redevient réutilisable (index unique incluant IsDeleted).
/// </summary>
public class MentionsEndpointsTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    public Task InitializeAsync() => factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private record Tokens(string AccessToken, string RefreshToken, int ExpiresIn);
    private record MentionDto(Guid? Id, string Label, decimal MinAverage);

    private async Task<Tokens> LoginAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!;
    }

    private Task<Tokens> LoginAsDirecteurAsync() =>
        LoginAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);

    private async Task<HttpResponseMessage> SendAsync(string token, HttpMethod method, string path, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(request);
    }

    private async Task<MentionDto> CreateMentionAsync(string token, string label, decimal minAverage)
    {
        var response = await SendAsync(token, HttpMethod.Post, "/api/v1/grades/mentions", new { label, minAverage });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<MentionDto>())!;
    }

    [Fact]
    public async Task A_Directeur_Should_Update_A_Mention_Without_Losing_The_Others()
    {
        var directeur = await LoginAsDirecteurAsync();
        await CreateMentionAsync(directeur.AccessToken, "Bien", 12);
        var target = await CreateMentionAsync(directeur.AccessToken, "Moyen", 8);

        var response = await SendAsync(
            directeur.AccessToken, HttpMethod.Patch, $"/api/v1/grades/mentions/{target.Id}",
            new { label = "Moyen Corrigé", minAverage = 9 });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var list = await (await SendAsync(directeur.AccessToken, HttpMethod.Get, "/api/v1/grades/mentions"))
            .Content.ReadFromJsonAsync<List<MentionDto>>();

        list.Should().Contain(m => m.Label == "Moyen Corrigé" && m.MinAverage == 9);
        list.Should().Contain(m => m.Label == "Bien", "corriger une mention ne doit pas toucher les autres");
    }

    [Fact]
    public async Task Updating_A_Mention_To_An_Existing_Label_Should_Return_409()
    {
        var directeur = await LoginAsDirecteurAsync();
        await CreateMentionAsync(directeur.AccessToken, "Bien", 12);
        var target = await CreateMentionAsync(directeur.AccessToken, "Moyen", 8);

        var response = await SendAsync(
            directeur.AccessToken, HttpMethod.Patch, $"/api/v1/grades/mentions/{target.Id}",
            new { label = "Bien", minAverage = 8 });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_Secretariat_Without_Delegation_Must_Not_Be_Allowed_To_Update_A_Mention()
    {
        var directeur = await LoginAsDirecteurAsync();
        var target = await CreateMentionAsync(directeur.AccessToken, "Bien", 12);
        var secretaire = await LoginAsync(AuthApiFactory.SecretaireEmail, AuthApiFactory.SecretairePassword);

        var response = await SendAsync(
            secretaire.AccessToken, HttpMethod.Patch, $"/api/v1/grades/mentions/{target.Id}",
            new { label = "Bien Modifié", minAverage = 12 });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_Directeur_Should_Delete_A_Mention_Logically_Not_Physically()
    {
        var directeur = await LoginAsDirecteurAsync();
        var target = await CreateMentionAsync(directeur.AccessToken, "Éphémère", 12);

        var deleteResponse = await SendAsync(
            directeur.AccessToken, HttpMethod.Delete, $"/api/v1/grades/mentions/{target.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var list = await (await SendAsync(directeur.AccessToken, HttpMethod.Get, "/api/v1/grades/mentions"))
            .Content.ReadFromJsonAsync<List<MentionDto>>();
        list.Should().NotContain(m => m.Label == "Éphémère");
    }

    [Fact]
    public async Task Deleting_A_Mention_Should_Free_Its_Label_For_Reuse()
    {
        // AGENTS.md règle #6 (suppression logique) + MentionConfiguration : l'index unique porte sur
        // (SchoolId, Label, IsDeleted) — un libellé supprimé doit donc redevenir immédiatement utilisable.
        var directeur = await LoginAsDirecteurAsync();
        var first = await CreateMentionAsync(directeur.AccessToken, "Réutilisable", 12);

        await SendAsync(directeur.AccessToken, HttpMethod.Delete, $"/api/v1/grades/mentions/{first.Id}");

        var recreateResponse = await SendAsync(
            directeur.AccessToken, HttpMethod.Post, "/api/v1/grades/mentions",
            new { label = "Réutilisable", minAverage = 10 });

        recreateResponse.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_Secretariat_Without_Delegation_Must_Not_Be_Allowed_To_Delete_A_Mention()
    {
        var directeur = await LoginAsDirecteurAsync();
        var target = await CreateMentionAsync(directeur.AccessToken, "Bien", 12);
        var secretaire = await LoginAsync(AuthApiFactory.SecretaireEmail, AuthApiFactory.SecretairePassword);

        var response = await SendAsync(
            secretaire.AccessToken, HttpMethod.Delete, $"/api/v1/grades/mentions/{target.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Updating_A_Nonexistent_Mention_Should_Return_404()
    {
        var directeur = await LoginAsDirecteurAsync();

        var response = await SendAsync(
            directeur.AccessToken, HttpMethod.Patch, $"/api/v1/grades/mentions/{Guid.NewGuid()}",
            new { label = "Fantôme", minAverage = 10 });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
