using SamaEcole.Domain.Common;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Année scolaire d'un établissement (ticket JGK-C01) — le PIVOT du reste du produit : les frais
/// sont paramétrés par année (JGK-F01), les inscriptions la portent (JGK-E01), les bulletins la
/// datent. Une donnée rattachée à la mauvaise année est une donnée perdue.
///
/// Deux invariants, tenus par la BASE et pas seulement par le C# :
///
///   * UNE SEULE année active par école à tout instant — index unique partiel
///     UX_school_years_single_active (migration AddSchoolYears). Deux requêtes concurrentes ne
///     peuvent donc pas activer deux années : PostgreSQL en refuse une, et le conflit remonte en 409.
///
///   * Les années PASSÉES sont en lecture seule : aucune route ne modifie une année existante, et la
///     seule mutation possible — l'activation — est refusée sur une année déjà terminée.
///
/// À ne pas confondre avec <see cref="AcademicYear"/> : celle-ci est DÉDUITE de la date (bascule
/// d'octobre) et sert à numéroter les matricules ; une SchoolYear est DÉCLARÉE par l'établissement,
/// avec ses propres dates. Le libellé est un texte d'affichage, jamais une clé de calcul.
/// </summary>
public class SchoolYear : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    /// <summary>Libellé affiché, ex. « 2026-2027 ». Unique au sein de l'école.</summary>
    public required string Label { get; set; }

    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }

    /// <summary>L'année sur laquelle l'établissement travaille. Une seule à la fois.</summary>
    public bool IsActive { get; set; }

    /// <summary>Année terminée : en lecture seule, et plus jamais activable.</summary>
    public bool IsClosedOn(DateOnly today) => EndDate < today;
}
