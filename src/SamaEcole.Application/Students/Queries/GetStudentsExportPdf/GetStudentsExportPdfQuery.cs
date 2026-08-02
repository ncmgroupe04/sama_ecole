using SamaEcole.Application.Common.Interfaces;
using MediatR;

namespace SamaEcole.Application.Students.Queries.GetStudentsExportPdf;

/// <summary>
/// GET /api/v1/students/export/pdf — liste tabulaire des élèves, filtrable par classe et par année
/// scolaire active, réservée à Directeur/Secrétariat/Finance (docs/Volume_7_Security.md §15, matrice
/// Élèves, ligne « Export PDF »). <see cref="IAuditableRequest"/> : un export est une opération
/// sensible (Volume_7_Security.md §7).
/// </summary>
public record GetStudentsExportPdfQuery : IRequest<StudentsExportPdfResult>, IAuditableRequest
{
    /// <summary>Filtre optionnel sur une classe.</summary>
    public Guid? ClassroomId { get; init; }

    /// <summary>
    /// Ne conserver que les élèves inscrits (non annulés) pour l'année scolaire ACTIVE — même
    /// sémantique que GetStudentsQuery.ActiveYearOnly. FAUX par défaut : sans ce filtre, l'export
    /// couvre l'annuaire complet de l'école.
    /// </summary>
    public bool ActiveYearOnly { get; init; }
}

public record StudentsExportPdfResult(byte[] Content);
