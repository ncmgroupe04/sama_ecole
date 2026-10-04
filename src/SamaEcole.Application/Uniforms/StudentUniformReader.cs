using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Uniforms;

/// <summary>
/// Lecture des dotations, partagée par la requête et par les commandes (qui renvoient l'objet à jour). Tout passe
/// par les <c>DbSet</c> filtrés par tenant : aucune lecture hors RLS.
/// </summary>
internal static class StudentUniformReader
{
    public static Task<IReadOnlyList<StudentUniformDto>> GetByIdsAsync(
        IApplicationDbContext dbContext, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        ReadAsync(dbContext, dbContext.StudentUniforms.Where(u => ids.Contains(u.Id)), cancellationToken);

    public static async Task<IReadOnlyList<StudentUniformDto>> ReadAsync(
        IApplicationDbContext dbContext, IQueryable<Domain.Entities.StudentUniform> source, CancellationToken cancellationToken)
    {
        var rows = await source.AsNoTracking()
            .Select(u => new
            {
                u.Id, u.EnrollmentId, u.SchoolYearId, u.UniformItemId, u.UniformVariantId, u.Quantity, u.Status,
                u.BillingStatus, u.DeliveredOn, u.ItemLabel, u.SizeLabel, u.CreatedAt,
                RowVersion = EF.Property<uint>(u, "xmin")
            })
            .ToListAsync(cancellationToken);

        if (rows.Count == 0) return [];

        var itemIds = rows.Select(r => r.UniformItemId).Distinct().ToList();
        var variantIds = rows.Where(r => r.UniformVariantId != null).Select(r => r.UniformVariantId!.Value).Distinct().ToList();
        var enrollmentIds = rows.Select(r => r.EnrollmentId).Distinct().ToList();

        var items = await dbContext.UniformItems.AsNoTracking()
            .Where(i => itemIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, cancellationToken);

        var variants = await dbContext.UniformVariants.AsNoTracking()
            .Where(v => variantIds.Contains(v.Id))
            .Join(dbContext.InventoryItems.AsNoTracking(), v => v.InventoryItemId, lot => lot.Id,
                (v, lot) => new { v.Id, v.SizeLabel, lot.QuantityAvailable })
            .ToDictionaryAsync(v => v.Id, cancellationToken);

        var billedCategories = await BilledCategoriesAsync(dbContext, enrollmentIds, cancellationToken);

        return rows
            .OrderBy(r => r.CreatedAt)
            .Select(r =>
            {
                items.TryGetValue(r.UniformItemId, out var item);
                var variant = r.UniformVariantId is { } vid && variants.TryGetValue(vid, out var v) ? v : null;

                // Une ligne remise ou retournée garde SA facturation figée ; une ligne encore à remettre (ou annulée)
                // montre la facturation attendue, recalculée à chaque lecture depuis les frais de l'inscription.
                var billing = r.Status is StudentUniformStatus.Delivered or StudentUniformStatus.Returned
                    ? r.BillingStatus
                    : item is null
                        ? UniformBillingStatus.NotBilled
                        : UniformBillingCheck.Evaluate(
                            item.FeeCategoryId, item.QuantityIncluded, r.Quantity,
                            billedCategories.GetValueOrDefault(r.EnrollmentId) ?? []);

                return new StudentUniformDto(
                    r.Id, r.EnrollmentId, r.SchoolYearId, r.UniformItemId,
                    item?.Name ?? r.ItemLabel ?? "—",
                    r.UniformVariantId,
                    variant?.SizeLabel ?? r.SizeLabel,
                    r.Quantity, r.Status.ToString(), billing.ToString(), r.DeliveredOn,
                    variant?.QuantityAvailable, r.RowVersion);
            })
            .ToList();
    }

    /// <summary>Catégories de frais portées par les lignes de frais de chaque inscription (ce que la famille a réellement choisi).</summary>
    public static async Task<Dictionary<Guid, HashSet<Guid>>> BilledCategoriesAsync(
        IApplicationDbContext dbContext, IReadOnlyCollection<Guid> enrollmentIds, CancellationToken cancellationToken)
    {
        var pairs = await dbContext.EnrollmentFeeLines.AsNoTracking()
            .Where(l => enrollmentIds.Contains(l.EnrollmentId))
            .Select(l => new { l.EnrollmentId, l.FeeCategoryId })
            .ToListAsync(cancellationToken);

        return pairs
            .GroupBy(p => p.EnrollmentId)
            .ToDictionary(g => g.Key, g => g.Select(p => p.FeeCategoryId).ToHashSet());
    }
}
