/**
 * Sélecteur d'établissement (groupe scolaire) — barre supérieure. Dépend de auth.js et api.js.
 *
 * Ne s'affiche QUE si le compte est rattaché à plusieurs établissements : GET /auth/my-schools
 * renvoie une liste vide pour un compte mono-école, et `schools.length > 1` garde le composant
 * invisible pour l'immense majorité des utilisateurs. Rien ne change pour eux.
 *
 * Basculer, c'est REMPLACER le jeton de session par celui que renvoie l'API pour l'école cible —
 * même mécanique que l'impersonation Super Admin (auth.enterImpersonation), et pour la même raison :
 * un jeton ne porte jamais deux établissements, la RLS n'a donc rien à connaître de tout ceci.
 *
 * La page est RECHARGÉE après bascule plutôt que rafraîchie composant par composant : tous les
 * écrans déjà montés portent des données de l'école précédente, et il n'existe aucun moyen fiable de
 * tous les réinitialiser — un rechargement est à la fois plus simple et plus sûr.
 */
document.addEventListener('alpine:init', () => {
    Alpine.data('schoolSwitcher', () => ({
        schools: [],
        isOpen: false,
        isSwitching: false,
        error: null,

        async init() {
            try {
                this.schools = await window.api.get('/auth/my-schools');
            } catch {
                // Sélecteur purement optionnel : une erreur ici ne doit pas gêner la navigation.
                this.schools = [];
            }
        },

        get hasMultipleSchools() {
            return this.schools.length > 1;
        },

        get currentSchoolName() {
            const current = this.schools.find((s) => s.isCurrent);
            return current ? current.name : '';
        },

        async switchTo(school) {
            if (school.isCurrent || this.isSwitching) return;

            this.isSwitching = true;
            this.error = null;

            try {
                const tokens = await window.api.post('/auth/switch-school', { schoolId: school.schoolId });

                window.auth.saveSession(tokens);
                window.location.reload();
            } catch (err) {
                this.error = err.message || "Bascule impossible vers cet établissement.";
                this.isSwitching = false;
            }
        }
    }));
});
