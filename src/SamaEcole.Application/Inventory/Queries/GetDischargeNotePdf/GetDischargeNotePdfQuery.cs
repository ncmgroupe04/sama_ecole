using SamaEcole.Application.Common.Interfaces;
using MediatR;

namespace SamaEcole.Application.Inventory.Queries.GetDischargeNotePdf;

/// <summary>
/// GET /api/v1/inventory/assignments/{id}/pdf — la fiche de décharge à faire signer, ou sa
/// réimpression. <see cref="IAuditableRequest"/> : elle nomme un élève et engage sa famille, comme la
/// convocation ou l'exéat (docs/Volume_7_Security.md §7).
/// </summary>
public record GetDischargeNotePdfQuery(Guid AssignmentId) : IRequest<DischargeNotePdfResult>, IAuditableRequest;

public record DischargeNotePdfResult(byte[] Content, string Reference);
