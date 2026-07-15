using SamaEcole.Domain.Enums;
using MediatR;

namespace SamaEcole.Application.Finance.Commands.RecordPayment;

/// <summary>
/// POST /finance/payments — ticket JGK-F02. Encaisse un versement contre une inscription. Le montant
/// dû (<c>TotalDue</c>) n'est jamais fourni ni modifié par le client : la Finance ne fait qu'imputer un
/// versement sur le solde figé à l'inscription (AGENTS.md règles #4 et #10). L'inscription est identifiée
/// par son Id ; l'école vient du JWT.
/// </summary>
public record RecordPaymentCommand(Guid EnrollmentId, decimal Amount, PaymentMethod Method)
    : IRequest<RecordPaymentResult>;

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
