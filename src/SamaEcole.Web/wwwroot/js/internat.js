/**
 * Module Internat (/internat) — tableau de bord de lecture : occupation des dortoirs pour l'année
 * scolaire active, KPIs globaux et détail par chambre (Task 15, spec
 * docs/superpowers/specs/2026-09-18-module-internat-design.md §5.3).
 *
 * Écran volontairement en LECTURE SEULE à ce stade : la modale d'affectation (recherche d'élève,
 * badge de statut, transfert de chambre, confirmation) est ajoutée à ce même composant Alpine au
 * Task 16, pas dans un fichier séparé — voir le stub `openAssignModal(room)` en bas de fichier.
 *
 * Consomme GET /api/v1/internat/dashboard (Task 5/8, InternatController.Dashboard), réservé à
 * Directeur/Secrétariat/Surveillant côté serveur et verrouillé par [RequireModule(Internat)] : un
 * Directeur qui n'a pas activé le module reçoit un 403 MODULE_DISABLED, restitué ici comme un
 * message d'erreur générique via window.api.toMessage — cet écran n'a pas besoin de connaître le
 * code d'erreur pour rester correct.
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

        /** Implémenté au Task 16 (modale d'affectation) — stub pour que le bouton de la carte existe déjà. */
        openAssignModal(room) {
            // eslint-disable-next-line no-console
            console.info('Affectation à la chambre — modale à venir (Task 16).', room);
        }
    }));
});
