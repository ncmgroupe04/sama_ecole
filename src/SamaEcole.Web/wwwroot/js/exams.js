/**
 * Module Examens officiels (/examens) — constitution et suivi des dossiers de candidature CFEE/BFEM/BAC,
 * de l'ouverture du dossier à la transmission à l'IEF/l'IA et à la saisie des résultats de délibération
 * (Volume 1 §22, docs/Volume_4_API_Design.md §22).
 *
 * Matrice de droits reproduite ICI en confort d'affichage — la garde réelle est ExamsController :
 * - Sessions, statistiques, audit, création/modification de dossier, attribution centre/table,
 *   transmission, résultats, export ministériel, impression par lot, dispatch de convocations :
 *   `Directeur`/`Secretariat` uniquement (attribut de classe du contrôleur).
 * - Liste des dossiers + fiche détaillée : ouvertes en plus à l'`Enseignant`, borné par le serveur à
 *   ses classes assignées (ExamDossierScopeAuthorizer, ticket JGK-J08) — cet écran ne réplique PAS ce
 *   filtre, il se contente de ne proposer aucune action d'écriture à l'Enseignant et de laisser
 *   remonter un éventuel 403 (lien direct vers un dossier hors de ses classes) comme une erreur lisible.
 *
 * Un dossier `Incomplet` ne peut ni être transmis, ni entrer dans un lot d'impression ou un export
 * ministériel (Volume 1 §22.3) — c'est l'audit (onglet Audit) qui fait foi, pas une case cochée.
 */
