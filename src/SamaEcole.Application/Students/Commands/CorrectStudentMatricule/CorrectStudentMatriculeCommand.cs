using SamaEcole.Application.Common.Interfaces;
using MediatR;

namespace SamaEcole.Application.Students.Commands.CorrectStudentMatricule;

/// <summary>
/// PUT /api/v1/students/{id}/matricule — corrige le matricule d'un élève déjà enregistré (Option 2,
/// demande utilisateur du 10/09/2026).
///
/// EXCEPTION délibérée à la règle « le matricule n'est jamais modifiable » (AGENTS.md règle #3,
/// rappelée dans UpdateStudentCommand) : le matricule reste généré automatiquement à
/// l'enregistrement et absent du formulaire d'édition ordinaire ; seul le DIRECTEUR, sur un
/// endpoint DÉDIÉ, peut le rectifier — pour réparer une saisie erronée ou aligner un dossier repris
/// d'un ancien système. Réservé au Directeur (StudentsController), pas au Secrétariat comme le
/// reste de la fiche.
///
/// La règle #3 vise à empêcher qu'un matricule soit consommé PUIS abandonné (trou dans la
/// numérotation) ou réémis à la légère : elle n'interdit pas une correction encadrée. Le compteur
/// de séquence n'est pas touché ici — corriger « ELEV-2026-0007 » en « ELEV-2026-0071 » ne change
/// ni le dernier numéro attribué ni le prochain.
///
/// <see cref="IAuditableRequest"/> : la modification d'un identifiant officiel est une écriture
/// sensible, journalisée automatiquement (AuditLoggingBehavior).
///
/// <see cref="RowVersion"/> : verrouillage optimiste (AGENTS.md règle #5), jeton xmin lu depuis
/// StudentIdentityDto.RowVersion.
/// </summary>
public record CorrectStudentMatriculeCommand(Guid Id, string NewMatricule, uint RowVersion)
    : IRequest<CorrectStudentMatriculeResult>, IAuditableRequest;

/// <summary>Nouvel état après correction : le matricule appliqué et le jeton de concurrence rafraîchi.</summary>
public record CorrectStudentMatriculeResult(Guid Id, string Matricule, uint RowVersion);
