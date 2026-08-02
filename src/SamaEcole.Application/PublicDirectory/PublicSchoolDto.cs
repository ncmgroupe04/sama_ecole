namespace SamaEcole.Application.PublicDirectory;

/// <summary>
/// Fiche d'établissement telle que servie à un visiteur ANONYME de l'annuaire (B2C).
///
/// Aucun identifiant interne au-delà de <see cref="Id"/> (nécessaire au lien vers la fiche), aucun
/// statut, aucune mention fiscale : ce contrat est volontairement plus pauvre que celui du Directeur
/// sur son propre établissement (SchoolProfileDto). La restriction ne repose pas seulement sur ce
/// type — la vue SQL sous-jacente l'impose déjà (voir PublicSchoolListing).
/// </summary>
public record PublicSchoolDto(
    Guid Id,
    string Name,
    string? City,
    string? Region,
    string? Description,
    string? LogoUrl,
    string? Address,
    string? Phone,
    string? Email,

    /// <summary>Cycles proposés, en libellés lisibles (« Primaire », « Collège »…) — voir CycleLabels.</summary>
    IReadOnlyList<string> Cycles);

/// <summary>Page de résultats de l'annuaire. Même forme que les listes internes (Items + TotalCount).</summary>
public record PaginatedPublicSchools(
    IReadOnlyList<PublicSchoolDto> Items,
    int TotalCount,
    int Page,
    int PageSize);
