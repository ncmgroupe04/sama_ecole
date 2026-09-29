/**
 * Écran Coran/Franco-Arabe (/coran) — Phase 3, spec docs/superpowers/specs/2026-09-29-coran-screen-design.md.
 *
 * Un sélecteur de classe charge en parallèle GET /quran/progress/classroom/{id} et
 * GET /quran/evaluations/classroom/{id} (une ligne par élève, sa liste d'observations) —
 * QuranController, réservé par [RequireModule(SchoolModule.Coran)] côté serveur. Deux onglets
 * indépendants : l'échec de l'un n'empêche pas l'affichage de l'autre (Task 3).
 *
 * Écriture (ajout/correction) : Directeur + Enseignant (QuranController.WriteRoles). Lecture : +
 * Secrétariat — décision déjà actée en Phase 2, aucune garde JS nouvelle au-delà du masquage des
 * boutons (la vraie garde reste le serveur, 403/422/409).
 */
document.addEventListener('alpine:init', () => {
    Alpine.data('coranPage', () => ({
        canWrite: window.auth.role === 'Directeur' || window.auth.role === 'Enseignant',

        classrooms: [],
        selectedClassroomId: '',
        activeTab: 'progress', // 'progress' | 'evaluations'

        loadingClassrooms: true,
        // Consommé par le partiel _ErrorBanner partagé (x-show="error") : échec du chargement des classes.
        error: null,

        async init() {
            this.loadingClassrooms = true;
            this.error = null;
            try {
                this.classrooms = await window.api.get('/classrooms');
            } catch (err) {
                this.error = window.api.toMessage(err, 'Erreur lors du chargement des classes.');
            } finally {
                this.loadingClassrooms = false;
            }
        },

        async selectClassroom(id) {
            this.selectedClassroomId = id;
            // Task 3 : déclenche ici le chargement des deux onglets (Promise.all, indépendants).
        }
    }));
});
