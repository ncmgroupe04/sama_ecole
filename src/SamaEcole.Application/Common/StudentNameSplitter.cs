namespace SamaEcole.Application.Common;

/// <summary>
/// Découpe un <c>FullName</c> en (Prénoms, Nom de famille) — le DERNIER mot est le nom, le reste les
/// prénoms, conformément à l'usage sénégalais (« Mame Diarra Bousso FAYE »).
///
/// Simple heuristique d'AFFICHAGE et d'EXPORT : rien n'est modifié en base, et le modèle continue de
/// ne porter qu'un seul champ. Elle vit ici, dans Application, et non dans le générateur du bulletin
/// où elle est née : le bulletin de notes ET l'export Planète doivent découper un même élève de la
/// même façon, sans quoi le nom imprimé sur la pièce remise au tuteur ne correspondrait pas à celui
/// transmis au ministère. Deux copies de cette règle auraient divergé à la première correction.
///
/// Un nom d'un seul mot part entièrement côté prénoms, la case Nom reste vide — plutôt que l'inverse,
/// parce qu'un formulaire officiel accepte un nom de famille manquant, jamais un prénom manquant.
/// </summary>
public static class StudentNameSplitter
{
    public static (string FirstNames, string LastName) Split(string fullName)
    {
        var tokens = (fullName ?? "").Split(
            ' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return tokens.Length switch
        {
            0 => ("", ""),
            1 => (tokens[0], ""),
            _ => (string.Join(' ', tokens[..^1]), tokens[^1])
        };
    }
}
