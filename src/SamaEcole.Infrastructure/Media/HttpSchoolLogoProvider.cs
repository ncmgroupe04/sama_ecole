using SamaEcole.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace SamaEcole.Infrastructure.Media;

/// <summary>
/// Récupère le logo de l'établissement (ticket JGK-E02) via un client HTTP dédié, configuré dans
/// <c>DependencyInjection</c> avec la garde anti-SSRF (<see cref="SsrfSafeConnect"/>), un timeout court et
/// aucune redirection automatique. Ici, on ajoute les garde-fous de CONTENU :
/// <list type="bullet">
///   <item>plafond de taille strict (streaming borné, même si l'en-tête <c>Content-Length</c> ment) ;</item>
///   <item>on n'accepte qu'un PNG ou un JPEG RECONNU À SA SIGNATURE — le contenu prime sur le type
///         déclaré, une page d'erreur HTML servie en « image/png » est donc écartée.</item>
/// </list>
/// Toute anomalie rend <c>null</c> : le reçu officiel s'émet alors sans logo, jamais en erreur.
/// </summary>
public class HttpSchoolLogoProvider(
    IHttpClientFactory httpClientFactory,
    ILogger<HttpSchoolLogoProvider> logger) : ISchoolLogoProvider
{
    /// <summary>Nom du client HTTP dédié, configuré avec la garde SSRF et les bornes temps/taille.</summary>
    public const string HttpClientName = "school-logo";

    private const int MaxBytes = 2 * 1024 * 1024; // 2 Mio : un logo d'en-tête, pas une photo pleine page.

    public async Task<byte[]?> TryFetchAsync(string? logoUrl, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(logoUrl)
            || !Uri.TryCreate(logoUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);

            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength is > MaxBytes)
            {
                logger.LogWarning("Logo établissement écarté ({Url}) : statut {Status}, taille annoncée {Length}.",
                    uri, (int)response.StatusCode, response.Content.Headers.ContentLength);
                return null;
            }

            var bytes = await ReadCappedAsync(response, cancellationToken);
            if (bytes is null || !LooksLikeSupportedImage(bytes))
            {
                logger.LogWarning("Logo établissement écarté ({Url}) : contenu vide, trop volumineux ou non-image.", uri);
                return null;
            }

            return bytes;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // annulation par l'appelant : on la laisse remonter, ce n'est pas un logo « injoignable ».
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException)
        {
            // Hôte injoignable, timeout, ou adresse interne refusée par la garde SSRF (ConnectCallback).
            // Le logo est optionnel : on n'empêche jamais l'émission d'un reçu officiel pour autant.
            logger.LogWarning(ex, "Logo établissement inaccessible ({Url}) — reçu généré sans logo.", uri);
            return null;
        }
    }

    /// <summary>Lit le corps avec un plafond dur : on s'arrête net dès qu'il dépasse, sans tout charger.</summary>
    private static async Task<byte[]?> ReadCappedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.Length == 0 ? null : buffer.ToArray();
    }

    /// <summary>Signature binaire d'un PNG ou d'un JPEG — les deux formats que le moteur PDF sait rendre.</summary>
    private static bool LooksLikeSupportedImage(byte[] b) =>
        (b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47
            && b[4] == 0x0D && b[5] == 0x0A && b[6] == 0x1A && b[7] == 0x0A)  // PNG
        || (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF);    // JPEG
}
