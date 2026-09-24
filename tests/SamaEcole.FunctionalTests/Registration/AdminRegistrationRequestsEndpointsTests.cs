using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Xunit;

namespace SamaEcole.FunctionalTests.Registration;

/// <summary>
/// Ticket JGK-I03 — revue et approbation des demandes d'inscription self-service par le Super Admin.
/// Critères testés : réservé au Super Admin ; approbation atomique (établissement + Directeur +
/// abonnement AwaitingPayment, ou rien) ; un rejet n'a AUCUN effet sur Schools/Users ; motif de rejet
/// obligatoire ; le Directeur créé peut se connecter avec le mot de passe choisi à la soumission (I01).
/// </summary>
public class AdminRegistrationRequestsEndpointsTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    public Task InitializeAsync() => factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private record Tokens(string AccessToken, string RefreshToken, int ExpiresIn);
    private record SubmitResult(string TrackingReference);
    private record ListItem(
        Guid Id, string TrackingReference, string SchoolName, string DirectorEmail, string Status, Guid? CreatedSchoolId);
    private record ApprovalResult(Guid SchoolId, Guid DirectorUserId, Guid SubscriptionId, string SubscriptionStatus, bool EmailSent);
    private record SchoolSummary(Guid Id, string Name, string? Address, string? Phone, string Status);

    private const string DirectorPassword = "Correct-Horse-9";

    private async Task<Tokens> LoginAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!;
    }

    private Task<Tokens> LoginAsSuperAdminAsync() =>
        LoginAsync(AuthApiFactory.SuperAdminEmail, AuthApiFactory.SuperAdminPassword);

    private async Task<Guid> SubmitAndGetIdAsync(string schoolName, string directorEmail)
    {
        var submit = await _client.PostAsJsonAsync("/api/v1/registration-requests", new
        {
            directorFullName = "Fatou Sarr",
            directorEmail,
            directorPhone = "+221771119988",
            directorPassword = DirectorPassword,
            schoolName,
            requestedPlan = "Standard"
        });
        var reference = (await submit.Content.ReadFromJsonAsync<SubmitResult>())!.TrackingReference;

        var superAdmin = await LoginAsSuperAdminAsync();
        var list = await GetListAsync(superAdmin.AccessToken, status: null);

        return list.Single(r => r.TrackingReference == reference).Id;
    }

    private async Task<List<ListItem>> GetListAsync(string accessToken, string? status)
    {
        var url = status is null ? "/api/v1/admin/registration-requests" : $"/api/v1/admin/registration-requests?status={status}";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<List<ListItem>>())!;
    }

    private async Task<HttpResponseMessage> ApproveAsync(string accessToken, Guid id)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/registration-requests/{id}/approve");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> RejectAsync(string accessToken, Guid id, string reason)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/registration-requests/{id}/reject")
        {
            Content = JsonContent.Create(new { reason })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await _client.SendAsync(request);
    }

    // ------------------------------------------------------------ Liste

    [Fact]
    public async Task SuperAdmin_Should_List_Pending_Requests()
    {
        await SubmitAndGetIdAsync("École Liste Un", "liste1@revue.sn");
        await SubmitAndGetIdAsync("École Liste Deux", "liste2@revue.sn");

        var superAdmin = await LoginAsSuperAdminAsync();
        var pending = await GetListAsync(superAdmin.AccessToken, "Pending");

        pending.Should().Contain(r => r.SchoolName == "École Liste Un");
        pending.Should().Contain(r => r.SchoolName == "École Liste Deux");
        pending.Should().OnlyContain(r => r.Status == "Pending");
    }

    [Fact]
    public async Task A_Directeur_Must_Not_Be_Allowed_To_List_Requests()
    {
        var directeur = await LoginAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/registration-requests");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", directeur.AccessToken);
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Listing_Requests_Without_A_Token_Should_Return_401()
    {
        var response = await _client.GetAsync("/api/v1/admin/registration-requests");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ------------------------------------------------------------ Approbation

    [Fact]
    public async Task Approving_A_Request_Should_Atomically_Create_School_Director_And_Subscription()
    {
        var id = await SubmitAndGetIdAsync("École Approbation Complète", "approbation@revue.sn");
        var superAdmin = await LoginAsSuperAdminAsync();

        var response = await ApproveAsync(superAdmin.AccessToken, id);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<ApprovalResult>())!;
        result.SchoolId.Should().NotBeEmpty();
        result.DirectorUserId.Should().NotBeEmpty();
        result.SubscriptionId.Should().NotBeEmpty();
        result.SubscriptionStatus.Should().Be("AwaitingPayment",
            "aucune date d'expiration tant que le premier paiement n'est pas confirmé (Volume 1 §11.5)");
        result.EmailSent.Should().BeTrue("LoggingEmailSender (Development) ne fait jamais échouer l'envoi");

        // La demande reste un historique immuable — jamais transformée, seul son statut avance.
        var updated = (await GetListAsync(superAdmin.AccessToken, null)).Single(r => r.Id == id);
        updated.Status.Should().Be("Approved");
        updated.CreatedSchoolId.Should().Be(result.SchoolId);
    }

    [Fact]
    public async Task The_Newly_Approved_Director_Should_Be_Able_To_Login_With_The_Password_Chosen_At_Submission()
    {
        // LE test de bout en bout : il prouve toute la chaîne I01 -> I03 — mot de passe haché à la
        // soumission, transmis intact jusqu'au compte créé par l'approbation.
        const string email = "boutenbout@revue.sn";
        var id = await SubmitAndGetIdAsync("École Bout En Bout", email);
        var superAdmin = await LoginAsSuperAdminAsync();

        await ApproveAsync(superAdmin.AccessToken, id);

        var tokens = await LoginAsync(email, DirectorPassword);
        tokens.AccessToken.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task The_Approval_Email_Should_State_The_Login_Identifier_And_Never_Contain_The_Password()
    {
        // Le Directeur a choisi son mot de passe à la soumission : l'e-mail d'approbation lui rappelle son
        // IDENTIFIANT (l'adresse de la demande) mais ne transporte jamais de mot de passe.
        const string email = "identifiant@revue.sn";
        var id = await SubmitAndGetIdAsync("École Identifiant", email);
        var superAdmin = await LoginAsSuperAdminAsync();

        await ApproveAsync(superAdmin.AccessToken, id);

        var approval = factory.Emails.LastTo(email);
        approval.Should().NotBeNull();
        approval!.Subject.Should().Be("Votre inscription Unikol est validée");
        approval.Body.Should().Contain($"Identifiant de connexion : {email}");
        approval.Body.Should().NotContain(DirectorPassword);
    }

    [Fact]
    public async Task Approving_An_Already_Processed_Request_Should_Be_Rejected()
    {
        var id = await SubmitAndGetIdAsync("École Double Approbation", "double@revue.sn");
        var superAdmin = await LoginAsSuperAdminAsync();

        await ApproveAsync(superAdmin.AccessToken, id);
        var second = await ApproveAsync(superAdmin.AccessToken, id);

        second.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Approving_An_Unknown_Request_Should_Return_404()
    {
        var superAdmin = await LoginAsSuperAdminAsync();

        var response = await ApproveAsync(superAdmin.AccessToken, Guid.NewGuid());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_Directeur_Must_Not_Be_Allowed_To_Approve_A_Request()
    {
        var id = await SubmitAndGetIdAsync("École Approbation Interdite", "interdite@revue.sn");
        var directeur = await LoginAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);

        var response = await ApproveAsync(directeur.AccessToken, id);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// ATOMICITÉ (critère explicite du ticket) : deux demandes distinctes partageant le même e-mail de
    /// Directeur (autorisé à la soumission, I01) sont approuvées EN PARALLÈLE. La contrainte d'unicité
    /// de `users.Email` ne peut laisser passer qu'une seule création — l'autre doit échouer ENTIÈREMENT
    /// (aucune école, aucun Directeur, aucun abonnement orphelins), pas s'arrêter à mi-chemin.
    /// </summary>
    [Fact]
    public async Task Concurrently_Approving_Two_Requests_With_The_Same_Director_Email_Should_Create_Exactly_One_School()
    {
        const string sharedEmail = "concurrence@atomicite.sn";
        var idA = await SubmitAndGetIdAsync("École Concurrente A", sharedEmail);
        var idB = await SubmitAndGetIdAsync("École Concurrente B", sharedEmail);
        var superAdmin = await LoginAsSuperAdminAsync();

        var results = await Task.WhenAll(
            ApproveAsync(superAdmin.AccessToken, idA),
            ApproveAsync(superAdmin.AccessToken, idB));

        var succeeded = results.Count(r => r.StatusCode == HttpStatusCode.OK);
        succeeded.Should().Be(1, "l'unicité de l'e-mail Directeur ne peut laisser passer qu'UNE seule création complète");

        // La demande dont l'approbation a échoué doit être intacte : ni école, ni statut modifié — la
        // transaction a tout annulé, pas seulement l'insertion en échec.
        var list = await GetListAsync(superAdmin.AccessToken, null);
        var itemA = list.Single(r => r.Id == idA);
        var itemB = list.Single(r => r.Id == idB);

        var approvedCount = new[] { itemA, itemB }.Count(r => r.Status == "Approved");
        var pendingCount = new[] { itemA, itemB }.Count(r => r.Status == "Pending");
        approvedCount.Should().Be(1);
        pendingCount.Should().Be(1, "la demande dont la transaction a échoué doit rester Pending, réapprouvable");

        // Aucune école fantôme : exactement UNE des deux écoles candidates existe réellement.
        var schoolsRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/schools");
        schoolsRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", superAdmin.AccessToken);
        var schoolsResponse = await _client.SendAsync(schoolsRequest);
        var schools = (await schoolsResponse.Content.ReadFromJsonAsync<List<SchoolSummary>>())!;

        var createdCount = schools.Count(s => s.Name == "École Concurrente A" || s.Name == "École Concurrente B");
        createdCount.Should().Be(1, "la demande dont la création a échoué ne doit laisser AUCUNE école orpheline");
    }

    /// <summary>
    /// Audit sécurité — LE scénario du bug : deux demandes dont l'e-mail de Directeur ne diffère QUE
    /// par la casse (« casse@ecole.sn » / « CASSE@ECOLE.SN »). Elles pouvaient toutes deux être
    /// approuvées car l'index unique de users.Email était un btree sensible à la casse. Colonne citext
    /// + normalisation : la seconde approbation est refusée (422) et ne crée AUCUNE école.
    /// </summary>
    [Fact]
    public async Task Approving_A_Second_Request_Whose_Email_Differs_Only_By_Case_Is_Rejected()
    {
        var idLower = await SubmitAndGetIdAsync("École Casse Minuscule", "casse.approbation@ecole.sn");
        var idUpper = await SubmitAndGetIdAsync("École Casse Majuscule", "CASSE.APPROBATION@ECOLE.SN");
        var superAdmin = await LoginAsSuperAdminAsync();

        var firstApproval = await ApproveAsync(superAdmin.AccessToken, idLower);
        firstApproval.StatusCode.Should().Be(HttpStatusCode.OK);

        var secondApproval = await ApproveAsync(superAdmin.AccessToken, idUpper);
        secondApproval.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "un e-mail n'identifie qu'un compte sur toute la plateforme, casse comprise");

        // La seconde demande reste Pending (réapprouvable après résolution), et aucune école majuscule.
        var list = await GetListAsync(superAdmin.AccessToken, null);
        list.Single(r => r.Id == idUpper).Status.Should().Be("Pending");

        var schoolsRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/schools");
        schoolsRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", superAdmin.AccessToken);
        var schools = (await (await _client.SendAsync(schoolsRequest)).Content.ReadFromJsonAsync<List<SchoolSummary>>())!;
        schools.Should().NotContain(s => s.Name == "École Casse Majuscule");
    }

    // ------------------------------------------------------------ Rejet

    [Fact]
    public async Task Rejecting_A_Request_Should_Not_Affect_Schools_Or_Users()
    {
        var superAdminBefore = await LoginAsSuperAdminAsync();
        var schoolsBefore = await GetSchoolCountAsync(superAdminBefore.AccessToken);

        var id = await SubmitAndGetIdAsync("École Rejetée", "rejet@revue.sn");
        var response = await RejectAsync(superAdminBefore.AccessToken, id, "Établissement non identifiable.");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var schoolsAfter = await GetSchoolCountAsync(superAdminBefore.AccessToken);
        schoolsAfter.Should().Be(schoolsBefore, "un rejet ne doit créer AUCUNE école (critère du ticket)");

        var updated = (await GetListAsync(superAdminBefore.AccessToken, null)).Single(r => r.Id == id);
        updated.Status.Should().Be("Rejected");
        updated.CreatedSchoolId.Should().BeNull();
    }

    private async Task<int> GetSchoolCountAsync(string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/schools");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var response = await _client.SendAsync(request);
        var schools = (await response.Content.ReadFromJsonAsync<List<SchoolSummary>>())!;
        return schools.Count;
    }

    [Fact]
    public async Task Rejecting_Without_A_Reason_Should_Return_422()
    {
        var id = await SubmitAndGetIdAsync("École Sans Motif", "sansmotif@revue.sn");
        var superAdmin = await LoginAsSuperAdminAsync();

        var response = await RejectAsync(superAdmin.AccessToken, id, "");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task The_Rejection_Reason_Should_Be_Visible_Through_The_Public_Tracking_Endpoint()
    {
        var submit = await _client.PostAsJsonAsync("/api/v1/registration-requests", new
        {
            directorFullName = "Fatou Sarr",
            directorEmail = "motifpublic@revue.sn",
            directorPhone = "+221771119988",
            directorPassword = DirectorPassword,
            schoolName = "École Motif Public",
            requestedPlan = "Standard"
        });
        var reference = (await submit.Content.ReadFromJsonAsync<SubmitResult>())!.TrackingReference;

        var superAdmin = await LoginAsSuperAdminAsync();
        var id = (await GetListAsync(superAdmin.AccessToken, null)).Single(r => r.TrackingReference == reference).Id;
        await RejectAsync(superAdmin.AccessToken, id, "Dossier incomplet : adresse manquante.");

        var statusResponse = await _client.GetAsync($"/api/v1/registration-requests/{reference}/status");
        var body = await statusResponse.Content.ReadAsStringAsync();

        body.Should().Contain("Dossier incomplet");
    }

    [Fact]
    public async Task A_Directeur_Must_Not_Be_Allowed_To_Reject_A_Request()
    {
        var id = await SubmitAndGetIdAsync("École Rejet Interdit", "rejetinterdit@revue.sn");
        var directeur = await LoginAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);

        var response = await RejectAsync(directeur.AccessToken, id, "Motif quelconque.");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
