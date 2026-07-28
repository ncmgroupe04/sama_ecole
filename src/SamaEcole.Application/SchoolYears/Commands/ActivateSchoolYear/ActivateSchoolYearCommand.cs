using MediatR;

namespace SamaEcole.Application.SchoolYears.Commands.ActivateSchoolYear;

/// <summary>
/// POST /api/v1/school-years/{id}/activate — ticket JGK-C01, « une seule année active à la fois ».
///
/// Activer une année fait basculer TOUT l'établissement : les inscriptions, les frais et les
/// bulletins du jour se rattacheront désormais à cet exercice. C'est pourquoi
/// docs/Volume_7_Security.md §16 range « changement de l'année scolaire active » parmi les
/// opérations à double confirmation : le Directeur ressaisit son mot de passe. Un poste laissé
/// déverrouillé cinq minutes ne suffit donc pas à faire basculer une école entière.
/// </summary>
public record ActivateSchoolYearCommand(Guid Id, string Password) : IRequest<SchoolYearDto>;
