/**
 * Écran Internat (/internat) — conteneur à onglets.
 *
 * L'écran est découpé en onglets, chacun un composant Alpine dans son propre fichier :
 *   - Occupation  → internat-occupation.js  (tableau de bord et affectation des élèves)
 *   - Pavillons   → internat-dormitories.js (pavillons, chambres, lits, corbeilles)
 *   - Pensionnaires → internat-boarders.js  (liste des séjours, fiche d'urgence et profil ; fiche médicale selon le rôle)
 *   - Sorties     → internat-leaves.js     (registre, déclaration, retour ; accompagnateur hors liste = avertissement)
 *   - Pointage    → internat-attendance.js (feuille d'appel du soir, pointage collectif ligne par ligne, registre PDF)
 * La logique pure partagée (jauge, libellés, corps de requête, codes d'erreur, liste des onglets) est dans internat-helpers.js.
 *
 * Tout passe par /api/v1/boarding/* (modèle Pavillon/Lit). Le composant historique de cet écran — tableau de bord par chambre, recherche d'élèves
 * et affectation — appelait GET /internat/dashboard, GET /internat/students/search et POST /internat/assignments/{id} : ces trois routes héritées, fondées
 * sur l'ancien modèle Room, ne sont plus appelées par aucun écran et peuvent être supprimées au lot F (un test de non-régression l'impose).
 */
document.addEventListener('alpine:init', () => {
    /**
     * Conteneur à onglets. L'onglet ouvert suit le fragment d'URL (`/internat#pavillons`) pour pouvoir être partagé et survivre à un rechargement.
     * La liste des onglets et la lecture du fragment sont de la logique pure (internat-helpers.js, testée).
     */
    Alpine.data('internatTabs', () => ({
        tabs: window.internatHelpers.TABS,
        tab: window.internatHelpers.tabFromHash(window.location.hash),

        init() {
            // Le fragment n'était lu qu'à l'ouverture de la page : le modifier sur la page déjà ouverte (lien collé, saisie dans la barre d'adresse) ne
            // changeait pas d'onglet. replaceState ne déclenche pas hashchange : pas de boucle avec select().
            window.addEventListener('hashchange', () => {
                const key = window.internatHelpers.tabFromHash(window.location.hash);
                if (key !== this.tab) this.select(key);
            });
        },

        select(key) {
            this.tab = key;
            // replaceState : changer d'onglet n'empile pas l'historique du navigateur.
            if (window.history && window.history.replaceState) window.history.replaceState(null, '', `#${key}`);
            // Chaque onglet relit ses données à son ouverture (voir internat-helpers.js › shouldRefreshOnShow).
            window.internatHelpers.announceTabShown(key);
        }
    }));
});
