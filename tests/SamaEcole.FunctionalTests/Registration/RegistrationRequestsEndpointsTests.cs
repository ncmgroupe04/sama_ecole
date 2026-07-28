using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Xunit;

namespace SamaEcole.FunctionalTests.Registration;

/// <summary>
/// Ticket JGK-I01 — formulaire PUBLIC d'inscription self-service. Critères testés : le mot de passe
/// n'apparaît nulle part (réponse HTTP, e-mail) sauf haché en base ; deux soumissions avec le même
/// e-mail sont autorisées ; chaque référence de suivi est unique ; le honeypot bloque silencieusement
/// une soumission robotisée ; la validation rejette une demande mal formée.
/// </summary>
public class RegistrationRequestsEndpointsTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    public Task InitializeAsync() => factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private record RegistrationResult(string TrackingReference);

    private static object ValidPayload(string schoolName, string directorEmail, string? website = null) => new
    {
        directorFullName = "Awa Ndiaye",
        directorEmail,
        directorPhone = "+221771234567",
        directorPassword = "Correct-Horse-9",
        schoolName,
        schoolAddress = "Rue 12, Médina, Dakar",
        city = "Dakar",
        region = "Dakar",
        estimatedStudentCount = 250,
        requestedPlan = "Standard",
        website
    };

    [Fact]
    public async Task Submitting_A_Valid_Request_Should_Return_A_Tracking_Reference()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/v1/registration-requests", ValidPayload("École Les Baobabs", "directeur@baobabs.sn"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = (await response.Content.ReadFromJsonAsync<RegistrationResult>())!;
        result.TrackingReference.Should().StartWith("REG-");
    }

    [Fact]
    public async Task No_Authentication_Should_Be_Required()
    {
        // Le point d'entrée précède l'existence de tout compte : aucun en-tête Authorization envoyé ici.
        var response = await _client.PostAsJsonAsync(
            "/api/v1/registration-requests", ValidPayload("École Sans Auth", "sansauth@baobabs.sn"));

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_Password_Must_Never_Appear_In_The_Http_Response()
    {
        const string password = "Correct-Horse-9";

        var response = await _client.PostAsJsonAsync("/api/v1/registration-requests", new
        {
            directorFullName = "Awa Ndiaye",
            directorEmail = "directeur@confidentielle.sn",
            directorPhone = "+221771234567",
            directorPassword = password,
            schoolName = "École Confidentielle",
            requestedPlan = "Standard"
        });

        var body = await response.Content.ReadAsStringAsync();

        body.Should().NotContain(password);
    }

    [Fact]
    public async Task The_Confirmation_Email_Should_Contain_The_Tracking_Reference_But_Never_The_Password()
    {
        const string password = "Correct-Horse-9";
        const string email = "directeur@confirmation.sn";

        var response = await _client.PostAsJsonAsync(
            "/api/v1/registration-requests", ValidPayload("École Confirmation", email));

        var result = (await response.Content.ReadFromJsonAsync<RegistrationResult>())!;

        var sentEmail = factory.Emails.LastTo(email);
        sentEmail.Should().NotBeNull("une référence de suivi doit être communiquée par e-mail (Volume 1 §11.5)");
        sentEmail!.Body.Should().Contain(result.TrackingReference);
        sentEmail.Body.Should().NotContain(password);
    }

    [Fact]
    public async Task Two_Submissions_With_The_Same_Email_Should_Both_Be_Accepted()
    {
        // Critère explicite du ticket : « une école peut retenter ».
        const string email = "retente@baobabs.sn";

        var first = await _client.PostAsJsonAsync(
            "/api/v1/registration-requests", ValidPayload("École Première Tentative", email));
        var second = await _client.PostAsJsonAsync(
            "/api/v1/registration-requests", ValidPayload("École Seconde Tentative", email));

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);

        var firstResult = (await first.Content.ReadFromJsonAsync<RegistrationResult>())!;
        var secondResult = (await second.Content.ReadFromJsonAsync<RegistrationResult>())!;

        firstResult.TrackingReference.Should().NotBe(secondResult.TrackingReference,
            "chaque référence de suivi doit rester unique même pour deux demandes du même e-mail");
    }

    [Fact]
    public async Task A_Filled_Honeypot_Should_Be_Silently_Ignored()
    {
        const string email = "robot@spam.sn";
        var beforeCount = factory.Emails.Sent.Count;

        var response = await _client.PostAsJsonAsync(
            "/api/v1/registration-requests", ValidPayload("École Robot", email, website: "https://spam.example"));

        // Même statut, même forme de réponse qu'un succès réel (aucun signal exploitable pour un bot) —
        // mais rien n'a été enregistré ni envoyé.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<RegistrationResult>())!;
        result.TrackingReference.Should().StartWith("REG-");

        factory.Emails.Sent.Should().HaveCount(beforeCount, "le honeypot doit empêcher tout envoi d'e-mail");
        factory.Emails.LastTo(email).Should().BeNull();
    }

    [Fact]
    public async Task A_Weak_Password_Should_Be_Rejected()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/v1/registration-requests", new
            {
                directorFullName = "Awa Ndiaye",
                directorEmail = "faible@baobabs.sn",
                directorPhone = "+221771234567",
                directorPassword = "1234",
                schoolName = "École Faible",
                requestedPlan = "Standard"
            });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task An_Invalid_Director_Email_Should_Be_Rejected()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/v1/registration-requests", ValidPayload("École Email Invalide", "pas-un-email"));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }
}
