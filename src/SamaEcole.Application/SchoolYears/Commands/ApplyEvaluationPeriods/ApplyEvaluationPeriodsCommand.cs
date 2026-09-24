using MediatR;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.SchoolYears.Queries.GetTerms;

namespace SamaEcole.Application.SchoolYears.Commands.ApplyEvaluationPeriods;

/// <summary>
/// POST /api/v1/school-years/{id}/apply-evaluation-periods — rejoue le découpage du réglage courant
/// (SchoolSettings.EvaluationPeriodType) sur UNE année scolaire. Réservé au Directeur.
///
/// Refusé dès qu'une note ou une appréciation de bulletin pointe l'une des périodes de l'année :
/// elles perdraient leur période (les notes portent un TermId). Sans donnée, les périodes actuelles
/// sont archivées (suppression logique) et remplacées ; le nouveau découpage ne s'applique sinon
/// qu'aux années créées ensuite. Voir le plan 2026-09-24-evaluation-periods (arbitrage D3).
/// </summary>
public record ApplyEvaluationPeriodsCommand(Guid SchoolYearId)
    : IRequest<IReadOnlyList<TermDto>>, IAuditableRequest;
