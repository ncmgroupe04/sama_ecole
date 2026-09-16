using SamaEcole.Application.Common.Interfaces;
using MediatR;

namespace SamaEcole.Application.SchoolYears.Commands.DeleteSchoolYear;

/// <summary>
/// DELETE /api/v1/school-years/{id} — le Directeur retire une année scolaire de son établissement.
///
/// DEUX RÉGIMES, selon le mode de l'établissement (School.WentLiveAt) :
///
///   * MODE TEST — l'année et TOUT ce qu'elle porte (inscriptions, paiements, notes, appels, examens)
///     sont effacés définitivement, par la fonction <c>delete_school_year</c>. C'est la même exception
///     bornée à la règle #6 que la « Zone de danger ».
///
///   * MODE RÉEL — l'année n'est supprimée que si elle ne porte AUCUNE donnée (cas réel : une année
///     créée par erreur, ou préparée puis abandonnée). Elle est alors ARCHIVÉE, en suppression logique.
///     Une année qui a servi est refusée en 409 : ses inscriptions et ses paiements sont la
///     contrepartie de reçus déjà remis aux parents, et la règle #6 les déclare inaltérables. L'export
///     ZIP (GET /school-years/{id}/export) reste la voie d'archivage hors plateforme.
///
/// <paramref name="Confirmation"/> : le LIBELLÉ EXACT de l'année (« 2025-2026 »), revérifié côté
/// serveur — une modale ne protège que les appelants qui passent par l'interface.
///
/// Aucun SchoolId : l'établissement vient du JWT (AGENTS.md règle #10). IAuditableRequest — retirer un
/// exercice d'un établissement doit laisser une trace, que la suppression, elle, ne touche pas.
/// </summary>
public record DeleteSchoolYearCommand(Guid Id, string Confirmation)
    : IRequest<DeleteSchoolYearResult>, IAuditableRequest;

/// <param name="Label">Libellé de l'année supprimée, pour le message de confirmation à l'écran.</param>
/// <param name="WasActive">Vrai si l'établissement travaillait sur cette année.</param>
/// <param name="WasPurged">
/// Vrai en mode test (effacement définitif), faux en mode réel (archivage — suppression logique).
/// </param>
/// <param name="NewActiveYear">
/// L'année sur laquelle l'établissement a rebasculé automatiquement, ou <c>null</c> s'il n'y avait
/// aucune candidate — voir <paramref name="RequiresActiveYearSelection"/>.
/// </param>
/// <param name="RequiresActiveYearSelection">
/// Vrai quand l'établissement se retrouve SANS année active : aucune année ouverte ne restait. Le
/// Directeur doit en activer ou en créer une — l'écran le lui demande.
/// </param>
/// <param name="Summary">Détail de ce qui a été effacé (mode test uniquement), sinon <c>null</c>.</param>
public sealed record DeleteSchoolYearResult(
    string Label,
    bool WasActive,
    bool WasPurged,
    SchoolYearDto? NewActiveYear,
    bool RequiresActiveYearSelection,
    SchoolDataResetSummary? Summary);
