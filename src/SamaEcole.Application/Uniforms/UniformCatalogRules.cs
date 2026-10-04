using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Uniforms;

/// <summary>Bornes du catalogue de tenues, alignées sur les colonnes de <c>uniform_items</c> et <c>uniform_variants</c>.</summary>
public static class UniformCatalogRules
{
    public const int MaxItemNameLength = 100;
    public const int MaxSizeLabelLength = 30;
    public const int MaxNotesLength = 300;

    /// <summary>Catégorie d'inventaire qui reçoit les lots de tenues ; créée à la première taille si l'école n'en a pas.</summary>
    public const string InventoryCategoryName = "Tenues";
}

/// <summary>
/// Nom du lot d'inventaire qui porte le stock d'une taille : « Polo bleu — 10 ans ». C'est ce que lit l'utilisateur
/// dans l'écran Inventaire quand il enregistre une entrée de stock, d'où un libellé qui nomme le modèle ET la taille.
/// </summary>
public static class UniformLotNaming
{
    /// <summary>Borne de <c>inventory_items.Name</c>.</summary>
    public const int MaxInventoryNameLength = 150;

    public static string Build(string itemName, string sizeLabel) => $"{itemName.Trim()} — {sizeLabel.Trim()}";
}

/// <summary>
/// Quels modèles PROPOSER à un élève. Une proposition guide la saisie ; elle n'interdit rien (une école reste libre
/// de remettre un modèle « garçon » à une fille) et ne remplace pas la facturation, qui vient des lignes de frais.
/// </summary>
public static class UniformProposalRules
{
    /// <param name="itemGender">Public visé par le modèle.</param>
    /// <param name="itemCycle">Cycle visé, ou <c>null</c> pour tous.</param>
    /// <param name="studentGender">« M » ou « F » (<c>Student.Gender</c>) ; toute autre valeur ne correspond qu'à un modèle mixte.</param>
    /// <param name="classroomCycle">Cycle de la classe de l'inscription.</param>
    public static bool Matches(UniformGender itemGender, CycleType? itemCycle, string studentGender, CycleType classroomCycle)
    {
        if (itemCycle is { } cycle && cycle != classroomCycle) return false;

        return itemGender switch
        {
            UniformGender.Mixte => true,
            UniformGender.Garcon => string.Equals(studentGender, "M", StringComparison.OrdinalIgnoreCase),
            UniformGender.Fille => string.Equals(studentGender, "F", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }
}