document.addEventListener('alpine:init', () => {
    const EXAM_TYPE_OPTIONS = [
        { value: 'CFEE', label: 'CFEE' },
        { value: 'BFEM', label: 'BFEM' },
        { value: 'BAC', label: 'BAC' }
    ];

    const SESSION_STATUS_LABELS = {
        EnPreparation: 'En préparation',
        InscriptionsOuvertes: 'Inscriptions ouvertes',
        Transmis: 'Transmis',
        Clos: 'Clos'
    };

    const SESSION_STATUS_OPTIONS = [
        { value: 'EnPreparation', label: 'En préparation' },
        { value: 'InscriptionsOuvertes', label: 'Inscriptions ouvertes' },
        { value: 'Transmis', label: 'Transmis' },
        { value: 'Clos', label: 'Clos' }
    ];

    const DOSSIER_STATUS_LABELS = {
        Incomplet: 'Incomplet',
        Complet: 'Complet',
        Transmis: 'Transmis',
        Valide: 'Validé'
    };

    const DOSSIER_STATUS_FILTER_OPTIONS = [
        { value: '', label: 'Tous les statuts' },
        { value: 'Incomplet', label: 'Incomplet' },
        { value: 'Complet', label: 'Complet' },
        { value: 'Transmis', label: 'Transmis' },
        { value: 'Valide', label: 'Validé' }
    ];

    const MENTION_LABELS = {
        Passable: 'Passable',
        AssezBien: 'Assez bien',
        Bien: 'Bien',
        TresBien: 'Très bien'
    };

    const MENTION_OPTIONS = [
        { value: 'Passable', label: 'Passable' },
        { value: 'AssezBien', label: 'Assez bien' },
        { value: 'Bien', label: 'Bien' },
        { value: 'TresBien', label: 'Très bien' }
    ];

    const CIVIL_STATUS_OPTIONS = [
        { value: '', label: 'Non contrôlé' },
        { value: 'true', label: 'Conforme' },
        { value: 'false', label: 'Non conforme' }
    ];

    Alpine.data('examsView', () => ({
        ...window.pdfPreview.state(),

        // 'sessions' | 'dossiers' | 'audit' | 'statistiques'. L'Enseignant n'a droit qu'à Dossiers
        // (lecture) : les autres onglets restent Directeur/Secretariat (ExamsController, Roles de classe).
        tab: window.auth.role === 'Enseignant' ? 'dossiers' : 'sessions',
        error: null,

        canManage: window.auth.role === 'Directeur' || window.auth.role === 'Secretariat',

        examTypeOptions: EXAM_TYPE_OPTIONS,
        sessionStatusOptions: SESSION_STATUS_OPTIONS,
        dossierStatusFilterOptions: DOSSIER_STATUS_FILTER_OPTIONS,
        mentionOptions: MENTION_OPTIONS,
        civilStatusOptions: CIVIL_STATUS_OPTIONS,

        examSessionStatusLabel(status) { return SESSION_STATUS_LABELS[status] || status; },
        dossierStatusLabel(status) { return DOSSIER_STATUS_LABELS[status] || status; },
        mentionLabel(value) { return value ? (MENTION_LABELS[value] || value) : '—'; },

        dossierStatusVariant(status) {
            switch (status) {
                case 'Valide': return 'success';
                case 'Transmis': return 'primary';
                case 'Complet': return 'neutral';
                default: return 'warning'; // Incomplet
            }
        },

        civilStatusLabel(value) {
            if (value === true) return 'Conforme';
            if (value === false) return 'Non conforme';
            return 'Non contrôlé';
        },

        civilStatusVariant(value) {
            if (value === true) return 'success';
            if (value === false) return 'danger';
            return 'neutral';
        },

        // ---------------------------------------------------------------- Données de référence
        schoolYears: [],
        classrooms: [],
        // Sessions d'examen : rechargées par loadSessions() (onglet Sessions), mais aussi utilisées
        // comme source des sélecteurs de session (filtre Dossiers, création de dossier, audit,
        // dispatch) — d'où un chargement dès init() pour Directeur/Secretariat. Vide pour l'Enseignant
        // (GET /exams/sessions lui est fermé, ExamsController) : son filtre « Session » du tableau des
        // dossiers reste alors limité au seul choix « Toutes les sessions », dégradation acceptée.
        sessions: [],

        get schoolYearOptions() {
            return this.schoolYears.map((y) => ({ value: y.id, label: y.label }));
        },

        get activeSchoolYearId() {
            const active = this.schoolYears.find((y) => y.isActive);
            return active ? active.id : '';
        },

        get classroomOptions() {
            return this.classrooms.map((c) => ({ value: c.id, label: c.name }));
        },

        get sessionOptions() {
            return this.sessions.map((s) => ({ value: s.id, label: this.sessionLabel(s) }));
        },

        sessionLabel(session) {
            if (!session) return '';
            const type = session.examType + (session.series ? ` ${session.series}` : '');
            const year = this.schoolYearLabel(session.schoolYearId);
            return year ? `${type} — ${year}` : type;
        },

        schoolYearLabel(id) {
            const year = this.schoolYears.find((y) => y.id === id);
            return year ? year.label : '';
        },

        async init() {
            await this.loadReferenceData();
            // loadReferenceData() a déjà chargé les sessions pour Directeur/Secretariat (onglet par
            // défaut) : ne les redemander ici que pour l'Enseignant, dont l'onglet par défaut est
            // Dossiers et qui n'a pas accès à GET /exams/sessions.
            if (this.tab === 'dossiers') await this.loadDossiers();
        },

        async loadReferenceData() {
            try {
                const requests = [window.api.get('/school-years'), window.api.get('/classrooms')];
                const [schoolYears, classrooms] = await Promise.all(requests);
                this.schoolYears = schoolYears || [];
                this.classrooms = classrooms || [];
                if (this.canManage) await this.loadSessions();
            } catch (err) {
                this.error = window.api.toMessage(err, 'Erreur lors du chargement des données de référence.');
            }
        },

        tabClass(name) {
            return this.tab === name
                ? 'bg-white text-indigo-600 font-semibold shadow-sm'
                : 'bg-transparent text-slate-700 font-medium hover:text-slate-900 hover:bg-slate-200/50';
        },

        switchTab(tab) {
            this.tab = tab;
            if (tab === 'sessions' && this.sessions.length === 0) this.loadSessions();
            if (tab === 'dossiers' && this.dossiers.length === 0) this.loadDossiers();
            if (tab === 'statistiques' && !this.statistics) this.loadStatistics();
        },

        toNullableNumber(value) {
            return value === '' || value === null || value === undefined ? null : Number(value);
        },

        toNullableTriStateBool(value) {
            return value === '' || value === null || value === undefined ? null : value === 'true';
        },

        /** DateOnly (« 2026-06-15 ») → jj/mm/aaaa, sans passer par un Date() qui déraperait d'un jour selon le fuseau. */
        formatDate(isoDate) {
            if (!isoDate) return '—';
            const [year, month, day] = isoDate.split('-');
            return `${day}/${month}/${year}`;
        },

        // ================================================================== ONGLET SESSIONS
        sessionFilters: { schoolYearId: '', examType: '' },
        isLoadingSessions: false,

        get totalDossiersAcrossSessions() {
            return this.sessions.reduce((sum, s) => sum + (s.dossierCount || 0), 0);
        },

        async loadSessions() {
            this.isLoadingSessions = true;
            this.error = null;
            try {
                const params = new URLSearchParams();
                if (this.sessionFilters.schoolYearId) params.set('schoolYearId', this.sessionFilters.schoolYearId);
                if (this.sessionFilters.examType) params.set('examType', this.sessionFilters.examType);
                const query = params.toString();
                this.sessions = await window.api.get(`/exams/sessions${query ? `?${query}` : ''}`);
            } catch (err) {
                this.error = window.api.toMessage(err, 'Erreur lors du chargement des sessions.');
            } finally {
                this.isLoadingSessions = false;
            }
        },

        applySessionFilters() { this.loadSessions(); },

        resetSessionFilters() {
            this.sessionFilters = { schoolYearId: '', examType: '' };
            this.applySessionFilters();
        },

        // ------------------------------------------------------------ Nouvelle session
        isCreateSessionOpen: false,
        isSavingSession: false,
        newSession: { schoolYearId: '', examType: 'BFEM', series: '', centerName: '' },
        createSessionErrors: {},

        openCreateSession() {
            this.newSession = { schoolYearId: this.activeSchoolYearId, examType: 'BFEM', series: '', centerName: '' };
            this.createSessionErrors = {};
            this.isCreateSessionOpen = true;
        },

        closeCreateSession() { this.isCreateSessionOpen = false; },

        /** Le CFEE n'a pas de série (Volume 1 §22.1) : la vider évite d'envoyer une valeur sans objet. */
        onNewSessionExamTypeChanged() {
            if (this.newSession.examType === 'CFEE') this.newSession.series = '';
        },

        async submitCreateSession() {
            this.isSavingSession = true;
            this.createSessionErrors = {};
            try {
                await window.api.post('/exams/sessions', {
                    schoolYearId: this.newSession.schoolYearId,
                    examType: this.newSession.examType,
                    series: this.newSession.examType === 'CFEE' ? null : (this.newSession.series || null),
                    centerName: this.newSession.centerName || null
                });
                this.isCreateSessionOpen = false;
                toast.success('Session créée.');
                await this.loadSessions();
            } catch (err) {
                this.createSessionErrors = window.api.toFieldErrors(err, 'Erreur lors de la création de la session.');
            } finally {
                this.isSavingSession = false;
            }
        },

        // ------------------------------------------------------------ Modification de session
        editingSession: null, // { id, centerName, status, rowVersion }
        isSavingEditSession: false,
        editSessionErrors: {},

        openEditSession(session) {
            this.editingSession = {
                id: session.id, centerName: session.centerName || '', status: session.status,
                rowVersion: session.rowVersion
            };
            this.editSessionErrors = {};
        },

        closeEditSession() { this.editingSession = null; this.editSessionErrors = {}; },

        async submitEditSession() {
            if (!this.editingSession) return;
            this.isSavingEditSession = true;
            this.editSessionErrors = {};
            try {
                await window.api.put(`/exams/sessions/${this.editingSession.id}`, {
                    centerName: this.editingSession.centerName || null,
                    status: this.editingSession.status,
                    rowVersion: this.editingSession.rowVersion
                });
                this.closeEditSession();
                toast.success('Session modifiée.');
                await this.loadSessions();
            } catch (err) {
                if (err.code === 'CONCURRENCY_CONFLICT') {
                    this.editSessionErrors = { global: "Cette session vient d'être modifiée par un autre utilisateur. La liste a été rafraîchie — vérifiez les valeurs puis réessayez." };
                    await this.loadSessions();
                } else {
                    this.editSessionErrors = window.api.toFieldErrors(err, 'Erreur lors de la modification.');
                }
            } finally {
                this.isSavingEditSession = false;
            }
        },

        // ------------------------------------------------------------ Export ministériel (Excel)
        exportingSessionId: null,

        /**
         * Téléchargement direct — fetch bas niveau plutôt que window.api : la réponse est un binaire,
         * pas du JSON, et le jeton doit voyager en en-tête. Même mécanique que l'export du rapport
         * financier (financial-report.js, exportExcel).
         */
        async exportMinisterial(session) {
            if (this.exportingSessionId) return;
            this.exportingSessionId = session.id;
            this.error = null;
            try {
                if (window.auth.isAuthenticated() && window.auth.isAccessTokenStale()) {
                    await window.api.refreshOrRedirect();
                }
                const params = new URLSearchParams({ examSessionId: session.id });
                const response = await fetch(`/api/v1/exams/export/ministerial?${params.toString()}`, {
                    headers: { Authorization: `Bearer ${window.auth.accessToken}` },
                    credentials: 'same-origin'
                });
                if (!response.ok) {
                    const apiErr = await window.api.toError(response);
                    this.error = window.api.toMessage(apiErr, "Erreur lors de l'export ministériel.");
                    return;
                }
                const blob = await response.blob();
                const url = URL.createObjectURL(blob);
                const link = document.createElement('a');
                link.href = url;
                link.download = this.exportFileName(response, session);
                document.body.appendChild(link);
                link.click();
                link.remove();
                URL.revokeObjectURL(url);
            } catch (err) {
                this.error = window.api.toMessage(err, "Erreur lors de l'export ministériel.");
            } finally {
                this.exportingSessionId = null;
            }
        },

        exportFileName(response, session) {
            const disposition = response.headers.get('Content-Disposition') || '';
            const match = disposition.match(/filename\*?=(?:UTF-8'')?"?([^";]+)"?/i);
            if (match) return decodeURIComponent(match[1]);
            return `Export-Ministeriel-${session.examType}${session.series ? `-${session.series}` : ''}.xlsx`;
        },

        // ------------------------------------------------------------ Dispatch des convocations (SMS)
        dispatchingSession: null, // { id, label }
        isDispatching: false,
        dispatchError: null,
        dispatchResult: null, // { queuedCount, skippedCount, firstSkipReason }

        openDispatchConvocations(session) {
            this.dispatchingSession = { id: session.id, label: this.sessionLabel(session) };
            this.dispatchError = null;
            this.dispatchResult = null;
        },

        closeDispatchConvocations() {
            this.dispatchingSession = null;
            this.dispatchError = null;
            this.dispatchResult = null;
        },

        /** Refusé (409) tant qu'un dossier du lot n'a pas de centre/table attribué. */
        async confirmDispatchConvocations() {
            if (!this.dispatchingSession) return;
            this.isDispatching = true;
            this.dispatchError = null;
            try {
                this.dispatchResult = await window.api.post(
                    `/exams/sessions/${this.dispatchingSession.id}/dispatch-convocations`, null);
                toast.success('Convocations envoyées.');
                await this.loadSessions();
            } catch (err) {
                this.dispatchError = window.api.toMessage(err, "Erreur lors de l'envoi des convocations.");
            } finally {
                this.isDispatching = false;
            }
        },

        // ================================================================== ONGLET DOSSIERS
        dossiers: [],
        dossiersTotalCount: 0,
        dossiersPage: 1,
        dossiersPageSize: 20,
        isLoadingDossiers: false,
        dossierFilters: { examSessionId: '', classroomId: '', status: '', search: '' },

        async loadDossiers() {
            this.isLoadingDossiers = true;
            this.error = null;
            try {
                const params = new URLSearchParams({ page: this.dossiersPage, pageSize: this.dossiersPageSize });
                if (this.dossierFilters.examSessionId) params.set('examSessionId', this.dossierFilters.examSessionId);
                if (this.dossierFilters.classroomId) params.set('classroomId', this.dossierFilters.classroomId);
                if (this.dossierFilters.status) params.set('status', this.dossierFilters.status);
                if (this.dossierFilters.search) params.set('search', this.dossierFilters.search);

                const data = await window.api.get(`/exams/dossiers?${params.toString()}`);
                this.dossiers = (data && data.items) || [];
                this.dossiersTotalCount = (data && data.totalCount) || 0;
            } catch (err) {
                this.error = window.api.toMessage(err, 'Erreur lors du chargement des dossiers.');
            } finally {
                this.isLoadingDossiers = false;
            }
        },

        applyDossierFilters() {
            this.dossiersPage = 1;
            this.loadDossiers();
        },

        resetDossierFilters() {
            this.dossierFilters = { examSessionId: '', classroomId: '', status: '', search: '' };
            this.applyDossierFilters();
        },

        // ------------------------------------------------------------ Nouveau dossier
        isCreateDossierOpen: false,
        isSavingDossier: false,
        newDossier: { examSessionId: '', studentId: '', studentLabel: '', classroomId: '' },
        createDossierErrors: {},
        dossierStudentSearch: '',
        dossierStudentResults: [],
        isSearchingDossierStudents: false,
        dossierStudentSearchError: null,

        openCreateDossier() {
            this.newDossier = { examSessionId: '', studentId: '', studentLabel: '', classroomId: '' };
            this.dossierStudentSearch = '';
            this.dossierStudentResults = [];
            this.dossierStudentSearchError = null;
            this.createDossierErrors = {};
            this.isCreateDossierOpen = true;
        },

        closeCreateDossier() { this.isCreateDossierOpen = false; },

        /** Recherche côté serveur (GET /students?search=…) — même motif que caisse.js searchStudents(). */
        async searchDossierStudents() {
            const q = this.dossierStudentSearch.trim();
            if (q.length < 2) {
                this.dossierStudentResults = [];
                this.dossierStudentSearchError = null;
                return;
            }
            this.isSearchingDossierStudents = true;
            this.dossierStudentSearchError = null;
            try {
                const page = await window.api.get(`/students?search=${encodeURIComponent(q)}&page=1&pageSize=20`);
                this.dossierStudentResults = page.items;
            } catch (err) {
                this.dossierStudentSearchError = window.api.toMessage(err, 'Erreur lors de la recherche.');
            } finally {
                this.isSearchingDossierStudents = false;
            }
        },

        /** La classe est préremplie depuis la classe ACTUELLE de l'élève, modifiable au besoin. */
        selectDossierStudent(student) {
            this.newDossier.studentId = student.id;
            this.newDossier.studentLabel = `${student.matricule} — ${student.fullName}`;
            this.newDossier.classroomId = student.classroomId;
            this.dossierStudentResults = [];
            this.dossierStudentSearch = '';
        },

        clearDossierStudent() {
            this.newDossier.studentId = '';
            this.newDossier.studentLabel = '';
            this.newDossier.classroomId = '';
        },

        async submitCreateDossier() {
            this.isSavingDossier = true;
            this.createDossierErrors = {};
            try {
                await window.api.post('/exams/dossiers', {
                    examSessionId: this.newDossier.examSessionId,
                    studentId: this.newDossier.studentId,
                    classroomId: this.newDossier.classroomId
                });
                this.isCreateDossierOpen = false;
                toast.success('Dossier créé.');
                await Promise.all([this.loadDossiers(), this.loadSessions()]);
            } catch (err) {
                this.createDossierErrors = window.api.toFieldErrors(err, 'Erreur lors de la création du dossier.');
            } finally {
                this.isSavingDossier = false;
            }
        },

        // ------------------------------------------------------------ Fiche détaillée (lecture)
        detailDossier: null,
        isLoadingDetail: false,
        detailError: null,

        async openDossierDetail(dossier) {
            this.detailDossier = { id: dossier.id, studentFullName: dossier.studentFullName };
            this.detailError = null;
            this.isLoadingDetail = true;
            try {
                this.detailDossier = await window.api.get(`/exams/dossiers/${dossier.id}`);
            } catch (err) {
                // Un lien direct vers un dossier hors des classes assignées d'un Enseignant renvoie 403
                // (ExamDossierScopeAuthorizer, JGK-J08) — affiché ici comme une erreur lisible, jamais
                // une absence silencieuse de contenu.
                this.detailError = window.api.toMessage(err, 'Erreur lors du chargement de la fiche.');
            } finally {
                this.isLoadingDetail = false;
            }
        },

        closeDossierDetail() { this.detailDossier = null; this.detailError = null; },

        // ------------------------------------------------------------ Modification (état civil / centre déclaré)
        editingDossier: null, // { id, examCenterName, birthCertificateNumber, birthCertificatePresent, civilStatusConforming, civilStatusNotes, rowVersion }
        isLoadingEditDossier: false,
        isSavingEditDossier: false,
        editDossierErrors: {},

        /** La fiche liste n'expose pas birthCertificateNumber/civilStatusNotes : on relit le détail avant d'ouvrir le formulaire. */
        async openEditDossier(dossier) {
            this.editDossierErrors = {};
            this.isLoadingEditDossier = true;
            try {
                const detail = await window.api.get(`/exams/dossiers/${dossier.id}`);
                this.editingDossier = {
                    id: detail.id,
                    examCenterName: detail.examCenterName || '',
                    birthCertificateNumber: detail.birthCertificateNumber || '',
                    birthCertificatePresent: detail.birthCertificatePresent,
                    civilStatusConforming: detail.civilStatusConforming === null || detail.civilStatusConforming === undefined
                        ? '' : String(detail.civilStatusConforming),
                    civilStatusNotes: detail.civilStatusNotes || '',
                    rowVersion: detail.rowVersion
                };
            } catch (err) {
                toast.error(window.api.toMessage(err, 'Erreur lors du chargement de la fiche.'));
            } finally {
                this.isLoadingEditDossier = false;
            }
        },

        closeEditDossier() { this.editingDossier = null; this.editDossierErrors = {}; },

        async submitEditDossier() {
            if (!this.editingDossier) return;
            this.isSavingEditDossier = true;
            this.editDossierErrors = {};
            try {
                await window.api.put(`/exams/dossiers/${this.editingDossier.id}`, {
                    examCenterName: this.editingDossier.examCenterName || null,
                    birthCertificateNumber: this.editingDossier.birthCertificateNumber || null,
                    birthCertificatePresent: this.editingDossier.birthCertificatePresent,
                    civilStatusConforming: this.toNullableTriStateBool(this.editingDossier.civilStatusConforming),
                    civilStatusNotes: this.editingDossier.civilStatusNotes || null,
                    rowVersion: this.editingDossier.rowVersion
                });
                this.closeEditDossier();
                toast.success('Dossier modifié.');
                await this.loadDossiers();
            } catch (err) {
                if (err.code === 'CONCURRENCY_CONFLICT') {
                    this.editDossierErrors = { global: "Ce dossier vient d'être modifié par un autre utilisateur. La liste a été rafraîchie — vérifiez les valeurs puis réessayez." };
                    await this.loadDossiers();
                } else {
                    this.editDossierErrors = window.api.toFieldErrors(err, 'Erreur lors de la modification.');
                }
            } finally {
                this.isSavingEditDossier = false;
            }
        },

        // ------------------------------------------------------------ Attribution centre/table
        assigningDossier: null, // { id, currentCandidateNumber, examCenterName, candidateNumber, rowVersion }
        isSavingAssignCenter: false,
        assignCenterErrors: {},

        /**
         * Le numéro de candidat reste VIDE par défaut (génération automatique dans la transaction,
         * AGENTS.md règle #3) — seul un utilisateur averti le renseigne à la main pour reprendre une
         * numérotation déjà communiquée par l'IEF/l'IA.
         */
        openAssignCenter(dossier) {
            this.assigningDossier = {
                id: dossier.id, currentCandidateNumber: dossier.candidateNumber || '',
                examCenterName: dossier.examCenterName || '', candidateNumber: '', rowVersion: dossier.rowVersion
            };
            this.assignCenterErrors = {};
        },

        closeAssignCenter() { this.assigningDossier = null; this.assignCenterErrors = {}; },

        async submitAssignCenter() {
            if (!this.assigningDossier) return;
            this.isSavingAssignCenter = true;
            this.assignCenterErrors = {};
            try {
                await window.api.post(`/exams/dossiers/${this.assigningDossier.id}/assign-center`, {
                    examCenterName: this.assigningDossier.examCenterName || null,
                    candidateNumber: this.assigningDossier.candidateNumber || null,
                    rowVersion: this.assigningDossier.rowVersion
                });
                this.closeAssignCenter();
                toast.success('Centre et numéro de table attribués.');
                await this.loadDossiers();
            } catch (err) {
                if (err.code === 'CONCURRENCY_CONFLICT') {
                    this.assignCenterErrors = { global: "Ce dossier vient d'être modifié par un autre utilisateur. La liste a été rafraîchie — vérifiez les valeurs puis réessayez." };
                    await this.loadDossiers();
                } else {
                    this.assignCenterErrors = window.api.toFieldErrors(err, "Erreur lors de l'attribution.");
                }
            } finally {
                this.isSavingAssignCenter = false;
            }
        },

        // ------------------------------------------------------------ Transmission
        transmittingDossier: null, // { id, studentFullName, status }
        isTransmitting: false,
        transmitError: null,

        openTransmitDossier(dossier) {
            this.transmittingDossier = { id: dossier.id, studentFullName: dossier.studentFullName, status: dossier.status };
            this.transmitError = null;
        },

        closeTransmitDossier() { this.transmittingDossier = null; this.transmitError = null; },

        /** Refusé (409) si le dossier est encore Incomplet — désactivé par avance côté écran quand on le sait déjà. */
        async confirmTransmitDossier() {
            if (!this.transmittingDossier) return;
            this.isTransmitting = true;
            this.transmitError = null;
            try {
                await window.api.post(`/exams/dossiers/${this.transmittingDossier.id}/transmit`, null);
                this.transmittingDossier = null;
                toast.success('Dossier transmis.');
                await this.loadDossiers();
            } catch (err) {
                this.transmitError = window.api.toMessage(err,
                    "Ce dossier est encore incomplet et ne peut pas être transmis — voir l'onglet Audit pour le détail des pièces manquantes.");
            } finally {
                this.isTransmitting = false;
            }
        },

        // ------------------------------------------------------------ Résultat de délibération
        recordingDossier: null, // { id, studentFullName, examType }
        // isAdmitted est une CHAÎNE ('true'/'false'), pas un booléen : x-model sur un groupe de
        // boutons radio lit toujours l'attribut value="" du DOM, converti en chaîne — même convention
        // que civilStatusConforming ci-dessus (tri-état), aucun autre écran de l'application ne lie un
        // radio à un booléen JS directement (voir Payroll/Index.cshtml, employeeKind).
        resultForm: { isAdmitted: 'true', mention: '', averageScore: '', deliberatedOn: '' },
        resultErrors: {},
        isSavingResult: false,

        openRecordResult(dossier) {
            const session = this.sessions.find((s) => s.id === dossier.examSessionId);
            this.recordingDossier = {
                id: dossier.id, studentFullName: dossier.studentFullName,
                examType: session ? session.examType : null
            };
            this.resultForm = { isAdmitted: 'true', mention: '', averageScore: '', deliberatedOn: '' };
            this.resultErrors = {};
        },

        closeRecordResult() { this.recordingDossier = null; this.resultErrors = {}; },

        /** Sans objet pour le CFEE, qui n'attribue pas de mention (Volume 1 §22.6). */
        get recordResultShowsMention() {
            return !!this.recordingDossier && this.recordingDossier.examType !== 'CFEE';
        },

        async submitRecordResult() {
            if (!this.recordingDossier) return;
            this.isSavingResult = true;
            this.resultErrors = {};
            try {
                await window.api.put(`/exams/dossiers/${this.recordingDossier.id}/result`, {
                    isAdmitted: this.resultForm.isAdmitted === 'true',
                    mention: this.recordResultShowsMention ? (this.resultForm.mention || null) : null,
                    averageScore: this.toNullableNumber(this.resultForm.averageScore),
                    deliberatedOn: this.resultForm.deliberatedOn || null
                });
                this.closeRecordResult();
                toast.success('Résultat enregistré.');
                await this.loadDossiers();
            } catch (err) {
                this.resultErrors = window.api.toFieldErrors(err, "Erreur lors de l'enregistrement du résultat.");
            } finally {
                this.isSavingResult = false;
            }
        },

        // ------------------------------------------------------------ Impression individuelle
        async printCandidateForm(dossier) {
            await this.openPdfPreview(
                `/api/v1/exams/dossiers/${dossier.id}/candidate-form/pdf`,
                `Fiche de candidature — ${dossier.studentFullName}`,
                `Fiche-Candidature-${dossier.candidateNumber || dossier.studentFullName}.pdf`);
        },

        /** Refusée (409) tant que centre et numéro de table ne sont pas attribués — message API affiché tel quel dans la modale d'aperçu. */
        async printConvocation(dossier) {
            await this.openPdfPreview(
                `/api/v1/exams/dossiers/${dossier.id}/convocation/pdf`,
                `Convocation — ${dossier.studentFullName}`,
                `Convocation-${dossier.candidateNumber || dossier.studentFullName}.pdf`);
        },

        // ------------------------------------------------------------ Impression par lot
        isBatchPrintOpen: false,
        isBatchPrinting: false,
        batchPrintForm: { examSessionId: '', classroomId: '' },
        batchPrintError: null,

        openBatchPrint() {
            this.batchPrintForm = { examSessionId: this.dossierFilters.examSessionId || '', classroomId: this.dossierFilters.classroomId || '' };
            this.batchPrintError = null;
            this.isBatchPrintOpen = true;
        },

        closeBatchPrint() { this.isBatchPrintOpen = false; },

        /**
         * POST à réponse binaire (filtre en corps JSON, pas en query) : la modale d'aperçu partagée
         * (pdf-preview.js) ne fait que du GET — on télécharge donc directement, comme l'export Excel.
         * 422 si aucun dossier du filtre n'est Complet/Transmis/Valide (Volume 1 §22.5).
         */
        async submitBatchPrint() {
            if (!this.batchPrintForm.examSessionId && !this.batchPrintForm.classroomId) {
                this.batchPrintError = 'Choisissez une session ou une classe.';
                return;
            }
            this.isBatchPrinting = true;
            this.batchPrintError = null;
            try {
                if (window.auth.isAuthenticated() && window.auth.isAccessTokenStale()) {
                    await window.api.refreshOrRedirect();
                }
                const response = await fetch('/api/v1/exams/dossiers/candidate-forms/pdf', {
                    method: 'POST',
                    headers: { Authorization: `Bearer ${window.auth.accessToken}`, 'Content-Type': 'application/json' },
                    credentials: 'same-origin',
                    body: JSON.stringify({
                        examSessionId: this.batchPrintForm.examSessionId || null,
                        classroomId: this.batchPrintForm.classroomId || null
                    })
                });
                if (!response.ok) {
                    const apiErr = await window.api.toError(response);
                    this.batchPrintError = window.api.toMessage(apiErr, "Erreur lors de l'impression par lot.");
                    return;
                }
                const blob = await response.blob();
                const url = URL.createObjectURL(blob);
                const link = document.createElement('a');
                link.href = url;
                link.download = 'Fiches-Candidature.pdf';
                document.body.appendChild(link);
                link.click();
                link.remove();
                URL.revokeObjectURL(url);
                this.isBatchPrintOpen = false;
            } catch (err) {
                this.batchPrintError = window.api.toMessage(err, "Erreur lors de l'impression par lot.");
            } finally {
                this.isBatchPrinting = false;
            }
        },

        // ================================================================== ONGLET AUDIT
        auditSessionId: '',
        auditEntries: [],
        isLoadingAudit: false,
        auditError: null,

        async loadAudit() {
            if (!this.auditSessionId) {
                this.auditEntries = [];
                return;
            }
            this.isLoadingAudit = true;
            this.auditError = null;
            try {
                this.auditEntries = await window.api.get(`/exams/dossiers/audit?examSessionId=${this.auditSessionId}`);
            } catch (err) {
                this.auditError = window.api.toMessage(err, "Erreur lors du chargement de l'audit.");
            } finally {
                this.isLoadingAudit = false;
            }
        },

        // ================================================================== ONGLET STATISTIQUES
        statsSchoolYearId: '',
        statistics: null,
        isLoadingStatistics: false,
        statisticsError: null,

        async loadStatistics() {
            this.isLoadingStatistics = true;
            this.statisticsError = null;
            try {
                const query = this.statsSchoolYearId ? `?schoolYearId=${this.statsSchoolYearId}` : '';
                this.statistics = await window.api.get(`/exams/statistics${query}`);
            } catch (err) {
                this.statisticsError = window.api.toMessage(err, 'Erreur lors du chargement des statistiques.');
            } finally {
                this.isLoadingStatistics = false;
            }
        },

        get statsTotalCandidates() {
            if (!this.statistics) return 0;
            return this.statistics.bySeries.reduce((sum, s) => sum + s.candidateCount, 0);
        },

        get statsTotalAdmitted() {
            if (!this.statistics) return 0;
            return this.statistics.bySeries.reduce((sum, s) => sum + s.admittedCount, 0);
        },

        get statsOverallRate() {
            const total = this.statsTotalCandidates;
            return total > 0 ? (this.statsTotalAdmitted / total) * 100 : 0;
        },

        seriesLabel(item) {
            return item.examType + (item.series ? ` ${item.series}` : '');
        },

        formatPercent(value) {
            return `${Math.round(value * 10) / 10} %`;
        }
    }));
});
