using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.SchoolCalendar;

/// <summary>Fermeture du calendrier scolaire (intervalle fermé, dates incluses).</summary>
public record SchoolClosureDto(
    Guid Id,
    Guid SchoolYearId,
    DateOnly StartDate,
    DateOnly EndDate,
    SchoolClosureKind Kind,
    string Label,
    string? LabelAr);
