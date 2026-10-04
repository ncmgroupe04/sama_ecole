using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Application.Uniforms;

/// <summary>Une taille d'un modèle, avec le stock vivant du lot d'inventaire qui la porte.</summary>
public record UniformVariantDto(
    Guid Id,
    Guid UniformItemId,
    string SizeLabel,
    int SortOrder,
    Guid InventoryItemId,
    int QuantityTotal,
    int QuantityAvailable);

/// <summary>Un modèle de tenue et ses tailles. <c>RowVersion</c> est le jeton xmin à renvoyer à la modification.</summary>
public record UniformItemDto(
    Guid Id,
    string Name,
    string Kind,
    string Gender,
    string? Cycle,
    Guid? FeeCategoryId,
    string? FeeCategoryName,
    int QuantityIncluded,
    string? Notes,
    uint RowVersion,
    IReadOnlyList<UniformVariantDto> Variants);

/// <summary>
/// Lecture du catalogue, partagée par la requête et par les commandes (qui renvoient l'objet à jour). Le stock vient
/// du lot d'inventaire de chaque taille : il n'est jamais recopié, donc jamais périmé.
/// </summary>
internal static class UniformCatalogReader
{
    // Tri en mémoire, en français : la collation de la base ne range pas « École » ni « Élève » comme l'utilisateur.
    private static readonly StringComparer French = StringComparer.Create(new CultureInfo("fr-FR"), ignoreCase: true);

    public static async Task<IReadOnlyList<UniformItemDto>> GetItemsAsync(
        IApplicationDbContext dbContext, Guid? onlyItemId, CancellationToken cancellationToken)
    {
        var items = await dbContext.UniformItems.AsNoTracking()
            .Where(i => onlyItemId == null || i.Id == onlyItemId)
            .Select(i => new
            {
                i.Id, i.Name, i.Kind, i.Gender, i.Cycle, i.FeeCategoryId, i.QuantityIncluded, i.Notes,
                RowVersion = EF.Property<uint>(i, "xmin")
            })
            .ToListAsync(cancellationToken);

        if (items.Count == 0) return [];

        var itemIds = items.Select(i => i.Id).ToList();
        var feeIds = items.Where(i => i.FeeCategoryId != null).Select(i => i.FeeCategoryId!.Value).Distinct().ToList();

        var feeNames = await dbContext.FeeCategories.AsNoTracking()
            .Where(c => feeIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);

        var variants = await dbContext.UniformVariants.AsNoTracking()
            .Where(v => itemIds.Contains(v.UniformItemId))
            .Join(dbContext.InventoryItems.AsNoTracking(), v => v.InventoryItemId, lot => lot.Id,
                (v, lot) => new UniformVariantDto(
                    v.Id, v.UniformItemId, v.SizeLabel, v.SortOrder, v.InventoryItemId,
                    lot.QuantityTotal, lot.QuantityAvailable))
            .ToListAsync(cancellationToken);

        var variantsByItem = variants
            .GroupBy(v => v.UniformItemId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<UniformVariantDto>)g
                    .OrderBy(v => v.SortOrder).ThenBy(v => v.SizeLabel, French).ToList());

        return items
            .OrderBy(i => i.Name, French).ThenBy(i => i.Gender.ToString(), StringComparer.Ordinal)
            .Select(i => new UniformItemDto(
                i.Id, i.Name, i.Kind.ToString(), i.Gender.ToString(), i.Cycle?.ToString(), i.FeeCategoryId,
                i.FeeCategoryId is { } feeId && feeNames.TryGetValue(feeId, out var feeName) ? feeName : null,
                i.QuantityIncluded, i.Notes, i.RowVersion,
                variantsByItem.TryGetValue(i.Id, out var itemVariants) ? itemVariants : []))
            .ToList();
    }

    public static async Task<UniformItemDto> GetItemAsync(
        IApplicationDbContext dbContext, Guid itemId, CancellationToken cancellationToken) =>
        (await GetItemsAsync(dbContext, itemId, cancellationToken)).Single();

    public static async Task<UniformVariantDto> GetVariantAsync(
        IApplicationDbContext dbContext, Guid variantId, CancellationToken cancellationToken) =>
        await dbContext.UniformVariants.AsNoTracking()
            .Where(v => v.Id == variantId)
            .Join(dbContext.InventoryItems.AsNoTracking(), v => v.InventoryItemId, lot => lot.Id,
                (v, lot) => new UniformVariantDto(
                    v.Id, v.UniformItemId, v.SizeLabel, v.SortOrder, v.InventoryItemId,
                    lot.QuantityTotal, lot.QuantityAvailable))
            .SingleAsync(cancellationToken);
}
