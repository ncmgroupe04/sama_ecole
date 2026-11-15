using System.Text.Json;
using Microsoft.Extensions.Options;

namespace SamaEcole.Web.Security;

/// <summary>
/// Appelle <c>siteverify</c> de Cloudflare avec la clé secrète. Échoue FERMÉ : si Cloudflare ne répond pas, la demande
/// n'est pas acceptée (un robot ne doit pas profiter d'une panne), mais l'utilisateur peut réessayer.
/// La clé secrète n'est jamais journalisée.
/// </summary>
public sealed class CloudflareTurnstileVerifier(
    HttpClient httpClient,
    IOptions<TurnstileOptions> options,
    ILogger<CloudflareTurnstileVerifier> logger) : ITurnstileVerifier
{
    internal const string VerifyUrl = "https://challenges.cloudflare.com/turnstile/v0/siteverify";

    public async Task<TurnstileVerdict> VerifyAsync(string? token, string? remoteIp, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return TurnstileVerdict.Invalid;
        }

        var form = new List<KeyValuePair<string, string>>
        {
            new("secret", options.Value.SecretKey ?? string.Empty),
            new("response", token)
        };

        if (!string.IsNullOrWhiteSpace(remoteIp))
        {
            form.Add(new("remoteip", remoteIp));
        }

        try
        {
            using var response = await httpClient.PostAsync(VerifyUrl, new FormUrlEncodedContent(form), cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Turnstile siteverify a répondu {Status} : vérification indisponible.", (int)response.StatusCode);
                return TurnstileVerdict.Unavailable;
            }

            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
            var success = json.RootElement.TryGetProperty("success", out var flag) && flag.ValueKind == JsonValueKind.True;

            if (!success)
            {
                var codes = json.RootElement.TryGetProperty("error-codes", out var errors) ? errors.ToString() : "?";
                logger.LogInformation("Turnstile : jeton refusé ({Codes}).", codes);
            }

            return success ? TurnstileVerdict.Valid : TurnstileVerdict.Invalid;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            // Un jeton annulé par le CLIENT (déconnexion) remonte aussi ici : on distingue l'annulation voulue.
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            logger.LogWarning(ex, "Turnstile siteverify injoignable : vérification indisponible.");
            return TurnstileVerdict.Unavailable;
        }
    }
}
