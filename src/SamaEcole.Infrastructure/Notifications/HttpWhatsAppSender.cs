using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SamaEcole.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SamaEcole.Infrastructure.Notifications;

/// <summary>
/// Adaptateur HTTP réel vers l'API WhatsApp Cloud — remplace <see cref="LoggingWhatsAppSender"/> dès
/// que la section « WhatsApp » est configurée.
///
/// Une pièce jointe part en DEUX temps, comme l'impose l'API : on téléverse d'abord le fichier
/// (/media), qui renvoie un identifiant, puis on envoie un message qui le référence. Impossible de
/// joindre un PDF en une seule requête — d'où ce détour pour les bulletins.
///
/// NE LÈVE JAMAIS, même contrat qu'<see cref="HttpSmsService"/> : l'envoi d'un bulletin ne doit pas
/// échouer parce que Meta est indisponible. Les pannes sont journalisées, pas propagées.
/// </summary>
public class HttpWhatsAppSender(
    HttpClient httpClient,
    IOptions<WhatsAppOptions> options,
    ILogger<HttpWhatsAppSender> logger) : IWhatsAppSender
{
    private readonly WhatsAppOptions _options = options.Value;

    public async Task SendAsync(WhatsAppMessage message, CancellationToken cancellationToken)
    {
        try
        {
            await SendTextAsync(message.To, message.Body, cancellationToken);

            if (message.Attachments is not { Count: > 0 } attachments)
            {
                return;
            }

            if (!_options.IsMediaConfigured)
            {
                // Le texte est parti, la pièce jointe non : on le DIT. Rester silencieux laisserait
                // croire que le bulletin a été transmis.
                logger.LogWarning(
                    "WhatsApp : {Count} pièce(s) jointe(s) non envoyée(s) à {To} — l'endpoint /media n'est pas configuré.",
                    attachments.Count, message.To);
                return;
            }

            foreach (var attachment in attachments)
            {
                var mediaId = await UploadAsync(attachment, cancellationToken);

                if (mediaId is null)
                {
                    continue;
                }

                await SendDocumentAsync(message.To, mediaId, attachment.Filename, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogError(ex, "Envoi WhatsApp impossible vers {To}.", message.To);
        }
    }

    private async Task SendTextAsync(string to, string body, CancellationToken cancellationToken)
    {
        await PostJsonAsync(
            _options.BaseUrl,
            new
            {
                messaging_product = "whatsapp",
                to,
                type = "text",
                text = new { body }
            },
            cancellationToken);
    }

    private async Task SendDocumentAsync(
        string to, string mediaId, string filename, CancellationToken cancellationToken)
    {
        await PostJsonAsync(
            _options.BaseUrl,
            new
            {
                messaging_product = "whatsapp",
                to,
                type = "document",
                document = new { id = mediaId, filename }
            },
            cancellationToken);
    }

    /// <summary>Renvoie l'identifiant de média, ou null si le téléversement a échoué.</summary>
    private async Task<string?> UploadAsync(WhatsAppAttachment attachment, CancellationToken cancellationToken)
    {
        using var content = new MultipartFormDataContent
        {
            { new StringContent("whatsapp"), "messaging_product" },
            { new StringContent(attachment.ContentType), "type" }
        };

        var fileContent = new ByteArrayContent(attachment.Content);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(attachment.ContentType);
        content.Add(fileContent, "file", attachment.Filename);

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.MediaUrl) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AccessToken);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Téléversement WhatsApp refusé ({StatusCode}) pour {Filename}.",
                (int)response.StatusCode, attachment.Filename);
            return null;
        }

        using var document = JsonDocument.Parse(payload);

        return document.RootElement.TryGetProperty("id", out var id) ? id.GetString() : null;
    }

    private async Task PostJsonAsync(string url, object payload, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(payload)
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AccessToken);

        using var response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // Le corps de la réponse n'est PAS repris dans le journal applicatif visible par l'école :
            // Meta y renvoie parfois des fragments du jeton d'accès.
            logger.LogWarning(
                "Message WhatsApp refusé par Meta ({StatusCode}).", (int)response.StatusCode);
        }
    }
}
