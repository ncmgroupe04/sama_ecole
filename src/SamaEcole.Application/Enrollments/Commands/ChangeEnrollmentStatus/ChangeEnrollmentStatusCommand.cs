using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;

namespace SamaEcole.Application.Enrollments.Commands.ChangeEnrollmentStatus;

/// <summary>
/// POST /api/v1/enrollments/{id}/status — déclare un ABANDON (<see cref="EnrollmentStatus.DroppedOut"/>)
/// ou un TRANSFERT (<see cref="EnrollmentStatus.Transferred"/>) en cours d'année. Réservé au Directeur
/// et au Secrétariat (EnrollmentsController.EnrollmentWriters).
///
/// À la différence de CancelEnrollmentCommand (erreur de saisie, réservée aux inscriptions jamais
/// encaissées), cette commande s'applique précisément à une scolarité RÉELLE qui s'arrête en cours de
/// route : l'inscription N'EST PAS annulée, elle change de statut. Effets du Handler :
///   • L'élève sort des futures listes de présence actives (InitializeAttendanceSheetQueryHandler
///     exclut les élèves dont l'inscription de l'année active est DroppedOut/Transferred).
///   • Aucun appel de frais mensuel supplémentaire n'est généré — non par une action explicite ici,
///     mais parce qu'AUCUN mécanisme de génération récurrente n'existe dans ce dépôt : TotalDue est
///     calculé une seule fois à l'inscription (CreateEnrollmentCommandHandler.BuildFeeLinesAsync) et
///     n'est jamais recalculé mensuellement. Ce changement de statut n'a donc rien à arrêter — il
///     documente seulement l'état, pour qu'une éventuelle génération future en tienne compte.
///   • Notes et paiements déjà enregistrés restent intacts : ce Handler ne touche ni Grade ni Payment.
///
/// Seuls <see cref="EnrollmentStatus.DroppedOut"/> et <see cref="EnrollmentStatus.Transferred"/> sont
/// acceptés comme <see cref="NewStatus"/> (voir ChangeEnrollmentStatusCommandValidator) : les autres
/// transitions ont leur propre commande dédiée (CreateEnrollmentCommand pour Confirmed,
/// CancelEnrollmentCommand pour Cancelled), chacune avec ses propres règles métier.
///
/// <see cref="RowVersion"/> : verrouillage optimiste (AGENTS.md règle #5), jeton xmin lu depuis
/// AcademicHistoryEntryDto.RowVersion.
/// </summary>
public record ChangeEnrollmentStatusCommand(Guid Id, EnrollmentStatus NewStatus, uint RowVersion)
    : IRequest<Unit>, IAuditableRequest;
