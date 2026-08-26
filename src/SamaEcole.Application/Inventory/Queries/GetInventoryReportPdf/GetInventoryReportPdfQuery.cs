using SamaEcole.Application.Common.Interfaces;
using MediatR;

namespace SamaEcole.Application.Inventory.Queries.GetInventoryReportPdf;

/// <summary>
/// GET /api/v1/inventory/reports/global/pdf — fiche d'inventaire global de l'établissement, filtrable
/// par catégorie et/ou par salle. C'est le document qu'une école présente à la mairie, à l'IEF ou à
/// son conseil d'administration.
///
/// <see cref="IAuditableRequest"/> : un export du patrimoine complet est une opération sensible
/// (docs/Volume_7_Security.md §7), au même titre que l'export des élèves.
/// </summary>
public record GetInventoryReportPdfQuery : IRequest<InventoryReportPdfResult>, IAuditableRequest
{
    public Guid? CategoryId { get; init; }

    public Guid? RoomId { get; init; }
}

public record InventoryReportPdfResult(byte[] Content);
