using System.Net;
using FluentAssertions;
using SamaEcole.Infrastructure.Media;
using Xunit;

namespace SamaEcole.UnitTests.Media;

/// <summary>
/// Ticket JGK-E02 — garde anti-SSRF du logo de l'établissement. C'est le cœur sécuritaire de la
/// fonctionnalité : l'URL du logo vient du Directeur, et le serveur va la chercher. Ces cas vérouillent
/// le fait qu'AUCUNE adresse de réseau interne (loopback, plages privées, link-local/métadonnées cloud,
/// CGNAT, multicast, ULA IPv6…) ne peut être atteinte, tout en laissant passer l'Internet public.
/// </summary>
public class PrivateNetworkGuardTests
{
    [Theory]
    [InlineData("127.0.0.1")]        // loopback
    [InlineData("127.10.20.30")]     // tout 127.0.0.0/8
    [InlineData("0.0.0.0")]          // « ce réseau »
    [InlineData("10.0.0.1")]         // privé /8
    [InlineData("10.255.255.255")]
    [InlineData("172.16.0.1")]       // privé /12 (bornes incluses)
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.1")]      // privé /16
    [InlineData("169.254.169.254")]  // link-local + métadonnées cloud (AWS/GCP/Azure)
    [InlineData("169.254.0.1")]
    [InlineData("100.64.0.1")]       // CGNAT /10
    [InlineData("100.127.255.255")]
    [InlineData("224.0.0.1")]        // multicast
    [InlineData("239.255.255.255")]
    [InlineData("240.0.0.1")]        // réservé
    [InlineData("255.255.255.255")]  // broadcast
    [InlineData("::1")]              // loopback IPv6
    [InlineData("fe80::1")]          // link-local IPv6
    [InlineData("fc00::1")]          // ULA IPv6 (fc00::/7)
    [InlineData("fd12:3456::1")]
    [InlineData("ff02::1")]          // multicast IPv6
    [InlineData("::")]               // non spécifiée
    [InlineData("::ffff:10.0.0.1")]  // IPv4 privée encapsulée en IPv6
    [InlineData("::ffff:127.0.0.1")]
    public void Blocks_Internal_And_Reserved_Addresses(string ip)
    {
        PrivateNetworkGuard.IsBlocked(IPAddress.Parse(ip)).Should().BeTrue($"« {ip} » est une adresse interne/réservée");
    }

    [Theory]
    [InlineData("8.8.8.8")]          // Google DNS
    [InlineData("1.1.1.1")]          // Cloudflare DNS
    [InlineData("93.184.216.34")]    // example.com
    [InlineData("172.15.255.255")]   // juste SOUS 172.16.0.0/12
    [InlineData("172.32.0.1")]       // juste AU-DESSUS de 172.31.255.255
    [InlineData("192.169.0.1")]      // voisin public de 192.168.0.0/16
    [InlineData("100.63.255.255")]   // juste sous le CGNAT
    [InlineData("100.128.0.1")]      // juste au-dessus du CGNAT
    [InlineData("11.0.0.1")]         // voisin public de 10.0.0.0/8
    [InlineData("2606:4700:4700::1111")] // Cloudflare IPv6 public
    [InlineData("2001:4860:4860::8888")] // Google IPv6 public
    public void Allows_Public_Addresses(string ip)
    {
        PrivateNetworkGuard.IsBlocked(IPAddress.Parse(ip)).Should().BeFalse($"« {ip} » est une adresse publique légitime");
    }
}
