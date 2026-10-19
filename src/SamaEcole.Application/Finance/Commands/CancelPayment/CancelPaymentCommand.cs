using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;

namespace SamaEcole.Application.Finance.Commands.CancelPayment;

/// <summary>
/// POST /finance/payments/{id}/cancel — annule un versement saisi par erreur (spec
/// 2026-10-17-deletion-dependency-guard-design.md §2.4). Le paiement n'est jamais supprimé ni modifié dans
/// son montant : il passe au statut <c>Cancelled</c> avec un motif, et son numéro de reçu reste consommé.
///
/// <paramref name="Reversal"/> : contre-passation — annulation d'un versement APRÈS le délai de grâce de 48 h. Doit être
/// demandée explicitement et réservée au Directeur ; sans ce drapeau, un versement hors délai reste refusé (409).
///
/// IAuditableRequest : une annulation de paiement est une écriture financière sensible, au même titre que
/// l'encaissement lui-même (JGK-H01).
/// </summary>
public record CancelPaymentCommand(
    Guid PaymentId,
    string Reason,
    PaymentCancellationOutcome Outcome = PaymentCancellationOutcome.KeptBySchool,
    bool Reversal = false)
    : IRequest<CancelPaymentResult>, IAuditableRequest;

/// <summary>De quoi afficher le nouveau solde de l'inscription sans recharger la fiche.</summary>
public record CancelPaymentResult(
    Guid PaymentId,
    string ReceiptNumber,
    decimal CancelledAmount,
    decimal TotalDue,
    decimal AmountPaid,
    decimal RemainingBalance,
    DateTimeOffset CancelledAt,
    bool IsReversal = false);
