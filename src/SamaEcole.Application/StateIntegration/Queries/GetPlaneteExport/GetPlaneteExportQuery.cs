using MediatR;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.StateIntegration.Queries.GetPlaneteExport;

/// <summary>
/// GET /api/v1/state-integration/planete/export — matrice « Planète Ready » de l'établissement pour
/// une année scolaire, en JSON ou en CSV (Volume 1 §23.2, Volume 4 §23, ticket JGK-M02).
///
/// <c>IAuditableRequest</c> : exporter l'état civil de tous les élèves d'une école est une sortie de
/// données massive. Elle est journalisée (JGK-H01) au même titre qu'une écriture sensible — savoir QUI
/// a extrait le fichier, et QUAND, est le seul recours en cas de fuite.
///
/// <paramref name="ClassroomId"/> restreint l'export à une classe (contrôle avant transmission
/// globale). Null = tout l'établissement, le cas normal.
/// </summary>
public record GetPlaneteExportQuery(
    Guid SchoolYearId,
    StateExportFormat Format = StateExportFormat.Csv,
    Guid? ClassroomId = null)
    : IRequest<StateExportFile>, IAuditableRequest;
