/**
 * Annulation d'un paiement saisi par erreur (POST /finance/payments/{id}/cancel) — logique PARTAGÉE par
 * la fiche élève (onglet Paiements) et l'écran Caisse, pour que le motif se choisisse de la même façon
 * partout. Le modal correspondant est Views/Shared/_CancelPaymentModal.cshtml.
 *
 * Usage dans un composant Alpine :
 *     Alpine.data('xxx', () => ({ ...window.paymentCancel.mixin(), async afterPaymentCancelled() { … }, … }))
 * `afterPaymentCancelled()` (à fournir) recharge ce que l'écran affiche : solde, historique…
 *
 * Le motif est OBLIGATOIRE côté serveur (CancelPaymentCommandValidator, 3 à 500 caractères) : ce fichier
 * ne fait qu'aider à le composer. Le Directeur seul peut annuler (FinanceController) ; `canCancelPayment`
 * n'est qu'un confort d'affichage.
 */
(function () {
    const OTHER = 'Autre motif';

    const REASONS = [
        'Montant saisi incorrect',
        'Paiement enregistré sur le mauvais élève',
        'Paiement enregistré en double',
        'Mauvais mode de paiement',
        'Chèque ou virement non abouti',
        'Remboursement au parent',
        OTHER
    ];

    // Destination de l'argent (PaymentCancellationOutcome). Les écoles remboursent très rarement : la valeur par
    // défaut est donc « l'argent reste à l'école » ; l'annulation se comptabilise pareil dans les trois cas.
    const OUTCOMES = [
        { value: 'KeptBySchool', label: "Simple correction : l'argent reste à l'école" },
        { value: 'ReusedForEnrollment', label: "Argent réutilisé pour l'inscription de l'enfant" },
        { value: 'RefundedToParent', label: 'Argent remboursé au parent' }
    ];

    window.paymentCancel = {
        OTHER,
        REASONS,
        OUTCOMES,

        mixin() {
            return {
                canCancelPayment: ['Directeur', 'Finance'].includes(window.auth.role),
                cancelPaymentReasons: REASONS,
                cancelingPayment: null, // { paymentId, receiptNumber, amount }
                cancelPaymentOutcomes: OUTCOMES,
                cancelPaymentOutcome: 'KeptBySchool',
                cancelPaymentChoice: '',
                cancelPaymentDetail: '',
                cancelPaymentError: null,
                isCancelingPayment: false,

                openCancelPayment(payment) {
                    this.cancelingPayment = {
                        paymentId: payment.paymentId,
                        receiptNumber: payment.receiptNumber,
                        amount: payment.amount
                    };
                    this.cancelPaymentChoice = '';
                    this.cancelPaymentOutcome = 'KeptBySchool';
                    this.cancelPaymentDetail = '';
                    this.cancelPaymentError = null;
                },

                closeCancelPayment() {
                    this.cancelingPayment = null;
                    this.cancelPaymentChoice = '';
                    this.cancelPaymentOutcome = 'KeptBySchool';
                    this.cancelPaymentDetail = '';
                    this.cancelPaymentError = null;
                },

                /**
                 * Le délai de grâce court à partir de l'encaissement : `cancellableUntil` est calculé par le
                 * serveur (PaymentCancellationPolicy), jamais recopié ici. Confort d'affichage — le serveur refuse
                 * de toute façon (409) un paiement hors délai.
                 */
                isPaymentCancellable(payment) {
                    if (!payment || payment.status === 'Cancelled') return false;
                    if (!payment.cancellableUntil) return true;
                    return Date.now() <= new Date(payment.cancellableUntil).getTime();
                },

                /** Le champ de saisie libre est obligatoire pour « Autre motif », facultatif sinon. */
                cancelPaymentDetailRequired() {
                    return this.cancelPaymentChoice === OTHER;
                },

                /** Motif final envoyé au serveur : « Libellé — précision » ou « Autre : précision ». */
                composeCancelPaymentReason() {
                    const detail = (this.cancelPaymentDetail || '').trim();
                    if (this.cancelPaymentChoice === OTHER) {
                        return detail ? `Autre : ${detail}` : '';
                    }
                    if (!this.cancelPaymentChoice) return '';
                    return detail ? `${this.cancelPaymentChoice} — ${detail}` : this.cancelPaymentChoice;
                },

                async confirmCancelPayment() {
                    if (!this.cancelingPayment) return;

                    if (!this.cancelPaymentChoice) {
                        this.cancelPaymentError = "Choisissez un motif dans la liste.";
                        return;
                    }
                    const reason = this.composeCancelPaymentReason();
                    if (this.cancelPaymentDetailRequired() && (this.cancelPaymentDetail || '').trim().length < 3) {
                        this.cancelPaymentError = "Précisez le motif (3 caractères minimum).";
                        return;
                    }

                    this.isCancelingPayment = true;
                    this.cancelPaymentError = null;
                    try {
                        await window.api.post(`/finance/payments/${this.cancelingPayment.paymentId}/cancel`, { reason, outcome: this.cancelPaymentOutcome });
                        this.closeCancelPayment();
                        // Le solde, le total versé et l'historique changent : l'écran se relit.
                        await this.afterPaymentCancelled();
                    } catch (err) {
                        // Déjà annulé / caisse clôturée : le message serveur est déjà explicite, affiché tel quel.
                        this.cancelPaymentError = (err && err.message) || "Erreur lors de l'annulation du paiement.";
                        if (err && err.code === 'CONCURRENCY_CONFLICT') {
                            await this.afterPaymentCancelled();
                        }
                    } finally {
                        this.isCancelingPayment = false;
                    }
                }
            };
        }
    };
})();
