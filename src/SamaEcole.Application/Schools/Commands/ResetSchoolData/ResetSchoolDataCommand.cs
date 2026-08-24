using SamaEcole.Application.Common.Interfaces;
using MediatR;

namespace SamaEcole.Application.Schools.Commands.ResetSchoolData;

/// <summary>
/// POST /schools/current/reset-data — « Zone de danger » de l'écran Paramètres : le Directeur remet
/// son établissement à neuf après une phase d'essai.
///
/// Aucun SchoolId dans la commande : l'établissement vient du JWT, jamais du corps de la requête
/// (AGENTS.md règle #10). Sans quoi cette commande deviendrait l'arme la plus dangereuse de
/// l'application — un Directeur pourrait viser l'école d'un confrère.
///
/// <paramref name="Confirmation"/> rejoue la garde saisie à l'écran : le serveur la revérifie, car
/// une modale ne protège que les appelants qui passent par l'interface.
///
/// IAuditableRequest : l'opération est irréversible, elle doit laisser une trace dans le journal
/// d'audit — que la purge, précisément, ne touche pas.
/// </summary>
public record ResetSchoolDataCommand(string Confirmation)
    : IRequest<SchoolDataResetSummary>, IAuditableRequest;
