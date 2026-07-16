using MediatR;

namespace SamaEcole.Application.SchoolYears.Queries.GetTerms;

/// <summary>
/// GET /api/v1/school-years/{id}/terms — ticket JGK-G01. Les trois trimestres générés automatiquement
/// à la création de l'année (voir CreateSchoolYearCommandHandler) : l'écran de saisie de notes en a
/// besoin pour proposer une période, aucun endpoint ne les crée directement.
/// </summary>
public record GetTermsQuery(Guid SchoolYearId) : IRequest<IReadOnlyList<TermDto>>;

public record TermDto(Guid Id, string Label, int Order, DateOnly StartDate, DateOnly EndDate);
