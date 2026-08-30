using MediatR;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Application.StateIntegration.Queries.GetSkillsBookletPdf;

/// <summary>
/// GET /api/v1/state-integration/students/{studentId}/skills-booklet?schoolYearId= — le livret de
/// compétences d'un élève pour une année scolaire, en PDF (Volume 1 §23.6, ticket JGK-M07).
///
/// Recalculé à la demande, rien n'est persisté : régénérer donne toujours le livret à jour. C'est un
/// document APC — il n'a de sens que pour un niveau dont l'école a configuré une grille de
/// compétences (Subject.ParentSubjectId / MaxScore…). Pour un niveau à matières plates (secondaire
/// ordinaire), le Handler renvoie une erreur explicite plutôt qu'un document vide.
///
/// <c>IAuditableRequest</c> : c'est une pièce du dossier scolaire, remise à la famille et à l'école
/// d'accueil lors d'une mutation — sa production est journalisée comme le bulletin.
/// </summary>
public record GetSkillsBookletPdfQuery(Guid StudentId, Guid SchoolYearId)
    : IRequest<SkillsBookletPdfResult>, IAuditableRequest;

public record SkillsBookletPdfResult(byte[] Content, string FileName);
