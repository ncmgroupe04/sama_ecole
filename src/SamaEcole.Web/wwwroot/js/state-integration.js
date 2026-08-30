/**
 * Module Intégration étatique (/integration-etatique) — Volume 1 §23, docs/Volume_4_API_Design.md §23.
 *
 * Le socle producteur des pièces et fichiers réglementaires dus au ministère. AUCUN dialogue avec un
 * système externe : le SIMEN n'expose aucune API publique à ce jour. L'écran le dit explicitement
 * (bandeau « Statut du relais ») plutôt que d'afficher une action de transmission qui échouerait.
 *
 * Trois onglets :
 *  - Export Planète : matrice élèves (CSV ou JSON) pour une année, éventuellement une classe.
 *  - Rapport STATEDUC : agrégats à l'écran + téléchargement PDF (formulaire officiel) et Excel.
 *  - Certificats de mutation : le registre des pièces délivrées, avec révocation (JGK-M06).
 *
 * Matrice de droits reproduite ICI en confort d'affichage — la garde réelle est
 * StateIntegrationController : Planète et STATEDUC sont Directeur seul ; IEN et certificats de
 * mutation sont ouverts en plus au Secrétariat. L'écran masque simplement les onglets hors périmètre
 * et laisse remonter un éventuel 403 comme une erreur lisible.
 */
