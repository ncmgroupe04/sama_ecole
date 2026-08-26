using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Inventory.Queries.GetInventoryReportPdf;

public class GetInventoryReportPdfQueryHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    TimeProvider timeProvider,
    IInventoryReportPdfGenerator pdfGenerator)
    : IRequestHandler<GetInventoryReportPdfQuery, InventoryReportPdfResult>
{
    public async Task<InventoryReportPdfResult> Handle(
        GetInventoryReportPdfQuery request, CancellationToken cancellationToken)
    {
        // Aucun filtre sur SchoolId : le Global Query Filter l'applique automatiquement et la policy
        // RLS le rejouerait même s'il disparaissait un jour (AGENTS.md règle #2).
        var query = dbContext.InventoryItems.AsNoTracking();

        if (request.CategoryId is { } categoryId)
        {
            query = query.Where(i => i.CategoryId == categoryId);
        }

        if (request.RoomId is { } roomId)
        {
            query = query.Where(i => i.RoomId == roomId);
        }

        var rows = await dbContext.ToListOrEmptyOnMissingTableAsync(
            query
                .OrderBy(i => i.Name)
                .Select(i => new
                {
                    CategoryName = dbContext.InventoryCategories
                        .Where(c => c.Id == i.CategoryId)
                        .Select(c => c.Name)
                        .FirstOrDefault() ?? "Sans catégorie",
                    i.Name,
                    i.Code,
                    RoomName = i.RoomId == null
                        ? null
                        : dbContext.Rooms.Where(r => r.Id == i.RoomId).Select(r => r.Name).FirstOrDefault(),
                    i.LocationLabel,
                    i.QuantityTotal,
                    i.QuantityAvailable,
                    Condition = i.Condition.ToString(),
                    i.UnitPrice
                }),
            cancellationToken);

        var groups = rows
            .GroupBy(r => r.CategoryName)
            .OrderBy(g => g.Key)
            .Select(g => new InventoryReportGroup(
                g.Key,
                g.Select(r => new InventoryReportRow(
                        r.Name,
                        r.Code,
                        // La salle prime sur le texte libre quand les deux sont renseignés : elle est
                        // vérifiable dans le module Infrastructures, le libellé libre ne l'est pas.
                        r.RoomName ?? r.LocationLabel ?? "Non précisé",
                        r.QuantityTotal,
                        r.QuantityAvailable,
                        r.Condition,
                        r.UnitPrice))
                    .ToList()))
            .ToList();

        var school = await SchoolHeaderAsync(cancellationToken);

        var model = new InventoryReportModel(
            school.Name,
            school.InspectionAcademie,
            school.InspectionEducationFormation,
            await FilterLabelAsync(
                request.CategoryId,
                id => dbContext.InventoryCategories.AsNoTracking().Where(c => c.Id == id).Select(c => c.Name),
                cancellationToken),
            await FilterLabelAsync(
                request.RoomId,
                id => dbContext.Rooms.AsNoTracking().Where(r => r.Id == id).Select(r => r.Name),
                cancellationToken),
            DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime),
            groups);

        return new InventoryReportPdfResult(pdfGenerator.Generate(model));
    }

    private async Task<(string Name, string? InspectionAcademie, string? InspectionEducationFormation)>
        SchoolHeaderAsync(CancellationToken cancellationToken)
    {
        if (tenantProvider.CurrentSchoolId is not { } schoolId)
        {
            return (string.Empty, null, null);
        }

        var school = await dbContext.Schools.AsNoTracking()
            .Where(s => s.Id == schoolId)
            .Select(s => new { s.Name, s.InspectionAcademie, s.InspectionEducationFormation })
            .FirstOrDefaultAsync(cancellationToken);

        return (school?.Name ?? string.Empty, school?.InspectionAcademie, school?.InspectionEducationFormation);
    }

    /// <summary>
    /// Libellé d'un filtre appliqué, pour l'imprimer en tête de fiche. Un identifiant qui ne
    /// correspond à rien dans l'école courante rend null : l'en-tête affiche alors « toutes », il
    /// n'invente jamais un nom (et la liste, filtrée sur ce même identifiant, sera vide de son côté).
    /// </summary>
    private static async Task<string?> FilterLabelAsync(
        Guid? id, Func<Guid, IQueryable<string>> lookup, CancellationToken cancellationToken)
        => id is { } value ? await lookup(value).FirstOrDefaultAsync(cancellationToken) : null;
}
