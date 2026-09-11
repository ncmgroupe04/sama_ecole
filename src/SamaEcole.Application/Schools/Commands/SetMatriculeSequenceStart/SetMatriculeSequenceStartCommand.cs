using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;

namespace SamaEcole.Application.Schools.Commands.SetMatriculeSequenceStart;

/// <summary>
/// PUT /api/v1/schools/current/settings/matricule-sequences — fixe le NUMÉRO DE DÉPART de la
/// numérotation des matricules pour l'année scolaire en cours (Option 1, demande utilisateur du
/// 10/09/2026).
///
/// Le gabarit du matricule (« ELEV-{YEAR}-{SEQ:4} ») reste réglé ailleurs
/// (UpdateSchoolSettingsCommand) : cette commande ne touche QUE le compteur <c>{SEQ}</c>. Elle
/// permet à une école qui reprend une numérotation existante — ou qui veut réserver une plage —
/// de démarrer, par exemple, au numéro 1000 plutôt qu'à 1.
///
/// <see cref="Kind"/> ne vaut que <see cref="MatriculeKind.Student"/> ou
/// <see cref="MatriculeKind.Teacher"/> : les compteurs de reçus et de certificats de mutation ont
/// un gabarit FIXE (pièces officielles) et ne se règlent pas.
///
/// <see cref="NextValue"/> est le numéro que portera le PROCHAIN matricule généré. Le Handler refuse
/// une valeur ≤ au dernier numéro déjà attribué cette année : rétrograder le compteur réémettrait
/// des numéros déjà en circulation (AGENTS.md règle #3 — numérotation unique et sans trou).
///
/// L'année visée est TOUJOURS l'année scolaire courante (bascule d'octobre, voir AcademicYear) :
/// les compteurs sont propres à chaque année et repartent naturellement à la rentrée suivante.
/// </summary>
public record SetMatriculeSequenceStartCommand(MatriculeKind Kind, int NextValue)
    : IRequest<MatriculeSequenceInfo>, IAuditableRequest;
