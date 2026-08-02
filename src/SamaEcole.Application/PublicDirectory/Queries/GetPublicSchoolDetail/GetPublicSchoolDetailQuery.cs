using MediatR;

namespace SamaEcole.Application.PublicDirectory.Queries.GetPublicSchoolDetail;

/// <summary>
/// GET /api/v1/public/schools/{id} — fiche d'un établissement de l'annuaire, ANONYME.
///
/// Un identifiant qui ne correspond à aucune LIGNE DE LA VUE (école inexistante, mais aussi école
/// réelle sans consentement, suspendue ou archivée) donne un 404 indifférencié : la réponse ne doit
/// pas permettre de distinguer « n'existe pas » de « existe mais ne veut pas être listée », ce qui
/// reviendrait à confirmer l'existence d'un établissement ayant précisément refusé sa publication.
/// </summary>
public record GetPublicSchoolDetailQuery(Guid SchoolId) : IRequest<PublicSchoolDto>;
