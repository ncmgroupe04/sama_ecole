using SamaEcole.Application.Common.Interfaces;
using MediatR;

namespace SamaEcole.Application.Schools.Commands.GoLive;

/// <summary>
/// POST /schools/current/go-live — bascule l'établissement COURANT du mode test (bac à sable) vers le
/// mode réel (exploitation). Action DÉLIBÉRÉE du Directeur, jamais un effet de bord.
///
/// Conséquence, UNIQUEMENT le temps du mode réel : la « Zone de danger » (réinitialisation des
/// données) devient indisponible (409 RESET_UNAVAILABLE_LIVE_MODE). Un retour en mode test
/// (<c>RevertToTestCommand</c>, disponible à tout moment) la rouvre — cette bascule ne pose plus
/// aucun verrou permanent depuis le 19/09/2026 ; seul <c>LockProductionCommand</c>, une action
/// manuelle distincte, verrouille la purge de façon définitive.
///
/// Aucun SchoolId dans la commande : l'établissement vient du JWT (AGENTS.md règle #10).
/// <paramref name="Confirmation"/> rejoue la garde saisie à l'écran (« CONFIRMER » ou le nom de
/// l'école) — le serveur la revérifie, une modale ne protège que l'interface.
///
/// IAuditableRequest : changement structurant, il doit laisser une trace dans le journal
/// d'audit (module « Schools », action « GoLive »).
/// </summary>
public record GoLiveCommand(string Confirmation)
    : IRequest<GoLiveResult>, IAuditableRequest;

/// <param name="WentLiveAt">Horodatage du passage en mode réel qui vient d'être enregistré.</param>
public sealed record GoLiveResult(DateTimeOffset WentLiveAt);
