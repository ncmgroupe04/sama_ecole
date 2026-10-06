using MediatR;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Application.Schools.Commands.SetManagedCycles;

/// <summary>
/// PUT /schools/current/settings/managed-cycles — le Directeur déclare les cycles que son établissement gère
/// (Maternelle, Primaire, College, Lycee). Endpoint dédié, comme grading-scale et establishment-profile :
/// la règle de désactivation ci-dessous n'a rien à faire dans les trente champs d'UpdateSchoolSettingsCommand.
///
/// RÈGLE DE DÉSACTIVATION : retirer un cycle qui contient encore des classes vivantes est REFUSÉ (409
/// <c>CYCLE_HAS_CLASSROOMS</c>) — elles disparaîtraient des écrans qui filtrent sur les cycles gérés. Les
/// classes sans élève comptent aussi : le Directeur les supprime (suppression logique) avant de décocher.
/// Ajouter un cycle ne se refuse jamais.
///
/// Aucun SchoolId : l'établissement vient du JWT (AGENTS.md règle #10). IAuditableRequest : un réglage
/// structurant laisse une trace dans le journal d'audit.
/// </summary>
/// <param name="Cycles">Noms de CycleType : au moins un, sans doublon.</param>
public record SetManagedCyclesCommand(IReadOnlyList<string> Cycles)
    : IRequest<SchoolSettingsDto>, IAuditableRequest;
