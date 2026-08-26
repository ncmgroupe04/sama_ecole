namespace SamaEcole.Application.Inventory;

/// <summary>
/// Données prêtes à rendre pour la fiche d'inventaire global (GET /inventory/reports/global/pdf).
/// Le tenant et les filtres sont déjà résolus par le Handler — ce modèle ne porte aucune requête, il
/// n'est qu'un instantané, comme <c>StudentsExportModel</c> pour la liste des élèves.
/// </summary>
public record InventoryReportModel(
    string SchoolName,
    string? InspectionAcademie,
    string? InspectionEducationFormation,
    /// <summary>Libellé du filtre catégorie appliqué, ou null pour « toutes les catégories ».</summary>
    string? CategoryFilterName,
    /// <summary>Libellé du filtre emplacement appliqué, ou null pour « tous les emplacements ».</summary>
    string? LocationFilterName,
    DateOnly GeneratedOn,
    IReadOnlyList<InventoryReportGroup> Groups)
{
    public int GrandTotalQuantity => Groups.Sum(g => g.TotalQuantity);

    public int GrandTotalAvailable => Groups.Sum(g => g.TotalAvailable);

    public decimal GrandTotalValue => Groups.Sum(g => g.TotalValue);

    /// <summary>
    /// Vrai dès qu'AU MOINS un lot porte un prix indicatif. La colonne « Valeur » et la ligne de
    /// valorisation ne s'impriment que dans ce cas : une école qui n'a saisi aucun prix ne doit pas
    /// lire une colonne de zéros, qui se confondrait avec un patrimoine sans valeur.
    /// </summary>
    public bool HasValuation => Groups.Any(g => g.Rows.Any(r => r.UnitPrice.HasValue));
}

/// <summary>La fiche est groupée par catégorie : c'est ainsi qu'un inventaire se relit et se contrôle.</summary>
public record InventoryReportGroup(string CategoryName, IReadOnlyList<InventoryReportRow> Rows)
{
    public int TotalQuantity => Rows.Sum(r => r.QuantityTotal);

    public int TotalAvailable => Rows.Sum(r => r.QuantityAvailable);

    public decimal TotalValue => Rows.Sum(r => r.LineValue ?? 0m);
}

public record InventoryReportRow(
    string Name,
    string? Code,
    string Location,
    int QuantityTotal,
    int QuantityAvailable,
    string Condition,
    decimal? UnitPrice)
{
    public int OnLoanQuantity => QuantityTotal - QuantityAvailable;

    public decimal? LineValue => UnitPrice.HasValue ? UnitPrice.Value * QuantityTotal : null;
}
