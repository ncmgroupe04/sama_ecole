using SamaEcole.Application.Common.Interfaces;
using MediatR;

namespace SamaEcole.Application.Teachers.Commands.CorrectTeacherMatricule;

/// <summary>
/// PUT /api/v1/teachers/{id}/matricule — corrige le matricule d'un enseignant déjà enregistré
/// (Option 2, demande utilisateur du 10/09/2026). Pendant strict de
/// <c>CorrectStudentMatriculeCommand</c> pour le corps enseignant — voir cette commande pour le
/// raisonnement complet (exception encadrée à AGENTS.md règle #3, Directeur seul, compteur de
/// séquence non touché, journalisation automatique, verrou optimiste).
///
/// <see cref="RowVersion"/> : jeton xmin lu depuis la fiche enseignant.
/// </summary>
public record CorrectTeacherMatriculeCommand(Guid Id, string NewMatricule, uint RowVersion)
    : IRequest<CorrectTeacherMatriculeResult>, IAuditableRequest;

/// <summary>Nouvel état après correction : le matricule appliqué et le jeton de concurrence rafraîchi.</summary>
public record CorrectTeacherMatriculeResult(Guid Id, string Matricule, uint RowVersion);
