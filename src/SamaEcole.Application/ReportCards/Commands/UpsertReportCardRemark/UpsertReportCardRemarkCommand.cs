using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;

namespace SamaEcole.Application.ReportCards.Commands.UpsertReportCardRemark;

/// <summary>
/// PUT /api/v1/report-cards/remark — ticket bulletin (JGK-G03), extension utilisateur : coche la
/// distinction du conseil (Blâme… Félicitations) et son observation, pour un élève et un trimestre.
///
/// UPSERT délibéré (pas de Create/Update séparés comme Grade) : au plus une ligne par
/// (élève, trimestre) a un sens, et l'écran de saisie n'a aucune raison de savoir si elle existe déjà
/// — un seul bouton Enregistrer, comme UpdateSchoolSettingsCommand.
///
/// Réservé au Directeur et à l'Enseignant (docs/Volume_7_Security.md « Bulletins » : Générer/Imprimer)
/// — c'est la même préparation du même document, jamais ouvert au Secrétariat.
///
/// IAuditableRequest (JGK-H01) : une distinction/observation figure sur un document officiel remis à
/// la famille, au même titre que la saisie de notes.
/// </summary>
public record UpsertReportCardRemarkCommand(
    Guid StudentId,
    Guid TermId,
    DisciplinaryMention? DisciplinaryMention,
    CouncilDecision? CouncilDecision,
    string? Observations) : IRequest<ReportCardRemarkDto>, IAuditableRequest;
