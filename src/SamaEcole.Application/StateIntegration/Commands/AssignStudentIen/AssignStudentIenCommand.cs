using MediatR;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Application.StateIntegration.Commands.AssignStudentIen;

/// <summary>
/// PUT /api/v1/state-integration/students/{studentId}/ien — enregistre l'IEN d'un élève
/// (Volume 1 §23.1, ticket JGK-M01).
///
/// DEUX MODES, et un seul est le mode normal :
///   • <paramref name="IenNumber"/> renseigné = l'école a REÇU le numéro officiel du SIMEN et le
///     saisit. C'est le mode attendu, et le seul qui produise un IEN opposable.
///   • <paramref name="IenNumber"/> null = demande de génération d'un numéro PROVISOIRE de secours.
///     À n'utiliser que faute de mieux — voir la mise en garde de <c>IIenGeneratorService</c>.
///
/// Un IEN officiel ÉCRASE un provisoire sans le conserver : garder deux identifiants pour un même
/// élève, c'est garantir qu'un traitement finira par utiliser le mauvais. L'inverse est INTERDIT —
/// aucun numéro provisoire ne remplace un IEN officiel déjà enregistré, et la commande refuse.
/// </summary>
public record AssignStudentIenCommand(Guid StudentId, string? IenNumber)
    : IRequest<AssignStudentIenResult>, IAuditableRequest;

/// <summary>
/// <paramref name="IsProvisional"/> doit être remonté à l'écran : l'utilisateur qui vient de
/// déclencher une génération de secours doit voir immédiatement que le numéro obtenu n'est pas
/// officiel, sans avoir à rouvrir la fiche.
/// </summary>
public record AssignStudentIenResult(Guid StudentId, string IenNumber, bool IsProvisional);
