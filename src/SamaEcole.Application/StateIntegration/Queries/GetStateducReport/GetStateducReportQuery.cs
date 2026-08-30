using MediatR;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Application.StateIntegration.Queries.GetStateducReport;

/// <summary>
/// GET /api/v1/state-integration/stateduc — rapport annuel STATEDUC (Volume 1 §23.3, ticket JGK-M04).
///
/// Renvoie le DTO agrégé. Le rendu (PDF A4 paysage, classeur .xlsx) est demandé par deux routes
/// distinctes qui réutilisent CE Handler : une seule définition des agrégats, jamais un second calcul
/// « pour l'Excel » qui donnerait d'autres chiffres que le PDF déposé à l'IEF.
///
/// <paramref name="ObservationDate"/> est la date à laquelle les âges sont calculés et l'effectif
/// arrêté. Null = aujourd'hui. Elle est explicite parce qu'une école qui réédite son rapport en
/// décembre pour l'année écoulée doit pouvoir retrouver les chiffres qu'elle a déclarés en juin.
/// </summary>
public record GetStateducReportQuery(Guid SchoolYearId, DateOnly? ObservationDate = null)
    : IRequest<StateducReportDto>, IAuditableRequest;
