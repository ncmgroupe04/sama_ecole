using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SamaEcole.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SamaEcole.Infrastructure.Notifications;

/// <summary>
/// Adaptateur HTTP réel vers l'API WhatsApp Cloud de Meta — remplace <see cref="LoggingWhatsAppSender"/>
/// dès que la section « WhatsApp » est configurée.
///
/// Une pièce jointe part en DEUX temps, comme l'impose l'API : on téléverse d'abord le fichier
/// (/media), qui renvoie un identifiant, puis on envoie un message (ou un modèle) qui le référence.
///
/// NE LÈVE PAS : l'envoi d'un bulletin ne doit pas planter parce que Meta est indisponible. MAIS,
/// contrairement à la version précédente, il ne fait plus SEMBLANT de réussir : chaque échec — rejet
/// 4xx/5xx de Meta, jeton absent/expiré, /media non configuré, API injoignable — est journalisé en
/// <c>ERROR</c> avec le code d'erreur graph.facebook.com, ET renvoyé dans un
/// <see cref="WhatsAppSendResult"/> que l'appelant peut promouvoir en erreur visible
/// (<see cref="SamaEcole.Application.Common.Exceptions.WhatsAppDeliveryException"/> côté envoi manuel).
///
/// Le corps brut d'une réponse d'erreur de Meta n'est JAMAIS journalisé tel quel (il contient parfois
/// des fragments du jeton d'accès) : seuls les champs connus de l'objet <c>error</c> le sont.
/// </summary>
public class HttpWhatsAppSender(
    HttpClient httpClient,
    IOptions<WhatsAppOptions> options,
    ILogger<HttpWhatsAppSender> logger) : IWhatsAppSender
{
    private readonly WhatsAppOptions _options = options.Value;

    public async Task<WhatsAppSendResult> SendAsync(WhatsAppMessage message, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.AccessToken) || _options.AccessToken == "REMPLACER")
        {
            logger.LogError(
                "WhatsApp : envoi vers {To} impossible — WhatsApp__AccessToken absent ou non renseigné.", message.To);
            return WhatsAppSendResult.Failed(
                "Jeton d'accès WhatsApp absent (WhatsApp__AccessToken) : impossible d'authentifier l'appel à Meta.");
        }

        try
        {
            return message.Template is { } template && _options.IsReportCardTemplateConfigured
                ? await SendTemplateAsync(message.To, template, cancellationToken)
                : await SendFreeFormAsync(message, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogError(ex, "WhatsApp : API Meta injoignable lors de l'envoi vers {To}.", message.To);
            return WhatsAppSendResult.Failed(
                "L'API WhatsApp de Meta est injoignable (délai dépassé ou erreur réseau). Réessayez dans un moment.");
        }
    }

    /// <summary>
    /// Texte libre + pièces jointes éventuelles. Ne franchit la fenêtre de service de 24 h de Meta que
    /// si le tuteur a écrit récemment au numéro — sinon Meta rejette (code 131047) et le résultat le
    /// dit, en orientant vers la configuration d'un modèle.
    /// </summary>
    private async Task<WhatsAppSendResult> SendFreeFormAsync(
        WhatsAppMessage message, CancellationToken cancellationToken)
    {
        var textResult = await PostMessageAsync(
            new
            {
                messaging_product = "whatsapp",
                to = message.To,
                type = "text",
                text = new { body = message.Body }
            },
            "message texte",
            cancellationToken);

        if (!textResult.IsSent || message.Attachments is not { Count: > 0 } attachments)
        {
            return textResult;
        }

        if (!_options.IsMediaConfigured)
        {
            logger.LogError(
                "WhatsApp : {Count} pièce(s) jointe(s) non envoyée(s) à {To} — WhatsApp__MediaUrl n'est pas configuré.",
                attachments.Count, message.To);
            return WhatsAppSendResult.Failed(
                "Le bulletin n'a pas pu être joint : WhatsApp__MediaUrl n'est pas configuré "
                + "(l'API WhatsApp exige le dépôt du fichier avant l'envoi du message).");
        }

        foreach (var attachment in attachments)
        {
            var (mediaId, uploadFailure) = await UploadAsync(attachment, cancellationToken);
            if (mediaId is null)
            {
                return uploadFailure!;
            }

            var documentResult = await PostMessageAsync(
                new
                {
                    messaging_product = "whatsapp",
                    to = message.To,
                    type = "document",
                    document = new { id = mediaId, filename = attachment.Filename }
                },
                "document",
                cancellationToken);

            if (!documentResult.IsSent)
            {
                return documentResult;
            }
        }

        return WhatsAppSendResult.Sent;
    }

    /// <summary>
    /// Envoi par modèle Meta approuvé (<see cref="WhatsAppOptions.ReportCardTemplateName"/>) : le seul
    /// moyen d'atteindre un tuteur qui n'a pas écrit au numéro dans les 24 h. En-tête = bulletin PDF,
    /// corps = paramètres positionnels ({{1}} nom de l'élève, {{2}} période).
    /// </summary>
    private async Task<WhatsAppSendResult> SendTemplateAsync(
        string to, WhatsAppTemplateContent template, CancellationToken cancellationToken)
    {
        var components = new List<object>();

        if (template.HeaderDocument is { } headerDocument)
        {
            if (!_options.IsMediaConfigured)
            {
                logger.LogError(
                    "WhatsApp : modèle {Template} vers {To} sans WhatsApp__MediaUrl — le bulletin ne peut pas être joint.",
                    _options.ReportCardTemplateName, to);
                return WhatsAppSendResult.Failed(
                    "Le modèle WhatsApp exige WhatsApp__MediaUrl pour joindre le bulletin en en-tête du message.");
            }

            var (mediaId, uploadFailure) = await UploadAsync(headerDocument, cancellationToken);
            if (mediaId is null)
            {
                return uploadFailure!;
            }

            components.Add(new
            {
                type = "header",
                parameters = new object[]
                {
                    new { type = "document", document = new { id = mediaId, filename = headerDocument.Filename } }
                }
            });
        }

        if (template.BodyParameters is { Count: > 0 })
        {
            components.Add(new
            {
                type = "body",
                parameters = template.BodyParameters
                    .Select(parameter => new { type = "text", text = parameter })
                    .ToArray()
            });
        }

        var payload = new
        {
            messaging_product = "whatsapp",
            to,
            type = "template",
            template = new
            {
                name = _options.ReportCardTemplateName,
                language = new { code = _options.TemplateLanguageCode },
                components = components.ToArray()
            }
        };

        return await PostMessageAsync(payload, $"modèle « {_options.ReportCardTemplateName} »", cancellationToken);
    }

    private async Task<WhatsAppSendResult> PostMessageAsync(
        object payload, string what, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _options.BaseUrl)
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AccessToken);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return WhatsAppSendResult.Sent;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var meta = MetaError.Parse(body);
        var httpStatus = (int)response.StatusCode;

        logger.LogError(
            "WhatsApp : {What} refusé par Meta. HTTP {StatusCode}, code {MetaCode}, sous-code {MetaSubcode}, "
            + "type {MetaType}, message « {MetaMessage} », détail « {MetaDetails} », trace {FbTraceId}.",
            what, httpStatus, meta.Code, meta.Subcode, meta.Type, meta.Message, meta.Details, meta.FbTraceId);

        return WhatsAppSendResult.Failed(DescribeFailure(meta, httpStatus), meta.Code, httpStatus);
    }

    /// <summary>Renvoie l'identifiant de média, ou (null, échec décrit) si le téléversement a échoué.</summary>
    private async Task<(string? MediaId, WhatsAppSendResult? Failure)> UploadAsync(
        WhatsAppAttachment attachment, CancellationToken cancellationToken)
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
            var meta = MetaError.Parse(payload);
            var httpStatus = (int)response.StatusCode;
            logger.LogError(
                "WhatsApp : téléversement de {Filename} refusé par Meta. HTTP {StatusCode}, code {MetaCode}, "
                + "message « {MetaMessage} », trace {FbTraceId}.",
                attachment.Filename, httpStatus, meta.Code, meta.Message, meta.FbTraceId);

            return (null, WhatsAppSendResult.Failed(
                "Le dépôt du bulletin sur WhatsApp a échoué : " + DescribeFailure(meta, httpStatus),
                meta.Code, httpStatus));
        }

        using var document = JsonDocument.Parse(payload);
        var mediaId = document.RootElement.TryGetProperty("id", out var id) ? id.GetString() : null;

        if (string.IsNullOrEmpty(mediaId))
        {
            logger.LogError(
                "WhatsApp : téléversement de {Filename} accepté mais sans identifiant de média dans la réponse.",
                attachment.Filename);
            return (null, WhatsAppSendResult.Failed(
                "Le dépôt du bulletin sur WhatsApp n'a pas renvoyé d'identifiant de fichier."));
        }

        return (mediaId, null);
    }

    /// <summary>
    /// Traduit un code d'erreur graph.facebook.com en phrase actionnable pour l'utilisateur.
    /// Réf. https://developers.facebook.com/docs/whatsapp/cloud-api/support/error-codes
    /// </summary>
    private static string DescribeFailure(MetaError meta, int httpStatus) => meta.Code switch
    {
        190 => "Le jeton d'accès WhatsApp est invalide ou expiré : générez un jeton permanent "
             + "(System User) et mettez WhatsApp__AccessToken à jour.",
        131047 => "Meta a refusé le message : le tuteur n'a pas écrit au numéro dans les dernières 24 h. "
                + "Configurez un modèle approuvé (WhatsApp__ReportCardTemplateName) pour le contacter « à froid ».",
        131026 => "Le numéro du tuteur n'a pas de compte WhatsApp actif, ou ne peut pas recevoir ce message.",
        131030 => "Le numéro du tuteur n'est pas dans la liste des destinataires autorisés "
                + "(application Meta encore en mode test).",
        132000 or 132001 or 132005 or 132007 or 132012 or 132015 or 132016 or 132068 or 132069
            => "Le modèle WhatsApp est introuvable, non approuvé, ou ses paramètres ne correspondent pas "
             + "à ce que Meta a validé (nom, langue, nombre de variables).",
        100 => "Meta a refusé la requête (paramètre invalide) : vérifiez WhatsApp__BaseUrl "
             + "(identifiant du numéro expéditeur) et le format du numéro du tuteur.",
        _ when !string.IsNullOrWhiteSpace(meta.Message)
            => $"Meta a refusé l'envoi : {meta.Message}"
             + (meta.Code is { } code ? $" (code {code})." : "."),
        _ => $"Meta a refusé l'envoi WhatsApp (HTTP {httpStatus}).",
    };

    /// <summary>
    /// Champs connus de l'objet <c>error</c> d'une réponse graph.facebook.com. Parsés isolément pour ne
    /// jamais journaliser le corps brut, où Meta glisse parfois des fragments du jeton d'accès.
    /// </summary>
    private readonly record struct MetaError(
        int? Code, int? Subcode, string? Type, string? Message, string? Details, string? FbTraceId)
    {
        public static MetaError Parse(string responseBody)
        {
            try
            {
                using var document = JsonDocument.Parse(responseBody);
                if (!document.RootElement.TryGetProperty("error", out var error)
                    || error.ValueKind != JsonValueKind.Object)
                {
                    return default;
                }

                return new MetaError(
                    Code: error.TryGetProperty("code", out var code) && code.TryGetInt32(out var codeValue)
                        ? codeValue : null,
                    Subcode: error.TryGetProperty("error_subcode", out var subcode) && subcode.TryGetInt32(out var subcodeValue)
                        ? subcodeValue : null,
                    Type: error.TryGetProperty("type", out var type) ? type.GetString() : null,
                    Message: error.TryGetProperty("message", out var message) ? message.GetString() : null,
                    Details: error.TryGetProperty("error_data", out var data)
                             && data.TryGetProperty("details", out var details)
                        ? details.GetString() : null,
                    FbTraceId: error.TryGetProperty("fbtrace_id", out var trace) ? trace.GetString() : null);
            }
            catch (JsonException)
            {
                return default;
            }
        }
    }
}
