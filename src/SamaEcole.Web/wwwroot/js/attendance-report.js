/**
 * JGK-R02 — Rapport d'assiduité détaillé par classe et par élève (/reports/attendance). Filtres :
 * classe (optionnelle) et période (obligatoire). Réservé au Directeur, au Secrétariat et au Super
 * Admin : confort d'affichage, la garde réelle est ReportsController + la RLS.
 */
document.addEventListener('alpine:init', () => {
    Alpine.data('attendanceReportView', () => ({
        // Aperçu PDF partagé (wwwroot/js/pdf-preview.js) : l'export PDF s'ouvre dans la modale
        // _PdfPreviewModal (impression / téléchargement au choix). L'export CSV, qui ne se
        // prévisualise pas, reste en téléchargement direct.
        ...window.pdfPreview.state(),

        // Convoquer depuis le bilan (02/09/2026). Le rapport est ouvert au Secrétariat, la convocation
        // ne l'est pas (ParentSummonsController : SuperAdmin, Directeur, Surveillant) — l'action n'est
        // proposée qu'à l'intersection. Confort d'affichage : le 403 reste la garde réelle.
        canSummon: ['Directeur', 'SuperAdmin'].includes(window.auth.role),

        isSummonsOpen: false,
        isSummoning: false,
        summonsErrors: {},
        summonsTarget: null,
        summonsForm: { scheduledDate: '', scheduledTime: '', reason: '' },
        showSummonsCreated: false,
        summonsCreatedName: '',
        createdSummonsId: null,

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
                this.classrooms = [];
                toast.error(window.api.toMessage(err, 'Erreur lors du chargement des classes.'));
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
                this.error = window.api.toMessage(err, 'Erreur lors du chargement du rapport.');
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

        // ------------------------------------------------- Convoquer depuis le bilan (02/09/2026)
        //
        // Le bilan comptait les retards et les absences ; convoquer un parent obligeait à ouvrir un
        // autre écran et à ressaisir ces chiffres de mémoire. Les deux modules existaient, rien ne
        // les reliait. Le motif part donc d'ici, PRÉ-REMPLI et modifiable — aucun seuil automatique
        // ne déclenche quoi que ce soit : c'est le Directeur qui convoque (Volume 1 §18.2).

        /** Une ligne sans un seul retard ni une seule absence n'a rien à convoquer. */
        hasAttendanceIssue(row) {
            if (!row) return false;
            return (row.late || 0) > 0
                || (row.unjustifiedAbsences || 0) > 0
                || (row.justifiedAbsences || 0) > 0;
        },

        openSummons(row) {
            this.summonsTarget = row;
            this.summonsErrors = {};
            // Demain, pas aujourd'hui : une convocation remise en main propre à l'élève doit laisser
            // au parent le temps de s'organiser.
            const tomorrow = new Date();
            tomorrow.setDate(tomorrow.getDate() + 1);
            this.summonsForm = {
                scheduledDate: this.toIsoDate(tomorrow),
                scheduledTime: '09:00',
                reason: this.buildSummonsReason(row)
            };
            this.isSummonsOpen = true;
        },

        closeSummons() {
            this.isSummonsOpen = false;
            this.summonsTarget = null;
            this.summonsErrors = {};
        },

        /**
         * Motif pré-rempli : uniquement ce qui a été RÉELLEMENT compté sur la période affichée.
         * Un poste à zéro n'est pas écrit — « 0 absence » sur une convocation ferait douter du
         * relevé entier. Les minutes de retard ne sont citées que si elles ont été saisies.
         */
        buildSummonsReason(row) {
            const parts = [];
            if (row.late > 0) {
                const minutes = row.totalLateMinutes > 0 ? ` (${row.totalLateMinutes} min au total)` : '';
                parts.push(`${row.late} retard${row.late > 1 ? 's' : ''}${minutes}`);
            }
            if (row.unjustifiedAbsences > 0) {
                parts.push(`${row.unjustifiedAbsences} absence${row.unjustifiedAbsences > 1 ? 's' : ''} non justifiée${row.unjustifiedAbsences > 1 ? 's' : ''}`);
            }
            if (row.justifiedAbsences > 0) {
                parts.push(`${row.justifiedAbsences} absence${row.justifiedAbsences > 1 ? 's' : ''} justifiée${row.justifiedAbsences > 1 ? 's' : ''}`);
            }

            const releve = parts.length > 0 ? parts.join(', ') : 'assiduité irrégulière';
            return `Entretien sur l'assiduité de l'élève : ${releve}, `
                + `relevé du ${this.formatDate(this.startDate)} au ${this.formatDate(this.endDate)}.`;
        },

        async submitSummons() {
            if (!this.summonsTarget) return;

            this.isSummoning = true;
            this.summonsErrors = {};
            try {
                const scheduledAt = new Date(
                    `${this.summonsForm.scheduledDate}T${this.summonsForm.scheduledTime || '09:00'}`);

                this.createdSummonsId = await window.api.post('/parent-summons', {
                    studentId: this.summonsTarget.studentId,
                    scheduledAt: scheduledAt.toISOString(),
                    reason: this.summonsForm.reason
                });

                this.summonsCreatedName = this.summonsTarget.fullName;
                this.closeSummons();
                this.showSummonsCreated = true;
            } catch (err) {
                this.summonsErrors = window.api.toFieldErrors(err, "Erreur lors de l'enregistrement de la convocation.");
            } finally {
                this.isSummoning = false;
            }
        },

        /** Avis de convocation, dans la même modale d'aperçu que l'export du rapport. */
        async printCreatedSummons() {
            if (!this.createdSummonsId) return;
            await this.openPdfPreview(
                `/api/v1/parent-summons/${this.createdSummonsId}/notice/pdf`,
                'Convocation parent',
                `Convocation-${this.createdSummonsId}.pdf`);
        },

        /**
         * Export PDF/CSV (JGK-R03). Le PDF s'ouvre dans la modale d'aperçu partagée (pdf-preview.js) —
         * l'utilisateur imprime ou télécharge depuis l'en-tête. Le CSV, non prévisualisable, se
         * récupère en blob authentifié (l'API exige le jeton en en-tête). Les filtres de période et de
         * classe actifs à l'écran sont conservés.
         */
        async exportReport(format) {
            if (!this.startDate || !this.endDate || this.exporting) return;

            const params = new URLSearchParams({ startDate: this.startDate, endDate: this.endDate, format });
            if (this.classId) params.set('classId', this.classId);
            const url = `/api/v1/reports/attendance/export?${params.toString()}`;

            if (format === 'pdf') {
                await this.openPdfPreview(url, "Rapport d'assiduité", `assiduite_${this.startDate}_${this.endDate}.pdf`);
                return;
            }

            this.exporting = true;
            this.error = null;
            try {
                if (window.auth.isAuthenticated() && window.auth.isAccessTokenStale()) {
                    await window.api.refreshOrRedirect();
                }

                const response = await fetch(url, {
                    headers: { Authorization: `Bearer ${window.auth.accessToken}` },
                    credentials: 'same-origin'
                });
                if (!response.ok) {
                    this.error = "Erreur lors de l'export du rapport.";
                    return;
                }

                const blob = await response.blob();
                const objectUrl = URL.createObjectURL(blob);
                const link = document.createElement('a');
                link.href = objectUrl;
                link.download = this.exportFileName(response, format);
                document.body.appendChild(link);
                link.click();
                link.remove();
                URL.revokeObjectURL(objectUrl);
            } catch (err) {
                this.error = window.api.toMessage(err, "Erreur lors de l'export du rapport.");
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
