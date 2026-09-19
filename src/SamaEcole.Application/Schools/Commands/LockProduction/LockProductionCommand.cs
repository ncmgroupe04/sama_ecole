using SamaEcole.Application.Common.Interfaces;
using MediatR;

namespace SamaEcole.Application.Schools.Commands.LockProduction;

/// <summary>
/// POST /schools/current/lock-production — « Zone de danger » de l'écran Paramètres : verrouille DE
/// FAÇON DÉFINITIVE la réinitialisation des données de l'établissement COURANT (School.
/// IsProductionLocked), quel que soit le régime test/réel ultérieur.
///
/// Distincte de <c>GoLiveCommand</c> depuis le 19/09/2026 : passer en mode réel est désormais une
/// bascule pleinement réversible qui ne verrouille plus rien par effet de bord. C'est CETTE commande,
/// et elle seule, qui pose le verrou — sur décision EXPLICITE et consciente du Directeur, jamais en
/// conséquence d'une autre action.
///
/// Non rejouable en sens inverse : aucune commande ne remet <c>IsProductionLocked</c> à faux, le
/// verrou est permanent une fois posé.
///
/// Aucun SchoolId dans la commande : l'établissement vient du JWT, jamais du corps de la requête
/// (AGENTS.md règle #10). <paramref name="Confirmation"/> rejoue la garde saisie à l'écran
/// (« VERROUILLER » ou le nom de l'école) — le serveur la revérifie, une modale ne protège que
/// l'interface.
///
/// IAuditableRequest : décision irréversible, elle doit laisser une trace dans le journal d'audit
/// (module « Schools », action « LockProduction »).
/// </summary>
public record LockProductionCommand(string Confirmation)
    : IRequest<LockProductionResult>, IAuditableRequest;

/// <param name="LockedAt">Horodatage du verrouillage définitif qui vient d'être enregistré.</param>
public sealed record LockProductionResult(DateTimeOffset LockedAt);
