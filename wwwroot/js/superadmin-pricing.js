/**
 * Console Super Admin — Tarification & Promotions. Consomme GET/POST /admin/promo-codes et
 * POST /admin/promo-codes/{id}/deactivate (PromoCodesController). Le montant réellement facturé
 * n'est JAMAIS calculé ici : cet écran ne fait que définir les règles, InitiateSubscriptionPaymentHandler
 * recalcule tout côté serveur au moment du paiement (AGENTS.md règle #10).
 */
document.addEventListener('alpine:init', () => {
    Alpine.data('superAdminPricing', () => ({
        promoCodes: [],
        isLoading: false,
        error: null,

        isCreateOpen: false,
        isSubmitting: false,
        createErrors: {},
        newCode: emptyNewCode(),

        deactivatingId: null,

        async load() {
            this.isLoading = true;
            this.error = null;
            try {
                this.promoCodes = await window.api.get('/admin/promo-codes');
            } catch (err) {
                this.error = err.message || 'Erreur lors du chargement des codes promo.';
            } finally {
                this.isLoading = false;
            }
        },

        get activeCodes() {
            const now = new Date();
            return this.promoCodes.filter((c) => c.isActive && new Date(c.endDateUtc) >= now);
        },

        get totalUses() {
            return this.promoCodes.reduce((sum, c) => sum + c.currentUses, 0);
        },

        get totalBeneficiarySchools() {
            const names = new Set();
            this.promoCodes.forEach((c) => c.beneficiarySchoolNames.forEach((n) => names.add(n)));
            return names.size;
        },

        get requiresValue() {
            return this.newCode.discountType === 'Percentage' || this.newCode.discountType === 'FixedAmount';
        },

        discountTypeLabel(type) {
            return {
                Percentage: 'Pourcentage', FixedAmount: 'Montant fixe',
                FreeTrialMonths: 'Essai gratuit', FullDiscount: 'Offert à 100 %'
            }[type] || type;
        },

        valueLabel(code) {
            if (code.discountType === 'Percentage') return `${code.discountValue}%`;
            if (code.discountType === 'FixedAmount') return this.formatXof(code.discountValue);
            return code.durationMonths ? `${code.durationMonths} mois offert(s)` : '—';
        },

        statusLabel(code) {
            if (!code.isActive) return 'Désactivé';
            return new Date(code.endDateUtc) < new Date() ? 'Expiré' : 'Actif';
        },

        statusClasses(code) {
            const status = this.statusLabel(code);
            return {
                Actif: 'bg-emerald-500/15 text-emerald-400 ring-1 ring-inset ring-emerald-500/20',
                Expiré: 'bg-amber-500/15 text-amber-400 ring-1 ring-inset ring-amber-500/20',
                Désactivé: 'bg-zinc-800 text-zinc-400'
            }[status];
        },

        formatDate(iso) {
            if (!iso) return '—';
            return new Date(iso).toLocaleDateString('fr-FR');
        },

        formatXof(amount) {
            return new Intl.NumberFormat('fr-FR', { style: 'currency', currency: 'XOF', maximumFractionDigits: 0 }).format(amount);
        },

        openCreate() {
            this.newCode = emptyNewCode();
            this.createErrors = {};
            this.isCreateOpen = true;
        },

        closeCreate() {
            this.isCreateOpen = false;
        },

        async submitCreate() {
            this.isSubmitting = true;
            this.createErrors = {};
            try {
                await window.api.post('/admin/promo-codes', {
                    code: this.newCode.code,
                    discountType: this.newCode.discountType,
                    discountValue: this.requiresValue ? this.newCode.discountValue : 0,
                    durationMonths: this.newCode.durationMonths || null,
                    maxUses: this.newCode.maxUses || null,
                    startDateUtc: new Date(this.newCode.startDate).toISOString(),
                    endDateUtc: new Date(this.newCode.endDate).toISOString()
                });

                this.isCreateOpen = false;
                await this.load();
            } catch (err) {
                this.createErrors = window.api.toFieldErrors(err, 'Impossible de créer ce code promo.');
            } finally {
                this.isSubmitting = false;
            }
        },

        async deactivate(code) {
            this.deactivatingId = code.id;
            this.error = null;
            try {
                await window.api.post(`/admin/promo-codes/${code.id}/deactivate`);
                code.isActive = false;
            } catch (err) {
                this.error = err.message || 'Erreur lors de la désactivation.';
            } finally {
                this.deactivatingId = null;
            }
        }
    }));
});

function emptyNewCode() {
    const today = new Date().toISOString().slice(0, 10);
    const nextYear = new Date(Date.now() + 365 * 24 * 60 * 60 * 1000).toISOString().slice(0, 10);
    return {
        code: '', discountType: 'Percentage', discountValue: null,
        durationMonths: null, maxUses: null, startDate: today, endDate: nextYear
    };
}
