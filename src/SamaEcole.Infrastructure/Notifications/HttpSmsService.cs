using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SamaEcole.Infrastructure.Notifications;

/// <summary>
/// Adaptateur HTTP réel vers l'agrégateur SMS (Infobip par défaut — voir <see cref="SmsOptions"/>
/// pour Orange/Twilio).
///
/// NE LÈVE JAMAIS pour un refus du fournisseur (contrat d'<see cref="ISmsService"/>) : la saisie
/// d'un retard ou l'encaissement qui a déclenché ce SMS ne doit pas échouer parce qu'un opérateur
/// est indisponible. Toute panne est convertie en <see cref="SmsSendResult"/> en échec, que
/// SmsDispatcher journalisera dans l'historique — visible par l'école, sans transaction perdue.
/// </summary>
public class HttpSmsService(
    HttpClient httpClient,
    IOptions<SmsOptions> options,
    ILogger<HttpSmsService> logger) : ISmsService
{
    private readonly SmsOptions _options = options.Value;

    public string ProviderName => _options.Provider;

    public async Task<SmsSendResult> SendAsync(SmsSendRequest request, CancellationToken cancellationToken)
    {
        var segments = SmsSegments.Count(request.Body);

        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, _options.BaseUrl)
            {
                Content = JsonContent.Create(new
                {
                    messages = new[]
                    {
                        new
                        {
                            from = _options.SenderId,
                            destinations = new[] { new { to = request.To } },
                            text = request.Body
                        }
                    }
                })
            };

            message.Headers.Authorization = new AuthenticationHeaderValue(_options.AuthScheme, _options.ApiKey);

            using var response = await httpClient.SendAsync(message, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // Le corps de la réponse est journalisé mais N'EST PAS repris tel quel dans
                // FailureReason : il peut contenir la clé d'API renvoyée en écho par certains
                // agrégateurs, et FailureReason est affiché dans l'historique côté école.
                logger.LogWarning(
                    "SMS refusé par {Provider} ({StatusCode}) pour {To}. Réponse : {Payload}",
                    ProviderName, (int)response.StatusCode, request.To, payload);

                return new SmsSendResult(
                    IsSent: false,
                    ProviderMessageId: null,
                    FailureReason: $"L'opérateur a refusé l'envoi (code {(int)response.StatusCode}).",
                    SegmentCount: segments);
            }

            return new SmsSendResult(true, ExtractMessageId(payload), null, segments);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogError(ex, "Envoi SMS impossible via {Provider} pour {To}.", ProviderName, request.To);

            return new SmsSendResult(
                IsSent: false,
                ProviderMessageId: null,
                FailureReason: "Opérateur SMS injoignable.",
                SegmentCount: segments);
        }
    }

    /// <summary>
    /// Identifiant de suivi de l'agrégateur, utile pour rapprocher une facture ou contester un envoi.
    /// Meilleur effort : sa position varie d'un fournisseur à l'autre, et un identifiant introuvable
    /// n'est pas une raison de considérer comme échoué un envoi que l'agrégateur vient d'accepter.
    /// </summary>
    private static string? ExtractMessageId(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);

            if (document.RootElement.TryGetProperty("messages", out var messages)
                && messages.ValueKind == JsonValueKind.Array
                && messages.GetArrayLength() > 0
                && messages[0].TryGetProperty("messageId", out var messageId))
            {
                return messageId.GetString();
            }

            return document.RootElement.TryGetProperty("sid", out var sid) ? sid.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
