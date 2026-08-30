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
 *  - Certificats de mutation : le registre des pièces délivrées (JGK-M06), avec délivrance,
 *    révocation, et téléchargement du livret de compétences (JGK-M07) — un élève se retrouve par
 *    recherche nom/matricule (même widget que /caisse), pas depuis la fiche élève.
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

        // Délivrance d'un certificat de mutation (JGK-M06) : recherche élève + formulaire + résultat.
        issueModal: {
            open: false,
            studentSearch: '', students: [], isSearchingStudents: false, studentsLoaded: false, studentSearchError: null,
            selectedStudent: null,
            schoolYearId: '', reason: 'Demenagement', reasonDetails: '',
            destinationSchoolName: '', destinationCity: '',
            busy: false, error: null, result: null,
            bookletBusy: false, bookletError: null
        },

        // Téléchargement autonome du livret de compétences (JGK-M07), sans passer par une mutation.
        livretModal: {
            open: false,
            studentSearch: '', students: [], isSearchingStudents: false, studentsLoaded: false, studentSearchError: null,
            selectedStudent: null,
            schoolYearId: '', busy: false, error: null
        },

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

        // ================================ Délivrance de certificat (JGK-M06) ================================

        get reasonOptions() {
            return Object.keys(REASON_LABELS).map((value) => ({ value, label: REASON_LABELS[value] }));
        },

        get issueYearOptions() {
            return [{ value: '', label: 'Sélectionnez une année…' }]
                .concat(this.schoolYears.map((y) => ({ value: y.id, label: y.label })));
        },

        openIssue() {
            const active = this.schoolYears.find((y) => y.isActive) || this.schoolYears[0];
            this.issueModal = {
                open: true,
                studentSearch: '', students: [], isSearchingStudents: false, studentsLoaded: false, studentSearchError: null,
                selectedStudent: null,
                schoolYearId: active ? active.id : '', reason: 'Demenagement', reasonDetails: '',
                destinationSchoolName: '', destinationCity: '',
                busy: false, error: null, result: null,
                bookletBusy: false, bookletError: null
            };
        },
        closeIssue() { this.issueModal.open = false; },

        /**
         * Recherche CÔTÉ SERVEUR (GET /students?search=…), même mécanique que /caisse (studentSearch
         * de caisse.js) : un établissement compte couramment plus de mille élèves, un plafond de page
         * chargé une fois pour toutes les rendrait invisibles à la recherche. Anti-rebond dans la vue
         * (x-on:input.debounce.300ms). Factorisée sur `modal` pour servir issueModal ET livretModal.
         */
        async searchStudentsFor(modal) {
            modal.selectedStudent = null;
            const q = modal.studentSearch.trim();
            if (q.length < 2) {
                modal.students = [];
                modal.studentsLoaded = false;
                modal.studentSearchError = null;
                return;
            }
            modal.isSearchingStudents = true;
            modal.studentSearchError = null;
            try {
                const page = await window.api.get(`/students?search=${encodeURIComponent(q)}&page=1&pageSize=20`);
                modal.students = page.items;
                modal.studentsLoaded = true;
            } catch (err) {
                modal.studentSearchError = window.api.toMessage(err, 'Erreur lors de la recherche.');
            } finally {
                modal.isSearchingStudents = false;
            }
        },
        selectStudentFor(modal, student) {
            modal.selectedStudent = student;
            modal.studentSearch = `${student.matricule} — ${student.fullName}`;
            modal.students = [];
        },

        searchIssueStudents() { this.searchStudentsFor(this.issueModal); },
        selectIssueStudent(s) { this.selectStudentFor(this.issueModal, s); },

        async submitIssue() {
            const m = this.issueModal;
            if (!m.selectedStudent || !m.schoolYearId || m.busy) return;
            if (m.reason === 'Autre' && !m.reasonDetails.trim()) return;

            m.busy = true;
            m.error = null;
            try {
                const outcome = await this.postAndDownloadFile(
                    `/api/v1/state-integration/students/${m.selectedStudent.id}/mutation-certificate`,
                    {
                        schoolYearId: m.schoolYearId,
                        reason: m.reason,
                        reasonDetails: m.reasonDetails.trim() || null,
                        destinationSchoolName: m.destinationSchoolName.trim() || null,
                        destinationCity: m.destinationCity.trim() || null
                    },
                    'certificat-mutation.pdf'
                );
                m.result = outcome;
                await this.loadCertificates();
            } catch (err) {
                m.error = window.api.toMessage(err, 'Erreur lors de la délivrance du certificat.');
            } finally {
                m.busy = false;
            }
        },

        // Après délivrance, le livret est la pièce que l'école d'accueil réclame « aux côtés du
        // certificat » (Volume 1 §23.6) : proposé ici avec le même élève et la même année, sans ressaisie.
        async downloadIssueBooklet() {
            const m = this.issueModal;
            if (!m.selectedStudent || !m.schoolYearId || m.bookletBusy) return;

            m.bookletBusy = true;
            m.bookletError = null;
            try {
                await this.downloadFile(
                    `/api/v1/state-integration/students/${m.selectedStudent.id}/skills-booklet?schoolYearId=${m.schoolYearId}`,
                    'livret-competences.pdf'
                );
            } catch (err) {
                m.bookletError = window.api.toMessage(err, "Erreur lors du téléchargement du livret.");
            } finally {
                m.bookletBusy = false;
            }
        },

        // ================================ Livret de compétences autonome (JGK-M07) ================================

        openLivret() {
            const active = this.schoolYears.find((y) => y.isActive) || this.schoolYears[0];
            this.livretModal = {
                open: true,
                studentSearch: '', students: [], isSearchingStudents: false, studentsLoaded: false, studentSearchError: null,
                selectedStudent: null,
                schoolYearId: active ? active.id : '', busy: false, error: null
            };
        },
        closeLivret() { this.livretModal.open = false; },

        searchLivretStudents() { this.searchStudentsFor(this.livretModal); },
        selectLivretStudent(s) { this.selectStudentFor(this.livretModal, s); },

        async downloadLivret() {
            const m = this.livretModal;
            if (!m.selectedStudent || !m.schoolYearId || m.busy) return;

            m.busy = true;
            m.error = null;
            try {
                await this.downloadFile(
                    `/api/v1/state-integration/students/${m.selectedStudent.id}/skills-booklet?schoolYearId=${m.schoolYearId}`,
                    'livret-competences.pdf'
                );
                m.open = false;
            } catch (err) {
                // 409 = pas de grille de compétences configurée pour le niveau de l'élève.
                m.error = window.api.toMessage(err, "Erreur lors du téléchargement du livret. Vérifiez que le niveau de l'élève dispose d'une grille de compétences APC configurée.");
            } finally {
                m.busy = false;
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
        },

        /**
         * Variante POST de downloadFile() : la délivrance d'un certificat de mutation ÉCRIT (numéro
         * séquentiel, ligne en base) et renvoie le PDF en même temps — le contrôleur fait voyager le
         * numéro et l'état financier en EN-TÊTES (X-Certificate-Number, X-Financially-Clear) parce que
         * le corps de la réponse est déjà pris par le PDF. On les lit ici pour les rendre à l'écran sans
         * ressaisir de requête.
         */
        async postAndDownloadFile(url, body, fallbackName) {
            if (window.auth.isAuthenticated() && window.auth.isAccessTokenStale()) {
                await window.api.refreshOrRedirect();
            }

            const response = await fetch(url, {
                method: 'POST',
                headers: {
                    Authorization: `Bearer ${window.auth.accessToken}`,
                    'Content-Type': 'application/json'
                },
                credentials: 'same-origin',
                body: JSON.stringify(body)
            });

            if (!response.ok) {
                const payload = await response.json().catch(() => null);
                const err = new Error((payload && payload.message) || `Erreur HTTP ${response.status}`);
                err.code = payload && payload.code;
                err.details = payload && payload.details;
                err.status = response.status;
                throw err;
            }

            const certificateNumber = response.headers.get('X-Certificate-Number');
            const wasFinanciallyClear = response.headers.get('X-Financially-Clear') === 'true';

            const blob = await response.blob();
            const objectUrl = URL.createObjectURL(blob);
            const link = document.createElement('a');
            link.href = objectUrl;
            link.download = this.fileNameFrom(response, fallbackName);
            document.body.appendChild(link);
            link.click();
            link.remove();
            URL.revokeObjectURL(objectUrl);

            return { certificateNumber, wasFinanciallyClear };
        }
    }));
});
