/**
 * Rapports institutionnels (Évolution N°7) — écran /rapports/institutionnels.
 *
 * Onglet « Rapport de rentrée IEF » : aperçu des trois tableaux du canevas (GET /institutional/ief-report), export
 * PDF (aperçu partagé) et Excel. Onglet « Normes d'âge » : tranche d'âge de chaque niveau, modèle national et réglage
 * de l'école (écriture Directeur seul — le serveur reste juge, 403). Aucun calcul ici : tout vient de l'API.
 */
document.addEventListener('alpine:init', () => {
    Alpine.data('institutionalReportsView', () => ({
        ...window.pdfPreview.state(),

        tab: 'ief',
        years: [],
        schoolYearId: '',
        ageReferenceDate: '',
        report: null,
        isLoading: false,
        downloading: '',
        error: null,
        notice: null,

        norms: [],
        normsLoading: false,

        get canEditNorms() {
            return window.auth.role === 'Directeur';
        },

        get yearOptions() {
            return this.years.map((y) => ({ value: y.id, label: y.isActive ? `${y.label} (active)` : y.label }));
        },

        async init() {
            try {
                this.years = await window.api.get('/school-years') || [];
                const active = this.years.find((y) => y.isActive) || this.years[0];
                if (active) this.schoolYearId = active.id;
            } catch (err) {
                this.error = window.api.toMessage(err, 'Impossible de charger les années scolaires.');
                return;
            }
            await Promise.all([this.load(), this.loadNorms()]);
        },

        params() {
            const params = new URLSearchParams({ schoolYearId: this.schoolYearId });
            if (this.ageReferenceDate) params.set('ageReferenceDate', this.ageReferenceDate);
            return params.toString();
        },

        async load() {
            if (!this.schoolYearId) return;
            this.isLoading = true;
            this.error = null;
            try {
                this.report = await window.api.get(`/institutional/ief-report?${this.params()}`);
            } catch (err) {
                this.error = window.api.toMessage(err, 'Erreur lors du calcul du rapport de rentrée.');
            } finally {
                this.isLoading = false;
            }
        },

        async exportReport(kind) {
            if (!this.schoolYearId || this.downloading) return;
            this.downloading = kind;
            this.error = null;
            try {
                const url = `/api/v1/institutional/ief-report/${kind}?${this.params()}`;
                if (kind === 'pdf') await this.openPdfPreview(url, 'Rapport de rentrée IEF', 'Rapport_rentree_IEF.pdf');
                else await this.downloadFile(url, 'Rapport_rentree_IEF.xlsx');
            } catch (err) {
                this.error = window.api.toMessage(err, "Erreur lors de l'export du rapport.");
            } finally {
                this.downloading = '';
            }
        },

        async downloadFile(url, fallbackName) {
            if (window.auth.isAuthenticated() && window.auth.isAccessTokenStale()) {
                await window.api.refreshOrRedirect();
            }
            const response = await fetch(url, {
                headers: { Authorization: `Bearer ${window.auth.accessToken}` },
                credentials: 'same-origin'
            });
            if (!response.ok) {
                const payload = await response.json().catch(() => null);
                const err = new Error((payload && payload.message) || window.api.httpFallbackMessage(response.status));
                err.status = response.status;
                throw err;
            }
            const blob = await response.blob();
            const objectUrl = URL.createObjectURL(blob);
            const link = document.createElement('a');
            link.href = objectUrl;
            link.download = fallbackName;
            document.body.appendChild(link);
            link.click();
            link.remove();
            URL.revokeObjectURL(objectUrl);
        },

        pct(value) {
            return value === null || value === undefined
                ? '—'
                : `${Number(value).toLocaleString('fr-FR', { maximumFractionDigits: 1 })} %`;
        },

        hours(value) {
            return `${Number(value || 0).toLocaleString('fr-FR', { maximumFractionDigits: 2 })} h`;
        },

        blank(value) {
            return value ? value : '';
        },

        totalOf(field) {
            return (this.report && this.report.classes || []).reduce((sum, c) => sum + (c[field] || 0), 0);
        },

        // ------------------------------------------------------------ Normes d'âge

        async loadNorms() {
            this.normsLoading = true;
            try {
                const rows = await window.api.get('/institutional/age-norms') || [];
                this.norms = rows.map((r) => ({ ...r, draftMin: String(r.minAge), draftMax: String(r.maxAge), saving: false, error: null }));
            } catch (err) {
                this.error = window.api.toMessage(err, "Impossible de charger les normes d'âge.");
            } finally {
                this.normsLoading = false;
            }
        },

        normDirty(row) {
            return Number(row.draftMin) !== row.minAge || Number(row.draftMax) !== row.maxAge;
        },

        async saveNorm(row) {
            const minAge = Number(row.draftMin);
            const maxAge = Number(row.draftMax);
            if (!Number.isInteger(minAge) || !Number.isInteger(maxAge)) {
                row.error = 'Saisissez des âges entiers.';
                return;
            }
            row.saving = true;
            row.error = null;
            this.notice = null;
            try {
                await window.api.put(`/institutional/age-norms/${encodeURIComponent(row.gradeLevel)}`, { minAge, maxAge });
                await this.loadNorms();
                this.notice = `Tranche d'âge de ${row.gradeLevel} enregistrée.`;
            } catch (err) {
                row.error = window.api.toMessage(err, "Erreur lors de l'enregistrement.");
            } finally {
                row.saving = false;
            }
        },

        async resetNorm(row) {
            row.saving = true;
            row.error = null;
            this.notice = null;
            try {
                await window.api.delete(`/institutional/age-norms/${encodeURIComponent(row.gradeLevel)}`);
                await this.loadNorms();
                this.notice = `${row.gradeLevel} : tranche d'âge du modèle national rétablie.`;
            } catch (err) {
                row.error = window.api.toMessage(err, 'Erreur lors du rétablissement.');
            } finally {
                row.saving = false;
            }
        }
    }));
});
