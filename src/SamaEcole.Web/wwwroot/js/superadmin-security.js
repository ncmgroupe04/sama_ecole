/**
 * Console Super Admin — Journal d'audit plateforme (toutes écoles confondues).
 *
 * Consomme le vrai `GET /api/v1/admin/platform/activity` (PlatformController, migration
 * AddPlatformAdminViews : fonction PostgreSQL SECURITY DEFINER `get_global_audit_logs`), paginé comme
 * l'onglet Journal d'audit des Paramètres (wwwroot/js/audit-log.js) — mêmes champs (module, action,
 * succès/échec, IP, acteur), avec en plus l'établissement concerné puisque cette vue traverse toutes
 * les écoles.
 */
document.addEventListener('alpine:init', () => {
    Alpine.data('superAdminSecurity', () => ({
        entries: [],
        isLoading: false,
        isDemoData: false,
        error: null,

        page: 1,
        pageSize: 20,
        totalCount: 0,

        async load() {
            this.isLoading = true;
            this.error = null;
            try {
                const params = new URLSearchParams({ page: this.page, pageSize: this.pageSize });
                const data = await window.api.get(`/admin/platform/activity?${params.toString()}`);
                this.entries = data.items || [];
                this.totalCount = data.totalCount || 0;
                this.isDemoData = false;
            } catch (err) {
                if (err.status === 404) {
                    this.entries = demoEntries();
                    this.totalCount = this.entries.length;
                    this.isDemoData = true;
                } else {
                    this.error = window.api.toMessage(err, 'Erreur lors du chargement du journal.');
                }
            } finally {
                this.isLoading = false;
            }
        },

        formatDateTime(iso) {
            if (!iso) return '';
            return new Date(iso).toLocaleString('fr-FR', { dateStyle: 'medium', timeStyle: 'short' });
        }
    }));
});

function demoEntries() {
    const now = Date.now();
    return [
        { id: 1, schoolName: 'Complexe Scolaire Teranga', actorFullName: 'Awa Ndiaye', module: 'Schools', action: 'CreateSchool', success: true, ipAddress: '102.244.12.8', occurredAt: new Date(now - 25 * 60000).toISOString() },
        { id: 2, schoolName: 'École Les Baobabs', actorFullName: 'Moussa Diop', module: 'Finance', action: 'RecordPayment', success: false, failureReason: 'Solde insuffisant', ipAddress: '41.82.10.3', occurredAt: new Date(now - 55 * 60000).toISOString() },
        { id: 3, schoolName: 'Institut Sainte-Marie', actorFullName: 'Fatou Sarr', module: 'Subscriptions', action: 'ProcessPaymentWebhook', success: true, ipAddress: '154.72.9.1', occurredAt: new Date(now - 3 * 3600000).toISOString() },
        { id: 4, schoolName: 'Lycée Moderne Thiès', actorFullName: 'Ibrahima Ba', module: 'Registration', action: 'ApproveRegistrationRequest', success: true, ipAddress: '105.101.4.2', occurredAt: new Date(now - 6 * 3600000).toISOString() },
        { id: 5, schoolName: 'École Al Azhar', actorFullName: 'Cheikh Fall', module: 'Users', action: 'UpdateUserStatus', success: false, failureReason: 'Le compte cible est déjà suspendu', ipAddress: '196.1.55.7', occurredAt: new Date(now - 9 * 3600000).toISOString() }
    ];
}
