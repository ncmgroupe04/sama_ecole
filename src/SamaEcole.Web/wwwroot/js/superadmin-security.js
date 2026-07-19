/**
 * Console Super Admin — Sécurité & Logs (refonte UI/UX).
 *
 * TODO BACKEND : `GET /api/v1/admin/platform/activity` n'existe pas. Contrat attendu : un tableau de
 * { id, type ('SchoolCreated'|'RateLimited'|'ApplicationError'|'RegistrationApproved'|'PaymentConfirmed'),
 * severity ('info'|'warning'|'critical'), message, occurredAt (ISO), schoolName? }. Voir le commentaire
 * de Security.cshtml pour la raison (PlatformAuditLogs distinct, hors MVP, pas encore conçu).
 */
document.addEventListener('alpine:init', () => {
    Alpine.data('superAdminSecurity', () => ({
        events: [],
        isLoading: false,
        isDemoData: false,
        error: null,
        severityFilter: 'All',

        async load() {
            this.isLoading = true;
            this.error = null;
            try {
                this.events = await window.api.get('/admin/platform/activity');
                this.isDemoData = false;
            } catch (err) {
                if (err.status === 404) {
                    this.events = demoEvents();
                    this.isDemoData = true;
                } else {
                    this.error = err.message || 'Erreur lors du chargement du journal.';
                }
            } finally {
                this.isLoading = false;
            }
        },

        get filteredEvents() {
            if (this.severityFilter === 'All') return this.events;
            return this.events.filter((e) => e.severity === this.severityFilter);
        },

        countByType(type) {
            return this.events.filter((e) => e.type === type).length;
        },

        eventIcon(type) {
            return {
                SchoolCreated: 'building',
                RateLimited: 'shield',
                ApplicationError: 'alert-circle',
                RegistrationApproved: 'checkmark-circle',
                PaymentConfirmed: 'wallet'
            }[type] || 'archive';
        },

        severityDot(severity) {
            return { info: 'bg-sky-400', warning: 'bg-amber-400', critical: 'bg-rose-400' }[severity] || 'bg-zinc-600';
        },

        severityIconBg(severity) {
            return { info: 'bg-sky-500/15', warning: 'bg-amber-500/15', critical: 'bg-rose-500/15' }[severity] || 'bg-zinc-800';
        },

        severityIconText(severity) {
            return { info: 'text-sky-400', warning: 'text-amber-400', critical: 'text-rose-400' }[severity] || 'text-zinc-400';
        },

        formatDateTime(iso) {
            if (!iso) return '';
            return new Date(iso).toLocaleString('fr-FR', { dateStyle: 'medium', timeStyle: 'short' });
        }
    }));
});

function demoEvents() {
    const now = Date.now();
    return [
        { id: 1, type: 'SchoolCreated', severity: 'info', message: "Nouvel établissement créé : « Complexe Scolaire Teranga »", occurredAt: new Date(now - 25 * 60000).toISOString() },
        { id: 2, type: 'RateLimited', severity: 'warning', message: 'Tentatives de connexion bloquées (limite atteinte)', schoolName: 'École Les Baobabs', occurredAt: new Date(now - 55 * 60000).toISOString() },
        { id: 3, type: 'PaymentConfirmed', severity: 'info', message: "Paiement d'abonnement confirmé (webhook PayDunya)", schoolName: 'Institut Sainte-Marie', occurredAt: new Date(now - 3 * 3600000).toISOString() },
        { id: 4, type: 'RegistrationApproved', severity: 'info', message: "Demande d'inscription approuvée", schoolName: 'Lycée Moderne Thiès', occurredAt: new Date(now - 6 * 3600000).toISOString() },
        { id: 5, type: 'ApplicationError', severity: 'critical', message: 'Erreur non gérée lors de la génération d\'un bulletin PDF', schoolName: 'École Al Azhar', occurredAt: new Date(now - 9 * 3600000).toISOString() },
        { id: 6, type: 'RateLimited', severity: 'warning', message: 'Limite de génération de bulletins atteinte', schoolName: 'Groupe Scolaire Diamniadio', occurredAt: new Date(now - 20 * 3600000).toISOString() }
    ];
}
