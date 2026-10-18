using SamaEcole.Application.Common.Interfaces;
using MediatR;

namespace SamaEcole.Application.SchoolYears.Commands.DeleteSchoolYear;

/// <summary>
/// DELETE /api/v1/school-years/{id} — le Directeur retire une année scolaire de son établissement.
///
/// DEUX RÉGIMES, selon le mode de l'établissement (School.WentLiveAt) :
///
///   * MODE RÉEL — suppression guidée (spec 2026-10-17) : un paiement non annulé sur une inscription de
///     l'année la bloque définitivement (409 FINANCIAL_RECORDS_EXIST) ; tout autre lien (inscriptions sans
///     paiement, notes, appels, examens…) donne un 409 DEPENDENCIES_EXIST qui les énumère, et ne se rompt
///     que par l'appel explicite POST /school-years/{id}/unlink-dependencies, après confirmation. L'année est
///     alors ARCHIVÉE, en suppression logique, avec sa configuration (trimestres, affectations, fermetures).
///     L'export ZIP (GET /school-years/{id}/export) reste la voie d'archivage hors plateforme.
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
