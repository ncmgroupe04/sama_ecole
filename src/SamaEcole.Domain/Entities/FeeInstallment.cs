using SamaEcole.Domain.Common;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Une échéance d'un <see cref="FeeInstallmentPlan"/> : un montant et une date, librement négociés
/// (contrairement à la synthèse mensuelle uniforme dérivée d'<see cref="EnrollmentFeeLine"/>). La
/// somme des <see cref="Amount"/> d'un plan doit égaler <see cref="Enrollment.TotalDue"/> — vérifié
/// par le Validator de la commande de création, jamais en base (même parti pris que
/// RecordPaymentCommandValidator pour la borne du versement).
///
/// N'enregistre jamais elle-même de paiement : l'allocation cumulative de
/// <see cref="Enrollment.AmountPaid"/> à chaque échéance (Paid/Partial/Overdue/Pending) est calculée
/// à la volée par InstallmentScheduleCalculator, exactement comme pour les échéances dérivées — une
/// échéance personnalisée n'est qu'un calendrier différent, pas un mécanisme d'encaissement distinct
/// (règle #4 : seul un Payment réel modifie le solde).
/// </summary>
public class FeeInstallment : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public Guid FeeInstallmentPlanId { get; set; }

    /// <summary>Ordre d'affichage et de calcul (1, 2, 3…) — l'allocation cumulative des paiements suit cet ordre.</summary>
    public int SequenceNo { get; set; }

    public required string Label { get; set; }

    public decimal Amount { get; set; }

    public DateOnly DueDate { get; set; }

    public FeeInstallmentPlan FeeInstallmentPlan { get; set; } = null!;
}
