/**
 * Onglet « Occupation » de l'écran Internat (/internat) — tableau de bord d'occupation et affectation des élèves, sur /api/v1/boarding (lot G, tâche 7).
 *
 * Remplace l'écran historique qui appelait GET /internat/dashboard, GET /internat/students/search et POST /internat/assignments/{id} : ces trois routes
 * héritées, fondées sur l'ancien modèle Room, peuvent ainsi être supprimées au lot F. Plus aucun appel à `/internat/*` ici (verrouillé par un test).
 *
 * Données : GET /boarding/dormitories (pavillons), GET /boarding/dormitories/{id} (chambres, lits et occupants), GET /boarding/boarders?regime=… (les
 * compteurs Internes / Demi-pensionnaires / En attente de lit se lisent dans `totalCount`, sans rapatrier les lignes).
 * Affectation : GET /boarding/boardable-students?term= → POST /boarding/assign-bed (un transfert renvoie le `rowVersion` du SÉJOUR, lu dans la recherche) ;
 * libération : DELETE /boarding/unassign-bed/{boarderId}?rowVersion=.
 *
 * Le serveur reste le juge : lit déjà pris (409 BED_UNAVAILABLE), lit en maintenance, genre du pavillon, sortie en cours (409 LEAVE_IN_PROGRESS), droits et
 * module (403). L'écran traduit ces refus (`window.internatHelpers.errorMessage`) et recharge au lieu de supposer.
 *
 * La logique pure vit dans `window.occupationLogic`, testée sans navigateur (tests/js/internat-occupation.test.mjs).
 */
window.occupationLogic = (() => {
    const REGIMES = {
        Interne: 'Interne',
        DemiPensionnaire: 'Demi-pensionnaire',
        Externe: 'Externe (libérer)'
    };

    /** Lits utilisables d'une chambre qui sont réellement libres. Un lit occupé ou en maintenance n'est jamais proposé. */
    function freeBeds(room) {
        return (room.beds || []).filter((b) => b.status === 'Available');
    }

    /** Nombre de lits occupés d'une chambre (statut projeté par l'API depuis le séjour actif). */
    function occupiedCount(room) {
        return (room.beds || []).filter((b) => b.status === 'Occupied').length;
    }

    /** Une chambre est complète quand elle n'a plus de lit libre mais en a au moins un utilisable (une chambre vide de lits n'est pas « complète »). */
    function isRoomFull(room) {
        return room.capacity > 0 && occupiedCount(room) >= room.capacity;
    }

    /**
     * Chambres de tous les pavillons, à plat, avec ce dont une carte a besoin. Les occupants viennent des lits occupés (nom et identifiant du séjour).
     */
    function flattenRooms(details) {
        return (details || []).flatMap((d) => (d.rooms || []).map((room) => ({
            dormitoryId: d.id,
            dormitoryName: d.name,
            roomId: room.id,
            roomName: room.name,
            capacity: room.capacity,
            occupied: occupiedCount(room),
            full: isRoomFull(room),
            beds: room.beds || [],
            occupants: (room.beds || [])
                .filter((b) => b.status === 'Occupied' && b.occupantBoarderId)
                .map((b) => ({ boarderId: b.occupantBoarderId, fullName: b.occupantName, bedId: b.id, bedNumber: b.bedNumber }))
        })));
    }

    /** Indicateurs globaux. Les compteurs de régime viennent du serveur (`totalCount`), l'occupation des lits des chambres. */
    function totals(rooms, counts) {
        const capacity = rooms.reduce((n, r) => n + r.capacity, 0);
        const occupied = rooms.reduce((n, r) => n + r.occupied, 0);
        return {
            capacity,
            occupied,
            roomCount: rooms.length,
            fullRooms: rooms.filter((r) => r.full).length,
            interne: counts.interne,
            demiPensionnaire: counts.demiPensionnaire,
            awaitingBed: counts.awaitingBed
        };
    }

    /**
     * Lits proposables dans la modale : les lits libres de la chambre ciblée, ou de tout l'internat si l'affectation est lancée sans chambre.
     * Chaque option porte le pavillon et la chambre pour lever toute ambiguïté.
     */
    function bedOptions(rooms, roomId) {
        return rooms
            .filter((r) => !roomId || r.roomId === roomId)
            .flatMap((r) => freeBeds(r).map((b) => ({
                value: b.id,
                label: `${r.dormitoryName} — ${r.roomName} — Lit ${b.bedNumber}`
            })));
    }

    /** Régime ACTUEL d'un candidat de la recherche : « Externe » s'il n'a pas de séjour actif. */
    function currentRegime(candidate) {
        return candidate.regime || 'Externe';
    }

    /** Phrase de situation d'un candidat (badge de la modale). */
    function situationLabel(candidate) {
        const regime = currentRegime(candidate);
        if (regime === 'Externe') return 'Externe';
        const where = candidate.bedNumber
            ? ` — ${candidate.dormitoryName} · ${candidate.roomName} · Lit ${candidate.bedNumber}`
            : '';
        return `${REGIMES[regime] || regime}${where}`;
    }

    /** Régime proposé par défaut : celui de l'élève (ne jamais convertir silencieusement un demi-pensionnaire), « Interne » pour un externe. */
    function defaultRegime(candidate) {
        const regime = currentRegime(candidate);
        return regime === 'Externe' ? 'Interne' : regime;
    }

    /** Vrai si l'élève a déjà un séjour actif et qu'on le déplace vers un AUTRE lit. */
    function isTransfer(candidate, regime, bedId) {
        return !!candidate && !!candidate.boarderId && regime !== 'Externe' && !!bedId && candidate.bedId !== bedId;
    }

    /**
     * Corps de POST /boarding/assign-bed. Un demi-pensionnaire n'a jamais de lit ; un interne en exige un (le serveur répond 422 sinon). Le `rowVersion`
     * est celui du SÉJOUR lu dans la recherche : il n'est envoyé que s'il y a un séjour (transfert), jamais inventé.
     */
    function buildAssignBody(candidate, regime, bedId, includeBoardingFee) {
        return {
            enrollmentId: candidate.enrollmentId,
            regime,
            bedId: regime === 'Interne' ? bedId : null,
            includeBoardingFee: !!includeBoardingFee,
            rowVersion: candidate.boarderId && candidate.rowVersion !== undefined ? candidate.rowVersion : null
        };
    }

    /** Message avant l'appel si l'affectation est incomplète ; chaîne vide si elle peut partir. */
    function assignBlocker(candidate, regime, bedId) {
        if (!candidate) return 'Choisissez un élève.';
        if (regime === 'Externe') return candidate.boarderId ? '' : "Cet élève n'a pas de séjour à libérer.";
        if (regime === 'Interne' && !bedId) return 'Choisissez un lit disponible.';
        return '';
    }

    return {
        REGIMES, freeBeds, occupiedCount, isRoomFull, flattenRooms, totals, bedOptions,
        currentRegime, situationLabel, defaultRegime, isTransfer, buildAssignBody, assignBlocker
    };
})();