document.addEventListener('alpine:init', () => {

    const REASON_LABELS = {
        Demenagement: 'Déménagement',
        ChangementEtablissement: "Changement d'établissement",
        RaisonFamiliale: 'Raison familiale',
        RaisonMedicale: 'Raison médicale',
        Autre: 'Autre'
    };

    Alpine.data('stateIntegrationView', () => ({
        error: null,

        // Le Directeur voit tout ; le Secrétariat n'a que l'onglet Certificats (IEN se gère sur la
        // fiche élève). Un rôle sans accès n'arrive pas ici — le lien de menu est déjà masqué.
        isDirector: window.auth.role === 'Directeur',

        tab: window.auth.role === 'Directeur' ? 'planete' : 'certificats',

        // Référentiels partagés.
        schoolYears: [],
        classrooms: [],

        // Statut du relais SIMEN — chargé une fois, affiché en bandeau permanent.
        relais: { isConfigured: false, message: '' },

        // ---- Onglet Export Planète ----------------------------------------------------------
        planete: { schoolYearId: '', format: 'Csv', classroomId: '', busy: false, notice: null },

        // ---- Onglet Rapport STATEDUC ------------------------------------------------------
        stateduc: { schoolYearId: '', observationDate: '', busy: false, downloading: '', data: null },

        // ---- Onglet Certificats ---------------------------------------------------------------
        certificates: { items: [], total: 0, page: 1, pageSize: 20, loading: false },
        revokeModal: { open: false, id: null, number: '', reason: '', busy: false, error: null },

        async init() {
            try {
                const [years, classrooms] = await Promise.all([
                    window.api.get('/school-years'),
                    window.api.get('/classrooms')
                ]);
                this.schoolYears = years || [];
                this.classrooms = classrooms || [];

                const active = this.schoolYears.find((y) => y.isActive) || this.schoolYears[0];
                if (active) {
                    this.planete.schoolYearId = active.id;
                    this.stateduc.schoolYearId = active.id;
                }
            } catch (err) {
                this.error = window.api.toMessage(err, 'Erreur lors du chargement des référentiels.');
            }

            // Best-effort : un échec du statut du relais ne doit pas bloquer l'écran.
            try {
                this.relais = await window.api.get('/state-integration/simen/status');
            } catch { /* le bandeau restera sur son défaut « non configuré » */ }

            if (this.tab === 'certificats') this.loadCertificates();
        },

        switchTab(name) {
            this.tab = name;
            this.error = null;
            if (name === 'certificats' && this.certificates.items.length === 0) this.loadCertificates();
        },

        tabClass(name) {
            return this.tab === name
                ? 'bg-white text-indigo-700 shadow-sm font-medium'
                : 'text-slate-500 hover:text-slate-800';
        },

        schoolYearLabel(id) {
            const y = this.schoolYears.find((x) => x.id === id);
            return y ? y.label : '';
        },

        reasonLabel(value) { return REASON_LABELS[value] || value; },

        formatDate(iso) {
            if (!iso) return '—';
            const d = new Date(iso);
            return Number.isNaN(d.getTime()) ? iso : d.toLocaleDateString('fr-FR');
        },

        // ================================ Export Planète ================================

        get planeteYearOptions() {
            return [{ value: '', label: 'Sélectionnez une année…' }]
                .concat(this.schoolYears.map((y) => ({ value: y.id, label: y.label })));
        },

        get planeteClassroomOptions() {
            return [{ value: '', label: 'Tout l’établissement' }]
                .concat(this.classrooms.map((c) => ({ value: c.id, label: `${c.name} — ${c.level}` })));
        },

        async downloadPlanete() {
            if (!this.planete.schoolYearId || this.planete.busy) return;

            this.planete.busy = true;
            this.planete.notice = null;
            this.error = null;
            try {
                const params = new URLSearchParams({
                    schoolYearId: this.planete.schoolYearId,
                    format: this.planete.format
                });
                if (this.planete.classroomId) params.set('classroomId', this.planete.classroomId);

                await this.downloadFile(
                    `/api/v1/state-integration/planete/export?${params.toString()}`,
                    this.planete.format === 'Json' ? 'planete.json' : 'planete.csv'
                );
            } catch (err) {
                // 409 = code établissement national manquant : message actionnable renvoyé par l'API.
                this.planete.notice = window.api.toMessage(err, "Erreur lors de la génération de l'export Planète.");
            } finally {
                this.planete.busy = false;
            }
        },

        // ================================ Rapport STATEDUC ================================

        get stateducYearOptions() {
            return [{ value: '', label: 'Sélectionnez une année…' }]
                .concat(this.schoolYears.map((y) => ({ value: y.id, label: y.label })));
        },

        async loadStateduc() {
            if (!this.stateduc.schoolYearId || this.stateduc.busy) return;

            this.stateduc.busy = true;
            this.stateduc.data = null;
            this.error = null;
            try {
                const params = new URLSearchParams({ schoolYearId: this.stateduc.schoolYearId });
                if (this.stateduc.observationDate) params.set('observationDate', this.stateduc.observationDate);
                this.stateduc.data = await window.api.get(`/state-integration/stateduc?${params.toString()}`);
            } catch (err) {
                this.error = window.api.toMessage(err, 'Erreur lors du chargement du rapport STATEDUC.');
            } finally {
                this.stateduc.busy = false;
            }
        },

        async downloadStateduc(kind) {
            if (!this.stateduc.schoolYearId || this.stateduc.downloading) return;

            this.stateduc.downloading = kind;
            this.error = null;
            try {
                const params = new URLSearchParams({ schoolYearId: this.stateduc.schoolYearId });
                if (this.stateduc.observationDate) params.set('observationDate', this.stateduc.observationDate);

                await this.downloadFile(
                    `/api/v1/state-integration/stateduc/${kind}?${params.toString()}`,
                    kind === 'pdf' ? 'STATEDUC.pdf' : 'STATEDUC.xlsx'
                );
            } catch (err) {
                this.error = window.api.toMessage(err, "Erreur lors du téléchargement du rapport STATEDUC.");
            } finally {
                this.stateduc.downloading = '';
            }
        },

        // Ratios rendus par l'API en pourcentage 0–100, ou null quand le dénominateur est nul.
        pct(value) {
            return value === null || value === undefined
                ? '—'
                : `${Number(value).toLocaleString('fr-FR', { maximumFractionDigits: 1 })} %`;
        },
        num(value) {
            return value === null || value === undefined
                ? '—'
                : Number(value).toLocaleString('fr-FR', { maximumFractionDigits: 1 });
        },

        // ================================ Certificats ================================

        get certificatePageCount() {
            return Math.max(1, Math.ceil(this.certificates.total / this.certificates.pageSize));
        },

        async loadCertificates() {
            this.certificates.loading = true;
            this.error = null;
            try {
                const params = new URLSearchParams({
                    page: this.certificates.page,
                    pageSize: this.certificates.pageSize
                });
                const res = await window.api.get(`/state-integration/certificates?${params.toString()}`);
                this.certificates.items = res.items || [];
                this.certificates.total = res.totalCount || 0;
            } catch (err) {
                this.error = window.api.toMessage(err, 'Erreur lors du chargement des certificats.');
            } finally {
                this.certificates.loading = false;
            }
        },

        changeCertificatePage(delta) {
            const next = this.certificates.page + delta;
            if (next < 1 || next > this.certificatePageCount) return;
            this.certificates.page = next;
            this.loadCertificates();
        },

        openRevoke(cert) {
            this.revokeModal = { open: true, id: cert.id, number: cert.certificateNumber, reason: '', busy: false, error: null };
        },
        closeRevoke() { this.revokeModal.open = false; },

        async submitRevoke() {
            if (!this.revokeModal.reason.trim() || this.revokeModal.busy) return;

            this.revokeModal.busy = true;
            this.revokeModal.error = null;
            try {
                await window.api.post(
                    `/state-integration/certificates/${this.revokeModal.id}/revoke`,
                    { reason: this.revokeModal.reason.trim() }
                );
                this.revokeModal.open = false;
                await this.loadCertificates();
            } catch (err) {
                this.revokeModal.error = window.api.toMessage(err, 'Erreur lors de la révocation.');
            } finally {
                this.revokeModal.busy = false;
            }
        },

        // ================================ Téléchargement de fichier ================================

        /**
         * Téléchargement binaire — fetch bas niveau plutôt que window.api : la réponse est un fichier
         * (CSV / PDF / XLSX), pas du JSON, et le jeton doit voyager en en-tête (il vit dans
         * localStorage, jamais dans un cookie). Même mécanique que financial-report.js /
         * attendance-report.js. Sur erreur, on relit le corps JSON pour remonter le message normalisé.
         */
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
                const err = new Error((payload && payload.message) || `Erreur HTTP ${response.status}`);
                err.code = payload && payload.code;
                err.details = payload && payload.details;
                err.status = response.status;
                throw err;
            }

            const blob = await response.blob();
            const objectUrl = URL.createObjectURL(blob);
            const link = document.createElement('a');
            link.href = objectUrl;
            link.download = this.fileNameFrom(response, fallbackName);
            document.body.appendChild(link);
            link.click();
            link.remove();
            URL.revokeObjectURL(objectUrl);
        },

        fileNameFrom(response, fallbackName) {
            const disposition = response.headers.get('Content-Disposition') || '';
            const match = disposition.match(/filename\*?=(?:UTF-8'')?"?([^";]+)"?/i);
            return match ? decodeURIComponent(match[1]) : fallbackName;
        }
    }));
});
