using System.Text.Json;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Subscriptions;
using SamaEcole.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace SamaEcole.Application.Platform.Commands.ReviewWavePayment;

/// <summary>
/// POST /admin/platform/wave-payments/{paymentId}/approve|reject — réservé au Super Admin. Un lien marchand Wave est
/// statique (ni montant ni référence, aucun webhook) : après avoir retrouvé le règlement sur son relevé Wave, le Super
/// Admin VALIDE (abonnement activé et prolongé, comme après un webhook confirmé) ou REJETTE la demande.
///
/// Garde-fous (AGENTS.md règle #11 : le CLIENT ne confirme jamais un paiement — ici seul le Super Admin le peut) :
///   · seules les demandes de la liste « en attente » (Provider = WaveMerchant, Status = Initiated) sont éligibles : un
///     paiement d'agrégateur ne peut JAMAIS être validé à la main, et une demande déjà traitée ne l'est pas deux fois ;
///   · la transition est atomique côté PostgreSQL (confirm_subscription_payment, garde Status = 'Initiated') : deux
///     clics simultanés ne prolongent l'abonnement qu'une seule fois ;
///   · l'acte est journalisé dans l'audit de l'école cible, attribué au Super Admin.
/// </summary>
/// <param name="TransactionRef">
/// Identifiant de la transaction Wave retrouvée sur le relevé — OBLIGATOIRE pour valider, inutile pour rejeter. Il est
/// unique : un même règlement ne peut pas activer deux abonnements (le lien marchand ne porte aucune référence).
/// </param>
// IRequest<Unit> et non IRequest : avec MediatR 12, une commande « void » (IRequest) ne traverse PAS les pipeline behaviors
// (ValidationBehavior, AuditLoggingBehavior) — son validateur ne serait jamais exécuté. Convention du dépôt : IRequest<Unit>.
public record ReviewWavePaymentCommand(Guid PaymentId, bool Approve, string? TransactionRef = null) : IRequest<Unit>;

public class ReviewWavePaymentCommandValidator : AbstractValidator<ReviewWavePaymentCommand>
{
    public ReviewWavePaymentCommandValidator()
    {
        RuleFor(c => c.PaymentId).NotEmpty();

        // Lettres, chiffres, « _ » et « - » seulement (ex. T_XXXXXXXXXX) : aucune ponctuation libre dans une clé unique.
        // Validé APRÈS rognage : le handler rogne et normalise (« ␣t_ab12␣ » = « T_AB12 »), la règle voit donc la même valeur que lui.
        RuleFor(c => (c.TransactionRef ?? string.Empty).Trim())
            .NotEmpty().WithMessage("Saisissez l'identifiant de la transaction Wave (visible sur le relevé du compte marchand).")
            .MinimumLength(6).WithMessage("L'identifiant de transaction Wave est trop court.")
            .MaximumLength(64)
            .Matches("^[A-Za-z0-9_-]+$").WithMessage("L'identifiant de transaction ne contient que des lettres, chiffres, « _ » et « - ».")
            .OverridePropertyName(nameof(ReviewWavePaymentCommand.TransactionRef))
            .When(c => c.Approve);
    }
}

public class ReviewWavePaymentCommandHandler(
    ISubscriptionPaymentStore paymentStore,
    IAuditLogStore auditLogStore,
    ICurrentUserService currentUser,
    IEmailSender emailSender,
    TimeProvider timeProvider,
    ILogger<ReviewWavePaymentCommandHandler> logger)
    : IRequestHandler<ReviewWavePaymentCommand, Unit>
{
    public async Task<Unit> Handle(ReviewWavePaymentCommand request, CancellationToken cancellationToken)
    {
        var superAdminId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Aucun utilisateur authentifié.");

        var pending = (await paymentStore.ListPendingWavePaymentsAsync(cancellationToken))
            .SingleOrDefault(p => p.PaymentId == request.PaymentId);

        if (pending is null)
        {
            // Inconnu, déjà traité, ou paiement d'agrégateur : dans tous les cas rien à valider ici.
            throw new KeyNotFoundException("Aucune demande de paiement Wave en attente ne correspond.");
        }

        var now = timeProvider.GetUtcNow();
        var finalStatus = request.Approve ? SubscriptionPaymentStatus.Confirmed : SubscriptionPaymentStatus.Failed;

        // Normalisé en majuscules : « t_ab12cd » et « T_AB12CD » sont la même transaction pour l'index unique.
        var transactionRef = request.Approve ? request.TransactionRef!.Trim().ToUpperInvariant() : null;

        // Trace d'audit du rapprochement manuel, rangée là où le webhook rangerait le sien (WebhookPayloadRaw).
        var payload = JsonSerializer.Serialize(new
        {
            source = "wave-merchant-manual",
            decision = request.Approve ? "approved" : "rejected",
            reviewedBy = superAdminId,
            reviewedAt = now,
            transactionRef
        });

        var result = (request.Approve
                ? await paymentStore.ConfirmWavePaymentAsync(request.PaymentId, transactionRef!, payload, cancellationToken)
                : await paymentStore.ConfirmAsync(request.PaymentId, finalStatus, payload, cancellationToken))
            ?? throw new BusinessRuleException(
                "Cette demande de paiement a déjà été traitée.", "WAVE_PAYMENT_ALREADY_REVIEWED");

        await auditLogStore.AppendAsync(
            result.SchoolId, superAdminId, "Subscriptions",
            request.Approve ? "ConfirmWaveSubscriptionPayment" : "RejectWaveSubscriptionPayment",
            success: true, failureReason: null, currentUser.IpAddress, now, cancellationToken);

        // Déjà persisté ci-dessus : un échec d'ENVOI ne doit pas défaire ni masquer la décision.
        try
        {
            await (request.Approve
                ? SubscriptionPaymentEmails.SendActivationAsync(emailSender, result, cancellationToken)
                : SubscriptionPaymentEmails.SendWaveRejectionAsync(
                    emailSender, result, pending.Amount, pending.Reference, cancellationToken));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Paiement Wave {PaymentId} traité ({Status}) pour l'école {SchoolId}, mais l'e-mail n'a pas pu être envoyé.",
                request.PaymentId, finalStatus, result.SchoolId);
        }

        logger.LogInformation(
            "Paiement Wave {PaymentId} -> {Status} par le Super Admin {SuperAdminId} pour l'école {SchoolId}.",
            request.PaymentId, finalStatus, superAdminId, result.SchoolId);

        return Unit.Value;
    }
}
