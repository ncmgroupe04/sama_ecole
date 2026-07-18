using System.Security.Cryptography;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Infrastructure.Security;

/// <summary>
/// Référence de suivi d'une demande d'inscription (ticket JGK-I01), au format « REG-XXXXXXXX ».
///
/// Tirée du CSPRNG (RandomNumberGenerator), jamais de Random : la référence sert à consulter une demande
/// sans authentification (ticket JGK-I02), une référence prédictible depuis l'horloge permettrait
/// d'énumérer les demandes des autres écoles.
///
/// 8 caractères sur un alphabet de 31 symboles ≈ 31^8 ≈ 8,5 × 10^11 combinaisons : une collision est
/// négligeable, et l'index unique de la base (SchoolRegistrationRequestConfiguration) reste le garde-fou
/// définitif. Caractères ambigus exclus (O/0, I/1, L) : la référence est lue et recopiée à la main.
/// « REG-XXXXXXXX » = 12 caractères, sous la limite varchar(20).
/// </summary>
public class RegistrationReferenceGenerator : IRegistrationReferenceGenerator
{
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    private const int Length = 8;

    public string Generate()
    {
        var chars = new char[Length];

        for (var i = 0; i < Length; i++)
        {
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return $"REG-{new string(chars)}";
    }
}
