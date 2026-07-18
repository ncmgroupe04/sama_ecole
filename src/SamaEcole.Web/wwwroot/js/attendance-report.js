/**
 * JGK-R02 — Rapport d'assiduité détaillé par classe et par élève (/reports/attendance). Filtres :
 * classe (optionnelle) et période (obligatoire). Réservé au Directeur, au Secrétariat et au Super
 * Admin : confort d'affichage, la garde réelle est ReportsController + la RLS.
 */
document.addEventListener('alpine:init', () => {
    Alpine.data('attendanceReportView', () => ({
        classrooms: [],
        data: null,
        totalCount: 0,
        page: 1,
        pageSize: 20,
        isLoading: false,
        error: null,

        // Filtres. Période par défaut : du 1er du mois courant à aujourd'hui.
        classId: '',
        startDate: '',
        endDate: '',

        exporting: false,

        init() {
            const today = new Date();
            const firstOfMonth = new Date(today.getFullYear(), today.getMonth(), 1);
            this.startDate = this.toIsoDate(firstOfMonth);
            this.endDate = this.toIsoDate(today);

            this.loadClassrooms();
            this.load();
        },

        toIsoDate(d) {
            const month = String(d.getMonth() + 1).padStart(2, '0');
            const day = String(d.getDate()).padStart(2, '0');
            return `${d.getFullYear()}-${month}-${day}`;
        },

        async loadClassrooms() {
            try {
                const data = await window.api.get('/classrooms');
                this.classrooms = Array.isArray(data) ? data : (data.items || []);
            } catch (err) {
                console.error('Erreur chargement classes:', err);
            }
        },

        async load() {
            if (!this.startDate || !this.endDate) return;

            this.isLoading = true;
            this.error = null;
            try {
                const params = new URLSearchParams({
                    startDate: this.startDate,
                    endDate: this.endDate,
                    page: this.page,
                    pageSize: this.pageSize
                });
                if (this.classId) params.set('classId', this.classId);

                this.data = await window.api.get(`/reports/attendance?${params.toString()}`);
                this.totalCount = this.data.totalCount || 0;
            } catch (err) {
                this.error = err.message || 'Erreur lors du chargement du rapport.';
                this.data = null;
                this.totalCount = 0;
            } finally {
                this.isLoading = false;
            }
        },

        /** Un changement de filtre repart de la page 1 : la page 3 d'un filtre précédent n'a pas de sens. */
        applyFilters() {
            this.page = 1;
            this.load();
        },

        resetFilters() {
            const today = new Date();
            const firstOfMonth = new Date(today.getFullYear(), today.getMonth(), 1);
            this.classId = '';
            this.startDate = this.toIsoDate(firstOfMonth);
            this.endDate = this.toIsoDate(today);
            this.applyFilters();
        },

        periodLabel() {
            if (!this.data) return '';
            return `${this.formatDate(this.data.startDate)} → ${this.formatDate(this.data.endDate)}`;
        },

        /**
         * Export PDF/CSV (JGK-R03). Comme le téléchargement de reçu (caisse.js/dashboard.js) : l'API
         * exige le jeton, on récupère donc le fichier en blob avec l'en-tête Authorization plutôt qu'un
         * simple lien. Conserve les filtres de période et de classe actifs à l'écran.
         */
        async exportReport(format) {
            if (!this.startDate || !this.endDate || this.exporting) return;

            this.exporting = true;
            this.error = null;
            try {
                if (window.auth.isAuthenticated() && window.auth.isAccessTokenStale()) {
                    await window.api.refreshOrRedirect();
                }

                const params = new URLSearchParams({ startDate: this.startDate, endDate: this.endDate, format });
                if (this.classId) params.set('classId', this.classId);

                const response = await fetch(`/api/v1/reports/attendance/export?${params.toString()}`, {
                    headers: { Authorization: `Bearer ${window.auth.accessToken}` },
                    credentials: 'same-origin'
                });
                if (!response.ok) {
                    this.error = "Erreur lors de l'export du rapport.";
                    return;
                }

                const blob = await response.blob();
                const url = URL.createObjectURL(blob);
                const link = document.createElement('a');
                link.href = url;
                link.download = this.exportFileName(response, format);
                document.body.appendChild(link);
                link.click();
                link.remove();
                URL.revokeObjectURL(url);
            } catch (err) {
                this.error = err.message || "Erreur lors de l'export du rapport.";
            } finally {
                this.exporting = false;
            }
        },

        /** Nom du fichier : Content-Disposition renvoyé par l'API si présent, sinon reconstruit des filtres. */
        exportFileName(response, format) {
            const disposition = response.headers.get('Content-Disposition') || '';
            const match = disposition.match(/filename="?([^"]+)"?/i);
            if (match) return match[1];
            return `assiduite_${this.startDate}_${this.endDate}.${format}`;
        },

        /** Taux : « — » quand il n'existe pas (aucun appel → averageAttendanceRate null). */
        formatPercent(rate) {
            if (rate === null || rate === undefined) return '—';
            return new Intl.NumberFormat('fr-FR', { style: 'percent', maximumFractionDigits: 0 }).format(rate);
        },

        /** Badge coloré selon le seuil : vert ≥ 90 %, orange ≥ 75 %, rouge en dessous. */
        rateBadgeClass(rate) {
            if (rate >= 0.9) return 'bg-success-bg text-success';
            if (rate >= 0.75) return 'bg-warning-bg text-warning';
            return 'bg-danger-bg text-danger';
        },

        formatDate(dateStr) {
            if (!dateStr) return '';
            const d = new Date(dateStr);
            const day = String(d.getDate()).padStart(2, '0');
            const month = String(d.getMonth() + 1).padStart(2, '0');
            return `${day}/${month}/${d.getFullYear()}`;
        },

        initials(name) {
            return (name || '').split(' ').filter(Boolean).slice(0, 2).map((p) => p[0]).join('').toUpperCase();
        }
    }));
});
