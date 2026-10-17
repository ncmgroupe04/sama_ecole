using SamaEcole.Domain.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Période de fermeture du calendrier scolaire : jour férié, vacances ou autre jour non travaillé,
/// rattachée à une <see cref="SchoolYear"/> (Franco-Arabe Bloc 5, docs/superpowers/specs/
/// 2026-10-14-franco-arabe-cahier-de-roulement-design.md §2.3).
///
/// Elle sert au CAHIER DE ROULEMENT (jours de classe d'un mois, cases grisées). Elle ne pilote PAS
/// <c>WorkingDayGuard</c> en v1 : un appel saisi un jour fermé reste accepté (décision validée le
/// 2026-10-14), pour ne rien changer au comportement en production.
///
/// Intervalle FERMÉ : <see cref="StartDate"/> et <see cref="EndDate"/> sont tous deux inclus ; un
/// jour férié isolé a <c>StartDate == EndDate</c>.
/// </summary>
public class SchoolClosure : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public Guid SchoolYearId { get; set; }

    public DateOnly StartDate { get; set; }

    /// <summary>Dernier jour fermé (inclus), jamais antérieur à <see cref="StartDate"/>.</summary>
    public DateOnly EndDate { get; set; }

    public SchoolClosureKind Kind { get; set; } = SchoolClosureKind.Holiday;

    /// <summary>Libellé français, ex. « Tabaski ».</summary>
    public required string Label { get; set; }

    /// <summary>
    /// Libellé arabe imprimé sur le cahier bilingue. Null = non saisi : le document n'invente aucune
    /// traduction et retombe sur le libellé français.
    /// </summary>
    public string? LabelAr { get; set; }

    /// <summary>Vrai si <paramref name="date"/> tombe dans l'intervalle fermé [StartDate, EndDate].</summary>
    public bool Covers(DateOnly date) => date >= StartDate && date <= EndDate;
}
