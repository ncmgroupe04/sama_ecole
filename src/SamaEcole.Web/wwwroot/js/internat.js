/**
 * Module Internat (/internat) — tableau de bord d'occupation des dortoirs pour l'année scolaire
 * active, KPIs globaux, détail par chambre (Task 15) et modale d'affectation rapide (Task 16, spec
 * docs/superpowers/specs/2026-09-18-module-internat-design.md §5.3/§5.4).
 *
 * Consomme GET /api/v1/internat/dashboard (Task 5/8, InternatController.Dashboard), réservé à
 * Directeur/Secrétariat/Surveillant côté serveur et verrouillé par [RequireModule(Internat)] : un
 * Directeur qui n'a pas activé le module reçoit un 403 MODULE_DISABLED, restitué ici comme un
 * message d'erreur générique via window.api.toMessage — cet écran n'a pas besoin de connaître le
 * code d'erreur pour rester correct.
 *
 * La modale d'affectation (Task 16) consomme en plus GET /api/v1/internat/students/search?term=
 * (autocomplétion, Task 8) et POST /api/v1/internat/assignments/{enrollmentId} (Task 8). Elle sert
 * aussi bien l'affectation initiale (bouton « + Affecter un élève » d'une carte de chambre) que le
 * transfert/la libération d'un occupant déjà logé (bouton « Changer / Libérer » de l'accordéon) — un
 * seul flux, spec §6.2 ne distinguant les deux qu'au niveau du payload envoyé, jamais de l'UI.
 */
document.addEventListener('alpine:init', () => {
    Alpine.data('internatPage', () => ({
        loading: true,
        error: null,
        dashboard: null, // InternatDashboardDto, ou null tant que non chargé/en erreur

        async init() {
            await this.loadDashboard();
        },

        async loadDashboard() {
            this.loading = true;
            this.error = null;
            try {
                this.dashboard = await window.api.get('/internat/dashboard');
            } catch (err) {
                this.error = window.api.toMessage(err, "Erreur lors du chargement du tableau de bord de l'internat.");
            } finally {
                this.loading = false;
            }
        },

        /**
         * Couleur de la jauge d'occupation d'une chambre. Trois seuils, dans le vocabulaire de teinte
         * déjà utilisé par StatCardTagHelper (emerald/amber/rose, jamais "success"/"danger" en classe
         * Tailwind directe) : vide (slate, rien à signaler), en cours de remplissage (emerald), proche
         * de la capacité — 80 % et plus, encore un lit libre (amber), complète (rose).
         */
        occupancyColor(room) {
            if (room.occupantsCount <= 0) return 'bg-slate-300';
            if (room.occupantsCount >= room.capacity) return 'bg-rose-500';
            if (room.occupantsCount / room.capacity >= 0.8) return 'bg-amber-500';
            return 'bg-emerald-500';
        },

        occupancyPercent(room) {
            return room.capacity > 0 ? Math.min(100, Math.round((room.occupantsCount / room.capacity) * 100)) : 0;
        },

        // --- Modale d'affectation rapide (Task 16) ---------------------------------------------

        assignModal: { open: false, room: null, searchTerm: '', results: [], selected: null, boardingStatus: 'Interne', includeBoardingFee: true, saving: false, error: null },
        searchDebounce: null,

        /** Ouvre la modale pour la chambre `room` — état neuf à chaque ouverture (jamais de résidu
         *  d'une affectation précédente : élève sélectionné, erreur, case pension...). */
        openAssignModal(room) {
            this.assignModal = { open: true, room, searchTerm: '', results: [], selected: null, boardingStatus: 'Interne', includeBoardingFee: true, saving: false, error: null };
        },

        closeAssignModal() {
            this.assignModal.open = false;
        },

        /** Debounce 300 ms : évite une requête à chaque frappe pendant la saisie du nom/matricule. */
        onSearchInput() {
            clearTimeout(this.searchDebounce);
            this.searchDebounce = setTimeout(() => this.searchStudents(), 300);
        },

        async searchStudents() {
            const term = this.assignModal.searchTerm.trim();
            if (term.length < 2) { this.assignModal.results = []; return; }
            try {
                this.assignModal.results = await window.api.get('/internat/students/search?term=' + encodeURIComponent(term));
            } catch {
                this.assignModal.results = [];
            }
        },

        selectStudent(student) {
            this.assignModal.selected = student;
            this.assignModal.results = [];
            this.assignModal.searchTerm = student.fullName;
        },

        /** Un transfert est un changement de chambre pour un élève DÉJÀ logé ailleurs — pas une simple
         *  confirmation de la chambre où il se trouve déjà, ni une libération (currentRoomId reste
         *  renseigné mais la comparaison porte sur la chambre CIBLE de cette modale). */
        isTransfer() {
            const s = this.assignModal.selected;
            return !!(s && s.currentRoomId && s.currentRoomId !== this.assignModal.room.roomId);
        },

        /** true pour l'option de régime « Externe (libérer) » — RoomId envoyé à null dans ce cas. */
        isReleasing() {
            return this.assignModal.boardingStatus === 'Externe';
        },

        async confirmAssignment() {
            const modal = this.assignModal;
            if (!modal.selected) return;

            modal.saving = true;
            modal.error = null;
            try {
                await window.api.post(`/internat/assignments/${modal.selected.enrollmentId}`, {
                    roomId: modal.boardingStatus === 'Externe' ? null : modal.room.roomId,
                    boardingStatus: modal.boardingStatus,
                    includeBoardingFee: modal.includeBoardingFee,
                    // Jeton xmin RÉEL renvoyé par la recherche (BoardableStudentDto.RowVersion,
                    // ajouté en Correction 1) — jamais un repli à 0, qui provoquerait un 409
                    // systématique dès qu'une inscription a déjà été modifiée une fois.
                    rowVersion: modal.selected.rowVersion
                });
                this.closeAssignModal();
                await this.loadDashboard();
            } catch (e) {
                const status = e && e.status;
                if (status === 422) {
                    modal.error = window.api.toMessage ? window.api.toMessage(e, "Cette chambre a atteint sa capacité maximale.") : "Cette chambre a atteint sa capacité maximale.";
                } else if (status === 409) {
                    modal.error = "Le dossier de cet élève a été modifié par un autre utilisateur.";
                } else {
                    modal.error = window.api.toMessage ? window.api.toMessage(e) : "Une erreur est survenue.";
                }
            } finally {
                modal.saving = false;
            }
        },

        /** Bouton « Changer / Libérer » d'un occupant déjà logé dans `room` (Task 15, accordéon) —
         *  réutilise la même modale : ouverture pour CETTE chambre, puis recherche pré-remplie sur le
         *  nom de l'occupant pour qu'il n'ait plus qu'à cliquer son propre nom dans les résultats. */
        openChangeOccupantModal(room, occupant) {
            this.openAssignModal(room);
            this.assignModal.searchTerm = occupant.fullName;
            this.searchStudents();
        }
    }));
});
