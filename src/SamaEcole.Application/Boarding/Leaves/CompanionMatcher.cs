using System.Text.RegularExpressions;
using SamaEcole.Domain.Entities;

namespace SamaEcole.Application.Boarding.Leaves;

/// <summary>
/// Un accompagnateur est « habilité » s'il correspond à une personne de la fiche du pensionnaire : espaces en trop
/// ignorés, casse ignorée, nom COMPLET identique (« Fatou » ne correspond pas à « Fatou Diop »). Le résultat sert à
/// poser <c>IsCompanionUnlisted</c> à la déclaration — un avertissement, jamais un blocage (spec Q5).
/// </summary>
public static partial class CompanionMatcher
{
    public static bool IsListed(string accompaniedBy, IEnumerable<AllowedExitPerson> persons)
    {
        var wanted = Normalize(accompaniedBy);
        return wanted.Length > 0
               && persons.Any(p => string.Equals(Normalize(p.Name), wanted, StringComparison.OrdinalIgnoreCase));
    }

    private static string Normalize(string value) => Spaces().Replace(value.Trim(), " ");

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
