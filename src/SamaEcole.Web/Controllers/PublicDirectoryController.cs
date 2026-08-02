using SamaEcole.Application.PublicDirectory;
using SamaEcole.Application.PublicDirectory.Queries.GetPublicSchoolDetail;
using SamaEcole.Application.PublicDirectory.Queries.GetPublicSchools;
using SamaEcole.Web.RateLimiting;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Annuaire PUBLIC des établissements (B2C) — le seul endroit de l'application où des données sont
/// servies à un visiteur qui n'appartient à aucun établissement. Entièrement ANONYME et en LECTURE
/// SEULE : aucun verbe d'écriture n'y a sa place, aujourd'hui ni demain.
///
/// TROIS BARRIÈRES INDÉPENDANTES tiennent l'étanchéité vis-à-vis des données scolaires, et aucune ne
/// dépend de la vigilance du code écrit ici :
///
///   1. La RLS PostgreSQL échoue EN FERMETURE. Une requête anonyme n'a pas de tenant, donc la variable
///      de session `app.current_school_id` est absente : les policies des tables élèves, notes,
///      paiements comparent leur SchoolId à NULL et ne renvoient AUCUNE ligne. Il ne s'agit pas d'un
///      filtre qu'on aurait pensé à écrire — c'est le comportement par défaut de la base.
///   2. La VUE `public_school_directory` fige les colonnes servies et la condition de consentement
///      (voir la migration AddPublicSchoolDirectory). Les Handlers n'interrogent qu'elle, jamais la
///      table `schools`.
///   3. L'API Data de Supabase (PostgREST) est fermée : le schéma `public` a été retiré des schémas
///      exposés et les rôles `anon`/`authenticated` n'ont aucun droit. Cet annuaire n'est donc joignable
///      QUE par ce contrôleur — donc toujours à travers le rate limiting et la journalisation.
///
/// Ce contrôleur n'ouvre AUCUN accès aux données d'un établissement : ni élèves, ni notes, ni
/// présences, ni finances. Il ne constitue donc pas le « portail Parents/Élèves » reporté en V3
/// (AGENTS.md, périmètre V1) — il ne fait qu'exposer une vitrine d'établissements consentants, au même
/// titre qu'une plaquette ou une fiche d'annuaire téléphonique.
/// </summary>
[ApiController]
[Route("api/v1/public/schools")]
[AllowAnonymous]
[EnableRateLimiting(SensitiveEndpointRateLimiting.PublicDirectoryPolicyName)]
public class PublicDirectoryController(ISender mediator) : ControllerBase
{
    /// <summary>
    /// Liste paginée des établissements ayant consenti à figurer dans l'annuaire, filtrable par ville,
    /// région, cycle et nom. La requête est liée depuis la chaîne de requête ; elle ne porte AUCUN
    /// identifiant d'établissement, et il n'existe aucun paramètre permettant d'atteindre une école
    /// non listée — la vue sous-jacente ne la contient tout simplement pas.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<PaginatedPublicSchools>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> List(
        [FromQuery] GetPublicSchoolsQuery query, CancellationToken cancellationToken)
        => Ok(await mediator.Send(query, cancellationToken));

    /// <summary>
    /// Fiche d'un établissement. Un identifiant valide mais dont l'école n'a pas consenti (ou est
    /// suspendue/archivée) donne le MÊME 404 qu'un identifiant inexistant : voir
    /// <see cref="GetPublicSchoolDetailQuery"/> pour le raisonnement.
    /// </summary>
    [HttpGet("{schoolId:guid}")]
    [ProducesResponseType<PublicSchoolDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> GetDetail(Guid schoolId, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetPublicSchoolDetailQuery(schoolId), cancellationToken));
}
