using SamaEcole.Domain.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Inscription (ou réinscription) d'un élève dans une classe pour une année scolaire (ticket JGK-E01,
/// docs/Volume_3_DDS.md §5.4). C'est le point de convergence du MVP : elle relie l'élève (matricule
/// généré à l'enregistrement, JGK-D01/B02), l'année scolaire active (JGK-C01), la classe (JGK-C02) et
/// le barème (JGK-F01) en un seul acte transactionnel.
///
/// <see cref="TotalDue"/> est le montant dû, CALCULÉ par le service d'inscription à partir du barème
/// de la classe — jamais fourni par le client (AGENTS.md règle #10). Une fois posé, aucune route
/// accessible au service Finance ne le modifie : la règle #4 réserve toute correction au
/// Secrétariat/Admin, et elle doit rester historisée. Le détail ligne à ligne est figé dans
/// <see cref="EnrollmentFeeLine"/> pour que le reçu reste fidèle même si le barème change ensuite.
///
/// VERROU OPTIMISTE (AGENTS.md règle #5) : <c>TotalDue</c> est une donnée financière sensible. Le
/// jeton est la colonne système xmin de PostgreSQL, configurée dans EnrollmentConfiguration.
///
/// Unicité (SchoolId, StudentId, SchoolYearId) hors inscriptions annulées : « un élève n'a qu'une
/// inscription active par année scolaire » (DDS §5.4), tenue par la BASE et pas seulement par le C#.
/// </summary>
public class Enrollment : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public Guid StudentId { get; set; }

    /// <summary>Année scolaire de l'inscription. Résolue serveur (année ACTIVE), jamais choisie par le client.</summary>
    public Guid SchoolYearId { get; set; }

    public Guid ClassroomId { get; set; }

    /// <summary>Première inscription ou réinscription (élève déjà connu, sans nouveau matricule).</summary>
    public EnrollmentType Type { get; set; }

    public EnrollmentStatus Status { get; set; } = EnrollmentStatus.Confirmed;

    /// <summary>Montant total dû, en FCFA. Calculé à partir du barème de la classe, jamais négatif.</summary>
    public decimal TotalDue { get; set; }

    public DateTimeOffset EnrolledAt { get; set; }
}
