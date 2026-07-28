using System.Net;
using System.Net.Sockets;

namespace SamaEcole.Infrastructure.Media;

/// <summary>
/// Anti-SSRF : décide si une adresse IP est INTERNE et doit donc être refusée quand on va chercher une
/// ressource dont l'URL vient d'un utilisateur (le logo de l'établissement, ticket JGK-E02). Un Directeur
/// pourrait sinon pointer <c>http://169.254.169.254/…</c> (métadonnées cloud) ou un service interne et
/// s'en servir comme relais depuis le serveur.
///
/// La logique est isolée et pure pour être testable adresse par adresse ; l'application effective se fait
/// au moment de la connexion TCP (<c>SocketsHttpHandler.ConnectCallback</c>), ce qui ferme aussi la
/// fenêtre de « DNS rebinding » : on ne vérifie pas un nom puis n'en résout un autre, on vérifie l'IP
/// exacte à laquelle on s'apprête à se connecter.
/// </summary>
public static class PrivateNetworkGuard
{
    public static bool IsBlocked(IPAddress address)
    {
        // Une IPv4 encapsulée en IPv6 (::ffff:10.0.0.1) doit être jugée sur sa vraie valeur v4.
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        return address.AddressFamily switch
        {
            AddressFamily.InterNetwork => IsBlockedV4(address.GetAddressBytes()),
            AddressFamily.InterNetworkV6 => IsBlockedV6(address),
            _ => true // famille inconnue : on refuse par défaut.
        };
    }

    private static bool IsBlockedV4(byte[] b) =>
        b[0] == 0                                    // 0.0.0.0/8   « ce réseau »
        || b[0] == 10                                // 10.0.0.0/8  privé
        || (b[0] == 100 && b[1] >= 64 && b[1] <= 127) // 100.64.0.0/10 CGNAT
        || b[0] == 127                               // 127.0.0.0/8 loopback
        || (b[0] == 169 && b[1] == 254)              // 169.254.0.0/16 link-local + métadonnées cloud
        || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) // 172.16.0.0/12 privé
        || (b[0] == 192 && b[1] == 168)              // 192.168.0.0/16 privé
        || b[0] >= 224;                              // 224.0.0.0/4 multicast + 240.0.0.0/4 réservé

    private static bool IsBlockedV6(IPAddress address)
    {
        if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast
            || address.Equals(IPAddress.IPv6Any))
        {
            return true;
        }

        // Adresses locales uniques fc00::/7 (l'équivalent v6 des plages privées).
        var b = address.GetAddressBytes();
        return (b[0] & 0xFE) == 0xFC;
    }
}
