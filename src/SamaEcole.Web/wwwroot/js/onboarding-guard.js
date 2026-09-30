/*
 * Garde-fou « Onboarding » (Setup Wizard) — redirige tout Directeur dont l'établissement n'a pas
 * encore de profil (SchoolSettings.ProfileEtablissement = NULL) vers /onboarding, une fois connecté.
 *
 * CONFORT D'ERGONOMIE, PAS UNE PROTECTION — même principe que sidebarNav (auth.js) : le profil ne
 * verrouille aucune donnée par lui-même, seuls les 4 commutateurs de modules qu'il applique
 * (IsPedagogyEnabled…) restent gardés côté API par ModuleAuthorizationHandler. Un Directeur qui
 * contournerait cette redirection verrait simplement un menu non adapté à son profil, rien de plus.
 *
 * Seuls les Directeurs sont concernés : ApplyEstablishmentProfileCommand (POST
 * /schools/current/settings/establishment-profile) est réservé à ce rôle — rediriger un autre rôle
 * vers un écran qu'il ne peut pas valider n'aiderait personne (même logique que school-mode-guard.js
 * › SETUP_ROLES).
 *
 * Chargé en script CLASSIQUE (comme school-mode-guard.js), donc avant le script différé d'Alpine :
 * l'écouteur alpine:init est enregistré à temps. Dépend de window.auth (auth.js) et du store Alpine
 * schoolConfig (auth.js, aussi chargé avant Alpine).
 */
document.addEventListener('alpine:init', () => {
    (async () => {
        if (!window.auth || !window.auth.isAuthenticated() || window.auth.role !== 'Directeur') return;

        try {
            await Alpine.store('schoolConfig').init();
        } catch {
            return; // Confort d'affichage : sans réponse fiable, on ne force aucune redirection.
        }

        // Seul NULL (valeur EXPLICITE du serveur) déclenche la redirection — `undefined` (non chargé
        // ou erreur réseau, voir schoolConfig.profileEtablissement) ne force JAMAIS /onboarding, pour
        // ne jamais piéger le Directeur dans une boucle de redirection au moindre aléa réseau.
        if (Alpine.store('schoolConfig').profileEtablissement !== null) return;

        const currentPath = (window.location.pathname || '').replace(/\/+$/, '') || '/';
        if (currentPath === '/onboarding') return;

        window.location.assign('/onboarding');
    })();
});
