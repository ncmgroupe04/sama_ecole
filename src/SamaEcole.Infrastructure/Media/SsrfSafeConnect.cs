using System.Net;
using System.Net.Sockets;

namespace SamaEcole.Infrastructure.Media;

/// <summary>
/// Rappel de connexion (<see cref="System.Net.Http.SocketsHttpHandler.ConnectCallback"/>) qui n'ouvre une
/// socket que vers une IP publique validée par <see cref="PrivateNetworkGuard"/>. En vérifiant l'IP juste
/// avant de s'y connecter — et non le nom d'hôte en amont — on ferme la fenêtre de « DNS rebinding » :
/// impossible de faire résoudre un nom vers une IP publique pour le contrôle puis vers une IP interne pour
/// la connexion réelle.
/// </summary>
public static class SsrfSafeConnect
{
    public static async ValueTask<Stream> ConnectAsync(
        System.Net.Http.SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
        var target = Array.Find(addresses, ip => !PrivateNetworkGuard.IsBlocked(ip))
            ?? throw new IOException(
                $"Connexion refusée vers « {context.DnsEndPoint.Host} » : adresse de réseau interne (garde anti-SSRF).");

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(new IPEndPoint(target, context.DnsEndPoint.Port), cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
