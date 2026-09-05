/**
 * Console Super Admin — Journal d'audit plateforme (toutes écoles confondues).
 *
 * Consomme le vrai `GET /api/v1/admin/platform/activity` (PlatformController, migrations
 * AddPlatformAdminViews puis ExtendGlobalAuditLogsFilters : fonction PostgreSQL SECURITY DEFINER
 * `get_global_audit_logs`), paginé comme l'onglet Journal d'audit des Paramètres
 * (wwwroot/js/audit-log.js) — mêmes champs (module, action, succès/échec, IP, acteur), avec en plus
 * l'établissement concerné puisque cette vue traverse toutes les écoles.
 *
 * Filtres : module, succès/échec, établissement, plage de dates — même convention de clés que
 * auditLogView() (moduleFilter/successFilter en chaîne vide = « tous »), plus schoolFilter et les
 * deux bornes de date propres à cet écran (l'onglet tenant n'en a pas besoin, une seule école n'a
 * jamais assez de volume pour le justifier).
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

        schools: [],

        moduleFilter: '',
        successFilter: '', // '' = tous, 'true' = succès, 'false' = échecs
        schoolFilter: '',
        dateFrom: '',
        dateTo: '',

        async load() {
            this.isLoading = true;
            this.error = null;
            try {
                const params = new URLSearchParams({ page: this.page, pageSize: this.pageSize });
                if (this.moduleFilter) params.set('module', this.moduleFilter);
                if (this.successFilter) params.set('success', this.successFilter);
                if (this.schoolFilter) params.set('schoolId', this.schoolFilter);
                if (this.dateFrom) params.set('dateFrom', new Date(this.dateFrom).toISOString());
                if (this.dateTo) params.set('dateTo', endOfDayIso(this.dateTo));

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

        /** Liste des écoles pour le filtre — chargée une seule fois, indépendamment de load(). */
        async loadSchools() {
            try {
                this.schools = await window.api.get('/schools');
            } catch {
                // silence-volontaire : le filtre École reste vide, le reste de l'écran fonctionne
                // normalement (searchQuery-like, aucune donnée du journal n'en dépend).
            }
        },

        applyFilters() {
            this.page = 1;
            this.load();
        },

        resetFilters() {
            this.moduleFilter = '';
            this.successFilter = '';
            this.schoolFilter = '';
            this.dateFrom = '';
            this.dateTo = '';
            this.applyFilters();
        },

        formatDateTime(iso) {
            if (!iso) return '';
            return new Date(iso).toLocaleString('fr-FR', { dateStyle: 'medium', timeStyle: 'short' });
        }
    }));
});

/**
 * Borne haute INCLUSE d'un `<input type="date">` (« jusqu'au 12 » doit couvrir toute la journée du
 * 12), en UTC explicite — un `<input type="date">` livre une date SANS heure, que `new Date(...)`
 * interprète déjà comme minuit UTC (spec ECMAScript) ; construire la fin de journée en LOCAL via
 * `setHours` romprait cette cohérence pour tout poste hors UTC+0 (Sénégal seul aujourd'hui, mais rien
 * ne le garantit demain).
 */
function endOfDayIso(yyyyMmDd) {
    const [year, month, day] = yyyyMmDd.split('-').map(Number);
    return new Date(Date.UTC(year, month - 1, day, 23, 59, 59, 999)).toISOString();
}

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