document.addEventListener('alpine:init', () => {
    const SEARCH_DEBOUNCE_MS = 300;
    const MIN_TERM = 2;
    const WRITE_ROLES = ['Directeur', 'Secretariat', 'Surveillant'];

    Alpine.data('occupationTab', () => ({
        loading: true,
        error: '',
        notice: '',
        dormitories: [],
        details: [],
        counts: { interne: 0, demiPensionnaire: 0, awaitingBed: 0 },
        canAssign: false,
        loadedAt: 0,
        searchDebounce: null,
        assignModal: {
            open: false, roomId: null, roomLabel: '', searchTerm: '', results: [], selected: null,
            regime: 'Interne', bedId: '', includeBoardingFee: true, saving: false, error: ''
        },

        async init() {
            this.canAssign = WRITE_ROLES.includes(window.auth.role);
            await this.load();
        },

        helpers() { return window.internatHelpers; },
        logic() { return window.occupationLogic; },

        // ------------------------------------------------------------------ Lecture

        async load() {
            this.loading = true;
            this.error = '';
            try {
                const summaries = await window.api.get('/boarding/dormitories');
                const [details, interne, demi, awaiting] = await Promise.all([
                    Promise.all(summaries.map((d) => window.api.get(`/boarding/dormitories/${d.id}`))),
                    window.api.get('/boarding/boarders?regime=Interne&pageSize=1'),
                    window.api.get('/boarding/boarders?regime=DemiPensionnaire&pageSize=1'),
                    window.api.get('/boarding/boarders?status=awaitingBed&pageSize=1')
                ]);

                this.dormitories = summaries;
                this.details = details;
                this.counts = { interne: interne.totalCount, demiPensionnaire: demi.totalCount, awaitingBed: awaiting.totalCount };
            } catch (err) {
                this.error = this.helpers().errorMessage(err, "Erreur lors du chargement du tableau de bord de l'internat.");
            } finally {
                this.loading = false;
                this.loadedAt = Date.now();
            }
        },

        /** Relit à l'ouverture de l'onglet (une affectation faite ailleurs doit apparaître), sauf modale d'affectation ouverte ou en cours. */
        async onTabShown(key) {
            const busy = this.loading || this.assignModal.open || this.assignModal.saving;
            if (this.helpers().shouldRefreshOnShow({ tabKey: 'occupation', shownKey: key, busy, loadedAt: this.loadedAt, now: Date.now() })) await this.load();
        },

        get rooms() { return this.logic().flattenRooms(this.details); },
        get totals() { return this.logic().totals(this.rooms, this.counts); },

        occupancyPercent(room) { return this.helpers().occupancyPercent(room.occupied, room.capacity); },
        occupancyColor(room) { return this.helpers().occupancyColor(room.occupied, room.capacity); },

        // ------------------------------------------------------------------ Modale d'affectation

        /** Ouvre la modale, pour une chambre précise (ses lits libres) ou sans chambre (tous les lits libres, ou un demi-pensionnaire). */
        openAssignModal(room) {
            this.assignModal = {
                open: true, roomId: room ? room.roomId : null, roomLabel: room ? `${room.roomName} (${room.dormitoryName})` : '',
                searchTerm: '', results: [], selected: null, regime: 'Interne', bedId: '', includeBoardingFee: true, saving: false, error: ''
            };
        },

        closeAssignModal() {
            clearTimeout(this.searchDebounce);
            this.assignModal.open = false;
        },

        /** Bouton « Changer / Libérer » d'un occupant : même modale, recherche pré-remplie sur son nom pour qu'il n'ait qu'à cliquer son nom. */
        openChangeOccupantModal(room, occupant) {
            this.openAssignModal(room);
            this.assignModal.searchTerm = occupant.fullName;
            this.searchStudents();
        },

        /**
         * Debounce 300 ms : pas une requête par frappe. Invalide aussi une sélection périmée : si l'élève sélectionné ne correspond plus au texte
         * affiché, on l'efface — sinon « Confirmer » resterait actif et soumettrait le premier élève choisi alors que le champ en montre un autre.
         */
        onSearchInput() {
            const modal = this.assignModal;
            if (modal.selected && modal.searchTerm !== modal.selected.fullName) modal.selected = null;
            clearTimeout(this.searchDebounce);
            this.searchDebounce = setTimeout(() => this.searchStudents(), SEARCH_DEBOUNCE_MS);
        },

        async searchStudents() {
            const term = this.assignModal.searchTerm.trim();
            if (term.length < MIN_TERM) {
                this.assignModal.results = [];
                return;
            }
            try {
                this.assignModal.results = await window.api.get(`/boarding/boardable-students?term=${encodeURIComponent(term)}`);
            } catch {
                this.assignModal.results = [];
            }
        },

        selectStudent(student) {
            const modal = this.assignModal;
            modal.selected = student;
            modal.results = [];
            modal.searchTerm = student.fullName;
            modal.regime = this.logic().defaultRegime(student);
            // Pré-sélection du lit : le premier libre de la chambre ciblée (ou de l'internat). Jamais le lit de l'élève lui-même : il n'est pas « libre ».
            const options = this.bedOptions;
            modal.bedId = options.length > 0 ? options[0].value : '';
        },

        get bedOptions() { return this.logic().bedOptions(this.rooms, this.assignModal.roomId); },
        get situation() { return this.assignModal.selected ? this.logic().situationLabel(this.assignModal.selected) : ''; },
        get regimeChoices() {
            const choices = [{ value: 'Interne', label: 'Interne' }, { value: 'DemiPensionnaire', label: 'Demi-pensionnaire' }];
            // « Libérer » n'a de sens que pour un élève qui a un séjour.
            if (this.assignModal.selected && this.assignModal.selected.boarderId) choices.push({ value: 'Externe', label: 'Externe (libérer)' });
            return choices;
        },

        isReleasing() { return this.assignModal.regime === 'Externe'; },
        needsBed() { return this.assignModal.regime === 'Interne'; },
        isTransfer() {
            const modal = this.assignModal;
            return this.logic().isTransfer(modal.selected, modal.regime, modal.bedId);
        },
        canConfirm() {
            const modal = this.assignModal;
            return !modal.saving && this.logic().assignBlocker(modal.selected, modal.regime, modal.bedId) === '';
        },

        async confirmAssignment() {
            const modal = this.assignModal;
            const blocker = this.logic().assignBlocker(modal.selected, modal.regime, modal.bedId);
            if (blocker) {
                modal.error = blocker;
                return;
            }

            modal.saving = true;
            modal.error = '';
            try {
                if (modal.regime === 'Externe') {
                    await window.api.delete(`/boarding/unassign-bed/${modal.selected.boarderId}?rowVersion=${modal.selected.rowVersion}`);
                    this.notice = `${modal.selected.fullName} a été libéré(e).`;
                } else {
                    await window.api.post(
                        '/boarding/assign-bed',
                        this.logic().buildAssignBody(modal.selected, modal.regime, modal.bedId, modal.includeBoardingFee));
                    this.notice = `${modal.selected.fullName} a été affecté(e).`;
                }
                this.closeAssignModal();
                await this.load();
            } catch (err) {
                modal.error = this.helpers().errorMessage(err, 'Une erreur est survenue.');
                // Lit pris entre-temps, ou dossier modifié par un autre : les lits proposés et le jeton sont périmés, on relit.
                if (err && (err.code === 'BED_UNAVAILABLE' || err.code === 'CONCURRENCY_CONFLICT')) {
                    modal.selected = null;
                    modal.results = [];
                    await this.load();
                }
            } finally {
                modal.saving = false;
            }
        }
    }));
});
