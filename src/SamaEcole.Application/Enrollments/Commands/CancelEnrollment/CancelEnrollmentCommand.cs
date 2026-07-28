using SamaEcole.Application.Common.Interfaces;
using MediatR;

namespace SamaEcole.Application.Enrollments.Commands.CancelEnrollment;

/// <summary>
/// DELETE /api/v1/enrollments/{id} — annule une inscription saisie par ERREUR (mauvais élève, mauvaise
/// classe…). Réservé au Directeur et au Secrétariat (EnrollmentsController.EnrollmentWriters).
///
/// Ne jamais supprimer physiquement une inscription (AGENTS.md règle #6) : elle porte l'historique
/// comptable. Le Handler positionne <c>Status = EnrollmentStatus.Cancelled</c> SANS appeler
/// <c>SoftDelete</c> — convention déjà en place dans ce module (voir EnrollmentConfiguration :
/// l'index d'unicité et CreateEnrollmentCommandHandler testent <c>Status != Cancelled</c>
/// indépendamment de <c>IsDeleted</c>). Un soft delete masquerait la ligne du Global Query Filter et la
/// ferait disparaître de l'historique scolaire (GetStudentDetailQueryHandler.academicHistory), qui
/// doit au contraire continuer à montrer « inscription annulée » — jamais un trou silencieux.
///
/// UNIQUEMENT si aucun paiement n'a encore été encaissé sur cette inscription — sinon voir
/// ChangeEnrollmentStatusCommand (abandon/transfert), qui préserve l'inscription et son historique de
/// paiement au lieu de prétendre qu'elle n'a jamais eu lieu.
///
/// <paramref name="RowVersion"/> : verrouillage optimiste (AGENTS.md règle #5), même contrat que
/// UpdateGradeCommand — le jeton xmin vient de AcademicHistoryEntryDto.RowVersion.
/// </summary>
public record CancelEnrollmentCommand(Guid Id, uint RowVersion) : IRequest<Unit>, IAuditableRequest;
