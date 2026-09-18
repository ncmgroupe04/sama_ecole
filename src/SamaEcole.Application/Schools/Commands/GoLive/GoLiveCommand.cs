using SamaEcole.Application.Common.Interfaces;
using MediatR;

namespace SamaEcole.Application.Schools.Commands.GoLive;

/// <summary>
/// POST /schools/current/go-live — bascule l'établissement COURANT du mode test (bac à sable) vers le
/// mode réel (exploitation). Action DÉLIBÉRÉE du Directeur, jamais un effet de bord.
///
/// Conséquence : la « Zone de danger » (réinitialisation des données) devient indisponible, POUR
/// TOUJOURS — même après un retour en mode test (<c>RevertToTestCommand</c>, disponible à tout
/// moment) : voir <c>School.HasEverGoneLive</c>, posé ici et jamais effacé.
///
/// Aucun SchoolId dans la commande : l'établissement vient du JWT (AGENTS.md règle #10).
/// <paramref name="Confirmation"/> rejoue la garde saisie à l'écran (« CONFIRMER » ou le nom de
/// l'école) — le serveur la revérifie, une modale ne protège que l'interface.
///
/// IAuditableRequest : changement structurant irréversible, il doit laisser une trace dans le journal
/// d'audit (module « Schools », action « GoLive »).
/// </summary>
public record GoLiveCommand(string Confirmation)
    : IRequest<GoLiveResult>, IAuditableRequest;

/// <param name="WentLiveAt">Horodatage du passage en mode réel qui vient d'être enregistré.</param>
public sealed record GoLiveResult(DateTimeOffset WentLiveAt);
