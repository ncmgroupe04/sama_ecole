using MediatR;

namespace SamaEcole.Application.Students.Queries.GetStudents;

/// <summary>
/// GET /api/v1/students?page=&amp;pageSize=&amp;search= — ticket JGK-D01 (lecture).
///
/// Paginée par construction : une école secondaire sénégalaise compte couramment plus de mille
/// élèves, et une liste non bornée finirait par saturer une connexion mobile (Volume 5 §1).
/// </summary>
public record GetStudentsQuery : IRequest<PaginatedStudents>
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;

    /// <summary>Recherche sur le nom ou le matricule (Volume 5 §1 : « recherche sur toutes les listes »).</summary>
    public string? Search { get; init; }

    /// <summary>Filtre optionnel sur une classe.</summary>
    public Guid? ClassroomId { get; init; }

    /// <summary>Filtre optionnel sur le genre ("M" ou "F").</summary>
    public string? Gender { get; init; }

    /// <summary>
    /// Ne conserver que les élèves AYANT une inscription (non annulée) pour l'année scolaire ACTIVE.
    ///
    /// Optionnel et FAUX par défaut, à dessein : l'API reste par défaut l'annuaire COMPLET des personnes
    /// de l'école. Un élève est une personne qui traverse les années (Student ne porte pas de SchoolYearId) ;
    /// il peut être créé ou importé AVANT d'être (ré)inscrit, et doit rester visible/administrable dans cet
    /// intervalle. C'est l'écran Élèves qui active ce filtre (wwwroot/js/students.js) pour coller à
    /// l'exercice courant — le laisser vrai par défaut casserait le parcours « importer un effectif puis
    /// inscrire », où les élèves ne sont pas encore rattachés à une année. Voir GetStudentsQueryHandler.
    /// </summary>
    public bool ActiveYearOnly { get; init; }
}

public record StudentListItem(
    Guid Id,
    string Matricule,
    string FullName,
    DateOnly BirthDate,
    string? BirthPlace,
    string Gender,
    Guid ClassroomId,
    string ClassroomName,

    /// <summary>URL externe BRUTE, telle que stockée — jamais la photo téléversée (voir PhotoDisplayUrl).
    /// Round-trip fidèle pour le formulaire d'édition : ne jamais y substituer une valeur calculée.</summary>
    string? PhotoUrl,

    /// <summary>Feature B — valeur À AFFICHER (photo téléversée en data: URI si présente, sinon PhotoUrl,
    /// sinon null). Réservée au rendu (avatar), jamais au formulaire d'édition.</summary>
    string? PhotoDisplayUrl,

    string? GuardianName,
    string? GuardianPhone);

public record PaginatedStudents(
    IReadOnlyList<StudentListItem> Items, 
    int TotalCount, 
    int Page, 
    int PageSize,
    int GirlsCount = 0,
    int BoysCount = 0,
    int NewEnrollmentsCount = 0);
