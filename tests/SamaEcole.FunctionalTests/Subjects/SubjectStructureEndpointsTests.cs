using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace SamaEcole.FunctionalTests.Subjects;

/// <summary>
/// Structure d'évaluation hiérarchique (grilles APC du primaire) contre un VRAI PostgreSQL, à travers
/// la vraie pile HTTP.
///
/// Le vrai moteur n'est pas un luxe ici : la moitié de ces règles sont tenues par un index unique dont
/// le comportement dépend de `NULLS NOT DISTINCT` (PostgreSQL 15+). Un test en mémoire dirait
/// exactement le contraire de la production sur les deux cas qui comptent — deux « Ressources » sous
/// deux domaines différents (autorisé) et deux « Maths » de premier niveau (refusé).
/// </summary>
public class SubjectStructureEndpointsTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public SubjectStructureEndpointsTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public Task InitializeAsync() => _factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private record Tokens(string AccessToken, int ExpiresIn);

    private record SubjectDto(
        Guid Id, string Name, string Level, decimal Coefficient, uint RowVersion,
        Guid? ParentSubjectId, decimal? MaxScore, int DisplayOrder,
        string? Column1Header, string? Column2Header);

    private async Task<string> DirecteurTokenAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = AuthApiFactory.DirecteurEmail, password = AuthApiFactory.DirecteurPassword });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!.AccessToken;
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await _client.SendAsync(request);
    }

    /// <summary>Chaque test travaille sur SON niveau : la base est partagée entre les tests de la classe.</summary>
    private static string UniqueLevel(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..24];

    private async Task<SubjectDto> CreateAsync(string token, object body)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/v1/subjects", token, body);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<SubjectDto>())!;
    }

    private async Task<List<SubjectDto>> ListAsync(string token)
    {
        var response = await SendAsync(HttpMethod.Get, "/api/v1/subjects", token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<List<SubjectDto>>())!;
    }

    /// <summary>
    /// Le cas que l'ancien index unique (SchoolId, Level, Name, IsDeleted) RENDAIT IMPOSSIBLE : la grille
    /// du CE1-CE2 porte « Ressources » sous Français ET sous Maths. Sans ParentSubjectId dans la clé, la
    /// seconde ligne était rejetée comme un doublon, et la grille officielle inexprimable.
    /// </summary>
    [Fact]
    public async Task The_Same_Activity_Name_Under_Two_Different_Domains_Should_Be_Allowed()
    {
        var token = await DirecteurTokenAsync();
        var level = UniqueLevel("CE1");

        var francais = await CreateAsync(token, new { name = "Français", level, coefficient = 4 });
        var maths = await CreateAsync(token, new { name = "Maths", level, coefficient = 4 });

        await CreateAsync(token, new { name = "Ressources", level, coefficient = 4, parentSubjectId = francais.Id, maxScore = 40 });

        var response = await SendAsync(HttpMethod.Post, "/api/v1/subjects", token,
            new { name = "Ressources", level, coefficient = 4, parentSubjectId = maths.Id, maxScore = 40 });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    /// <summary>
    /// Le REVERS du test précédent, et la raison d'être de `NULLS NOT DISTINCT` : ajouter
    /// ParentSubjectId à l'index ne doit PAS avoir relâché l'unicité des matières de premier niveau,
    /// dont ce champ est NULL. PostgreSQL les aurait toutes considérées distinctes par défaut.
    /// </summary>
    [Fact]
    public async Task Two_Top_Level_Subjects_With_The_Same_Name_Should_Still_Return_409()
    {
        var token = await DirecteurTokenAsync();
        var level = UniqueLevel("CM1");

        await CreateAsync(token, new { name = "Maths", level, coefficient = 4 });

        var duplicate = await SendAsync(HttpMethod.Post, "/api/v1/subjects", token,
            new { name = "Maths", level, coefficient = 5 });

        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>
    /// Deux niveaux, pas trois : le bulletin n'imprime qu'une colonne de regroupement, dont le RowSpan se
    /// calcule sur les activités d'un domaine. Une troisième profondeur n'aurait aucune colonne où
    /// s'afficher — elle est refusée en 422, sur le champ fautif, pas en violation de contrainte.
    /// </summary>
    [Fact]
    public async Task Attaching_An_Activity_To_Another_Activity_Should_Return_422()
    {
        var token = await DirecteurTokenAsync();
        var level = UniqueLevel("CI");

        var domain = await CreateAsync(token, new { name = "Lang & Com.", level, coefficient = 4 });
        var activity = await CreateAsync(token,
            new { name = "P. Alphabétique", level, coefficient = 4, parentSubjectId = domain.Id, maxScore = 10 });

        var response = await SendAsync(HttpMethod.Post, "/api/v1/subjects", token,
            new { name = "Sous-sous-activité", level, coefficient = 4, parentSubjectId = activity.Id });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>
    /// Une activité vit TOUJOURS au niveau de son domaine : le niveau envoyé par le client est ignoré au
    /// profit de celui du parent. Une activité égarée à un autre niveau serait simplement invisible sur
    /// le bulletin, sans le moindre message.
    /// </summary>
    [Fact]
    public async Task An_Activity_Inherits_The_Level_Of_Its_Domain()
    {
        var token = await DirecteurTokenAsync();
        var domainLevel = UniqueLevel("CP");

        var domain = await CreateAsync(token, new { name = "DDM", level = domainLevel, coefficient = 2 });

        var activity = await CreateAsync(token, new
        {
            name = "Histoire",
            level = "Un niveau qui n'est pas celui du domaine",
            coefficient = 2,
            parentSubjectId = domain.Id
        });

        activity.Level.Should().Be(domainLevel);
    }

    /// <summary>
    /// Un domaine désigné dans une AUTRE école est structurellement introuvable (Global Query Filter +
    /// policy RLS) : le rattachement est refusé sur le champ, jamais accepté en silence.
    /// </summary>
    [Fact]
    public async Task Attaching_To_An_Unknown_Domain_Should_Return_422()
    {
        var token = await DirecteurTokenAsync();

        var response = await SendAsync(HttpMethod.Post, "/api/v1/subjects", token,
            new { name = "Orpheline", level = UniqueLevel("CE2"), coefficient = 1, parentSubjectId = Guid.NewGuid() });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>
    /// Archiver un domaine qui porte encore des activités les laisserait orphelines — rattachées à un
    /// domaine archivé, donc absentes du bulletin sans le moindre message. Refusé en 409, avec un
    /// message qui dit quoi faire.
    /// </summary>
    [Fact]
    public async Task Deleting_A_Domain_That_Still_Has_Activities_Should_Return_409()
    {
        var token = await DirecteurTokenAsync();
        var level = UniqueLevel("CM2");

        var domain = await CreateAsync(token, new { name = "EDD", level, coefficient = 2 });
        await CreateAsync(token, new { name = "V. Ensemble", level, coefficient = 2, parentSubjectId = domain.Id, maxScore = 16 });

        var stored = (await ListAsync(token)).Single(s => s.Id == domain.Id);

        var response = await SendAsync(
            HttpMethod.Delete, $"/api/v1/subjects/{domain.Id}?rowVersion={stored.RowVersion}", token);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>
    /// Le barème par ligne, l'ordre d'affichage et les entêtes de colonnes font l'aller-retour complet :
    /// c'est exactement ce que le bulletin PDF relira pour composer son tableau.
    /// </summary>
    [Fact]
    public async Task Max_Score_Display_Order_And_Column_Headers_Round_Trip()
    {
        var token = await DirecteurTokenAsync();
        var level = UniqueLevel("CE2");

        var domain = await CreateAsync(token, new
        {
            name = "Français",
            level,
            coefficient = 4,
            displayOrder = 1,
            column1Header = "Activités",
            column2Header = "Contrôles"
        });

        await CreateAsync(token, new
        {
            name = "Compétences", level, coefficient = 4,
            parentSubjectId = domain.Id, maxScore = 60, displayOrder = 2
        });

        var stored = await ListAsync(token);

        var storedDomain = stored.Single(s => s.Id == domain.Id);
        storedDomain.Column1Header.Should().Be("Activités");
        storedDomain.Column2Header.Should().Be("Contrôles");
        storedDomain.MaxScore.Should().BeNull("un domaine sans barème propre suit celui du cycle");

        var storedActivity = stored.Single(s => s.ParentSubjectId == domain.Id && s.Name == "Compétences");
        storedActivity.MaxScore.Should().Be(60m);
        storedActivity.DisplayOrder.Should().Be(2);
        storedActivity.Level.Should().Be(level);
    }

    /// <summary>
    /// Les entêtes de colonnes qualifient la GRILLE, pas une ligne : une activité n'en porte aucun, même
    /// si le client en envoie — sans quoi deux lignes du même tableau pourraient en nommer les colonnes
    /// différemment, et le bulletin devrait arbitrer.
    /// </summary>
    [Fact]
    public async Task An_Activity_Never_Carries_Column_Headers()
    {
        var token = await DirecteurTokenAsync();
        var level = UniqueLevel("CI2");

        var domain = await CreateAsync(token, new { name = "EPSA", level, coefficient = 1 });

        var activity = await CreateAsync(token, new
        {
            name = "Dessin", level, coefficient = 1, parentSubjectId = domain.Id, maxScore = 20,
            column1Header = "Tentative", column2Header = "Tentative"
        });

        var stored = (await ListAsync(token)).Single(s => s.Id == activity.Id);
        stored.Column1Header.Should().BeNull();
        stored.Column2Header.Should().BeNull();
    }

    /// <summary>
    /// Modifier une matière NE DOIT PAS effacer sa structure. Le PUT remplace la ressource entière : un
    /// client qui renvoie ses champs au complet — ce que fait l'écran — conserve le rattachement et le
    /// barème. C'est le contrat sur lequel repose la réorganisation par flèches.
    /// </summary>
    [Fact]
    public async Task Renaming_An_Activity_Preserves_Its_Domain_And_Max_Score()
    {
        var token = await DirecteurTokenAsync();
        var level = UniqueLevel("CM3");

        var domain = await CreateAsync(token, new { name = "Maths", level, coefficient = 4 });
        var activity = await CreateAsync(token,
            new { name = "Ressources", level, coefficient = 4, parentSubjectId = domain.Id, maxScore = 40, displayOrder = 1 });

        var stored = (await ListAsync(token)).Single(s => s.Id == activity.Id);

        var response = await SendAsync(HttpMethod.Put, $"/api/v1/subjects/{activity.Id}", token, new
        {
            name = "Ressources (corrigé)",
            level = stored.Level,
            coefficient = stored.Coefficient,
            rowVersion = stored.RowVersion,
            parentSubjectId = stored.ParentSubjectId,
            maxScore = stored.MaxScore,
            displayOrder = stored.DisplayOrder
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = (await ListAsync(token)).Single(s => s.Id == activity.Id);
        updated.Name.Should().Be("Ressources (corrigé)");
        updated.ParentSubjectId.Should().Be(domain.Id);
        updated.MaxScore.Should().Be(40m);
        updated.DisplayOrder.Should().Be(1);
    }

    /// <summary>
    /// Déplacer un DOMAINE d'un niveau à l'autre emmène ses activités avec lui : les laisser derrière les
    /// rendrait invisibles sur les DEUX bulletins — celui de l'ancien niveau n'a plus leur domaine pour
    /// les regrouper, celui du nouveau ne les voit pas.
    /// </summary>
    [Fact]
    public async Task Moving_A_Domain_To_Another_Level_Carries_Its_Activities_Along()
    {
        var token = await DirecteurTokenAsync();
        var level = UniqueLevel("CI3");
        var newLevel = UniqueLevel("CI4");

        var domain = await CreateAsync(token, new { name = "Lang Etrangeres", level, coefficient = 2 });
        var activity = await CreateAsync(token,
            new { name = "Arabe", level, coefficient = 2, parentSubjectId = domain.Id, maxScore = 20 });

        var stored = (await ListAsync(token)).Single(s => s.Id == domain.Id);

        var response = await SendAsync(HttpMethod.Put, $"/api/v1/subjects/{domain.Id}", token, new
        {
            name = stored.Name,
            level = newLevel,
            coefficient = stored.Coefficient,
            rowVersion = stored.RowVersion
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var all = await ListAsync(token);
        all.Single(s => s.Id == domain.Id).Level.Should().Be(newLevel);
        all.Single(s => s.Id == activity.Id).Level.Should().Be(newLevel);
    }

    /// <summary>Barème hors bornes : erreur de saisie sur le champ (422), jamais une erreur de base.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    [InlineData(101)]
    public async Task An_Out_Of_Range_Max_Score_Should_Return_422(int maxScore)
    {
        var token = await DirecteurTokenAsync();

        var response = await SendAsync(HttpMethod.Post, "/api/v1/subjects", token,
            new { name = "Barème absurde", level = UniqueLevel("CP2"), coefficient = 1, maxScore });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }
}
