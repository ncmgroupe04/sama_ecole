using MediatR;

namespace SamaEcole.Application.PublicDirectory.Queries.GetPublicSchools;

/// <summary>
/// GET /api/v1/public/schools — annuaire B2C, ANONYME et en lecture seule.
///
/// Ne porte AUCUN identifiant d'établissement : à l'inverse du reste de l'application, il n'y a ici ni
/// JWT ni tenant courant. Le périmètre des données n'est donc pas déduit d'un appelant — il est figé
/// par la vue <c>public_school_directory</c>, qui ne contient que les écoles ayant consenti.
///
/// Volontairement PAS <c>IAuditableRequest</c> : consulter un annuaire public est une navigation
/// anonyme, pas une opération sensible d'établissement. La journaliser remplirait le journal d'audit —
/// lui-même cloisonné par école — d'entrées sans école ni utilisateur.
/// </summary>
public record GetPublicSchoolsQuery : IRequest<PaginatedPublicSchools>
{
    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 12;

    /// <summary>Recherche sur le nom de l'établissement (insensible à la casse).</summary>
    public string? Search { get; init; }

    /// <summary>Filtre exact sur la ville (le critère principal d'un parent).</summary>
    public string? City { get; init; }

    /// <summary>Filtre exact sur la région.</summary>
    public string? Region { get; init; }

    /// <summary>
    /// Filtre sur un cycle proposé, en valeur BRUTE de l'énumération (« Primaire », « Lycee »), pas en
    /// libellé accentué : un paramètre d'URL doit rester stable et indépendant de l'affichage.
    /// </summary>
    public string? Cycle { get; init; }
}
