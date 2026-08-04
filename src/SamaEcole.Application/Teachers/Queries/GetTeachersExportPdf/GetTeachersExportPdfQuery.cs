using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;

namespace SamaEcole.Application.Teachers.Queries.GetTeachersExportPdf;

/// <summary>
/// GET /api/v1/teachers/export/pdf — « LISTE DES ENSEIGNANTS », pendant de l'export élèves
/// (<c>GetStudentsExportPdfQuery</c>) : même périmètre que la liste écran, sans pagination.
/// <see cref="IAuditableRequest"/> : un export est une opération sensible (docs/Volume_7_Security.md §7).
/// </summary>
public record GetTeachersExportPdfQuery : IRequest<TeachersExportPdfResult>, IAuditableRequest
{
    /// <summary>Filtre optionnel sur le statut (Actif / Inactif). Null = tous les statuts.</summary>
    public EntityStatus? Status { get; init; }
}

public record TeachersExportPdfResult(byte[] Content);
