using SamaEcole.Domain.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Domain.Entities;

public class CashierSession : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public Guid CashierId { get; set; }

    public DateTimeOffset OpenedAt { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }

    public decimal OpeningBalance { get; set; }

    /// <summary>
    /// Solde de fermeture THÉORIQUE, toutes méthodes de paiement confondues (fonds d'ouverture + somme
    /// des encaissements de la session, Volume 1 §15.2) — c'est la figure déjà affichée en direct sur
    /// /caisse pendant la journée (<see cref="Application.Finance.Queries.GetCurrentCashierSession"/>).
    /// À ne jamais confondre avec <see cref="ExpectedCashAmount"/> : un versement par virement ou mobile
    /// money entre dans CE total, mais ne sera jamais dans le tiroir-caisse physique.
    /// </summary>
    public decimal? ClosingBalance { get; set; }

    /// <summary>
    /// Ticket JGK-F09 — contrôle du comptage physique. Espèces attendues dans le tiroir-caisse au
    /// moment de la clôture : fonds d'ouverture + SEULS les encaissements en espèces de la session
    /// (jamais les virements, chèques ou mobile money, qui ne transitent jamais par le tiroir). C'est
    /// cette figure, et non <see cref="ClosingBalance"/>, qui sert de référence à l'écart de caisse.
    /// </summary>
    public decimal? ExpectedCashAmount { get; set; }

    /// <summary>Espèces réellement comptées dans le tiroir-caisse par le caissier à la clôture (JGK-F09) — obligatoire pour clôturer.</summary>
    public decimal? ActualCashAmount { get; set; }

    /// <summary>ActualCashAmount − ExpectedCashAmount. Nul quand la caisse tombe juste.</summary>
    public decimal? DiscrepancyAmount { get; set; }

    /// <summary>Obligatoire dès que DiscrepancyAmount est non nul (JGK-F09) ; jamais rempli sinon.</summary>
    public string? DiscrepancyReason { get; set; }

    public CashierSessionStatus Status { get; set; } = CashierSessionStatus.Open;

    public User Cashier { get; set; } = null!;
}
