using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;

namespace SamaEcole.Application.Finance.Commands.RecordPayment;

/// <summary>
/// POST /finance/payments — ticket JGK-F02. Encaisse un versement contre une inscription. Le montant
/// dû (<c>TotalDue</c>) n'est jamais fourni ni modifié par le client : la Finance ne fait qu'imputer un
/// versement sur le solde figé à l'inscription (AGENTS.md règles #4 et #10). L'inscription est identifiée
/// par son Id ; l'école vient du JWT.
///
/// IAuditableRequest (JGK-H01) : « paiements » fait partie des écritures sensibles explicitement
/// listées par le journal d'audit centralisé.
/// </summary>
public record RecordPaymentCommand(
    Guid EnrollmentId,
    decimal Amount,
    PaymentMethod Method,
    PaymentCategory Category = PaymentCategory.Tuition,
    string? ReferencePeriod = null,
    List<PaymentBreakdownDto>? Breakdowns = null,

    /// <summary>
    /// Taux de TVA (fraction, ex. 0.18) si ce versement est assujetti, ou null (défaut) sinon — la
    /// scolarité (Category par défaut) est typiquement exonérée. Déclaré explicitement par la Finance,
    /// jamais déduit automatiquement d'une catégorie.
    /// </summary>
    decimal? VatRate = null)
    : IRequest<RecordPaymentResult>, IAuditableRequest;

public record PaymentBreakdownDto(Guid FeeCategoryId, decimal AmountAllocated);


/// <summary>
/// Résultat d'un encaissement : de quoi confirmer à la caisse et imprimer le reçu (le numéro officiel),
/// et afficher le nouveau solde sans recharger la fiche.
/// </summary>
public record RecordPaymentResult(
    Guid PaymentId,
    string ReceiptNumber,
    decimal Amount,
    decimal TotalDue,
    decimal AmountPaid,
    decimal RemainingBalance,
    string Status);
