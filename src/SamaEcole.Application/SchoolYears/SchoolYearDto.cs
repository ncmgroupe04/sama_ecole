namespace SamaEcole.Application.SchoolYears;

/// <summary>
/// Une année scolaire telle que l'API la restitue (openapi.yaml §SchoolYear, ticket JGK-C01).
///
/// <paramref name="IsClosed"/> est CALCULÉ à la lecture, jamais stocké : une année se termine toute
/// seule le jour venu, et une colonne le disant se démoderait dès le lendemain de sa dernière
/// écriture. C'est lui qui dit à l'interface ce qu'elle doit griser — les années passées sont en
/// lecture seule (ticket JGK-C01).
/// </summary>
public record SchoolYearDto(
    Guid Id,
    string Label,
    DateOnly StartDate,
    DateOnly EndDate,
    bool IsActive,
    bool IsClosed);
